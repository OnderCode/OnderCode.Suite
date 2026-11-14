using System.Globalization;

namespace Donanim.Core;

/// <summary>Türkçe gösterim biçimleri.</summary>
public static class Formats
{
    static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    public static string Gb(double gb)
    {
        if (gb <= 0) return "-";
        if (gb >= 1000) return (gb / 1000).ToString(gb >= 10000 ? "0" : "0.#", Tr) + " TB";
        return gb.ToString(gb >= 100 || Math.Abs(gb - Math.Round(gb)) < 0.05 ? "0" : "0.#", Tr) + " GB";
    }

    public static string Percent(double ratio, int decimals = 0) => "%" + (ratio * 100).ToString("N" + decimals, Tr);

    public static string Number(double v, int decimals = 0) => v.ToString("N" + decimals, Tr);

    public static string Date(DateOnly? d) => d is { } x ? x.ToString("dd.MM.yyyy", Tr) : "-";

    public static string Duration(TimeSpan t)
    {
        if (t.TotalDays >= 1) return $"{(int)t.TotalDays} gün {t.Hours} saat";
        if (t.TotalHours >= 1) return $"{(int)t.TotalHours} saat {t.Minutes} dk";
        return $"{Math.Max(0, t.Minutes)} dk";
    }

    public static string MemoryType(MemoryType t) => t switch
    {
        Core.MemoryType.Ddr2 => "DDR2",
        Core.MemoryType.Ddr3 => "DDR3",
        Core.MemoryType.Ddr4 => "DDR4",
        Core.MemoryType.Ddr5 => "DDR5",
        Core.MemoryType.Lpddr3 => "LPDDR3",
        Core.MemoryType.Lpddr4 => "LPDDR4",
        Core.MemoryType.Lpddr5 => "LPDDR5",
        _ => "RAM",
    };

    public static string FormFactor(MemoryFormFactor f) => f switch
    {
        MemoryFormFactor.Dimm => "DIMM (masaüstü)",
        MemoryFormFactor.SoDimm => "SO-DIMM (dizüstü)",
        MemoryFormFactor.Soldered => "Anakarta lehimli",
        _ => "-",
    };

    public static string FormFactorShort(MemoryFormFactor f) => f switch
    {
        MemoryFormFactor.Dimm => "DIMM",
        MemoryFormFactor.SoDimm => "SO-DIMM",
        MemoryFormFactor.Soldered => "lehimli",
        _ => "",
    };

    public static string DiskKind(DiskKind k) => k switch
    {
        Core.DiskKind.Hdd => "HDD",
        Core.DiskKind.SataSsd => "SATA SSD",
        Core.DiskKind.NvmeSsd => "NVMe SSD",
        Core.DiskKind.Usb => "USB disk",
        _ => "Disk",
    };

    public static string DiskHealth(DiskHealth h) => h switch
    {
        Core.DiskHealth.Healthy => "Sağlıklı",
        Core.DiskHealth.Warning => "Uyarı",
        Core.DiskHealth.Unhealthy => "Arızalı",
        _ => "Bilinmiyor",
    };

    public static string YesNo(bool? v, string yes = "Açık", string no = "Kapalı") => v switch { true => yes, false => no, _ => "Bilinmiyor" };
}
