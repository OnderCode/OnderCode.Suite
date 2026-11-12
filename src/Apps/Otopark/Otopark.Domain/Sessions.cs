using OnderCode.Kernel.Domain;
using OnderCode.Kernel.Exceptions;
using OnderCode.Kernel.Validation;

namespace Otopark.Domain;

public enum SessionStatus { Active, Closed }

public enum PaymentMethod { Cash, Card, Subscription }

/// <summary>
/// Bir aracın otoparktaki kalışı: giriş, park yeri, çıkış ve tahsilat. Zamanlar otoparkın yerel saatidir.
/// Aynı plaka aynı anda içeride iki kez bulunamaz (uygulama katmanı kontrol eder, veritabanı da benzersiz indeksle korur).
/// </summary>
public sealed class ParkingSession : AuditableEntity
{
    private ParkingSession() { }

    public ParkingSession(string plate, VehicleClass vehicle, ParkingSpot spot, DateTime entryAt, Subscription? subscription = null)
    {
        Plate = TurkishPlate.Normalize(plate) ?? throw new DomainException($"'{plate}' geçerli bir plaka değil.");
        spot.EnsureCanPark(vehicle);
        Vehicle = vehicle;
        Spot = spot;
        SpotId = spot.Id;
        EntryAt = entryAt;
        Status = SessionStatus.Active;
        if (subscription is not null)
        {
            Subscription = subscription;
            SubscriptionId = subscription.Id;
        }
    }

    public string Plate { get; private set; } = "";
    public VehicleClass Vehicle { get; private set; }
    public int SpotId { get; private set; }
    public ParkingSpot Spot { get; private set; } = null!;
    public int? SubscriptionId { get; private set; }
    public Subscription? Subscription { get; private set; }
    public DateTime EntryAt { get; private set; }
    public DateTime? ExitAt { get; private set; }
    public SessionStatus Status { get; private set; }

    /// <summary>Otopark ücreti (yıkama hariç).</summary>
    public decimal ParkingFee { get; private set; }

    /// <summary>Çıkışta tahsil edilen yıkama tutarı (yıkama siparişi bu kalışa bağlıysa).</summary>
    public decimal WashFee { get; private set; }

    public decimal Total => ParkingFee + WashFee;
    public PaymentMethod? Payment { get; private set; }

    /// <summary>Şu anki ya da kapanmış kalış için ücret dökümü.</summary>
    public ParkingFee Quote(DateTime now, bool washed)
        => TariffEngine.Calculate(Tariff.For(Vehicle), EntryAt, ExitAt ?? now, washed, SubscriptionId is not null);

    /// <summary>Çıkış: ücreti sabitler ve kalışı kapatır.</summary>
    public ParkingFee Close(DateTime exitAt, PaymentMethod payment, decimal washFee, bool washed)
    {
        if (Status == SessionStatus.Closed) throw new DomainException($"{Plate} zaten çıkış yapmış.");
        if (exitAt < EntryAt) throw new DomainException("Çıkış saati giriş saatinden önce olamaz.");
        var fee = TariffEngine.Calculate(Tariff.For(Vehicle), EntryAt, exitAt, washed, SubscriptionId is not null);
        ExitAt = exitAt;
        ParkingFee = fee.Total;
        WashFee = washFee;
        Payment = SubscriptionId is not null && Total == 0 ? PaymentMethod.Subscription : payment;
        Status = SessionStatus.Closed;
        return fee;
    }

    /// <summary>Park yeri değiştirme (ör. yanlış yere park edildiyse).</summary>
    public void MoveTo(ParkingSpot spot)
    {
        if (Status == SessionStatus.Closed) throw new DomainException("Çıkış yapmış aracın yeri değiştirilemez.");
        spot.EnsureCanPark(Vehicle);
        Spot = spot;
        SpotId = spot.Id;
    }

    /// <summary>Örnek veri ve içe aktarma için: geçmiş bir kalışı kapalı olarak kurar.</summary>
    public void ImportClosed(DateTime exitAt, decimal parkingFee, decimal washFee, PaymentMethod payment)
    {
        ExitAt = exitAt;
        ParkingFee = parkingFee;
        WashFee = washFee;
        Payment = payment;
        Status = SessionStatus.Closed;
    }
}

/// <summary>Aylık abonelik: abone plaka süre boyunca ücretsiz girer çıkar.</summary>
public sealed class Subscription : FullAuditableEntity
{
    private Subscription() { }

    public Subscription(string plate, string ownerName, VehicleClass vehicle, DateOnly start, int months, decimal monthlyFee)
    {
        Plate = TurkishPlate.Normalize(plate) ?? throw new DomainException($"'{plate}' geçerli bir plaka değil.");
        if (string.IsNullOrWhiteSpace(ownerName)) throw new DomainException("Abone adı boş olamaz.");
        if (months is < 1 or > 12) throw new DomainException("Abonelik 1-12 ay arasında olabilir.");
        OwnerName = ownerName.Trim();
        Vehicle = vehicle;
        StartDate = start;
        EndDate = start.AddMonths(months).AddDays(-1);
        MonthlyFee = monthlyFee;
        TotalPaid = monthlyFee * months;
    }

    public string Plate { get; private set; } = "";
    public string OwnerName { get; private set; } = "";
    public VehicleClass Vehicle { get; private set; }
    public DateOnly StartDate { get; private set; }

    /// <summary>Son geçerli gün (dahil).</summary>
    public DateOnly EndDate { get; private set; }

    public decimal MonthlyFee { get; private set; }
    public decimal TotalPaid { get; private set; }

    public bool IsActiveOn(DateOnly day) => day >= StartDate && day <= EndDate;

    public void Extend(int months)
    {
        if (months is < 1 or > 12) throw new DomainException("Uzatma 1-12 ay arasında olabilir.");
        EndDate = EndDate.AddMonths(months);
        TotalPaid += MonthlyFee * months;
    }

    public static decimal MonthlyFeeFor(VehicleClass vehicle) => vehicle switch
    {
        VehicleClass.Large => 4200m,
        VehicleClass.Motorcycle => 1200m,
        _ => 3000m,
    };
}
