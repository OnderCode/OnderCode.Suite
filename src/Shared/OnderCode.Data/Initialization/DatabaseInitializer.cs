using Microsoft.EntityFrameworkCore;

namespace OnderCode.Data.Initialization;

/// <summary>Başlangıç verisi. Tekrar tekrar çalışabilmeli: önce veri var mı diye bakmalı.</summary>
public interface ISeeder<in TContext> where TContext : DbContext
{
    /// <summary>Küçük olan önce çalışır.</summary>
    int Order => 0;

    Task SeedAsync(TContext context, CancellationToken cancellationToken);
}

/// <summary>
/// Uygulama açılışında bir kez çağrılır: veritabanı yoksa oluşturur, bekleyen migration'ları uygular,
/// başlangıç verisini yükler. Kullanıcı hiçbir kurulum yapmaz.
/// </summary>
public sealed class DatabaseInitializer<TContext>(IDbContextFactory<TContext> contextFactory, IEnumerable<ISeeder<TContext>> seeders)
    where TContext : SuiteDbContext
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        // Migration varsa onları uygula; yoksa (prototip aşaması) modelden doğrudan oluştur
        if (context.Database.GetMigrations().Any())
            await context.Database.MigrateAsync(cancellationToken);
        else
            await context.Database.EnsureCreatedAsync(cancellationToken);

        // WAL: okuma ve yazma birbirini beklemez, elektrik kesintisine dayanıklı. Dosyada kalıcıdır.
        await context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode = WAL;", cancellationToken);

        foreach (var seeder in seeders.OrderBy(s => s.Order))
        {
            await seeder.SeedAsync(context, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            context.ChangeTracker.Clear();
        }
    }
}
