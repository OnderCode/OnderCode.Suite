using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.DependencyInjection;
using OnderCode.Data;
using OnderCode.Data.Initialization;
using Sinema.Application;
using Sinema.Domain;

namespace Sinema.Infrastructure;

public sealed class SinemaDbContext(DbContextOptions<SinemaDbContext> options) : SuiteDbContext(options)
{
    public DbSet<Film> Films { get; set; } = null!;
    public DbSet<Hall> Halls { get; set; } = null!;
    public DbSet<Session> Sessions { get; set; } = null!;
    public DbSet<TicketType> TicketTypes { get; set; } = null!;
    public DbSet<PricingRules> PricingRules { get; set; } = null!;
    public DbSet<CinemaSettings> Settings { get; set; } = null!;
    public DbSet<Order> Orders { get; set; } = null!;
    public DbSet<Ticket> Tickets { get; set; } = null!;
    public DbSet<ConcessionItem> ConcessionItems { get; set; } = null!;
    public DbSet<ConcessionLine> ConcessionLines { get; set; } = null!;
    public DbSet<Employee> Employees { get; set; } = null!;
}

public static class SinemaInfrastructure
{
    /// <summary>Veri katmanı + uygulama katmanı + afiş deposu + örnek veri. Arayüz yalnızca bunu çağırır.</summary>
    /// <param name="posterRoot">Yüklenen afişlerin yazılacağı klasör (web'de wwwroot/uploads).</param>
    public static IServiceCollection AddSinema(this IServiceCollection services, string posterRoot, Action<SuiteDatabaseOptions>? configure = null)
    {
        services.AddSuiteData<SinemaDbContext>(o =>
        {
            o.AppName = "Sinema";
            configure?.Invoke(o);
        });
        services.AddSeeder<SinemaDbContext, SinemaDemoSeeder>();
        services.AddSingleton<IPosterStore>(new DiskPosterStore(posterRoot));
        services.AddSingleton<IDemoReset, DemoReset>();
        services.AddSinemaApplication();
        return services;
    }
}

/// <summary>Afişleri diske yazar: {kök}/afis-{filmId}-{zaman}.{uzantı}. Web'de "uploads/..." yolu döner.</summary>
internal sealed class DiskPosterStore(string root) : IPosterStore
{
    public async Task<string> SaveAsync(int filmId, byte[] content, string extension, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(root);
        foreach (var old in Directory.GetFiles(root, $"afis-{filmId}-*")) File.Delete(old);
        var name = $"afis-{filmId}-{DateTime.UtcNow:yyyyMMddHHmmss}{extension}";
        await File.WriteAllBytesAsync(Path.Combine(root, name), content, cancellationToken);
        return "uploads/" + name;
    }
}

