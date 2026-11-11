using OnderCode.Kernel.Domain;

namespace Otel.Domain;

/// <summary>Sezon: tarih aralığında gecelik fiyata çarpan uygular (bitiş günü dahil).</summary>
public sealed class Season : Entity
{
    private Season() { }

    public Season(string name, DateOnly start, DateOnly end, decimal multiplier)
    {
        Name = name;
        Start = start;
        End = end;
        Multiplier = multiplier;
    }

    public string Name { get; private set; } = "";
    public DateOnly Start { get; private set; }
    public DateOnly End { get; private set; }
    public decimal Multiplier { get; private set; }

    public bool Contains(DateOnly date) => date >= Start && date <= End;
}

/// <summary>Fiyat kuralları. Hafta sonu geceleri cuma ve cumartesidir.</summary>
public sealed record PricingPolicy(decimal WeekendMultiplier, int EarlyBookingDays, decimal EarlyBookingRate, int LoyaltyPointsPerNight)
{
    public static PricingPolicy Default { get; } = new(WeekendMultiplier: 1.20m, EarlyBookingDays: 30, EarlyBookingRate: 0.10m, LoyaltyPointsPerNight: 10);
}

public sealed record NightPrice(DateOnly Date, string? SeasonName, decimal SeasonMultiplier, bool IsWeekend, decimal Price);

public sealed record PriceQuote(IReadOnlyList<NightPrice> Nights, decimal Subtotal, decimal EarlyBookingDiscount, decimal Total)
{
    public int NightCount => Nights.Count;
    public decimal AverageNightly => NightCount == 0 ? 0 : decimal.Round(Total / NightCount, 2);
}

/// <summary>
/// Dinamik fiyat: gece başına taban fiyat × sezon çarpanı × (hafta sonuysa) hafta sonu çarpanı;
/// giriş tarihine en az N gün kala yapılan rezervasyona erken rezervasyon indirimi.
/// </summary>
public static class PricingEngine
{
    public static PriceQuote Quote(decimal baseRate, DateOnly checkIn, DateOnly checkOut, DateOnly bookedOn, IEnumerable<Season> seasons, PricingPolicy policy)
    {
        var seasonList = seasons as IReadOnlyCollection<Season> ?? seasons.ToList();
        var nights = new List<NightPrice>();
        for (var d = checkIn; d < checkOut; d = d.AddDays(1))
        {
            // Çakışan sezonlarda en yüksek çarpan geçerli
            var season = seasonList.Where(s => s.Contains(d)).OrderByDescending(s => s.Multiplier).FirstOrDefault();
            var weekend = d.DayOfWeek is DayOfWeek.Friday or DayOfWeek.Saturday;
            var multiplier = (season?.Multiplier ?? 1m) * (weekend ? policy.WeekendMultiplier : 1m);
            nights.Add(new NightPrice(d, season?.Name, season?.Multiplier ?? 1m, weekend, decimal.Round(baseRate * multiplier, 2)));
        }

        var subtotal = nights.Sum(n => n.Price);
        var discount = checkIn.DayNumber - bookedOn.DayNumber >= policy.EarlyBookingDays
            ? decimal.Round(subtotal * policy.EarlyBookingRate, 2)
            : 0m;
        return new PriceQuote(nights, subtotal, discount, subtotal - discount);
    }
}
