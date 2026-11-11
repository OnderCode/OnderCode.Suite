using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace OnderCode.Data.Conventions;

/// <summary>
/// SQLite'ın decimal türü yok; EF onu metin olarak saklar ve SQL'de toplayamaz, sıralayamaz.
/// Burada tutar 10.000 ile çarpılıp tam sayı saklanır: 1.234,5678 TL → 12345678.
/// Toplama, sıralama ve karşılaştırma veritabanında, kayıpsız çalışır.
/// </summary>
public sealed class DecimalToScaledInt64Converter() : ValueConverter<decimal, long>(
    v => (long)decimal.Round(v * Factor, 0, MidpointRounding.AwayFromZero),
    v => v / Factor)
{
    public const int Scale = 4;
    public const decimal Factor = 10_000m;
}

/// <summary>Denetim zamanları UTC yazılır ve UTC olarak geri okunur.</summary>
public sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
    v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
    v => DateTime.SpecifyKind(v, DateTimeKind.Utc))
{
    public static readonly UtcDateTimeConverter Instance = new();
}
