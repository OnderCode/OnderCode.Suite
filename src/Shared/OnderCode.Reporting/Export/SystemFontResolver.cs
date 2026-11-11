using System.Collections.Concurrent;
using PdfSharp.Fonts;

namespace OnderCode.Reporting.Export;

/// <summary>
/// PDFsharp'ın platformdan bağımsız sürümü yazı tipini kendisi aramaz. Bu çözücü işletim sistemindeki bir TrueType
/// yazı tipini kullanır: Windows'ta Segoe UI (yoksa Arial), Linux'ta DejaVu Sans ya da Liberation Sans, macOS'ta Arial.
/// Yazı tipi PDF'e gömülür; Türkçe karakterler ve ₺ her bilgisayarda aynı görünür.
/// </summary>
internal sealed class SystemFontResolver : IFontResolver
{
    private const string RegularFace = "OnderCodeSans";
    private const string BoldFace = "OnderCodeSans-Bold";

    private static readonly Lazy<string?> RegularPath = new(() => Find(bold: false));
    private static readonly Lazy<string?> BoldPath = new(() => Find(bold: true));
    private static readonly ConcurrentDictionary<string, byte[]> Cache = new();
    private static readonly Lock Gate = new();

    /// <summary>Uygulama kendi çözücüsünü kurmadıysa bunu kurar. PDFsharp çözücünün bir kez kurulmasına izin verir.</summary>
    public static void EnsureRegistered()
    {
        lock (Gate)
        {
            GlobalFontSettings.FontResolver ??= new SystemFontResolver();
        }
    }

    public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic)
        => isBold && BoldPath.Value is not null
            ? new FontResolverInfo(BoldFace, false, isItalic)
            : new FontResolverInfo(RegularFace, isBold, isItalic);

    public byte[] GetFont(string faceName)
    {
        var path = (faceName == BoldFace ? BoldPath.Value : null) ?? RegularPath.Value
                   ?? throw new InvalidOperationException("PDF için TrueType yazı tipi bulunamadı (Segoe UI, Arial, DejaVu Sans ya da Liberation Sans gerekli).");
        return Cache.GetOrAdd(path, File.ReadAllBytes);
    }

    private static string? Find(bool bold)
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        if (string.IsNullOrEmpty(windows) && OperatingSystem.IsWindows())
            windows = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");

        string[] candidates = bold
            ?
            [
                Path.Combine(windows, "segoeuib.ttf"),
                Path.Combine(windows, "arialbd.ttf"),
                "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
                "/usr/share/fonts/dejavu/DejaVuSans-Bold.ttf",
                "/usr/share/fonts/TTF/DejaVuSans-Bold.ttf",
                "/usr/share/fonts/truetype/liberation/LiberationSans-Bold.ttf",
                "/usr/share/fonts/liberation-sans/LiberationSans-Bold.ttf",
                "/System/Library/Fonts/Supplemental/Arial Bold.ttf",
                "/Library/Fonts/Arial Bold.ttf",
            ]
            :
            [
                Path.Combine(windows, "segoeui.ttf"),
                Path.Combine(windows, "arial.ttf"),
                "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
                "/usr/share/fonts/dejavu/DejaVuSans.ttf",
                "/usr/share/fonts/TTF/DejaVuSans.ttf",
                "/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf",
                "/usr/share/fonts/liberation-sans/LiberationSans-Regular.ttf",
                "/System/Library/Fonts/Supplemental/Arial.ttf",
                "/Library/Fonts/Arial.ttf",
            ];
        return candidates.FirstOrDefault(p => p.Length > 0 && File.Exists(p));
    }
}
