using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.DependencyInjection;
using OnderCode.Data;
using Otopark.Application;
using Otopark.Domain;

namespace Otopark.Infrastructure;

public sealed class OtoparkDbContext(DbContextOptions<OtoparkDbContext> options) : SuiteDbContext(options)
{
    public DbSet<ParkingSpot> Spots { get; set; } = null!;
    public DbSet<ParkingSession> Sessions { get; set; } = null!;
    public DbSet<Subscription> Subscriptions { get; set; } = null!;
    public DbSet<WashPackage> WashPackages { get; set; } = null!;
    public DbSet<WashOrder> WashOrders { get; set; } = null!;
}

public static class OtoparkInfrastructure
{
    /// <summary>Veri katmanı + uygulama katmanı + örnek veri. Arayüz yalnızca bunu çağırır.</summary>
    public static IServiceCollection AddOtopark(this IServiceCollection services, Action<SuiteDatabaseOptions>? configure = null)
    {
        services.AddSuiteData<OtoparkDbContext>(o =>
        {
            o.AppName = "Otopark";
            configure?.Invoke(o);
        });
        services.AddSeeder<OtoparkDbContext, OtoparkDemoSeeder>();
        services.AddOtoparkApplication();
        return services;
    }
}

internal sealed class ParkingSpotConfiguration : IEntityTypeConfiguration<ParkingSpot>
{
    public void Configure(EntityTypeBuilder<ParkingSpot> b)
    {
        b.Property(x => x.Code).HasMaxLength(10);
        b.Property(x => x.Zone).HasMaxLength(5);
        b.Property(x => x.ServiceNote).HasMaxLength(200);
        b.HasIndex(x => x.Code).IsUnique();
    }
}

internal sealed class ParkingSessionConfiguration : IEntityTypeConfiguration<ParkingSession>
{
    public void Configure(EntityTypeBuilder<ParkingSession> b)
    {
        b.Property(x => x.Plate).HasMaxLength(12);
        b.Ignore(x => x.Total);
        b.HasOne(x => x.Spot).WithMany().HasForeignKey(x => x.SpotId);
        b.HasOne(x => x.Subscription).WithMany().HasForeignKey(x => x.SubscriptionId);
        // Aynı plaka aynı anda içeride iki kez olamaz: yalnızca içerideki (Status = 0) kayıtlar için benzersiz
        b.HasIndex(x => x.Plate).IsUnique().HasFilter("\"Status\" = 0").HasDatabaseName("UX_Sessions_ActivePlate");
        // Aynı park yerinde aynı anda tek araç
        b.HasIndex(x => x.SpotId).IsUnique().HasFilter("\"Status\" = 0").HasDatabaseName("UX_Sessions_ActiveSpot");
        b.HasIndex(x => x.EntryAt);
        b.HasIndex(x => x.ExitAt);
        b.HasIndex(x => new { x.Plate, x.EntryAt });
    }
}

internal sealed class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>
{
    public void Configure(EntityTypeBuilder<Subscription> b)
    {
        b.Property(x => x.Plate).HasMaxLength(12);
        b.Property(x => x.OwnerName).HasMaxLength(120);
        b.HasIndex(x => new { x.Plate, x.EndDate });
    }
}

internal sealed class WashPackageConfiguration : IEntityTypeConfiguration<WashPackage>
{
    public void Configure(EntityTypeBuilder<WashPackage> b)
    {
        b.Property(x => x.Code).HasMaxLength(10);
        b.Property(x => x.Name).HasMaxLength(60);
        b.HasIndex(x => x.Code).IsUnique();
    }
}

internal sealed class WashOrderConfiguration : IEntityTypeConfiguration<WashOrder>
{
    public void Configure(EntityTypeBuilder<WashOrder> b)
    {
        b.Property(x => x.Plate).HasMaxLength(12);
        b.Property(x => x.Washer).HasMaxLength(60);
        b.Property(x => x.Note).HasMaxLength(200);
        b.Ignore(x => x.CountsAsRevenue);
        b.HasOne(x => x.Package).WithMany().HasForeignKey(x => x.PackageId);
        b.HasOne(x => x.Session).WithMany().HasForeignKey(x => x.SessionId);
        b.HasIndex(x => x.Status);
        b.HasIndex(x => x.DeliveredAt);
        b.HasIndex(x => x.SessionId);
    }
}
