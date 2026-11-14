using System.Management;
using System.Runtime.InteropServices;
using Donanim.Core;
using Microsoft.Win32;

namespace Donanim.Windows;

/// <summary>
/// Bu bilgisayarın donanımını Windows Management Instrumentation (WMI) ve kayıt defterinden okur.
/// Her bölüm ayrı ayrı denenir; okunamayan bölüm (ör. yönetici izni isteyenler) uyarı olarak eklenir, tarama durmaz.
/// Seri numarası, MAC adresi, kullanıcı adı gibi kimlik bilgileri okunmaz.
/// </summary>
public static class WmiReader
{
    const string Cimv2 = @"root\cimv2";
    const string Storage = @"root\Microsoft\Windows\Storage";
    const string Wmi = @"root\wmi";

    public static HardwareSnapshot Read(DateTime now)
    {
        var warnings = new List<string>();
        T Try<T>(string part, Func<T> read, T fallback)
        {
            try { return read(); }
            catch (Exception ex) when (ex is ManagementException or COMException or UnauthorizedAccessException or InvalidOperationException or FormatException or OverflowException or InvalidCastException)
            {
                warnings.Add($"{part}: {ex.Message}");
                return fallback;
            }
        }

        var machine = Try("Bilgisayar", ReadMachine, new MachineInfo("", "Bu bilgisayar", false));
        return new HardwareSnapshot
        {
            Source = "Bu bilgisayar",
            CapturedAt = now,
            Machine = machine,
            Cpu = Try("İşlemci", ReadCpu, new CpuInfo("Bilinmiyor", "", 0, 0, 0, 0, 0, "", null)),
            Board = Try("Anakart", ReadBoard, new BoardInfo("", "", "", "", null)),
            Memory = Try("Bellek", ReadMemory, new MemoryInfo([], null, 0)),
            Disks = Try("Diskler", () => ReadDisks(warnings), (IReadOnlyList<DiskInfo>)[]),
            Gpus = Try("Ekran kartı", ReadGpus, (IReadOnlyList<GpuInfo>)[]),
            Monitors = Try("Ekranlar", ReadMonitors, (IReadOnlyList<MonitorInfo>)[]),
            Battery = Try("Pil", ReadBattery, null),
            Os = Try("İşletim sistemi", ReadOs, new OsInfo(Environment.OSVersion.VersionString, Environment.OSVersion.Version.ToString(), Environment.OSVersion.Version.Build, Environment.Is64BitOperatingSystem ? "64 bit" : "32 bit", null, null)),
            Security = Try("Güvenlik", ReadSecurity, new SecurityInfo(null, null, null)),
            Network = Try("Ağ", ReadNetwork, (IReadOnlyList<NetworkInfo>)[]),
            Warnings = warnings,
        };
    }

    // ---------------- yardımcılar ----------------

    static List<ManagementBaseObject> Query(string scope, string wql)
    {
        using var searcher = new ManagementObjectSearcher(new ManagementScope(scope), new ObjectQuery(wql));
        searcher.Options.Timeout = TimeSpan.FromSeconds(15);
        return searcher.Get().Cast<ManagementBaseObject>().ToList();
    }

    static string Str(ManagementBaseObject o, string p) => (Get(o, p)?.ToString() ?? "").Trim();
    static object? Get(ManagementBaseObject o, string p)
    {
        try { return o[p]; }
        catch (ManagementException) { return null; }
    }
    static long Num(ManagementBaseObject o, string p) => Get(o, p) is { } v ? Convert.ToInt64(v) : 0;
    static long? NumOrNull(ManagementBaseObject o, string p) => Get(o, p) is { } v ? Convert.ToInt64(v) : null;
    static DateTime? Date(ManagementBaseObject o, string p)
    {
        var s = Str(o, p);
        if (s.Length < 14) return null;
        try { return ManagementDateTimeConverter.ToDateTime(s); }
        catch (ArgumentOutOfRangeException) { return null; }
    }

