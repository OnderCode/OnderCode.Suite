using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using OnderCode.Reporting.Export;
using PdfSharp.Pdf.IO;

namespace OnderCode.Reporting.Tests;

public class ExportTests
{
    private sealed record Row(string Name, DateOnly Day, long Nights, decimal Revenue, double Share);

    private static ReportDocument Sample(int rows = 3, bool landscape = false)
    {
        var data = Enumerable.Range(1, rows).Select(i => new Row(
            i == 1 ? "=HYPERLINK(\"x\")" : i == 2 ? "Şule; \"Çağrı\" İnce" : $"Misafir {i}",
            new DateOnly(2026, 11, 1).AddDays(i % 28), i, 1000.5m * i, 1.0 / rows)).ToList();
        var doc = new ReportDocument
        {
            Title = "Gelir ve Doluluk Raporu",
            Subtitle = "01.11.2026 – 30.11.2026",
            Organization = "OnderCode Otel",
            GeneratedAt = new DateTime(2026, 11, 11, 20, 30, 0),
            Landscape = landscape,
        };
        doc.Kpis.Add(new ReportKpi("Doluluk", ReportFormat.Percent(0.784), "önceki döneme göre +3,1 puan"));
        doc.Kpis.Add(new ReportKpi("ADR", ReportFormat.Money(3120m)));
        doc.Charts.Add(new ReportChart
        {
            Title = "Günlük doluluk",
            Labels = ["01.11", "02.11", "03.11", "04.11"],
            Series = { new ReportSeries("Gerçekleşen", [70, 75, double.NaN, 80]), new ReportSeries("Tahmin", [double.NaN, double.NaN, 78, 82], Dashed: true) },
            MarkerIndex = 2,
            MarkerLabel = "Bugün",
            Min = 0,
            Max = 100,
        });
        doc.Tables.Add(ReportTable.For("Misafirler", data)
            .Text("Misafir", r => r.Name, 24)
            .Date("Giriş", r => r.Day)
            .Integer("Gece", r => r.Nights, total: true)
            .Money("Gelir", r => r.Revenue, total: true)
            .Percent("Pay", r => r.Share, total: true)
            .Build());
        doc.Notes.Add("Tutarlar KDV dahildir.");
        return doc;
    }

    [Fact]
    public void Turkce_bicimler()
    {
        Assert.Equal("1.234,50 ₺", ReportFormat.Money(1234.5m));
        Assert.Equal("%78,4", ReportFormat.Percent(0.784));
        Assert.Equal("-%5,0", ReportFormat.Percent(-0.05));
        Assert.Equal("1,2 Mn", ReportFormat.Compact(1_200_000));
        Assert.Equal("11.11.2026", ReportFormat.Date(new DateOnly(2026, 11, 11)));
    }

    [Fact]
    public void Toplam_satiri_yalnizca_isaretli_sutunlari_toplar()
    {
        var totals = Sample().Tables[0].Totals();
        Assert.Null(totals[0]);
        Assert.Equal(6L, totals[2]);
        Assert.Equal(6003m, totals[3]);
        Assert.Equal(1.0, (double)totals[4]!, 9);
    }

    [Fact]
    public void Dosya_adi_onerisi_turkce_karakterleri_sadelestirir()
        => Assert.Equal("gelir-ve-doluluk-raporu-20261111.pdf", ReportExporters.SuggestFileName(Sample(), ReportExporters.Pdf));

    [Fact]
    public void Uzantiya_gore_bicim_secilir()
    {
        Assert.Same(ReportExporters.Excel, ReportExporters.ForExtension("XLSX"));
        Assert.Same(ReportExporters.Csv, ReportExporters.ForExtension(".csv"));
        Assert.Throws<NotSupportedException>(() => ReportExporters.ForExtension(".docx"));
    }

    [Fact]
    public void Csv_turkce_excel_icin_yazilir()
    {
        var bytes = ReportExporters.ToBytes(Sample(), ReportExporters.Csv);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);

