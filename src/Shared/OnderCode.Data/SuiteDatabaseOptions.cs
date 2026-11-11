using Microsoft.Data.Sqlite;

namespace OnderCode.Data;

/// <summary>Veritabanı dosyasının yeri ve davranışı. Hiçbiri zorunlu değil.</summary>
public sealed class SuiteDatabaseOptions
{
    /// <summary>Uygulama adı: klasör ve dosya adı bundan üretilir (ör. "Otel").</summary>
    public string AppName { get; set; } = "OnderCode";

    /// <summary>Boşsa {AppName}.db</summary>
    public string? FileName { get; set; }

    /// <summary>
    /// Boşsa %LocalAppData%\OnderCode\{AppName}. Uygulama klasöründe "portable" adlı bir dosya varsa
    /// veritabanı exe'nin yanındaki Data klasöründe tutulur (USB'den çalışan demo için).
    /// </summary>
    public string? DataDirectory { get; set; }

    /// <summary>SQL komutlarını Visual Studio çıktı penceresine yazar (geliştirme için).</summary>
    public bool LogSqlToDebug { get; set; }

    public string ResolveDatabasePath()
    {
        var dir = DataDirectory;
        if (string.IsNullOrWhiteSpace(dir))
        {
            dir = File.Exists(Path.Combine(AppContext.BaseDirectory, "portable"))
                ? Path.Combine(AppContext.BaseDirectory, "Data")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OnderCode", AppName);
        }
        return Path.Combine(dir, string.IsNullOrWhiteSpace(FileName) ? AppName + ".db" : FileName);
    }

    public string BuildConnectionString() => new SqliteConnectionStringBuilder
    {
        DataSource = ResolveDatabasePath(),
        ForeignKeys = true,
        Pooling = true,
        DefaultTimeout = 30,
    }.ToString();
}
