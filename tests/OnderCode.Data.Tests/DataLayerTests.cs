using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OnderCode.Data.Initialization;
using OnderCode.Kernel.Abstractions;
using OnderCode.Kernel.Domain;
using OnderCode.Kernel.Exceptions;
using OnderCode.Kernel.Persistence;
using OnderCode.Kernel.Specifications;

namespace OnderCode.Data.Tests;

/// <summary>Her test kendi geçici veritabanı dosyasıyla, gerçek SQLite üzerinde çalışır.</summary>
public sealed class DataLayerTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Start = new(2026, 11, 11, 9, 0, 0, TimeSpan.Zero);
    private static readonly string[] CityNames = ["Şanlıurfa", "İzmir", "Adana", "Iğdır", "Çorum", "Sakarya", "Bursa"];

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ondercode-tests", Guid.NewGuid().ToString("N"));
    private readonly ManualTimeProvider _time = new(Start);
    private readonly RecordingDispatcher _events = new();
    private ServiceProvider _services = null!;

    private IUnitOfWork Uow() => _services.GetRequiredService<IUnitOfWorkFactory>().Create();
    private IDbContextFactory<TestDbContext> Factory => _services.GetRequiredService<IDbContextFactory<TestDbContext>>();
    private DatabaseInitializer<TestDbContext> Initializer => _services.GetRequiredService<DatabaseInitializer<TestDbContext>>();

    public async Task InitializeAsync()
    {
        var user = new CurrentUser();
        user.SignIn("test.kullanici");

        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(_time);
        services.AddSingleton(user);
        services.AddSingleton<IDomainEventDispatcher>(_events);
        services.AddSuiteData<TestDbContext>(o =>
        {
            o.AppName = "Test";
            o.DataDirectory = _dir;
        });
        services.AddSeeder<TestDbContext, CategorySeeder>();
        _services = services.BuildServiceProvider();

        await Initializer.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        SqliteConnection.ClearAllPools(); // havuzdaki bağlantılar dosyayı kilitli tutmasın
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task Initializer_CreatesDatabase_InWalMode_AndSeedsOnlyOnce()
    {
        Assert.True(File.Exists(Path.Combine(_dir, "Test.db")));

        await Initializer.InitializeAsync(); // ikinci açılış

        await using var db = await Factory.CreateDbContextAsync();
        Assert.Equal(2, await db.Categories.CountAsync());
        var mode = await db.Database.SqlQueryRaw<string>("SELECT journal_mode AS Value FROM pragma_journal_mode").SingleAsync();
        Assert.Equal("wal", mode);
    }

    [Fact]
    public async Task Audit_FillsCreatedAndUpdatedFields_AndBumpsVersion()
    {
        var id = await AddProductAsync("P1", 100m);

        await using (var uow = Uow())
        {
            var product = await uow.Repository<Product>().GetRequiredAsync(id);
            Assert.Equal(Start.UtcDateTime, product.CreatedAt);
            Assert.Equal(DateTimeKind.Utc, product.CreatedAt.Kind);
            Assert.Equal("test.kullanici", product.CreatedBy);
            Assert.Null(product.UpdatedAt);
            Assert.Equal(1L, product.Version);

            _time.Advance(TimeSpan.FromMinutes(5));
            product.ChangePrice(120m);
            await uow.SaveChangesAsync();
        }

        await using (var uow = Uow())
        {
            var product = await uow.Repository<Product>().GetRequiredAsync(id);
            Assert.Equal(Start.UtcDateTime.AddMinutes(5), product.UpdatedAt);
            Assert.Equal(2L, product.Version);
            Assert.Equal(120m, product.Price);
        }
    }

    [Fact]
    public async Task SoftDelete_HidesRecord_ButKeepsTheRow()
    {
        var id = await AddProductAsync("P2", 10m);

        await using (var uow = Uow())
        {
            var products = uow.Repository<Product>();
            products.Remove(await products.GetRequiredAsync(id));
            await uow.SaveChangesAsync();
        }

        await using (var uow = Uow())
        {
            var products = uow.Repository<Product>();
            Assert.Null(await products.GetByIdAsync(id));
            Assert.False(await products.AnyAsync(new Specification<Product>().Where(p => p.Id == id)));

            var deleted = await products.FirstOrDefaultAsync(new Specification<Product>().Where(p => p.Id == id).WithDeleted());
            Assert.NotNull(deleted);
            Assert.True(deleted.IsDeleted);
            Assert.Equal("test.kullanici", deleted.DeletedBy);
        }
    }

    [Fact]
    public async Task Concurrency_SecondWriterGetsFriendlyConflict()
    {
        var id = await AddProductAsync("P3", 50m);

        await using var first = Uow();
        await using var second = Uow();
        var a = await first.Repository<Product>().GetRequiredAsync(id);
        var b = await second.Repository<Product>().GetRequiredAsync(id);

        a.ChangePrice(60m);
        await first.SaveChangesAsync();

        b.ChangePrice(70m);
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task Update_AttachesDetachedEntity_FromAnotherScreen()
    {
        var id = await AddProductAsync("P4", 5m);

        Product detached;
        await using (var uow = Uow())
            detached = (await uow.Repository<Product>().ListAsync(new Specification<Product>().Where(p => p.Id == id))).Single();

        detached.Name = "Yeni ad";
        await using (var uow = Uow())
        {
            uow.Repository<Product>().Update(detached);
            await uow.SaveChangesAsync();
        }

        await using (var uow = Uow())
        {
            var product = await uow.Repository<Product>().GetRequiredAsync(id);
            Assert.Equal("Yeni ad", product.Name);
            Assert.Equal(2L, product.Version);
        }
    }

    [Fact]
    public async Task UniqueIndex_IsTurkishCaseInsensitive()
    {
        await using var uow = Uow();
        var cities = uow.Repository<City>();
        cities.Add(new City("İstanbul", "İST"));
        await uow.SaveChangesAsync();

        cities.Add(new City("İstanbul Anadolu", "ist"));
        await Assert.ThrowsAsync<DuplicateRecordException>(() => uow.SaveChangesAsync());
    }

    [Fact]
    public async Task ForeignKey_RestrictsDelete_ByDefault()
    {
        await AddProductAsync("P5", 5m);

        await using var uow = Uow();
        var categories = uow.Repository<Category>();
        var category = await categories.FirstOrDefaultAsync(new Specification<Category>().OrderBy(c => c.Id).Tracking());
        categories.Remove(category!);

        await Assert.ThrowsAsync<ReferenceInUseException>(() => uow.SaveChangesAsync());
    }

    [Fact]
    public async Task Ordering_FollowsTheTurkishAlphabet()
    {
        await AddCitiesAsync();

        await using var uow = Uow();
        var names = await uow.Repository<City>().ListAsync(new Specification<City>().OrderBy(c => c.Name), c => c.Name);

        Assert.Equal(new[] { "Adana", "Bursa", "Çorum", "Iğdır", "İzmir", "Sakarya", "Şanlıurfa" }, names);
    }

    [Fact]
    public async Task Search_IgnoresCaseAndTurkishCharacters_InTheDatabase()
    {
        await AddCitiesAsync();

        await using var uow = Uow();
        var cities = uow.Repository<City>();
        async Task<string[]> Find(string term) =>
            [.. await cities.ListAsync(new Specification<City>().Search(term, c => c.Name, c => c.Code).OrderBy(c => c.Name), c => c.Name)];

        Assert.Equal(new[] { "Şanlıurfa" }, await Find("sanliurfa"));
        Assert.Equal(new[] { "İzmir" }, await Find("IZMIR"));
        Assert.Equal(new[] { "Iğdır" }, await Find("ığdır"));
        Assert.Equal(new[] { "Şanlıurfa" }, await Find("şan URFA"));
        Assert.Equal(new[] { "Bursa", "Sakarya", "Şanlıurfa" }, await Find("SA"));
    }

    [Fact]
    public async Task Money_IsStoredAsInteger_AndAggregatesExactlyInSql()
    {
        await AddProductAsync("M1", 0.1m, discount: 0.05m);
        await AddProductAsync("M2", 0.2m);
        await AddProductAsync("M3", 1234.5678m, discount: 1m);

        await using var db = await Factory.CreateDbContextAsync();
        Assert.Equal(1234.8678m, await db.Products.SumAsync(p => p.Price));
        Assert.Equal(1.05m, await db.Products.SumAsync(p => p.Discount));
        Assert.Equal("M3", await db.Products.OrderByDescending(p => p.Price).Select(p => p.Code).FirstAsync());
        Assert.Equal(1, await db.Products.CountAsync(p => p.Price > 1000m));

        var types = await db.Database
            .SqlQueryRaw<string>("SELECT type AS Value FROM pragma_table_info('Products') WHERE name IN ('Price', 'Discount')")
            .ToListAsync();
        Assert.Equal(new[] { "INTEGER", "INTEGER" }, types);
    }

    [Fact]
    public async Task Paging_ReturnsPageAndTotals()
    {
        await using (var uow = Uow())
        {
            uow.Repository<City>().AddRange(Enumerable.Range(1, 25).Select(i => new City($"Şehir {i:00}", $"K{i:00}")));
            await uow.SaveChangesAsync();
        }

        await using (var uow = Uow())
        {
            var page = await uow.Repository<City>().PagedAsync(new Specification<City>().OrderBy(c => c.Name), page: 2, pageSize: 10);
            Assert.Equal(25, page.TotalCount);
            Assert.Equal(3, page.TotalPages);
            Assert.Equal(10, page.Items.Count);
            Assert.Equal("Şehir 11", page.Items[0].Name);
            Assert.True(page.HasPrevious);
            Assert.True(page.HasNext);
        }
    }

    [Fact]
    public async Task Include_LoadsNavigation()
    {
        var id = await AddProductAsync("I1", 1m);

        await using var uow = Uow();
        var product = await uow.Repository<Product>().FirstOrDefaultAsync(
            new Specification<Product>().Where(p => p.Id == id).Include(p => p.Category));

        Assert.NotNull(product);
        Assert.Equal("Elektronik", product.Category.Name);
    }

    [Fact]
    public async Task Transaction_RollsBackEverything_OnError()
    {
        await using (var uow = Uow())
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => uow.ExecuteInTransactionAsync(async ct =>
            {
                uow.Repository<City>().Add(new City("Ankara", "ANK"));
                await uow.SaveChangesAsync(ct);
                throw new InvalidOperationException("İş kuralı bozuldu");
            }));
        }

        await using (var uow = Uow())
            Assert.Equal(0, await uow.Repository<City>().CountAsync());
    }

    [Fact]
    public async Task DomainEvents_AreDispatched_AfterSuccessfulSave()
    {
        var id = await AddProductAsync("E1", 10m);

        await using (var uow = Uow())
        {
            var product = await uow.Repository<Product>().GetRequiredAsync(id);
            product.ChangePrice(15m);
            await uow.SaveChangesAsync();
        }

        var changed = Assert.Single(_events.Received.OfType<PriceChanged>());
        Assert.Equal(id, changed.ProductId);
        Assert.Equal(10m, changed.OldPrice);
        Assert.Equal(15m, changed.NewPrice);
    }

    [Fact]
    public async Task GetRequired_ThrowsNotFound_WithEntityName()
    {
        await using var uow = Uow();
        var ex = await Assert.ThrowsAsync<NotFoundException>(() => uow.Repository<Product>().GetRequiredAsync(999));
        Assert.Equal(nameof(Product), ex.EntityName);
    }

    private async Task<int> AddProductAsync(string code, decimal price, decimal? discount = null)
    {
        await using var uow = Uow();
        var category = await uow.Repository<Category>().FirstOrDefaultAsync(new Specification<Category>().OrderBy(c => c.Id).Tracking());
        var product = new Product(code, "Ürün " + code, price, category!) { Discount = discount };
        uow.Repository<Product>().Add(product);
        await uow.SaveChangesAsync();
        return product.Id;
    }

    private async Task AddCitiesAsync()
    {
        await using var uow = Uow();
        uow.Repository<City>().AddRange(CityNames.Select((name, i) => new City(name, $"C{i}")));
        await uow.SaveChangesAsync();
    }
}
