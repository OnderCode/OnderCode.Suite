using OnderCode.Kernel.Domain;
using OnderCode.Kernel.Exceptions;
using OnderCode.Kernel.Validation;

namespace Otopark.Domain;

/// <summary>Yıkama paketi. Fiyat araç sınıfına göre değişir.</summary>
public sealed class WashPackage : Entity
{
    private WashPackage() { }

    public WashPackage(string code, string name, int minutes, decimal carPrice, decimal largePrice, decimal motorcyclePrice, int sortOrder)
    {
        Code = code;
        Name = name;
        Minutes = minutes;
        CarPrice = carPrice;
        LargePrice = largePrice;
        MotorcyclePrice = motorcyclePrice;
        SortOrder = sortOrder;
    }

    public string Code { get; private set; } = "";
    public string Name { get; private set; } = "";

    /// <summary>Ortalama iş süresi; kuyrukta tahmini bitiş saati buradan hesaplanır.</summary>
    public int Minutes { get; private set; }

    public decimal CarPrice { get; private set; }
    public decimal LargePrice { get; private set; }

    /// <summary>0 ise motosiklete uygulanmaz.</summary>
    public decimal MotorcyclePrice { get; private set; }

    public int SortOrder { get; private set; }

    public decimal PriceFor(VehicleClass vehicle) => vehicle switch
    {
        VehicleClass.Large => LargePrice,
        VehicleClass.Motorcycle => MotorcyclePrice > 0 ? MotorcyclePrice : throw new DomainException($"{Name} motosiklete uygulanmaz."),
        _ => CarPrice,
    };
}

/// <summary>Yıkama iş emrinin akışı: Bekliyor → Yıkanıyor → Hazır → Teslim edildi (ya da İptal).</summary>
public enum WashStatus { Waiting, Washing, Ready, Delivered, Cancelled }

public sealed class WashOrder : AuditableEntity
{
    private WashOrder() { }

    public WashOrder(string plate, VehicleClass vehicle, WashPackage package, DateTime createdAt, ParkingSession? session = null, string? note = null)
    {
        Plate = TurkishPlate.Normalize(plate) ?? throw new DomainException($"'{plate}' geçerli bir plaka değil.");
        Vehicle = vehicle;
        Package = package;
        PackageId = package.Id;
        Price = package.PriceFor(vehicle);
        OrderedAt = createdAt;
        Status = WashStatus.Waiting;
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (session is not null)
        {
            if (session.Status != SessionStatus.Active) throw new DomainException("Çıkış yapmış araca yıkama eklenemez.");
            Session = session;
            SessionId = session.Id;
        }
    }

    public string Plate { get; private set; } = "";
    public VehicleClass Vehicle { get; private set; }
    public int PackageId { get; private set; }
    public WashPackage Package { get; private set; } = null!;

    /// <summary>Sipariş anındaki fiyat; paket fiyatı sonradan değişse de bu sipariş etkilenmez.</summary>
    public decimal Price { get; private set; }

    /// <summary>Otoparktaki bir kalışa bağlıysa ücret çıkışta otopark ücretiyle birlikte alınır.</summary>
    public int? SessionId { get; private set; }

    public ParkingSession? Session { get; private set; }
    public WashStatus Status { get; private set; }
    public string? Washer { get; private set; }
    public string? Note { get; private set; }
    public DateTime OrderedAt { get; private set; }
    public DateTime? StartedAt { get; private set; }
    public DateTime? ReadyAt { get; private set; }
    public DateTime? DeliveredAt { get; private set; }

    /// <summary>Kuyruğu bir adım ilerletir.</summary>
    public WashStatus Advance(DateTime now, string? washer = null)
    {
        switch (Status)
        {
            case WashStatus.Waiting:
                Status = WashStatus.Washing;
                StartedAt = now;
                if (!string.IsNullOrWhiteSpace(washer)) Washer = washer.Trim();
                break;
            case WashStatus.Washing:
                Status = WashStatus.Ready;
                ReadyAt = now;
                break;
            case WashStatus.Ready:
                Status = WashStatus.Delivered;
                DeliveredAt = now;
                break;
            default:
                throw new DomainException(Status == WashStatus.Delivered ? $"{Plate} zaten teslim edildi." : $"{Plate} siparişi iptal edilmiş.");
        }
        return Status;
    }

    public void Cancel()
    {
        if (Status is WashStatus.Delivered or WashStatus.Cancelled) throw new DomainException("Teslim edilmiş ya da iptal edilmiş sipariş iptal edilemez.");
        if (Status != WashStatus.Waiting) throw new DomainException("Yıkamaya başlanmış sipariş iptal edilemez.");
        Status = WashStatus.Cancelled;
    }

    /// <summary>Araç çıkarken hazır ama teslim edilmemiş yıkama teslim edilmiş sayılır.</summary>
    public void DeliverWithExit(DateTime now)
    {
        if (Status is WashStatus.Waiting or WashStatus.Washing)
            throw new DomainException($"{Plate} için yıkama henüz bitmedi ({(Status == WashStatus.Waiting ? "sırada bekliyor" : "yıkanıyor")}).");
        if (Status == WashStatus.Ready)
        {
            Status = WashStatus.Delivered;
            DeliveredAt = now;
        }
    }

    /// <summary>Örnek veri için: geçmiş bir siparişi tamamlanmış olarak kurar.</summary>
    public void ImportHistory(string washer, DateTime started, DateTime ready, DateTime? delivered, WashStatus status)
    {
        Washer = washer;
        StartedAt = status == WashStatus.Waiting ? null : started;
        ReadyAt = status is WashStatus.Ready or WashStatus.Delivered ? ready : null;
        DeliveredAt = status == WashStatus.Delivered ? delivered : null;
        Status = status;
    }

    public bool CountsAsRevenue => Status is not WashStatus.Cancelled;
}
