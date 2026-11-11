using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;
using OnderCode.Kernel.Text;

namespace OnderCode.Data.Interceptors;

/// <summary>
/// Her bağlantı açıldığında: Türkçe sıralama kuralını ve arama fonksiyonunu SQLite'a tanıtır,
/// masaüstü için uygun ayarları yapar.
/// </summary>
public sealed class SqliteConnectionSetupInterceptor : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData) => Setup(connection);

    public override Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        Setup(connection);
        return Task.CompletedTask;
    }

    private static void Setup(DbConnection connection)
    {
        if (connection is not SqliteConnection sqlite) return;

        sqlite.CreateCollation(SuiteDbContext.TurkishCollation, TurkishText.Compare);
        sqlite.CreateFunction<string?, string>(SuiteDbContext.FoldFunction, TurkishText.Fold, isDeterministic: true);

        using var command = sqlite.CreateCommand();
        // WAL ile birlikte güvenli ve hızlı; geçici tablolar bellekte
        command.CommandText = "PRAGMA synchronous = NORMAL; PRAGMA temp_store = MEMORY;";
        command.ExecuteNonQuery();
    }
}