    static string Clean(string s)
    {
        s = s.Replace("(R)", "").Replace("(TM)", "").Replace("(tm)", "").Replace("  ", " ").Trim();
        return s is "To Be Filled By O.E.M." or "Default string" or "System Product Name" or "System manufacturer" or "Not Applicable" or "None" ? "" : s;
    }

    // ---------------- bölümler ----------------

    static MachineInfo ReadMachine()
    {
        var cs = Query(Cimv2, "SELECT Manufacturer, Model, PCSystemType FROM Win32_ComputerSystem").FirstOrDefault();
        var laptop = cs is not null && Num(cs, "PCSystemType") == 2;
        foreach (var e in Query(Cimv2, "SELECT ChassisTypes FROM Win32_SystemEnclosure"))
            if (Get(e, "ChassisTypes") is ushort[] types && types.Any(t => t is 8 or 9 or 10 or 11 or 14 or 30 or 31 or 32))
                laptop = true;
        if (!laptop && Query(Cimv2, "SELECT Name FROM Win32_Battery").Count > 0) laptop = true;
        var maker = Maker(cs is null ? "" : Clean(Str(cs, "Manufacturer")));
        var model = cs is null ? "" : Clean(Str(cs, "Model"));
        // Lenovo, modelin satış adını (ör. "LOQ 15IAX9") Model'de değil Version'da tutar.
        if (maker == "Lenovo")
        {
            var product = Query(Cimv2, "SELECT Version FROM Win32_ComputerSystemProduct").FirstOrDefault();
            var friendly = product is null ? "" : Clean(Str(product, "Version"));
            if (friendly.Length > 0 && !friendly.Equals("Lenovo", StringComparison.OrdinalIgnoreCase)) model = friendly.Replace("Lenovo ", "", StringComparison.OrdinalIgnoreCase);
        }
        return new MachineInfo(maker, model, laptop);
    }

    static string Maker(string m)
    {
        var u = m.ToUpperInvariant();
        if (u.StartsWith("LENOVO", StringComparison.Ordinal)) return "Lenovo";
        if (u.StartsWith("ASUSTEK", StringComparison.Ordinal) || u == "ASUS") return "ASUS";
        if (u.StartsWith("HEWLETT", StringComparison.Ordinal) || u is "HP" or "HPE") return "HP";
        if (u.StartsWith("DELL", StringComparison.Ordinal)) return "Dell";
        if (u.StartsWith("MICRO-STAR", StringComparison.Ordinal)) return "MSI";
        if (u.StartsWith("ACER", StringComparison.Ordinal)) return "Acer";
        if (u.StartsWith("GIGA", StringComparison.Ordinal)) return "Gigabyte";
        if (u.StartsWith("MICROSOFT", StringComparison.Ordinal)) return "Microsoft";
        if (u.StartsWith("SAMSUNG", StringComparison.Ordinal)) return "Samsung";
        if (u.StartsWith("HUAWEI", StringComparison.Ordinal)) return "Huawei";
        if (u.StartsWith("MONSTER", StringComparison.Ordinal)) return "Monster";
        if (u.StartsWith("CASPER", StringComparison.Ordinal)) return "Casper";
        if (u.StartsWith("TOSHIBA", StringComparison.Ordinal) || u.StartsWith("DYNABOOK", StringComparison.Ordinal)) return "Dynabook";
        return m;
    }

