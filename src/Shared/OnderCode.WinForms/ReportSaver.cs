using System.Diagnostics;
using OnderCode.Reporting;
using OnderCode.Reporting.Export;

namespace OnderCode.WinForms;

/// <summary>Raporu kaydetme penceresiyle seçilen biçimde dosyaya yazar ve açar.</summary>
public static class ReportSaver
{
    public static async Task SaveAsync(IWin32Window? owner, ReportDocument? document, IReportExporter exporter)
    {
        if (document is null) { OndaToast.Info("Rapor henüz yüklenmedi."); return; }

        using var dialog = new SaveFileDialog
        {
            Title = "Raporu kaydet",
            Filter = exporter.Filter,
            FileName = ReportExporters.SuggestFileName(document, exporter),
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dialog.ShowDialog(owner) != DialogResult.OK) return;

        try
        {
            await Task.Run(() => ReportExporters.Save(document, dialog.FileName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            OndaToast.Error(ex.Message, "Rapor kaydedilemedi");
            return;
        }
        OndaToast.Success(Path.GetFileName(dialog.FileName), "Rapor kaydedildi");
        try { Process.Start(new ProcessStartInfo(dialog.FileName) { UseShellExecute = true }); } catch { }
    }

    /// <summary>Belgeyi bir klasöre verilen biçimlerde yazar (örnek dosyalar için).</summary>
    public static void SaveAll(ReportDocument document, string directory, string baseName, params IReportExporter[] exporters)
    {
        Directory.CreateDirectory(directory);
        foreach (var e in exporters) ReportExporters.Save(document, Path.Combine(directory, baseName + e.Extension));
    }
}
