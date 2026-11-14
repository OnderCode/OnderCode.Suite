using OnderCode.Kernel.Domain;
using OnderCode.Kernel.Exceptions;

namespace Sinema.Domain;

/// <summary>Bilet türü (Tam, Öğrenci, Çocuk, 65+...). Belge gerektirenlerde kapıda/gişede kontrol hatırlatılır.</summary>
public sealed class TicketType : Entity
{
    private TicketType() { }

    public TicketType(string name, decimal price, string? description = null, bool requiresProof = false, int sortOrder = 0)
    {
        Update(name, price, description, requiresProof);
        SortOrder = sortOrder;
        IsActive = true;
    }

    public string Name { get; private set; } = "";
    public decimal Price { get; private set; }
    public string? Description { get; private set; }
    /// <summary>Öğrenci belgesi, kimlik vb. gösterilmesi gerekir.</summary>
    public bool RequiresProof { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; }

    public void Update(string name, decimal price, string? description, bool requiresProof)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Bilet türü adı boş olamaz.");
        if (price is < 0 or > 10_000) throw new DomainException("Bilet fiyatı 0 ile 10.000 ₺ arasında olmalı.");
        Name = name.Trim();
        Price = price;
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        RequiresProof = requiresProof;
    }

    public void SetActive(bool active) => IsActive = active;
}

/// <summary>Fiyat kuralları (tek satır): koltuk ve format farkları, halk günü ve matine indirimi.</summary>
public sealed class PricingRules : Entity
{
    public PricingRules() { }

    public decimal VipSurcharge { get; private set; } = 90m;
    public decimal CoupleSurcharge { get; private set; } = 60m;
    public decimal ThreeDSurcharge { get; private set; } = 40m;
    /// <summary>Halk günü (ör. Çarşamba). null ise yok.</summary>
    public DayOfWeek? DiscountDay { get; private set; } = DayOfWeek.Wednesday;
    /// <summary>Halk günü indirimi, yüzde (0-90).</summary>
    public int DiscountPercent { get; private set; } = 40;
    /// <summary>Bu saatten önce başlayan seanslara matine indirimi.</summary>
    public TimeOnly MatineeBefore { get; private set; } = new(13, 0);
    public decimal MatineeDiscount { get; private set; } = 40m;

    public void Update(decimal vip, decimal couple, decimal threeD, DayOfWeek? discountDay, int discountPercent, TimeOnly matineeBefore, decimal matineeDiscount)
    {
        if (vip < 0 || couple < 0 || threeD < 0 || matineeDiscount < 0) throw new DomainException("Fiyat farkları negatif olamaz.");
        if (discountPercent is < 0 or > 90) throw new DomainException("Halk günü indirimi %0-90 arasında olmalı.");
        VipSurcharge = vip;
        CoupleSurcharge = couple;
        ThreeDSurcharge = threeD;
        DiscountDay = discountDay;
        DiscountPercent = discountPercent;
        MatineeBefore = matineeBefore;
        MatineeDiscount = matineeDiscount;
    }
}

/// <summary>Bir biletin fiyat dökümü.</summary>
public sealed record PriceQuote(decimal Base, decimal Discount, string? DiscountReason, decimal SeatSurcharge, decimal FormatSurcharge)
{
    public decimal Total => Math.Max(0, Base - Discount) + SeatSurcharge + FormatSurcharge;
}

/// <summary>
/// Fiyat motoru. Sıra: bilet türü fiyatı → indirim (halk günü ya da matine; hangisi büyükse, ikisi birden uygulanmaz)
/// → koltuk farkı (VIP / çift) → 3D farkı. Kuruşlar tam liraya yuvarlanır.
/// </summary>
public static class PricingEngine
{
    public static PriceQuote Quote(TicketType type, SeatType seat, SessionFormat format, DateTime start, PricingRules rules)
    {
        var basePrice = type.Price;
        decimal discount = 0;
        string? reason = null;
        if (rules.DiscountDay is { } day && start.DayOfWeek == day && rules.DiscountPercent > 0)
        {
            discount = Math.Round(basePrice * rules.DiscountPercent / 100m, 0, MidpointRounding.AwayFromZero);
            reason = $"Halk günü %{rules.DiscountPercent}";
        }
        if (TimeOnly.FromDateTime(start) < rules.MatineeBefore && rules.MatineeDiscount > discount)
        {
            discount = Math.Min(basePrice, rules.MatineeDiscount);
            reason = "Matine";
        }
        var seatSurcharge = seat switch
        {
            SeatType.Vip => rules.VipSurcharge,
            SeatType.Couple => rules.CoupleSurcharge,
            _ => 0m,
        };
        var formatSurcharge = format == SessionFormat.ThreeD ? rules.ThreeDSurcharge : 0m;
        return new PriceQuote(basePrice, Math.Min(discount, basePrice), reason, seatSurcharge, formatSurcharge);
    }
}
