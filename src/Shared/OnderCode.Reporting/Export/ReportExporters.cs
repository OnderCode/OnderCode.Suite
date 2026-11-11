using System.Text;

namespace OnderCode.Reporting.Export;

/// <summary>Bir rapor belgesini belirli bir dosya biçimine yazar.</summary>
public interface IReportExporter
{
    /// <summary>Kullanıcıya görünen ad: "Excel", "PDF", "CSV".</summary>
    string Name { get; }

    /// <summary>Uzantı, noktayla: ".xlsx".</summary>
    string Extension { get; }

    /// <summary>Dosya kaydetme penceresi süzgeci.</summary>
    string Filter => $"{Name} dosyası (*{Extension})|*{Extension}";

    void Export(ReportDocument document, Stream output);
}

public static class ReportExporters
{
    public static IReportExporter Excel { get; } = new XlsxExporter();
    public static IReportExporter Pdf { get; } = new PdfExporter();
    public static IReportExporter Csv { get; } = new CsvExporter();

    public static IReadOnlyList<IReportExporter> All { get; } = [Excel, Pdf, Csv];

    public static IReportExporter ForExtension(string extension)
        => All.FirstOrDefault(e => string.Equals(e.Extension, NormalizeExtension(extension), StringComparison.OrdinalIgnoreCase))
           ?? throw new NotSupportedException($"'{extension}' biçiminde rapor yazılamıyor. Desteklenenler: {string.Join(", ", All.Select(e => e.Extension))}");

    /// <summary>Uzantıya göre biçimi seçip dosyaya yazar. Yarım kalan dosya bırakmaz.</summary>
    public static void Save(ReportDocument document, string path)
    {
        var exporter = ForExtension(Path.GetExtension(path));
        var temp = path + ".tmp";
        try
        {
            using (var stream = File.Create(temp)) exporter.Export(document, stream);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    public static byte[] ToBytes(ReportDocument document, IReportExporter exporter)
    {
        using var ms = new MemoryStream();
        exporter.Export(document, ms);
        return ms.ToArray();
    }

    /// <summary>Önerilen dosya adı: "gelir-ve-doluluk-20261111.pdf".</summary>
    public static string SuggestFileName(ReportDocument document, IReportExporter exporter)
        => $"{Slug(document.Title)}-{document.GeneratedAt:yyyyMMdd}{exporter.Extension}";

    internal static string Slug(string text)
    {
        var map = new Dictionary<char, string> { ['ç'] = "c", ['ğ'] = "g", ['ı'] = "i", ['İ'] = "i", ['ö'] = "o", ['ş'] = "s", ['ü'] = "u", ['Ç'] = "c", ['Ğ'] = "g", ['Ö'] = "o", ['Ş'] = "s", ['Ü'] = "u" };
        var sb = new StringBuilder();
        foreach (var ch in text)
        {
            if (map.TryGetValue(ch, out var r)) sb.Append(r);
            else if (char.IsAsciiLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        var slug = sb.ToString().Trim('-');
        return slug.Length == 0 ? "rapor" : slug;
    }

    private static string NormalizeExtension(string ext) => ext.StartsWith('.') ? ext : "." + ext;
}