    static CpuInfo ReadCpu()
    {
        var all = Query(Cimv2, "SELECT Name, Manufacturer, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed, L2CacheSize, L3CacheSize, SocketDesignation, VirtualizationFirmwareEnabled FROM Win32_Processor");
        var p = all.First();
        bool? virt = Get(p, "VirtualizationFirmwareEnabled") is bool b ? b : null;
        // Hyper-V açıkken firmware bayrağı false görünür; bu durumda sanallaştırma zaten kullanılıyordur.
        if (virt == false && IsHypervisorPresent()) virt = true;
        return new CpuInfo(
            Clean(Str(p, "Name")),
            Clean(Str(p, "Manufacturer")).Replace("GenuineIntel", "Intel").Replace("AuthenticAMD", "AMD"),
            (int)all.Sum(x => Num(x, "NumberOfCores")),
            (int)all.Sum(x => Num(x, "NumberOfLogicalProcessors")),
            (int)Num(p, "MaxClockSpeed"),
            (int)Num(p, "L2CacheSize"),
            (int)Num(p, "L3CacheSize"),
            Str(p, "SocketDesignation"),
            virt);
    }

    static bool IsHypervisorPresent()
    {
        try { return Query(Cimv2, "SELECT HypervisorPresent FROM Win32_ComputerSystem").FirstOrDefault() is { } o && Get(o, "HypervisorPresent") is true; }
        catch (ManagementException) { return false; }
    }

    static BoardInfo ReadBoard()
    {
        var b = Query(Cimv2, "SELECT Manufacturer, Product FROM Win32_BaseBoard").FirstOrDefault();
        var bios = Query(Cimv2, "SELECT Manufacturer, SMBIOSBIOSVersion, ReleaseDate FROM Win32_BIOS").FirstOrDefault();
        DateOnly? date = bios is not null && Date(bios, "ReleaseDate") is { } d ? DateOnly.FromDateTime(d) : null;
        return new BoardInfo(
            b is null ? "" : Maker(Clean(Str(b, "Manufacturer"))),
            b is null ? "" : Clean(Str(b, "Product")),
            bios is null ? "" : Clean(Str(bios, "Manufacturer")),
            bios is null ? "" : Str(bios, "SMBIOSBIOSVersion"),
            date);
    }

    static MemoryInfo ReadMemory()
    {
        long slotCount = 0, maxKb = 0;
        foreach (var a in Query(Cimv2, "SELECT MemoryDevices, MaxCapacity, MaxCapacityEx, Use FROM Win32_PhysicalMemoryArray"))
        {
            if (NumOrNull(a, "Use") is { } use && use != 3) continue; // 3 = sistem belleği
            slotCount += Num(a, "MemoryDevices");
            var ex = NumOrNull(a, "MaxCapacityEx") ?? 0;
            maxKb += ex > 0 ? ex : Num(a, "MaxCapacity");
        }

        var slots = new List<MemorySlot>();
        double total = 0;
        foreach (var m in Query(Cimv2, "SELECT BankLabel, DeviceLocator, Capacity, Speed, ConfiguredClockSpeed, Manufacturer, PartNumber, SMBIOSMemoryType, FormFactor FROM Win32_PhysicalMemory"))
        {
            var gb = Num(m, "Capacity") / 1024.0 / 1024 / 1024;
            total += gb;
            var type = Num(m, "SMBIOSMemoryType") switch
            {
                19 or 20 => MemoryType.Unknown,
                21 => MemoryType.Ddr2,
                24 => MemoryType.Ddr3,
                26 => MemoryType.Ddr4,
                29 => MemoryType.Lpddr3,
                30 => MemoryType.Lpddr4,
                34 => MemoryType.Ddr5,
                35 => MemoryType.Lpddr5,
                _ => MemoryType.Unknown,
            };
            var ff = Num(m, "FormFactor") switch
            {
                8 => MemoryFormFactor.Dimm,
                12 => MemoryFormFactor.SoDimm,
                0 or 1 when type is MemoryType.Lpddr3 or MemoryType.Lpddr4 or MemoryType.Lpddr5 => MemoryFormFactor.Soldered,
                _ => MemoryFormFactor.Unknown,
            };
            var rated = (int)Num(m, "Speed");
            var configured = (int)Num(m, "ConfiguredClockSpeed");
            var module = new MemoryModule(Clean(Str(m, "Manufacturer")), Str(m, "PartNumber"), Math.Round(gb, 1), type, ff, rated, configured == 0 ? rated : configured);
            slots.Add(new MemorySlot(Str(m, "DeviceLocator"), Str(m, "BankLabel"), module));
        }

        // Boş yuvalar: Windows yalnızca dolu yuvaları listeler; toplam yuva sayısından çıkarılır.
        var empty = (int)Math.Max(0, slotCount - slots.Count);
        for (var i = 0; i < empty; i++) slots.Add(new MemorySlot($"Boş yuva {i + 1}", "", null));
        slots = slots.OrderBy(s => s.Locator, StringComparer.OrdinalIgnoreCase).ToList();

        if (total <= 0)
        {
            var cs = Query(Cimv2, "SELECT TotalPhysicalMemory FROM Win32_ComputerSystem").FirstOrDefault();
            if (cs is not null) total = Num(cs, "TotalPhysicalMemory") / 1024.0 / 1024 / 1024;
        }
        double? max = maxKb > 0 ? maxKb / 1024.0 / 1024 : null;
        if (max is { } mx && mx < total) max = null; // hatalı BIOS bilgisi
        return new MemoryInfo(slots, max, Math.Round(total, 1));
    }

