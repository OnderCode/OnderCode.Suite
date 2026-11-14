using Donanim.WinForms.Pages;
using OnderCode.Reporting.Export;

namespace Donanim.WinForms;

/// <summary>README ve site için ekran görüntüleri (--shots klasör). Yalnızca kurgusal örnek bilgisayarlar kullanılır.</summary>
static class Screenshots
{
    public static async void Run(ShellForm shell, string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            shell.WindowState = FormWindowState.Normal;
            shell.Bounds = new Rectangle(10, 10, 1440, 900);

            await App.LoadAsync(Samples.OfficeLaptopName);
            await Snap(shell, dir, "01-karne", "karne");
            await Snap(shell, dir, "02-bellek-dizustu", "memory", s => ((MemoryPage)s).Board.SetHover(1));
            await Snap(shell, dir, "03-depolama", "storage");
            await Snap(shell, dir, "04-pil-sistem", "system");
            await Snap(shell, dir, "05-oneriler", "advice");
            ReportSaver.SaveAll(KarneReport.Build(App.Karne!), Path.Combine(dir, "ornek-rapor"), "donanim-karnesi-ofis-dizustu", ReportExporters.Pdf, ReportExporters.Excel);

            await App.LoadAsync(Samples.GamingDesktopName);
            await Snap(shell, dir, "06-karne-oyun", "karne");
            await Snap(shell, dir, "07-bellek-oyun", "memory", s => ((MemoryPage)s).Board.SetHover(1));
            await Snap(shell, dir, "08-islemci-anakart", "cpu", delay: 4000);
            await Snap(shell, dir, "09-ekran-karti", "graphics");
            ReportSaver.SaveAll(KarneReport.Build(App.Karne!), Path.Combine(dir, "ornek-rapor"), "donanim-karnesi-oyun", ReportExporters.Pdf);

            OndaTheme.Apply(OndaThemeMode.Dark);
            await Snap(shell, dir, "10-koyu-karne", "karne");
            await Snap(shell, dir, "11-koyu-bellek", "memory", s => ((MemoryPage)s).Board.SetHover(3));
            await App.LoadAsync(Samples.OfficeLaptopName);
            await Snap(shell, dir, "12-koyu-oneriler", "advice");
            OndaTheme.Apply(OndaThemeMode.Light);
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(dir, "hata.txt"), ex.ToString());
        }
        shell.Close();
    }

    /// <summary>Bu bilgisayarın gerçek taramasını denetim için kaydeder (yayınlanmaz).</summary>
    public static async void RunReal(ShellForm shell, string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            shell.WindowState = FormWindowState.Normal;
            shell.Bounds = new Rectangle(10, 10, 1440, 900);
            await App.LoadAsync(App.ThisComputer);
            File.WriteAllText(Path.Combine(dir, "tarama.json"), System.Text.Json.JsonSerializer.Serialize(App.Snapshot, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            await Snap(shell, dir, "r1-karne", "karne");
            await Snap(shell, dir, "r2-bellek", "memory");
            await Snap(shell, dir, "r3-islemci", "cpu", delay: 3500);
            await Snap(shell, dir, "r4-depolama", "storage");
            await Snap(shell, dir, "r5-ekran", "graphics");
            await Snap(shell, dir, "r6-sistem", "system");
            await Snap(shell, dir, "r7-oneriler", "advice");
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(dir, "hata.txt"), ex.ToString());
        }
        shell.Close();
    }

    static async Task Snap(ShellForm shell, string dir, string name, string page, Action<KarnePage>? after = null, int delay = 1600)
    {
        var p = shell.ShowPage(page);
        await p.ReloadAsync();
        await Task.Delay(delay);
        after?.Invoke(p);
        await Task.Delay(200);
        ScreenCapture.Capture(shell, dir, name);
    }
}
