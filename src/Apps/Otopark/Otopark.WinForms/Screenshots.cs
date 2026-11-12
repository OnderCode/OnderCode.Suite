using OnderCode.Reporting.Export;
using Otopark.Application;
using Otopark.Domain;
using Otopark.WinForms.Pages;

namespace Otopark.WinForms;

/// <summary>README ve web sitesi için ekran görüntüleri ve örnek raporlar (--shots klasör). Her çalıştırmada taze demo verisi kullanılır.</summary>
static class Screenshots
{
    public static async void Run(ShellForm shell, string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            shell.WindowState = FormWindowState.Normal;
            shell.Bounds = new Rectangle(10, 10, 1440, 900);

            await ScreenCapture.SnapAsync(shell, dir, "01-gosterge-paneli", shell.ShowPage("dash"));

            var lot = (LotPage)shell.ShowPage("lot");
            await lot.ReloadAsync();
            var board = await App.Send(new GetLotBoard());
            IReadOnlyList<SpotTile> tiles = board.IsSuccess ? board.Value : Array.Empty<SpotTile>();
            if ((tiles.FirstOrDefault(t => t.SessionId is not null && t.Wash is not null) ?? tiles.FirstOrDefault(t => t.SessionId is not null)) is { } occupied)
                lot.Map.Select(occupied.SpotId);
            await ScreenCapture.SnapAsync(shell, dir, "02-otopark-haritasi");

            await ScreenCapture.SnapAsync(shell, dir, "03-arac-hareketleri", shell.ShowPage("sessions"));

            var wash = (WashPage)shell.ShowPage("wash");
            await wash.ReloadAsync();
            var cards = await App.Send(new GetWashBoard());
            if (cards.IsSuccess && cards.Value.FirstOrDefault(c => c.Status == WashStatus.Washing) is { } washing) wash.Board.Select(washing.Id);
            await ScreenCapture.SnapAsync(shell, dir, "04-yikama-kuyrugu");

            await ScreenCapture.SnapAsync(shell, dir, "05-abonelikler", shell.ShowPage("subs"));

            var reports = (ReportsPage)shell.ShowPage("reports");
            var samples = Path.Combine(dir, "ornek-raporlar");
            await reports.ShowAsync(ReportsPage.Kind.Revenue);
            await ScreenCapture.SnapAsync(shell, dir, "06-rapor-gelir");
            reports.SaveAll(samples, "gelir-raporu", ReportExporters.Pdf, ReportExporters.Excel, ReportExporters.Csv);
            await reports.ShowAsync(ReportsPage.Kind.PeakHours);
            await ScreenCapture.SnapAsync(shell, dir, "07-rapor-yogunluk");
            reports.SaveAll(samples, "yogunluk-saatleri", ReportExporters.Pdf, ReportExporters.Excel);
            await reports.ShowAsync(ReportsPage.Kind.Forecast);
            await ScreenCapture.SnapAsync(shell, dir, "08-rapor-tahmin");
            reports.SaveAll(samples, "ciro-tahmini", ReportExporters.Pdf, ReportExporters.Excel);

            using (var entry = new EntryForm { StartPosition = FormStartPosition.Manual, Location = new Point(60, 60) })
            {
                entry.Show(shell);
                await Task.Delay(800);
                await entry.FillSampleAsync();
                await Task.Delay(500);
                ScreenCapture.Capture(entry, dir, "09-arac-girisi");
                entry.Close();
            }

            var exitTile = tiles.FirstOrDefault(t => t.SessionId is not null && !t.IsSubscriber && t.Wash == WashStatus.Ready)
                ?? tiles.FirstOrDefault(t => t.SessionId is not null && !t.IsSubscriber && t.Wash is null);
            if (exitTile is { SessionId: int sessionId })
            {
                using var exit = new ExitForm(sessionId) { StartPosition = FormStartPosition.Manual, Location = new Point(60, 60) };
                await exit.LoadAsync();
                exit.Show(shell);
                await Task.Delay(700);
                ScreenCapture.Capture(exit, dir, "10-cikis-tahsilat");
                exit.Close();
            }

            OndaTheme.Apply(OndaThemeMode.Dark);
            await ScreenCapture.SnapAsync(shell, dir, "11-koyu-tema", shell.ShowPage("lot"));
            await ScreenCapture.SnapAsync(shell, dir, "12-koyu-yikama", shell.ShowPage("wash"));
            OndaTheme.Apply(OndaThemeMode.Light);
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(dir, "hata.txt"), ex.ToString());
        }
        shell.Close();
    }
}