    static IReadOnlyList<DiskInfo> ReadDisks(List<string> warnings)
    {
        var sysLetter = (Environment.GetEnvironmentVariable("SystemDrive") ?? "C:").ToUpperInvariant();

        // Bölüm → disk eşleşmesi
        var partitions = Query(Storage, "SELECT DiskNumber, DriveLetter FROM MSFT_Partition")
            .Select(p => (Disk: Num(p, "DiskNumber"), Letter: Get(p, "DriveLetter") is char c && c != '\0' ? $"{char.ToUpperInvariant(c)}:" : ""))
            .Where(p => p.Letter.Length > 0)
            .ToList();
        var volumes = Query(Cimv2, "SELECT DeviceID, VolumeName, FileSystem, Size, FreeSpace FROM Win32_LogicalDisk WHERE DriveType = 3")
            .ToDictionary(v => Str(v, "DeviceID").ToUpperInvariant(),
                v => new VolumeInfo(Str(v, "DeviceID").ToUpperInvariant(), Str(v, "VolumeName"), Str(v, "FileSystem"), Num(v, "Size") / 1e9, Num(v, "FreeSpace") / 1e9));

        // Güvenilirlik sayaçları çoğunlukla yönetici izni ister; yoksa boş geçilir.
        var reliability = new Dictionary<string, ManagementBaseObject>();
        try
        {
            foreach (var r in Query(Storage, "SELECT DeviceId, Temperature, PowerOnHours, Wear FROM MSFT_StorageReliabilityCounter"))
                reliability[Str(r, "DeviceId")] = r;
        }
        catch (ManagementException) { warnings.Add("Disk sıcaklığı ve çalışma saati için uygulamayı yönetici olarak açın."); }

        var disks = new List<DiskInfo>();
        foreach (var d in Query(Storage, "SELECT DeviceId, FriendlyName, MediaType, BusType, Size, HealthStatus FROM MSFT_PhysicalDisk"))
        {
            var id = Str(d, "DeviceId");
            var bus = Num(d, "BusType");
            var media = Num(d, "MediaType");
            var kind = bus switch
            {
                7 => DiskKind.Usb,
                17 => DiskKind.NvmeSsd,
                _ => media switch { 3 => DiskKind.Hdd, 4 => DiskKind.SataSsd, _ => DiskKind.Unknown },
            };
            var health = Num(d, "HealthStatus") switch { 0 => DiskHealth.Healthy, 1 => DiskHealth.Warning, 2 => DiskHealth.Unhealthy, _ => DiskHealth.Unknown };
            var vols = partitions.Where(p => p.Disk.ToString() == id).Select(p => volumes.GetValueOrDefault(p.Letter)).OfType<VolumeInfo>().OrderBy(v => v.Letter).ToList();
            int? temp = null, hours = null, wear = null;
            if (reliability.TryGetValue(id, out var r))
            {
                temp = NumOrNull(r, "Temperature") is { } t and > 0 and < 120 ? (int)t : null;
                hours = NumOrNull(r, "PowerOnHours") is { } h and > 0 ? (int)h : null;
                wear = NumOrNull(r, "Wear") is { } w and > 0 and <= 100 ? (int)w : null;
            }
            disks.Add(new DiskInfo(Clean(Str(d, "FriendlyName")), kind, BusName(bus), Num(d, "Size") / 1e9, health,
                vols.Any(v => v.Letter == sysLetter), temp, hours, wear, vols));
        }
        return disks.OrderByDescending(d => d.IsSystem).ThenBy(d => d.Kind == DiskKind.Usb).ToList();
    }

