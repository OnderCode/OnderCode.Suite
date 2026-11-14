namespace Donanim.Core;

/// <summary>
/// Kurgusal örnek bilgisayarlar: ekran görüntüleri, tanıtım videosu ve testler için.
/// Marka ve model adları uydurmadır; parçalar piyasada bulunan tiplerden seçilmiştir.
/// </summary>
public static class Samples
{
    public const string OfficeLaptopName = "Örnek: ofis dizüstü";
    public const string GamingDesktopName = "Örnek: oyun bilgisayarı";

    public static IReadOnlyList<string> Names { get; } = [OfficeLaptopName, GamingDesktopName];

    public static HardwareSnapshot Get(string name, DateTime now) => name == GamingDesktopName ? GamingDesktop(now) : OfficeLaptop(now);

    /// <summary>5–6 yaşında bir ofis dizüstüsü: tek bellek modülü, sabit disk, yıpranmış pil, Windows 10.</summary>
    public static HardwareSnapshot OfficeLaptop(DateTime now)
    {
        var sodimm = new MemoryModule("SK hynix", "HMA81GS6CJR8N-VK", 8, MemoryType.Ddr4, MemoryFormFactor.SoDimm, 2666, 2400);
        return new HardwareSnapshot
        {
            Source = OfficeLaptopName,
            IsSample = true,
            CapturedAt = now,
            Machine = new MachineInfo("Kuzey Bilişim", "Atlas 14 Pro", IsLaptop: true, "OFIS-DIZUSTU"),
            Cpu = new CpuInfo("Intel Core i5-8250U", "Intel", 4, 8, 1800, 1024, 6144, "U3E1", true),
            Board = new BoardInfo("Kuzey Bilişim", "AT14-MB rev 1.2", "Kuzey", "1.17.0", new DateOnly(2020, 3, 11)),
            Memory = new MemoryInfo(
                [new MemorySlot("ChannelA-DIMM0", "BANK 0", sodimm), new MemorySlot("ChannelB-DIMM0", "BANK 2", null)],
                MaxCapacityGb: 32, TotalGb: 8),
            Disks =
            [
                new DiskInfo("WDC WD10SPZX-21Z10T0", DiskKind.Hdd, "SATA", 1000, DiskHealth.Healthy, IsSystem: true, 41, 14_820, null,
                    [new VolumeInfo("C:", "Windows", "NTFS", 476, 61), new VolumeInfo("D:", "Veri", "NTFS", 455, 212)]),
            ],
            Gpus = [new GpuInfo("Intel UHD Graphics 620", 0.13, IsIntegrated: true, "26.20.100.7262", new DateOnly(2019, 9, 26), "1920 × 1080", 60)],
            Monitors = [new MonitorInfo("Dahili ekran", "Kuzey", 2019, 14.0, IsInternal: true)],
            Battery = new BatteryInfo(DesignWh: 48.0, FullWh: 30.2, CycleCount: 512, ChargePercent: 76, Charging: false),
            Os = new OsInfo("Windows 10 Pro", "10.0.19045", 19045, "64 bit", now.AddYears(-3).AddDays(-41), now.AddDays(-9).AddHours(-3)),
            Security = new SecurityInfo(Uefi: true, SecureBoot: true, TpmVersion: "2.0"),
            Network =
            [
                new NetworkInfo("Intel Dual Band Wireless-AC 8265", "Kablosuz", 433, true),
                new NetworkInfo("Realtek PCIe GbE Family Controller", "Ethernet", null, false),
            ],
        };
    }

    /// <summary>Toplama oyun bilgisayarı: güçlü parçalar ama XMP kapalı, 2 boş bellek yuvası, dolmak üzere bir oyun diski.</summary>
    public static HardwareSnapshot GamingDesktop(DateTime now)
    {
        MemoryModule Stick() => new("Örnek Bellek", "OB32G36C18K2", 16, MemoryType.Ddr4, MemoryFormFactor.Dimm, 3600, 2133);
        return new HardwareSnapshot
        {
            Source = GamingDesktopName,
            IsSample = true,
            CapturedAt = now,
            Machine = new MachineInfo("Toplama", "Oyun Bilgisayarı", IsLaptop: false, "OYUN-PC"),
            Cpu = new CpuInfo("AMD Ryzen 7 5800X 8-Core Processor", "AMD", 8, 16, 3800, 4096, 32768, "AM4", true),
            Board = new BoardInfo("Kuzey", "KZ-B550 Oyun", "American Megatrends", "F17b", new DateOnly(2024, 2, 6)),
            Memory = new MemoryInfo(
                [
                    new MemorySlot("DIMM_A1", "BANK 0", null),
                    new MemorySlot("DIMM_A2", "BANK 1", Stick()),
                    new MemorySlot("DIMM_B1", "BANK 2", null),
                    new MemorySlot("DIMM_B2", "BANK 3", Stick()),
                ],
                MaxCapacityGb: 128, TotalGb: 32),
            Disks =
            [
                new DiskInfo("Örnek NV2 1TB", DiskKind.NvmeSsd, "NVMe", 1000, DiskHealth.Healthy, IsSystem: true, 46, 6_210, 7,
                    [new VolumeInfo("C:", "Windows", "NTFS", 930, 402)]),
                new DiskInfo("Seagate ST2000DM008", DiskKind.Hdd, "SATA", 2000, DiskHealth.Healthy, IsSystem: false, 38, 18_440, null,
                    [new VolumeInfo("D:", "Oyunlar", "NTFS", 1863, 141)]),
            ],
            Gpus = [new GpuInfo("NVIDIA GeForce RTX 3060", 12, IsIntegrated: false, "32.0.15.6094", DateOnly.FromDateTime(now.AddMonths(-3)), "2560 × 1440", 165)],
            Monitors =
            [
                new MonitorInfo("KZ27Q165", "Kuzey", 2023, 27.0, IsInternal: false),
                new MonitorInfo("KZ24F", "Kuzey", 2019, 23.8, IsInternal: false),
            ],
            Battery = null,
            Os = new OsInfo("Windows 11 Pro", "10.0.26100", 26100, "64 bit", now.AddYears(-1).AddDays(-20), now.AddHours(-5).AddMinutes(-12)),
            Security = new SecurityInfo(Uefi: true, SecureBoot: true, TpmVersion: "2.0"),
            Network = [new NetworkInfo("Realtek Gaming 2.5GbE Family Controller", "Ethernet", 1000, true)],
        };
    }
}
