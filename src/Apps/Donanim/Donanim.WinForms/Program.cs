using System.Globalization;

namespace Donanim.WinForms;

static class Program
{
    /// <summary>"--shots klasör": README/site ekran görüntülerini kaydedip kapanır.</summary>
    public static string? ShotsDir { get; private set; }
    /// <summary>"--video klasör": tanıtım videosunun karelerini kaydedip kapanır.</summary>
    public static string? VideoDir { get; private set; }
    /// <summary>"--tara klasör": bu bilgisayarı tarayıp sayfaların görüntüsünü ve ham bilgiyi kaydeder (geliştirici denetimi).</summary>
    public static string? ScanDir { get; private set; }
    public static bool Automated => ShotsDir is not null || VideoDir is not null || ScanDir is not null;

    [STAThread]
    static void Main(string[] args)
    {
        ShotsDir = ScreenCapture.ShotsDirectory(args);
        var vi = Array.IndexOf(args, "--video");
        if (vi >= 0 && vi + 1 < args.Length) VideoDir = Path.GetFullPath(args[vi + 1]);
        var ti = Array.IndexOf(args, "--tara");
        if (ti >= 0 && ti + 1 < args.Length) ScanDir = Path.GetFullPath(args[ti + 1]);
        var oi = Array.IndexOf(args, "--ornek");
        var start = oi >= 0 ? (oi + 1 < args.Length && args[oi + 1].Contains("oyun", StringComparison.OrdinalIgnoreCase) ? Samples.GamingDesktopName : Samples.OfficeLaptopName) : App.ThisComputer;

        var tr = new CultureInfo("tr-TR");
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = tr;
        System.Windows.Forms.Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        System.Windows.Forms.Application.ThreadException += (_, e) => Report(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Report(e.ExceptionObject as Exception);
        ApplicationConfiguration.Initialize();
        System.Windows.Forms.Application.Run(new ShellForm(start));
    }

    static void Report(Exception? ex)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OnderCode", "Donanim");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "hata.log"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\r\n\r\n");
            if ((ShotsDir ?? VideoDir ?? ScanDir) is { } d) File.AppendAllText(Path.Combine(d, "hata.txt"), ex + "\r\n");
        }
        catch (IOException) { }
        if (!Automated)
        {
            try { OndaMessageBox.Error(null, ex?.Message ?? "Bilinmeyen hata", "Beklenmeyen hata"); } catch (InvalidOperationException) { }
        }
    }
}