    static string BusName(long bus) => bus switch
    {
        1 => "SCSI", 3 => "ATA", 6 => "Fibre Channel", 7 => "USB", 8 => "RAID", 10 => "SAS", 11 => "SATA", 12 => "SD", 13 => "MMC", 17 => "NVMe", 15 => "Dosya", _ => "-",
    };

    static IReadOnlyList<GpuInfo> ReadGpus()
    {
        var vram = ReadVramFromRegistry();
        var list = new List<GpuInfo>();
        foreach (var v in Query(Cimv2, "SELECT Name, AdapterRAM, DriverVersion, DriverDate, CurrentHorizontalResolution, CurrentVerticalResolution, CurrentRefreshRate, PNPDeviceID FROM Win32_VideoController"))
        {
            var name = Clean(Str(v, "Name"));
            if (name.Length == 0 || name.Contains("Basic Display", StringComparison.OrdinalIgnoreCase) || name.Contains("Remote", StringComparison.OrdinalIgnoreCase) || name.Contains("Virtual", StringComparison.OrdinalIgnoreCase)) continue;
            double? gb = vram.TryGetValue(name, out var q) ? q / 1024.0 / 1024 / 1024 : NumOrNull(v, "AdapterRAM") is { } ar and > 0 ? ar / 1024.0 / 1024 / 1024 : (double?)null;
            var integrated = IsIntegrated(name, gb);
            var w = Num(v, "CurrentHorizontalResolution");
            var h = Num(v, "CurrentVerticalResolution");
            list.Add(new GpuInfo(name, gb is { } g ? Math.Round(g, 1) : null, integrated, Str(v, "DriverVersion"),
                Date(v, "DriverDate") is { } dd ? DateOnly.FromDateTime(dd) : null,
                w > 0 ? $"{w} × {h}" : "-", (int)Num(v, "CurrentRefreshRate")));
        }
        return list;
    }

    static bool IsIntegrated(string name, double? vramGb)
    {
        var n = name.ToUpperInvariant();
        if (n.Contains("INTEL") && !n.Contains("ARC")) return true;
        if (n.Contains("RADEON") && (n.Contains("VEGA") || n == "AMD RADEON GRAPHICS" || n.EndsWith("RADEON GRAPHICS", StringComparison.Ordinal) || n.Contains("RADEON(TM) GRAPHICS"))) return true;
        if (n.Contains("RADEON") && n.Contains(" M") && vramGb is < 1.1) return true;
        return vramGb is > 0 and < 0.6;
    }

