using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using OnderCode.Application.Messaging;
using OnderCode.Application.Results;
using OnderCode.Data;
using OnderCode.Data.Initialization;

namespace OnderCode.Application.Tests;

/// <summary>Dağıtıcı + davranış hattı + veri katmanı birlikte, gerçek SQLite üzerinde.</summary>
public sealed class ApplicationLayerTests : IAsyncLifetime
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ondercode-tests", Guid.NewGuid().ToString("N"));
    private readonly WelcomeLog _welcome = new();
    private ServiceProvider _services = null!;

    private ISender Sender => _services.GetRequiredService<ISender>();

    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_welcome);
        services.AddSuiteData<SampleDbContext>(o =>
        {
            o.AppName = "AppTest";
            o.DataDirectory = _dir;
        });
        services.AddSuiteApplication(typeof(ApplicationLayerTests).Assembly);
        _services = services.BuildServiceProvider();
        await _services.GetRequiredService<DatabaseInitializer<SampleDbContext>>().InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task Command_ReturnsNewId_AndDomainEventIsHandledAfterSave()
    {
        var result = await Sender.SendAsync(new RegisterCustomer("Ayşe Yılmaz", "10000000146"));

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.Value > 0);
        Assert.Equal($"{result.Value}:Ayşe Yılmaz", Assert.Single(_welcome.Entries));
    }

    [Fact]
    public async Task Validation_ReturnsFieldErrorsInTurkish_AndHandlerNeverRuns()
    {
        var result = await Sender.SendAsync(new RegisterCustomer("", "12345678901"));

        Assert.True(result.IsFailure);
        Assert.Equal(new[] { "Name", "Tckn" }, result.Errors.Select(e => e.Field ?? ""));
        Assert.All(result.Errors, e => Assert.Equal(ErrorCodes.Validation, e.Code));
        Assert.Contains("'Ad'", result.Errors[0].Message);
        Assert.DoesNotContain("must", result.Errors[0].Message);
        Assert.Equal("'T.C. kimlik no' geçerli bir T.C. kimlik numarası değil.", result.Errors[1].Message);
        Assert.Empty(_welcome.Entries);

        var list = await Sender.SendAsync(new ListCustomers());
        Assert.Equal(0, list.Value.TotalCount);
    }

    [Fact]
    public async Task NotFound_BecomesFailedResult_InsteadOfException()
    {
        var result = await Sender.SendAsync(new GetCustomer(404));

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal(ErrorCodes.NotFound, error.Code);
        Assert.Contains("bulunamadı", error.Message);
        Assert.Throws<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public async Task DuplicateRecord_BecomesFailedResult_AndNoEventIsPublished()
    {
        await Sender.SendAsync(new RegisterCustomer("Ali", "10000000146"));
        var second = await Sender.SendAsync(new RegisterCustomer("Veli", "10000000146"));

        Assert.Equal(ErrorCodes.Duplicate, Assert.Single(second.Errors).Code);
        Assert.Single(_welcome.Entries);
    }

    [Fact]
    public async Task CommandWithoutValue_UpdatesRecord()
    {
        var id = (await Sender.SendAsync(new RegisterCustomer("Eski Ad", "12345678950"))).Value;

        var rename = await Sender.SendAsync(new RenameCustomer(id, "Yeni Ad"));
        Assert.True(rename.IsSuccess, rename.ErrorMessage);

        var customer = await Sender.SendAsync(new GetCustomer(id));
        Assert.Equal("Yeni Ad", customer.Value.Name);
    }

    [Fact]
    public async Task PagedQuery_SearchesInTurkish_AndValidatesPaging()
    {
        await Sender.SendAsync(new RegisterCustomer("Ayşe Yılmaz", "10000000146"));
        await Sender.SendAsync(new RegisterCustomer("Şükrü Öztürk", "12345678950"));
        await Sender.SendAsync(new RegisterCustomer("Can Demir", "11111111110"));

        var found = await Sender.SendAsync(new ListCustomers { Search = "sukru ozturk" });
        Assert.Equal("Şükrü Öztürk", Assert.Single(found.Value.Items).Name);

        var all = await Sender.SendAsync(new ListCustomers { PageSize = 2 });
        Assert.Equal(3, all.Value.TotalCount);
        Assert.Equal(new[] { "Ayşe Yılmaz", "Can Demir" }, all.Value.Items.Select(c => c.Name));

        var invalid = await Sender.SendAsync(new ListCustomers { PageSize = 0 });
        Assert.Equal("PageSize", Assert.Single(invalid.Errors).Field);
    }

    [Fact]
    public async Task MissingHandler_ThrowsClearMessage()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Sender.SendAsync(new OrphanCommand()));
        Assert.Contains(nameof(OrphanCommand), ex.Message);
    }
}
