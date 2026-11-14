namespace Donanim.Core;

/// <summary>Bir bilgisayarın tek anlık donanım görüntüsü. Seri numarası, MAC adresi gibi kimlik bilgileri tutulmaz.</summary>
public sealed record HardwareSnapshot
{
    public required string Source { get; init; }
    public bool IsSample { get; init; }
    public DateTime CapturedAt { get; init; } = DateTime.Now;
    public required MachineInfo Machine { get; init; }
    public required CpuInfo Cpu { get; init; }
    public required BoardInfo Board { get; init; }
    public required MemoryInfo Memory { get; init; }
    public IReadOnlyList<DiskInfo> Disks { get; init; } = [];
    public IReadOnlyList<GpuInfo> Gpus { get; init; } = [];
    public IReadOnlyList<MonitorInfo> Monitors { get; init; } = [];
    public BatteryInfo? Battery { get; init; }
    public required OsInfo Os { get; init; }
    public SecurityInfo Security { get; init; } = new(null, null, null);
    public IReadOnlyList<NetworkInfo> Network { get; init; } = [];
    /// <summary>Okunamayan bölümler (ör. yönetici izni gerekenler).</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    public DiskInfo? SystemDisk => Disks.FirstOrDefault(d => d.IsSystem) ?? Disks.FirstOrDefault();
    public GpuInfo? MainGpu => Gpus.OrderByDescending(g => g.IsIntegrated ? 0 : 1).ThenByDescending(g => g.VramGb ?? 0).FirstOrDefault();
    public bool HasDedicatedGpu => Gpus.Any(g => !g.IsIntegrated);
    /// <summary>Ekranı süren kartın çözünürlüğü (Optimus dizüstülerde harici kart "-" bildirir).</summary>
    public string DisplayResolution => Gpus.Select(g => g.Resolution).FirstOrDefault(r => r != "-") ?? "-";
    public int DisplayRefreshHz => Gpus.Where(g => g.Resolution != "-").Select(g => g.RefreshHz).FirstOrDefault();
}

public sealed record MachineInfo(string Manufacturer, string Model, bool IsLaptop, string? ComputerName = null)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Model) ? Manufacturer : Model.StartsWith(Manufacturer, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(Manufacturer) ? Model : $"{Manufacturer} {Model}";
    public string KindText => IsLaptop ? "Dizüstü" : "Masaüstü";
}

public sealed record CpuInfo(string Name, string Manufacturer, int Cores, int Threads, int MaxClockMhz, int L2CacheKb, int L3CacheKb, string Socket, bool? Virtualization)
{
    /// <summary>"12th Gen Intel Core i5-12600HX" → "Intel Core i5-12600HX".</summary>
    public string ShortName => System.Text.RegularExpressions.Regex.Replace(Name, @"^\d+(st|nd|rd|th) Gen ", "").Replace(" Processor", "").Replace(" CPU", "").Trim();
    public string ClockText => MaxClockMhz > 0 ? $"{MaxClockMhz / 1000.0:0.0#} GHz" : "-";
    public string CoreText => $"{Cores} çekirdek · {Threads} iş parçacığı";
}

public sealed record BoardInfo(string Manufacturer, string Product, string BiosVendor, string BiosVersion, DateOnly? BiosDate)
{
    public string DisplayName => string.Join(" ", new[] { Manufacturer, Product }.Where(s => !string.IsNullOrWhiteSpace(s)));
}

public enum MemoryType { Unknown, Ddr2, Ddr3, Ddr4, Ddr5, Lpddr3, Lpddr4, Lpddr5 }
public enum MemoryFormFactor { Unknown, Dimm, SoDimm, Soldered }

public sealed record MemoryModule(string Manufacturer, string PartNumber, double CapacityGb, MemoryType Type, MemoryFormFactor FormFactor, int RatedMts, int ConfiguredMts)
{
    public string TypeText => Formats.MemoryType(Type);
    public string SpeedText => ConfiguredMts > 0 && RatedMts > 0 && ConfiguredMts != RatedMts
        ? $"{ConfiguredMts} MT/s (modül {RatedMts})"
        : $"{Math.Max(ConfiguredMts, RatedMts)} MT/s";
    public string ShortText => $"{Formats.Gb(CapacityGb)} {TypeText}-{Math.Max(RatedMts, ConfiguredMts)}";
}

