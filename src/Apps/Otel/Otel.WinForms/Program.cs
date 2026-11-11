using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using OnderCode.Data.Initialization;
using Otel.Infrastructure;

namespace Otel.WinForms;

static class Program
{
    /// <summary>"--shots klasör" ile açılırsa ekran görüntülerini kaydedip kapanır (taze demo veritabanıyla).</summary>
    public static string? ShotsDir { get; private set; }

    [STAThread]
    static void Main(string[] args)
    {
        var si = Array.IndexOf(args, "--shots");
        if (si >= 0 && si + 1 < args.Length) ShotsDir = Path.GetFullPath(args[si + 1]);

        var tr = new CultureInfo("tr-TR");
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = tr;
        System.Windows.Forms.Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        System.Windows.Forms.Application.ThreadException += (_, e) => Report(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Report(e.ExceptionObject as Exception);
        ApplicationConfiguration.Initialize();

        var services = new ServiceCollection();
        services.AddOtel(o =>
        {
            if (ShotsDir is null) return;
            o.DataDirectory = Path.Combine(ShotsDir, "veri");
            if (Directory.Exists(o.DataDirectory)) Directory.Delete(o.DataDirectory, recursive: true);
        });
        var provider = services.BuildServiceProvider();
        App.Services = provider;

        try
        {
            provider.GetRequiredService<DatabaseInitializer<OtelDbContext>>().InitializeAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Report(ex);
            return;
        }
        System.Windows.Forms.Application.Run(new ShellForm());
        provider.Dispose();
    }

    /// <summary>Beklenmeyen hatayı günlüğe yazar ve gösterir.</summary>
    static void Report(Exception? ex)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OnderCode", "Otel");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "hata.log"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\r\n\r\n");
            if (ShotsDir is not null) File.AppendAllText(Path.Combine(ShotsDir, "hata.txt"), ex + "\r\n");
        }
        catch { }
        if (ShotsDir is null)
        {
            try { OndaMessageBox.Error(null, ex?.Message ?? "Bilinmeyen hata", "Beklenmeyen hata"); } catch { }
        }
    }
}