    /// <summary>WMI'daki AdapterRAM 4 GB'ta takılır; gerçek değer sürücünün kayıt defteri anahtarındadır.</summary>
    static Dictionary<string, long> ReadVramFromRegistry()
    {
        var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var cls = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            if (cls is null) return result;
            foreach (var sub in cls.GetSubKeyNames().Where(n => n.Length == 4 && n.All(char.IsDigit)))
            {
                using var k = cls.OpenSubKey(sub);
                if (k?.GetValue("DriverDesc") is not string desc) continue;
                var v = k.GetValue("HardwareInformation.qwMemorySize") ?? k.GetValue("HardwareInformation.MemorySize");
                long size = v switch { long l => l, int i => (uint)i, byte[] b when b.Length >= 8 => BitConverter.ToInt64(b, 0), byte[] b when b.Length >= 4 => BitConverter.ToUInt32(b, 0), _ => 0 };
                if (size > 0) result[Clean(desc)] = Math.Max(size, result.GetValueOrDefault(Clean(desc)));
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
        return result;
    }

    static IReadOnlyList<MonitorInfo> ReadMonitors()
    {
        var sizes = new Dictionary<string, double>();
        try
        {
            foreach (var p in Query(Wmi, "SELECT InstanceName, MaxHorizontalImageSize, MaxVerticalImageSize FROM WmiMonitorBasicDisplayParams"))
            {
                double w = Num(p, "MaxHorizontalImageSize"), h = Num(p, "MaxVerticalImageSize");
                if (w > 0 && h > 0) sizes[Str(p, "InstanceName")] = Math.Round(Math.Sqrt(w * w + h * h) / 2.54, 1);
            }
        }
        catch (ManagementException) { }

        var embedded = new HashSet<string>();
        try
        {
            foreach (var c in Query(Wmi, "SELECT InstanceName, VideoOutputTechnology FROM WmiMonitorConnectionParams"))
            {
                var tech = unchecked((uint)Num(c, "VideoOutputTechnology"));
                if (tech is 6 or 11 or 13 or 0x80000000) embedded.Add(Str(c, "InstanceName"));
            }
        }
        catch (ManagementException) { }

        var list = new List<MonitorInfo>();
        foreach (var m in Query(Wmi, "SELECT InstanceName, UserFriendlyName, ManufacturerName, YearOfManufacture FROM WmiMonitorID"))
        {
            var inst = Str(m, "InstanceName");
            var name = Decode(Get(m, "UserFriendlyName"));
            var maker = Decode(Get(m, "ManufacturerName"));
            var year = (int)Num(m, "YearOfManufacture");
            var internalPanel = embedded.Contains(inst);
            list.Add(new MonitorInfo(name.Length > 0 ? name : "Dahili ekran", MakerName(maker), year > 1990 ? year : null, sizes.TryGetValue(inst, out var d) ? d : null, internalPanel));
        }
        return list;
    }

    static string Decode(object? v) => v is ushort[] chars ? new string(chars.TakeWhile(c => c != 0).Select(c => (char)c).ToArray()).Trim() : "";

    static string MakerName(string pnp) => pnp switch
    {
        "SAM" => "Samsung", "GSM" => "LG", "DEL" => "Dell", "HWP" or "HPN" => "HP", "AUS" => "ASUS", "ACR" => "Acer", "LEN" => "Lenovo",
        "BNQ" => "BenQ", "PHL" => "Philips", "AOC" => "AOC", "VSC" => "ViewSonic", "MSI" => "MSI", "BOE" => "BOE", "AUO" => "AU Optronics",
        "LGD" => "LG Display", "SDC" => "Samsung Display", "CMN" => "Innolux", "SHP" => "Sharp", "IVO" => "InfoVision", "APP" => "Apple", _ => pnp,
    };

    static BatteryInfo? ReadBattery()
    {
        if (Query(Cimv2, "SELECT Name FROM Win32_Battery").Count == 0) return null;
        double design = 0, full = 0;
        int? cycles = null;
        try { foreach (var b in Query(Wmi, "SELECT DesignedCapacity FROM BatteryStaticData")) design += Num(b, "DesignedCapacity"); } catch (ManagementException) { }
        try { foreach (var b in Query(Wmi, "SELECT FullChargedCapacity FROM BatteryFullChargedCapacity")) full += Num(b, "FullChargedCapacity"); } catch (ManagementException) { }
        try { foreach (var b in Query(Wmi, "SELECT CycleCount FROM BatteryCycleCount")) cycles = (int)Num(b, "CycleCount"); } catch (ManagementException) { }
        var st = Query(Cimv2, "SELECT EstimatedChargeRemaining, BatteryStatus FROM Win32_Battery").FirstOrDefault();
        int? charge = st is null ? null : (int)Num(st, "EstimatedChargeRemaining");
        bool? charging = st is null ? null : Num(st, "BatteryStatus") is 2 or 6 or 7 or 8 or 9;
        if (design <= 0 || full <= 0) return new BatteryInfo(0, 0, cycles, charge, charging);
        return new BatteryInfo(design / 1000.0, Math.Min(full, design * 1.05) / 1000.0, cycles is > 0 ? cycles : null, charge, charging);
    }

    static OsInfo ReadOs()
    {
        var o = Query(Cimv2, "SELECT Caption, Version, BuildNumber, OSArchitecture, InstallDate, LastBootUpTime FROM Win32_OperatingSystem").First();
        var name = Str(o, "Caption").Replace("Microsoft ", "");
        return new OsInfo(name, Str(o, "Version"), int.TryParse(Str(o, "BuildNumber"), out var b) ? b : 0,
            Str(o, "OSArchitecture").Replace("-bit", " bit").Replace("64 bit", "64 bit"), Date(o, "InstallDate"), Date(o, "LastBootUpTime"));
    }

    static SecurityInfo ReadSecurity()
    {
        bool? uefi = null, secure = null;
        if (GetFirmwareType(out var ft)) uefi = ft == 2;
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State");
            if (k?.GetValue("UEFISecureBootEnabled") is int v) secure = v == 1;
            else if (uefi == false) secure = false;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException) { }

        string? tpm = null;
        try
        {
            var t = Query(@"root\cimv2\Security\MicrosoftTpm", "SELECT SpecVersion, IsEnabled_InitialValue FROM Win32_Tpm").FirstOrDefault();
            if (t is not null) tpm = Str(t, "SpecVersion").Split(',').FirstOrDefault()?.Trim();
        }
        catch (ManagementException) { }
        catch (UnauthorizedAccessException) { }
        return new SecurityInfo(uefi, secure, string.IsNullOrEmpty(tpm) ? null : tpm);
    }

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetFirmwareType(out int firmwareType);

