using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OnderCode.Data;
using OnderCode.Data.Initialization;
using OnderCode.Kernel.Domain;

namespace OnderCode.Data.Tests;

// Testler için küçük bir alan modeli: kategori (sert silme), ürün (denetim + yumuşak silme), şehir (Türkçe metin)

public sealed class Category : Entity
{
    private Category() { }
    public Category(string name) => Name = name;
    public string Name { get; private set; } = "";
}

public sealed class Product : FullAuditableEntity
{
    private Product() { }

    public Product(string code, string name, decimal price, Category category)
    {
        Code = code;
        Name = name;
        Price = price;
        Category = category;
    }

    public string Code { get; private set; } = "";
    public string Name { get; set; } = "";
    public decimal Price { get; private set; }
    public decimal? Discount { get; set; }
    public int CategoryId { get; private set; }
    public Category Category { get; private set; } = null!;

    public void ChangePrice(decimal newPrice)
    {
        var old = Price;
        Price = newPrice;
        Raise(new PriceChanged(Id, old, newPrice));
    }
}

public sealed record PriceChanged(int ProductId, decimal OldPrice, decimal NewPrice) : IDomainEvent;

public sealed class City : Entity
{
    private City() { }

    public City(string name, string code)
    {
        Name = name;
        Code = code;
    }

    public string Name { get; private set; } = "";
    public string Code { get; private set; } = "";
}

public sealed class TestDbContext(DbContextOptions<TestDbContext> options) : SuiteDbContext(options)
{
    public DbSet<Category> Categories { get; set; } = null!;
    public DbSet<Product> Products { get; set; } = null!;
    public DbSet<City> Cities { get; set; } = null!;
}

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.Property(p => p.Code).HasMaxLength(20);
        builder.HasIndex(p => p.Code).IsUnique();
        builder.HasOne(p => p.Category).WithMany().HasForeignKey(p => p.CategoryId);
    }
}

internal sealed class CityConfiguration : IEntityTypeConfiguration<City>
{
    public void Configure(EntityTypeBuilder<City> builder) => builder.HasIndex(c => c.Code).IsUnique();
}

public sealed class CategorySeeder : ISeeder<TestDbContext>
{
    public async Task SeedAsync(TestDbContext context, CancellationToken cancellationToken)
    {
        if (await context.Categories.AnyAsync(cancellationToken)) return;
        context.Categories.AddRange(new Category("Elektronik"), new Category("Gıda"));
    }
}

public sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan by) => _now += by;
}

public sealed class RecordingDispatcher : IDomainEventDispatcher
{
    public List<IDomainEvent> Received { get; } = [];

    public Task DispatchAsync(IReadOnlyList<IDomainEvent> events, CancellationToken cancellationToken = default)
    {
        Received.AddRange(events);
        return Task.CompletedTask;
    }
}
