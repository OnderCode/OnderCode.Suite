using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.DependencyInjection;
using OnderCode.Data;
using Otel.Application;
using Otel.Domain;

namespace Otel.Infrastructure;

public sealed class OtelDbContext(DbContextOptions<OtelDbContext> options) : SuiteDbContext(options)
{
    public DbSet<RoomType> RoomTypes { get; set; } = null!;
    public DbSet<Room> Rooms { get; set; } = null!;
    public DbSet<Guest> Guests { get; set; } = null!;
    public DbSet<Reservation> Reservations { get; set; } = null!;
    public DbSet<FolioItem> FolioItems { get; set; } = null!;
    public DbSet<HousekeepingTask> HousekeepingTasks { get; set; } = null!;
    public DbSet<Season> Seasons { get; set; } = null!;
}

public static class OtelInfrastructure
{
    /// <summary>Veri katmanı + uygulama katmanı + örnek veri. Arayüz yalnızca bunu çağırır.</summary>
    public static IServiceCollection AddOtel(this IServiceCollection services, Action<SuiteDatabaseOptions>? configure = null)
    {
        services.AddSuiteData<OtelDbContext>(o =>
        {
            o.AppName = "Otel";
            configure?.Invoke(o);
        });
        services.AddSeeder<OtelDbContext, OtelDemoSeeder>();
        services.AddOtelApplication();
        return services;
    }
}

internal sealed class RoomTypeConfiguration : IEntityTypeConfiguration<RoomType>
{
    public void Configure(EntityTypeBuilder<RoomType> b)
    {
        b.Property(x => x.Code).HasMaxLength(10);
        b.Property(x => x.Name).HasMaxLength(60);
        b.HasIndex(x => x.Code).IsUnique();
    }
}

internal sealed class RoomConfiguration : IEntityTypeConfiguration<Room>
{
    public void Configure(EntityTypeBuilder<Room> b)
    {
        b.Property(x => x.Number).HasMaxLength(10);
        b.Property(x => x.MaintenanceNote).HasMaxLength(200);
        b.HasIndex(x => x.Number).IsUnique();
        b.HasOne(x => x.RoomType).WithMany().HasForeignKey(x => x.RoomTypeId);
    }
}

internal sealed class GuestConfiguration : IEntityTypeConfiguration<Guest>
{
    public void Configure(EntityTypeBuilder<Guest> b)
    {
        b.Property(x => x.FullName).HasMaxLength(120);
        b.Property(x => x.IdentityNo).HasMaxLength(20);
        b.Property(x => x.Email).HasMaxLength(120);
        b.Property(x => x.Nationality).HasMaxLength(3);
        b.HasIndex(x => x.FullName);
    }
}

internal sealed class ReservationConfiguration : IEntityTypeConfiguration<Reservation>
{
    public void Configure(EntityTypeBuilder<Reservation> b)
    {
        b.Property(x => x.Code).HasMaxLength(20);
        b.Property(x => x.Notes).HasMaxLength(500);
        b.HasIndex(x => x.Code).IsUnique();
        // Çakışma sorgusu ve çizelge için
        b.HasIndex(x => new { x.RoomId, x.CheckIn, x.CheckOut });
        b.HasIndex(x => x.Status);
        b.HasOne(x => x.Guest).WithMany().HasForeignKey(x => x.GuestId);
        b.HasOne(x => x.Room).WithMany().HasForeignKey(x => x.RoomId);
        // Folyo rezervasyonun parçasıdır: rezervasyonla birlikte silinir
        b.HasMany(x => x.Charges).WithOne().HasForeignKey(x => x.ReservationId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Charges).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class FolioItemConfiguration : IEntityTypeConfiguration<FolioItem>
{
    public void Configure(EntityTypeBuilder<FolioItem> b) => b.Property(x => x.Description).HasMaxLength(100);
}

internal sealed class HousekeepingTaskConfiguration : IEntityTypeConfiguration<HousekeepingTask>
{
    public void Configure(EntityTypeBuilder<HousekeepingTask> b)
    {
        b.Property(x => x.AssignedTo).HasMaxLength(60);
        b.Property(x => x.Note).HasMaxLength(200);
        b.HasIndex(x => x.State);
        b.HasOne(x => x.Room).WithMany().HasForeignKey(x => x.RoomId);
    }
}

internal sealed class SeasonConfiguration : IEntityTypeConfiguration<Season>
{
    public void Configure(EntityTypeBuilder<Season> b) => b.Property(x => x.Name).HasMaxLength(60);
}
