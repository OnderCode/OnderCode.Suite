using OnderCode.Reporting.Export;
using Eczane.WinForms.Pages;

namespace Eczane.WinForms;

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

            using (var sale = new SaleForm(SaleKind.Prescription) { StartPosition = FormStartPosition.Manual, Location = new Point(40, 40) })
            {
                sale.Show(shell);
                await Task.Delay(900);
                await sale.FillSampleAsync();
                await Task.Delay(900);
                ScreenCapture.Capture(sale, dir, "02-receteli-satis");
                sale.Close();
            }

            await ScreenCapture.SnapAsync(shell, dir, "03-satislar", shell.ShowPage("sales"));
            await ScreenCapture.SnapAsync(shell, dir, "04-ilaclar-stok", shell.ShowPage("products"));

            var products = await App.Send(new ListProducts());
            if (products.IsSuccess && products.Value.OrderByDescending(p => p.Sold30).FirstOrDefault() is { } top)
            {
                DetailForm.AutoCapture = (dir, "05-urun-karti");
                await ProductsPage.ShowCardAsync(shell, top.Id);
            }

            await ScreenCapture.SnapAsync(shell, dir, "06-miad-takibi", shell.ShowPage("expiry"));
            await ScreenCapture.SnapAsync(shell, dir, "07-siparis-onerisi", shell.ShowPage("reorder"));
            await ScreenCapture.SnapAsync(shell, dir, "08-ecza-depolari", shell.ShowPage("wholesalers"));
            await ScreenCapture.SnapAsync(shell, dir, "09-hastalar", shell.ShowPage("patients"));

            var patients = await App.Send(new ListPatients());
            if (patients.IsSuccess && patients.Value.Where(p => p.ChronicReport).OrderByDescending(p => p.Prescriptions).FirstOrDefault() is { } patient)
            {
                DetailForm.AutoCapture = (dir, "10-hasta-gecmisi");
                await PatientsPage.ShowHistoryAsync(shell, patient.Id);
            }

            await ScreenCapture.SnapAsync(shell, dir, "11-hastaneler", shell.ShowPage("hospitals"));
            await ScreenCapture.SnapAsync(shell, dir, "12-personel-izin", shell.ShowPage("staff"));

            var duty = (DutyPage)shell.ShowPage("duty");
            await duty.ReloadAsync();
            duty.Calendar.Select(App.Today);
            await ScreenCapture.SnapAsync(shell, dir, "13-nobet-cizelgesi");

            await ScreenCapture.SnapAsync(shell, dir, "14-demirbas", shell.ShowPage("assets"));
            await ScreenCapture.SnapAsync(shell, dir, "15-kurum-faturalari", shell.ShowPage("invoices"));

            var reports = (ReportsPage)shell.ShowPage("reports");
            var samples = Path.Combine(dir, "ornek-raporlar");
            await reports.ShowAsync(ReportsPage.Kind.Sales);
            await ScreenCapture.SnapAsync(shell, dir, "16-rapor-satis");
            reports.SaveAll(samples, "satis-raporu", ReportExporters.Pdf, ReportExporters.Excel, ReportExporters.Csv);
            await reports.ShowAsync(ReportsPage.Kind.Hospitals);
            await ScreenCapture.SnapAsync(shell, dir, "17-rapor-hastaneler");
            reports.SaveAll(samples, "hastane-raporu", ReportExporters.Pdf, ReportExporters.Excel);
            await reports.ShowAsync(ReportsPage.Kind.Products);
            await ScreenCapture.SnapAsync(shell, dir, "18-rapor-urun-abc");
            reports.SaveAll(samples, "urun-abc-raporu", ReportExporters.Pdf, ReportExporters.Excel);
            await reports.ShowAsync(ReportsPage.Kind.Suppliers);
            reports.SaveAll(samples, "ecza-deposu-raporu", ReportExporters.Pdf, ReportExporters.Excel);
            await reports.ShowAsync(ReportsPage.Kind.Staff);
            await ScreenCapture.SnapAsync(shell, dir, "19-rapor-personel-nobet");
            reports.SaveAll(samples, "personel-nobet-raporu", ReportExporters.Pdf, ReportExporters.Excel);
            await reports.ShowAsync(ReportsPage.Kind.Forecast);
            await ScreenCapture.SnapAsync(shell, dir, "20-rapor-tahmin");
            reports.SaveAll(samples, "ciro-tahmini", ReportExporters.Pdf, ReportExporters.Excel);
            await reports.ShowAsync(ReportsPage.Kind.Assets);
            reports.SaveAll(samples, "demirbas-raporu", ReportExporters.Pdf, ReportExporters.Excel);

            OndaTheme.Apply(OndaThemeMode.Dark);
            await ScreenCapture.SnapAsync(shell, dir, "21-koyu-tema", shell.ShowPage("dash"));
            await ScreenCapture.SnapAsync(shell, dir, "22-koyu-nobet", shell.ShowPage("duty"));
            OndaTheme.Apply(OndaThemeMode.Light);
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(dir, "hata.txt"), ex.ToString());
        }
        shell.Close();
    }
}