    static IReadOnlyList<NetworkInfo> ReadNetwork()
    {
        var list = new List<NetworkInfo>();
        foreach (var n in Query(Cimv2, "SELECT Name, Speed, NetConnectionStatus, PhysicalAdapter, PNPDeviceID FROM Win32_NetworkAdapter WHERE PhysicalAdapter = TRUE"))
        {
            var pnp = Str(n, "PNPDeviceID");
            if (!pnp.StartsWith("PCI", StringComparison.OrdinalIgnoreCase) && !pnp.StartsWith("USB", StringComparison.OrdinalIgnoreCase)) continue;
            var name = Clean(Str(n, "Name"));
            var lower = name.ToLowerInvariant();
            var kind = lower.Contains("wi-fi") || lower.Contains("wireless") || lower.Contains("wlan") || lower.Contains("802.11") ? "Kablosuz"
                : lower.Contains("bluetooth") ? "Bluetooth" : "Ethernet";
            if (kind == "Bluetooth") continue;
            var connected = Num(n, "NetConnectionStatus") == 2;
            long? speed = connected && NumOrNull(n, "Speed") is { } sp and > 0 and < 1_000_000_000_000 ? sp / 1_000_000 : null;
            list.Add(new NetworkInfo(name, kind, speed, connected));
        }
        return list;
    }
}
