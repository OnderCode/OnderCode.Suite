using OnderCode.Kernel.Domain;
using OnderCode.Kernel.Exceptions;

namespace Otopark.Domain;

/// <summary>Araç sınıfı: tarifeyi ve sığabileceği park yerini belirler.</summary>
public enum VehicleClass
{
    Car,         // Otomobil
    Large,       // SUV, hafif ticari, minibüs
    Motorcycle,  // Motosiklet
}

/// <summary>Park yeri türü.</summary>
public enum SpotKind
{
    Standard,
    Disabled,    // Engelli
    Electric,    // Şarj istasyonlu
    Large,       // Büyük araç
    Motorcycle,
}

public sealed class ParkingSpot : AuditableEntity
{
    private ParkingSpot() { }

    public ParkingSpot(string code, string zone, SpotKind kind)
    {
        Code = code;
        Zone = zone;
        Kind = kind;
    }

    /// <summary>"A-07" gibi.</summary>
    public string Code { get; private set; } = "";

    /// <summary>Bölge / kat: "A", "B", "C", "M".</summary>
    public string Zone { get; private set; } = "";

    public SpotKind Kind { get; private set; }
    public bool OutOfService { get; private set; }
    public string? ServiceNote { get; private set; }

    /// <summary>Bu yere hangi sınıf araç park edebilir? Büyük araç yalnızca büyük yere, motosiklet yalnızca motosiklet yerine; otomobil engelli yer dışında her otomobil yerine.</summary>
    public bool Accepts(VehicleClass vehicle) => vehicle switch
    {
        VehicleClass.Motorcycle => Kind == SpotKind.Motorcycle,
        VehicleClass.Large => Kind == SpotKind.Large,
        _ => Kind is SpotKind.Standard or SpotKind.Electric or SpotKind.Disabled or SpotKind.Large,
    };

    /// <summary>Otomatik atamada öncelik: önce aracın kendi türündeki yerler, engelli ve şarj yerleri en sona.</summary>
    public int Preference(VehicleClass vehicle) => (vehicle, Kind) switch
    {
        (VehicleClass.Car, SpotKind.Standard) => 0,
        (VehicleClass.Car, SpotKind.Large) => 1,
        (VehicleClass.Car, SpotKind.Electric) => 2,
        (VehicleClass.Car, SpotKind.Disabled) => 3,
        _ => 0,
    };

    public void TakeOutOfService(string? note)
    {
        OutOfService = true;
        ServiceNote = string.IsNullOrWhiteSpace(note) ? "Bakımda" : note.Trim();
    }

    public void PutInService()
    {
        OutOfService = false;
        ServiceNote = null;
    }

    /// <summary>Aracı bu yere almadan önce kontrol.</summary>
    public void EnsureCanPark(VehicleClass vehicle)
    {
        if (OutOfService) throw new DomainException($"{Code} kullanım dışı: {ServiceNote}");
        if (!Accepts(vehicle)) throw new DomainException($"{Code} bu araç sınıfı için uygun değil.");
    }
}