/// <summary>Anakarttaki bir bellek yuvası. Module null ise yuva boştur.</summary>
public sealed record MemorySlot(string Locator, string Bank, MemoryModule? Module)
{
    public bool IsEmpty => Module is null;
}

public sealed record MemoryInfo(IReadOnlyList<MemorySlot> Slots, double? MaxCapacityGb, double TotalGb)
{
    public IEnumerable<MemoryModule> Modules => Slots.Where(s => s.Module is not null).Select(s => s.Module!);
    public int SlotCount => Slots.Count;
    public int UsedSlots => Slots.Count(s => !s.IsEmpty);
    public int FreeSlots => Slots.Count(s => s.IsEmpty);
    public MemoryType Type => Modules.Select(m => m.Type).FirstOrDefault(t => t != MemoryType.Unknown);
    public MemoryFormFactor FormFactor => Modules.Select(m => m.FormFactor).FirstOrDefault(f => f != MemoryFormFactor.Unknown);
    public int RatedMts => Modules.Select(m => m.RatedMts).DefaultIfEmpty().Min();
    public int ConfiguredMts => Modules.Select(m => m.ConfiguredMts).DefaultIfEmpty().Min();
    /// <summary>Tek modül ve birden fazla yuva: bellek tek kanalda çalışır.</summary>
    public bool IsSingleChannel => UsedSlots == 1 && SlotCount >= 2;
    public bool IsMixed => Modules.Select(m => (m.CapacityGb, m.RatedMts)).Distinct().Count() > 1;
}

public enum DiskKind { Unknown, Hdd, SataSsd, NvmeSsd, Usb }
public enum DiskHealth { Unknown, Healthy, Warning, Unhealthy }

public sealed record VolumeInfo(string Letter, string Label, string FileSystem, double SizeGb, double FreeGb)
{
    public double UsedGb => Math.Max(0, SizeGb - FreeGb);
    public double FreeRatio => SizeGb > 0 ? FreeGb / SizeGb : 0;
    public string DisplayName => string.IsNullOrWhiteSpace(Label) ? Letter : $"{Letter} {Label}";
}

public sealed record DiskInfo(string Name, DiskKind Kind, string Bus, double SizeGb, DiskHealth Health, bool IsSystem,
    int? TemperatureC, int? PowerOnHours, int? WearPercent, IReadOnlyList<VolumeInfo> Volumes)
{
    public string KindText => Formats.DiskKind(Kind);
    public string HealthText => Formats.DiskHealth(Health);
}

public sealed record GpuInfo(string Name, double? VramGb, bool IsIntegrated, string DriverVersion, DateOnly? DriverDate, string Resolution, int RefreshHz)
{
    public string KindText => IsIntegrated ? "Tümleşik" : "Harici";
}

public sealed record MonitorInfo(string Name, string Manufacturer, int? Year, double? DiagonalInch, bool IsInternal);

public sealed record BatteryInfo(double DesignWh, double FullWh, int? CycleCount, int? ChargePercent, bool? Charging)
{
    /// <summary>Fabrika kapasitesine göre kaybedilen pay (0..1).</summary>
    public double Wear => DesignWh > 0 ? Math.Clamp(1 - FullWh / DesignWh, 0, 1) : 0;
    public double Health => 1 - Wear;
}

public sealed record OsInfo(string Name, string Version, int Build, string Architecture, DateTime? InstallDate, DateTime? BootTime)
{
    public bool IsWindows11 => Build >= 22000;
    public string ShortName => Name.Replace(" Single Language", "");
    public bool IsWindows10 => Build is >= 10240 and < 22000;
    public TimeSpan? Uptime(DateTime now) => BootTime is { } b ? now - b : null;
}

public sealed record SecurityInfo(bool? Uefi, bool? SecureBoot, string? TpmVersion);

public sealed record NetworkInfo(string Name, string Kind, long? SpeedMbps, bool Connected);
