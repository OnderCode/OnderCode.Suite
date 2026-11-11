namespace OnderCode.Kernel.Validation;

/// <summary>T.C. kimlik no, vergi kimlik no ve IBAN kontrol algoritmaları. Arayüzden ve sunucudan aynı kural çağrılır.</summary>
public static class TurkishIdentity
{
    /// <summary>11 hane, ilk hane 0 değil, 10. ve 11. hane kontrol basamakları tutuyor.</summary>
    public static bool IsValidTckn(string? value)
    {
        var s = value?.Trim();
        if (s is not { Length: 11 } || !s.All(char.IsAsciiDigit) || s[0] == '0') return false;
        var d = s.Select(ch => ch - '0').ToArray();
        var odd = d[0] + d[2] + d[4] + d[6] + d[8];
        var even = d[1] + d[3] + d[5] + d[7];
        if (((odd * 7 - even) % 10 + 10) % 10 != d[9]) return false;
        return d.Take(10).Sum() % 10 == d[10];
    }

    /// <summary>Vergi kimlik no: 10 hane, Gelir İdaresi kontrol algoritması.</summary>
    public static bool IsValidVkn(string? value)
    {
        var s = value?.Trim();
        if (s is not { Length: 10 } || !s.All(char.IsAsciiDigit)) return false;
        var d = s.Select(ch => ch - '0').ToArray();
        var sum = 0;
        for (var i = 0; i < 9; i++)
        {
            var tmp = (d[i] + 9 - i) % 10;
            var v = tmp * (1 << (9 - i)) % 9;
            if (tmp != 0 && v == 0) v = 9;
            sum += v;
        }
        return (10 - sum % 10) % 10 == d[9];
    }

    /// <summary>Boşluksuz, büyük harf IBAN.</summary>
    public static string CompactIban(string? value) =>
        value is null ? string.Empty : new string(value.Where(char.IsAsciiLetterOrDigit).ToArray()).ToUpperInvariant();

    /// <summary>ISO 13616 mod-97 kontrolü. TR IBAN'ları 26 karakterdir.</summary>
    public static bool IsValidIban(string? value)
    {
        var x = CompactIban(value);
        if (x.Length is < 15 or > 34 || !char.IsAsciiLetter(x[0]) || !char.IsAsciiLetter(x[1])) return false;
        if (x.StartsWith("TR", StringComparison.Ordinal) && x.Length != 26) return false;
        var m = 0;
        foreach (var ch in x[4..] + x[..4])
            m = ch is >= '0' and <= '9' ? (m * 10 + (ch - '0')) % 97 : (m * 100 + (ch - 'A' + 10)) % 97;
        return m == 1;
    }
}
