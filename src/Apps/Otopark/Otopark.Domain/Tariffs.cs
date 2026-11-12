namespace Otopark.Domain;

/// <summary>
/// Saatlik tarife. İlk <see cref="GraceMinutes"/> dakika ücretsiz, ardından ilk saat <see cref="FirstHour"/>,
/// başlayan her ek saat <see cref="ExtraHour"/>. Her 24 saatlik dilimin ücreti <see cref="DailyCap"/>'i geçmez.
/// </summary>
public sealed record Tariff(VehicleClass Vehicle, decimal FirstHour, decimal ExtraHour, decimal DailyCap, int GraceMinutes = 15)
{
    public static readonly IReadOnlyList<Tariff> Defaults =
    [
        new(VehicleClass.Car, 60m, 40m, 350m),
        new(VehicleClass.Large, 90m, 60m, 500m),
        new(VehicleClass.Motorcycle, 30m, 20m, 150m),
    ];

    public static Tariff For(VehicleClass vehicle) => Defaults.First(t => t.Vehicle == vehicle);
}

/// <summary>Ücret dökümü: ekranda ve fişte satır satır gösterilir.</summary>
public sealed record ParkingFee(
    TimeSpan Duration,
    int BilledHours,
    int FullDays,
    decimal Gross,
    decimal WashDiscount,
    decimal SubscriptionDiscount,
    decimal Total,
    string Explanation);

/// <summary>Tarife motoru. Saf fonksiyon: zaman ve kurallar dışarıdan verilir, test edilmesi kolaydır.</summary>
public static class TariffEngine
{
    /// <summary>Yıkama yaptıran araca bu kadar dakika otopark ücretsizdir.</summary>
    public const int WashFreeMinutes = 120;

    public static ParkingFee Calculate(Tariff tariff, DateTime entry, DateTime exit, bool washed = false, bool subscriber = false)
    {
        if (exit < entry) throw new ArgumentException("Çıkış girişten önce olamaz.", nameof(exit));
        var duration = exit - entry;

        var gross = Price(tariff, duration, out var billedHours, out var fullDays);

        // Yıkama yaptırana ilk 2 saat ücretsiz: süreden düşülerek yeniden hesaplanır
        var washDiscount = 0m;
        if (washed && gross > 0)
        {
            var reduced = duration - TimeSpan.FromMinutes(WashFreeMinutes);
            washDiscount = gross - (reduced <= TimeSpan.Zero ? 0 : Price(tariff, reduced, out _, out _));
        }
        var afterWash = gross - washDiscount;
        var subscription = subscriber ? afterWash : 0m;
        var total = afterWash - subscription;

        var explanation = subscriber ? "Abone aracı: ücret alınmaz"
            : duration.TotalMinutes <= tariff.GraceMinutes ? $"İlk {tariff.GraceMinutes} dakika ücretsiz"
            : fullDays > 0 ? $"{fullDays} gün × {tariff.DailyCap:N0} ₺ + {billedHours} saat"
            : $"{billedHours} saat (ilk saat {tariff.FirstHour:N0} ₺, sonraki saatler {tariff.ExtraHour:N0} ₺)";
        if (washDiscount > 0) explanation += $" · yıkama: ilk {WashFreeMinutes / 60} saat ücretsiz";

        return new ParkingFee(duration, billedHours, fullDays, gross, washDiscount, subscription, total, explanation);
    }

    private static decimal Price(Tariff t, TimeSpan duration, out int billedHours, out int fullDays)
    {
        billedHours = 0;
        fullDays = 0;
        if (duration.TotalMinutes <= t.GraceMinutes) return 0m;

        fullDays = (int)(duration.TotalHours / 24);
        var rest = duration - TimeSpan.FromDays(fullDays);
        var hours = (int)Math.Ceiling(rest.TotalHours - 1e-9);
        billedHours = hours;
        var restPrice = hours == 0 ? 0m : Math.Min(t.DailyCap, t.FirstHour + (hours - 1) * t.ExtraHour);
        // Tam günden sonra kalan süre için "ilk saat" yeniden başlamaz: ek saat fiyatı uygulanır
        if (fullDays > 0 && hours > 0) restPrice = Math.Min(t.DailyCap, hours * t.ExtraHour);
        return fullDays * t.DailyCap + restPrice;
    }
}
