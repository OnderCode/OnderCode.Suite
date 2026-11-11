using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using OnderCode.Data.Initialization;
using OnderCode.Data.Interceptors;
using OnderCode.Kernel.Abstractions;
using OnderCode.Kernel.Persistence;

namespace OnderCode.Data;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Veri katmanını kaydeder. Uygulama açılışında <see cref="DatabaseInitializer{TContext}"/> çağrılmalıdır:
    /// <code>
    /// services.AddSuiteData&lt;OtelDbContext&gt;(o =&gt; o.AppName = "Otel")
    ///         .AddSeeder&lt;OtelDbContext, OdaSeeder&gt;();
    /// await provider.GetRequiredService&lt;DatabaseInitializer&lt;OtelDbContext&gt;&gt;().InitializeAsync();
    /// </code>
    /// </summary>
    public static IServiceCollection AddSuiteData<TContext>(this IServiceCollection services, Action<SuiteDatabaseOptions>? configure = null)
        where TContext : SuiteDbContext
    {
        var options = new SuiteDatabaseOptions();
        configure?.Invoke(options);
        Directory.CreateDirectory(Path.GetDirectoryName(options.ResolveDatabasePath())!);

        services.AddSingleton(options);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<CurrentUser>();
        services.TryAddSingleton<ICurrentUser>(sp => sp.GetRequiredService<CurrentUser>());
        services.TryAddSingleton<SqliteConnectionSetupInterceptor>();
        services.TryAddSingleton<AuditSaveChangesInterceptor>();

        services.AddDbContextFactory<TContext>((sp, builder) =>
        {
            builder.UseSqlite(options.BuildConnectionString());
            builder.AddInterceptors(
                sp.GetRequiredService<SqliteConnectionSetupInterceptor>(),
                sp.GetRequiredService<AuditSaveChangesInterceptor>());
            if (options.LogSqlToDebug)
                builder.LogTo(message => Debug.WriteLine(message), LogLevel.Information).EnableSensitiveDataLogging();
        });

        services.AddSingleton<IUnitOfWorkFactory, UnitOfWorkFactory<TContext>>();
        services.AddSingleton<DatabaseInitializer<TContext>>();
        return services;
    }

    public static IServiceCollection AddSeeder<TContext, TSeeder>(this IServiceCollection services)
        where TContext : SuiteDbContext
        where TSeeder : class, ISeeder<TContext>
        => services.AddTransient<ISeeder<TContext>, TSeeder>();
}