/// <summary>Demo: veritabanını silip örnek veriyle yeniden kurar.</summary>
internal sealed class DemoReset(IDbContextFactory<SinemaDbContext> factory, DatabaseInitializer<SinemaDbContext> initializer) : IDemoReset
{
    public async Task ResetAsync(CancellationToken cancellationToken)
    {
        await using (var db = await factory.CreateDbContextAsync(cancellationToken))
        {
            await db.Database.EnsureDeletedAsync(cancellationToken);
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        await initializer.InitializeAsync(cancellationToken);
    }
}

internal sealed class FilmConfiguration : IEntityTypeConfiguration<Film>
{
    public void Configure(EntityTypeBuilder<Film> b)
    {
        b.Property(x => x.Title).HasMaxLength(120);
        b.Property(x => x.OriginalTitle).HasMaxLength(120);
        b.Property(x => x.Genres).HasMaxLength(120);
        b.Property(x => x.Director).HasMaxLength(80);
        b.Property(x => x.Cast).HasMaxLength(300);
        b.Property(x => x.Country).HasMaxLength(60);
        b.Property(x => x.Synopsis).HasMaxLength(1500);
        b.Property(x => x.PosterPath).HasMaxLength(200);
        b.Property(x => x.AccentColor).HasMaxLength(7);
        b.Ignore(x => x.Duration);
        b.Ignore(x => x.CanSchedule);
    }
}

internal sealed class HallConfiguration : IEntityTypeConfiguration<Hall>
{
    public void Configure(EntityTypeBuilder<Hall> b)
    {
        b.Property(x => x.Name).HasMaxLength(40);
        b.Property(x => x.Layout).HasMaxLength(1100);
        b.Property(x => x.Features).HasMaxLength(120);
        b.HasIndex(x => x.Name).IsUnique();
    }
}

internal sealed class SessionConfiguration : IEntityTypeConfiguration<Session>
{
    public void Configure(EntityTypeBuilder<Session> b)
    {
        b.HasOne(x => x.Film).WithMany().HasForeignKey(x => x.FilmId);
        b.HasOne(x => x.Hall).WithMany().HasForeignKey(x => x.HallId);
        b.HasIndex(x => new { x.HallId, x.Start });
        b.HasIndex(x => x.Start);
    }
}

internal sealed class TicketTypeConfiguration : IEntityTypeConfiguration<TicketType>
{
    public void Configure(EntityTypeBuilder<TicketType> b)
    {
        b.Property(x => x.Name).HasMaxLength(30);
        b.Property(x => x.Description).HasMaxLength(120);
    }
}

internal sealed class SettingsConfiguration : IEntityTypeConfiguration<CinemaSettings>
{
    public void Configure(EntityTypeBuilder<CinemaSettings> b)
    {
        b.Property(x => x.Name).HasMaxLength(60);
        b.Property(x => x.Branch).HasMaxLength(60);
    }
}

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> b)
    {
        b.Property(x => x.Code).HasMaxLength(9);
        b.Property(x => x.CustomerName).HasMaxLength(60);
        b.HasIndex(x => x.Code).IsUnique();
        b.HasIndex(x => x.SoldAt);
        b.HasOne(x => x.Session).WithMany().HasForeignKey(x => x.SessionId);
        b.HasOne(x => x.SoldBy).WithMany().HasForeignKey(x => x.SoldById);
        b.HasMany(x => x.Tickets).WithOne(x => x.Order).HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.Concessions).WithOne(x => x.Order).HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Tickets).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Navigation(x => x.Concessions).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class TicketConfiguration : IEntityTypeConfiguration<Ticket>
{
    public void Configure(EntityTypeBuilder<Ticket> b)
    {
        b.Property(x => x.SeatCode).HasMaxLength(4);
        b.Property(x => x.TicketTypeName).HasMaxLength(30);
        b.HasOne(x => x.Session).WithMany().HasForeignKey(x => x.SessionId);
        // Aynı seansta aynı koltuk iki kez satılamaz (iade edilenler hariç). Eşzamanlı iki satışta ikincisi burada durur.
        b.HasIndex(x => new { x.SessionId, x.SeatCode }).IsUnique().HasFilter("\"Status\" <> 2");
        b.HasIndex(x => x.UsedAt);
    }
}

internal sealed class ConcessionItemConfiguration : IEntityTypeConfiguration<ConcessionItem>
{
    public void Configure(EntityTypeBuilder<ConcessionItem> b) => b.Property(x => x.Name).HasMaxLength(50);
}

internal sealed class ConcessionLineConfiguration : IEntityTypeConfiguration<ConcessionLine>
{
    public void Configure(EntityTypeBuilder<ConcessionLine> b)
    {
        b.Property(x => x.Name).HasMaxLength(50);
        b.HasOne(x => x.Item).WithMany().HasForeignKey(x => x.ItemId);
        b.Ignore(x => x.Total);
    }
}

internal sealed class EmployeeConfiguration : IEntityTypeConfiguration<Employee>
{
    public void Configure(EntityTypeBuilder<Employee> b)
    {
        b.Property(x => x.FullName).HasMaxLength(80);
        b.Property(x => x.Shift).HasMaxLength(60);
    }
}
