using System.Globalization;
using System.Text;

namespace OnderCode.Kernel.Text;

/// <summary>Türkçe metin yardımcıları. Veri katmanı bunları SQLite'a fonksiyon ve sıralama kuralı olarak kaydeder.</summary>
public static class TurkishText
{
    private static readonly CompareInfo Turkish = CultureInfo.GetCultureInfo("tr-TR").CompareInfo;

    /// <summary>Türk alfabesine göre, büyük/küçük harf duyarsız karşılaştırma (Ç, C'den sonra; İ ile i aynı).</summary>
    public static int Compare(string? x, string? y) => Turkish.Compare(x, y, CompareOptions.IgnoreCase);

    /// <summary>
    /// Arama anahtarı: küçük harf ve Türkçe karakterler sadeleşmiş hâli.
    /// "Şanlıurfa", "SANLIURFA" ve "sanliurfa" aynı anahtarı üretir.
    /// </summary>
    public static string Fold(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            sb.Append(ch switch
            {
                'I' or 'ı' or 'İ' or 'i' or 'Î' or 'î' => 'i',
                'Ç' or 'ç' => 'c',
                'Ğ' or 'ğ' => 'g',
                'Ö' or 'ö' => 'o',
                'Ş' or 'ş' => 's',
                'Ü' or 'ü' or 'Û' or 'û' => 'u',
                'Â' or 'â' => 'a',
                _ => char.ToLowerInvariant(ch),
            });
        }
        return sb.ToString();
    }
}