        var lines = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3).Split("\r\n");
        Assert.Equal("Misafir;Giriş;Gece;Gelir;Pay (%)", lines[0]);
        Assert.StartsWith("\"'=HYPERLINK(\"\"x\"\")\";", lines[1]);           // formül enjeksiyonu engellendi
        Assert.StartsWith("\"Şule; \"\"Çağrı\"\" İnce\";", lines[2]);         // ayraç ve tırnak kaçışı
        Assert.EndsWith(";2;2001,00;33,3333", lines[2]);                      // ondalık virgül, yüzde ×100
    }

    [Fact]
    public void Excel_gecerli_parcalar_ve_formuller_icerir()
    {
        var bytes = ReportExporters.ToBytes(Sample(), ReportExporters.Excel);
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);

        foreach (var entry in zip.Entries)
        {
            using var s = entry.Open();
            XDocument.Load(s); // her parça iyi biçimli XML
        }

        var names = Read(zip, "xl/workbook.xml").Descendants().Where(e => e.Name.LocalName == "sheet").Select(e => (string)e.Attribute("name")!).ToList();
        Assert.Equal(new[] { "Özet", "Misafirler", "Grafik - Günlük doluluk" }, names);

        var sheet = Read(zip, "xl/worksheets/sheet2.xml").ToString();
        Assert.Contains("SUBTOTAL(109,C5:C7)", sheet);
        Assert.Contains("SUBTOTAL(109,D5:D7)", sheet);
        Assert.Contains("<autoFilter ref=\"A4:E7\"", sheet.Replace(" xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"", ""));
        Assert.Contains("state=\"frozen\"", sheet);
        Assert.Contains("1000.5", sheet);   // para gerçek sayı olarak yazılır
        Assert.Contains("Şule; \"Çağrı\" İnce", Read(zip, "xl/worksheets/sheet2.xml").Descendants().Where(e => e.Name.LocalName == "t").Select(e => e.Value));
    }

    [Fact]
    public void Excel_sayfa_adlari_kisaltilir_ve_benzersiz_olur()
    {
        var doc = new ReportDocument { Title = "Uzun" };
        var rows = new[] { 1 };
        doc.Tables.Add(ReportTable.For("Çok uzun bir tablo adı: [Kasım/Aralık] dönemi", rows).Integer("Sayı", x => x).Build());
        doc.Tables.Add(ReportTable.For("Çok uzun bir tablo adı: [Kasım/Aralık] dönemi", rows).Integer("Sayı", x => x).Build());

        using var zip = new ZipArchive(new MemoryStream(ReportExporters.ToBytes(doc, ReportExporters.Excel)), ZipArchiveMode.Read);
        var names = Read(zip, "xl/workbook.xml").Descendants().Where(e => e.Name.LocalName == "sheet").Select(e => (string)e.Attribute("name")!).ToList();

        Assert.Equal(2, names.Count);
        Assert.All(names, n => Assert.True(n.Length <= 31 && n.IndexOfAny([':', '/', '[', ']']) < 0, n));
        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Pdf_uretilir_ve_uzun_tablo_sayfalara_bolunur()
    {
        var shortPdf = ReportExporters.ToBytes(Sample(), ReportExporters.Pdf);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(shortPdf, 0, 4));
        Assert.Equal(1, PageCount(shortPdf));

        var longPdf = ReportExporters.ToBytes(Sample(rows: 180), ReportExporters.Pdf);
        Assert.True(PageCount(longPdf) >= 4);
    }

    [Fact]
    public void Pdf_yatay_sayfa()
    {
        using var pdf = PdfReader.Open(new MemoryStream(ReportExporters.ToBytes(Sample(landscape: true), ReportExporters.Pdf)), PdfDocumentOpenMode.Import);
        Assert.True(pdf.Pages[0].Width > pdf.Pages[0].Height);
    }

    [Fact]
    public void Dosyaya_kaydetme_yarim_dosya_birakmaz()
    {
        var dir = Path.Combine(Path.GetTempPath(), "oc-report-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "rapor.xlsx");
            ReportExporters.Save(Sample(), path);
            Assert.True(new FileInfo(path).Length > 1000);
            Assert.Single(Directory.GetFiles(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static int PageCount(byte[] pdf)
    {
        using var doc = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Import);
        return doc.PageCount;
    }

    private static XDocument Read(ZipArchive zip, string path)
    {
        using var s = zip.GetEntry(path)!.Open();
        return XDocument.Load(s);
    }
}
