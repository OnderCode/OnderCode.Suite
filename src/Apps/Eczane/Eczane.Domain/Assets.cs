using OnderCode.Kernel.Domain;
using OnderCode.Kernel.Exceptions;

namespace Eczane.Domain;

public enum AssetCategory { Furniture, Computer, ColdChain, Device, Security, Other }

public enum AssetStatus { InUse, InRepair, Retired }

/// <summary>
/// Demirbaş. Amortisman doğrusal yöntemle aylık hesaplanır: (maliyet − hurda değeri) / (ömür × 12), alındığı aydan başlar.
/// Bakım periyodu olanlarda (soğuk zincir dolabı, terazi, tansiyon aleti) bir sonraki bakım tarihi izlenir.
/// </summary>
public sealed class Asset : FullAuditableEntity
{
    private Asset() { }

    public Asset(string code, string name, AssetCategory category, DateOnly purchaseDate, decimal cost, int usefulLifeYears,
        string location, decimal salvageValue = 0m, int maintenanceIntervalMonths = 0, string? serialNo = null)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new DomainException("Demirbaş kodu boş olamaz.");
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Demirbaş adı boş olamaz.");
        if (cost <= 0) throw new DomainException("Maliyet sıfırdan büyük olmalı.");
        if (usefulLifeYears is < 1 or > 50) throw new DomainException("Faydalı ömür 1-50 yıl arasında olmalı.");
        if (salvageValue < 0 || salvageValue >= cost) throw new DomainException("Hurda değeri maliyetten küçük olmalı.");
        if (maintenanceIntervalMonths is < 0 or > 60) throw new DomainException("Bakım periyodu 0-60 ay arasında olmalı.");
        Code = code.Trim().ToUpperInvariant();
        Name = name.Trim();
        Category = category;
        PurchaseDate = purchaseDate;
        Cost = cost;
        UsefulLifeYears = usefulLifeYears;
        SalvageValue = salvageValue;
        Location = location.Trim();
        SerialNo = string.IsNullOrWhiteSpace(serialNo) ? null : serialNo.Trim();
        MaintenanceIntervalMonths = maintenanceIntervalMonths;
        NextMaintenance = maintenanceIntervalMonths > 0 ? purchaseDate.AddMonths(maintenanceIntervalMonths) : null;
        Status = AssetStatus.InUse;
    }

    public string Code { get; private set; } = "";
    public string Name { get; private set; } = "";
    public AssetCategory Category { get; private set; }
    public DateOnly PurchaseDate { get; private set; }
    public decimal Cost { get; private set; }
    public int UsefulLifeYears { get; private set; }
    public decimal SalvageValue { get; private set; }
    public string Location { get; private set; } = "";
    public string? SerialNo { get; private set; }
    public AssetStatus Status { get; private set; }
    public DateOnly? RetiredOn { get; private set; }
    public int MaintenanceIntervalMonths { get; private set; }
    public DateOnly? NextMaintenance { get; private set; }

    /// <summary>Amortismana tabi aylık tutar.</summary>
    public decimal MonthlyDepreciation => Math.Round((Cost - SalvageValue) / (UsefulLifeYears * 12m), 2);

    /// <summary>Verilen güne kadar ayrılmış birikmiş amortisman (alış ayı dahil, ömrü dolunca durur, hurdaya ayrılınca donar).</summary>
    public decimal AccumulatedDepreciation(DateOnly asOf)
    {
        var until = RetiredOn is { } r && r < asOf ? r : asOf;
        if (until < PurchaseDate) return 0m;
        var months = (until.Year - PurchaseDate.Year) * 12 + until.Month - PurchaseDate.Month + 1;
        months = Math.Min(months, UsefulLifeYears * 12);
        return months >= UsefulLifeYears * 12 ? Cost - SalvageValue : Math.Min(Cost - SalvageValue, MonthlyDepreciation * months);
    }

    public decimal BookValue(DateOnly asOf) => Cost - AccumulatedDepreciation(asOf);

    /// <summary>Takvim yılı içinde ayrılan amortisman.</summary>
    public decimal DepreciationInYear(int year)
        => AccumulatedDepreciation(new DateOnly(year, 12, 31)) - AccumulatedDepreciation(new DateOnly(year - 1, 12, 31));

    public bool IsFullyDepreciated(DateOnly asOf) => AccumulatedDepreciation(asOf) >= Cost - SalvageValue;

    public bool MaintenanceDue(DateOnly asOf, int withinDays = 0)
        => Status != AssetStatus.Retired && NextMaintenance is { } n && n.DayNumber - asOf.DayNumber <= withinDays;

    public void Move(string location)
    {
        if (string.IsNullOrWhiteSpace(location)) throw new DomainException("Konum boş olamaz.");
        Location = location.Trim();
    }

    public void SendToRepair()
    {
        if (Status == AssetStatus.Retired) throw new DomainException("Hurdaya ayrılmış demirbaş tamire gönderilemez.");
        Status = AssetStatus.InRepair;
    }

    public void BackInService()
    {
        if (Status == AssetStatus.Retired) throw new DomainException("Hurdaya ayrılmış demirbaş kullanıma alınamaz.");
        Status = AssetStatus.InUse;
    }

    public void Retire(DateOnly on)
    {
        if (on < PurchaseDate) throw new DomainException("Hurdaya ayırma tarihi alış tarihinden önce olamaz.");
        Status = AssetStatus.Retired;
        RetiredOn = on;
        NextMaintenance = null;
    }

    /// <summary>Bakım kaydı işlendiğinde bir sonraki bakım tarihi ileri alınır.</summary>
    internal void Maintained(DateOnly on)
    {
        if (MaintenanceIntervalMonths > 0) NextMaintenance = on.AddMonths(MaintenanceIntervalMonths);
    }
}

public enum MaintenanceKind { Maintenance, Calibration, Repair }

public sealed class MaintenanceRecord : AuditableEntity
{
    private MaintenanceRecord() { }

    public MaintenanceRecord(Asset asset, DateOnly date, MaintenanceKind kind, decimal cost, string? note = null)
    {
        if (cost < 0) throw new DomainException("Bakım tutarı negatif olamaz.");
        if (asset.Status == AssetStatus.Retired) throw new DomainException("Hurdaya ayrılmış demirbaşa bakım girilemez.");
        if (date < asset.PurchaseDate) throw new DomainException("Bakım tarihi alış tarihinden önce olamaz.");
        Asset = asset;
        AssetId = asset.Id;
        Date = date;
        Kind = kind;
        Cost = cost;
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (kind != MaintenanceKind.Repair) asset.Maintained(date);
        else if (asset.Status == AssetStatus.InRepair) asset.BackInService();
    }

    public int AssetId { get; private set; }
    public Asset Asset { get; private set; } = null!;
    public DateOnly Date { get; private set; }
    public MaintenanceKind Kind { get; private set; }
    public decimal Cost { get; private set; }
    public string? Note { get; private set; }
}
