using System.Text.RegularExpressions;

namespace OnderCode.Kernel.Validation;

/// <summary>
/// Türk araç plakası: il kodu (01-81) + 1-3 harf + 2-4 rakam.
/// 1 harf → 4 rakam, 2 harf → 3-4 rakam, 3 harf → 2-3 rakam. Yazım serbesttir ("34abc123", "34-ABC-123", "34 abc 123");
/// <see cref="Normalize"/> hepsini "34 XWQ 123" biçimine getirir.
/// </summary>
public static partial class TurkishPlate
{
    [GeneratedRegex("^(?<il>\\d{2})(?<harf>[A-Z]{1,3})(?<no>\\d{2,4})$")]
    private static partial Regex Pattern();

    /// <summary>Geçerliyse "34 XWQ 123" biçimini, değilse null döner.</summary>
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var compact = string.Concat(value.Where(ch => !char.IsWhiteSpace(ch) && ch is not '-' and not '.')
            .Select(ch => ch switch { 'i' or 'ı' or 'İ' => 'I', _ => char.ToUpperInvariant(ch) }));
        var m = Pattern().Match(compact);
        if (!m.Success) return null;

        var il = int.Parse(m.Groups["il"].Value);
        var harf = m.Groups["harf"].Value;
        var no = m.Groups["no"].Value;
        if (il is < 1 or > 81) return null;
        var ok = harf.Length switch
        {
            1 => no.Length == 4,
            2 => no.Length is 3 or 4,
            _ => no.Length is 2 or 3,
        };
        return ok ? $"{m.Groups["il"].Value} {harf} {no}" : null;
    }

    public static bool IsValid(string? value) => Normalize(value) is not null;
}
