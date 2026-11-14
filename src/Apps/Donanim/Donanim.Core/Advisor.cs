namespace Donanim.Core;

public enum GradeCategory { Processor, Memory, Storage, Graphics, Battery, System }

/// <summary>Karnede bir ders: 1 (zayıf) – 5 (pekiyi).</summary>
public sealed record Grade(GradeCategory Category, int Score, string Reason)
{
    public string Title => Category switch
    {
        GradeCategory.Processor => "İşlemci",
        GradeCategory.Memory => "Bellek",
        GradeCategory.Storage => "Depolama",
        GradeCategory.Graphics => "Grafik",
        GradeCategory.Battery => "Pil",
        _ => "Sistem",
    };

    public string Label => Karne.Label(Score);
}

public enum AdvicePriority { Critical, Recommended, Info, Good }

public sealed record Advice(AdvicePriority Priority, GradeCategory Category, string Title, string Detail, string? Benefit = null)
{
    public string PriorityText => Priority switch
    {
        AdvicePriority.Critical => "Önemli",
        AdvicePriority.Recommended => "Önerilir",
        AdvicePriority.Info => "Bilgi",
        _ => "Yolunda",
    };
}

/// <summary>Alışveriş listesindeki bir parça.</summary>
public sealed record ShoppingItem(int Quantity, string Part, string Why);

public sealed record Karne(HardwareSnapshot Snapshot, IReadOnlyList<Grade> Grades, IReadOnlyList<Advice> Advice, IReadOnlyList<ShoppingItem> Shopping)
{
    public double Average => Grades.Count == 0 ? 0 : Math.Round(Grades.Average(g => g.Score), 2);
    public string AverageLabel => Label((int)Math.Round(Average, MidpointRounding.AwayFromZero));
    public int CriticalCount => Advice.Count(a => a.Priority == AdvicePriority.Critical);
    public int RecommendedCount => Advice.Count(a => a.Priority == AdvicePriority.Recommended);

    public static string Label(int score) => score switch
    {
        >= 5 => "Pekiyi",
        4 => "İyi",
        3 => "Orta",
        2 => "Geçer",
        _ => "Zayıf",
    };
}

/// <summary>
/// Donanım görüntüsünden karne çıkarır: her bileşene 1–5 arası not, somut öneriler
/// ("2 boş yuva var, 8 GB DDR4-3200 SO-DIMM ekleyin") ve alışveriş listesi.
/// </summary>
public static class Advisor
{
    public static Karne Evaluate(HardwareSnapshot s, DateTime now)
    {
        var grades = new List<Grade>();
        var advice = new List<Advice>();
        var shopping = new List<ShoppingItem>();

        grades.Add(Processor(s));
        grades.Add(Memory(s, advice, shopping));
        grades.Add(Storage(s, advice, shopping));
        grades.Add(Graphics(s, advice));
        if (s.Battery is not null) grades.Add(Battery(s.Battery, advice, shopping));
        grades.Add(SystemGrade(s, now, advice));
        ProcessorAdvice(s, advice);

        if (advice.All(a => a.Priority is AdvicePriority.Info or AdvicePriority.Good))
            advice.Add(new Advice(AdvicePriority.Good, GradeCategory.System, "Bu bilgisayar işini görüyor",
                "Acil bir eksik bulunmadı. Düzenli Windows güncellemesi ve yedekle böyle devam edebilir."));

        var ordered = advice.OrderBy(a => a.Priority).ThenBy(a => a.Category).ToList();
        return new Karne(s, grades, ordered, shopping);
    }

    // ---------------- İşlemci ----------------

    static Grade Processor(HardwareSnapshot s)
    {
        var c = s.Cpu;
        var score = c.Threads switch
        {
            >= 16 => 5,
            >= 12 => 4,
            >= 8 => c.Cores >= 6 ? 4 : 3,
            >= 4 => 2,
            _ => 1,
        };
        if (c.Threads >= 8 && c.Cores >= 8) score = Math.Max(score, 4);
        if (c.MaxClockMhz is > 0 and < 2000) score = Math.Max(1, score - 1);
        var reason = $"{c.CoreText}, {c.ClockText}";
        return new Grade(GradeCategory.Processor, score, reason);
    }

    static void ProcessorAdvice(HardwareSnapshot s, List<Advice> advice)
    {
        if (s.Cpu.Threads < 4)
            advice.Add(new Advice(AdvicePriority.Info, GradeCategory.Processor, "İşlemci bu bilgisayarın sınırı",
                $"{s.Cpu.Threads} iş parçacığı bugünkü tarayıcı ve ofis yükü için az. İşlemci genelde anakartla birlikte değişir; bu bilgisayarda en büyük kazancı bellek ve SSD verir."));
        if (s.Cpu.Virtualization == false)
            advice.Add(new Advice(AdvicePriority.Info, GradeCategory.Processor, "Sanallaştırma kapalı",
                "BIOS/UEFI ayarlarında Intel VT-x veya AMD-V (SVM) açılırsa sanal makine, WSL ve Android öykünücüleri çalışır."));
    }

    // ---------------- Bellek ----------------

    static Grade Memory(HardwareSnapshot s, List<Advice> advice, List<ShoppingItem> shopping)
    {
        var m = s.Memory;
        var total = m.TotalGb;
        var score = total switch
        {
            >= 31 => 5,
            >= 15 => 4,
            >= 7.5 => 3,
            >= 5.5 => 2,
            _ => 1,
        };
        if (m.IsSingleChannel && score > 1 && !s.HasDedicatedGpu) score--;

        var type = Formats.MemoryType(m.Type);
        var ff = Formats.FormFactorShort(m.FormFactor);
        var speed = Math.Max(m.RatedMts, m.ConfiguredMts);
        var part = $"{type}-{(speed > 0 ? speed : 3200)}{(ff.Length > 0 ? " " + ff : "")}";
        var largest = m.Modules.Select(x => x.CapacityGb).DefaultIfEmpty(8).Max();
        var max = m.MaxCapacityGb;
        var soldered = m.FormFactor == MemoryFormFactor.Soldered || m.Type is MemoryType.Lpddr3 or MemoryType.Lpddr4 or MemoryType.Lpddr5;

        if (soldered)
        {
            if (total < 15)
                advice.Add(new Advice(AdvicePriority.Info, GradeCategory.Memory, "Bellek anakarta lehimli",
                    $"Bu bilgisayardaki {type} bellek lehimli görünüyor; sonradan eklenemez. {Formats.Gb(total)} ile çok sekme ve büyük dosyalarda yavaşlama olabilir."));
        }
        else if (m.IsSingleChannel)
        {
            var add = largest;
            var after = total + add;
            if (max is { } mx && after > mx) after = mx;
            advice.Add(new Advice(total < 7.5 ? AdvicePriority.Critical : AdvicePriority.Recommended, GradeCategory.Memory,
                $"Boş yuvaya {Formats.Gb(add)} {part} takın",
                $"Bellek tek modülle tek kanalda çalışıyor ve {m.FreeSlots} yuva boş. Aynı boyut ve hızda ikinci bir modül hem toplamı {Formats.Gb(after)} yapar hem de çift kanala geçirir.",
                s.HasDedicatedGpu ? "Çok sekmede ve büyük dosyalarda belirgin rahatlama" : "Tümleşik grafikte %20–40'a varan hızlanma, çok sekmede rahatlama"));
            shopping.Add(new ShoppingItem(1, $"{Formats.Gb(add)} {part} bellek", "Boş yuvaya, mevcut modülle çift kanal"));
        }
        else if (total < 15 && m.FreeSlots > 0)
        {
            var count = Math.Min(m.FreeSlots, m.UsedSlots >= 2 ? 2 : 1);
            var after = total + count * largest;
            if (max is { } mx && after > mx) after = mx;
            advice.Add(new Advice(total < 7.5 ? AdvicePriority.Critical : AdvicePriority.Recommended, GradeCategory.Memory,
                $"{count} × {Formats.Gb(largest)} {part} ekleyin",
                $"{m.FreeSlots} yuva boş. Mevcut modüllerle aynı tipte {count} modül eklenirse toplam {Formats.Gb(after)} olur.",
                "Çok sekme, Office ve görüntülü görüşme aynı anda rahat çalışır"));
            shopping.Add(new ShoppingItem(count, $"{Formats.Gb(largest)} {part} bellek", "Boş yuvalara"));
        }
        else if (total < 15 && m.FreeSlots == 0 && m.SlotCount > 0)
        {
            var target = max is { } mx ? Math.Min(16, mx) : 16;
            var each = target / m.SlotCount;
            advice.Add(new Advice(total < 7.5 ? AdvicePriority.Critical : AdvicePriority.Recommended, GradeCategory.Memory,
                $"Modülleri {Formats.Gb(target)} kit ile değiştirin",
                $"Bütün yuvalar dolu ({m.SlotCount} × {Formats.Gb(largest)}). Toplamı artırmanın yolu daha büyük modüllere geçmek: {m.SlotCount} × {Formats.Gb(each)} {part}.",
                "Çok sekme ve ağır programlarda rahatlama"));
            shopping.Add(new ShoppingItem(m.SlotCount, $"{Formats.Gb(each)} {part} bellek", "Eski modüllerin yerine"));
        }

        if (m.IsMixed && m.UsedSlots >= 2)
            advice.Add(new Advice(AdvicePriority.Info, GradeCategory.Memory, "Farklı modüller karışık takılı",
                "Boyutu ya da hızı farklı modüller en yavaş olanın hızında çalışır; bazı anakartlarda çift kanal da kısmen devre dışı kalır."));

        if (m.RatedMts > 0 && m.ConfiguredMts > 0 && m.ConfiguredMts < m.RatedMts * 0.9)
        {
            if (!s.Machine.IsLaptop)
                advice.Add(new Advice(AdvicePriority.Recommended, GradeCategory.Memory, "Bellek hızının altında çalışıyor: XMP/EXPO'yu açın",
                    $"Modüller {m.RatedMts} MT/s için üretilmiş ama {m.ConfiguredMts} MT/s'de çalışıyor. BIOS/UEFI'de XMP (Intel) ya da EXPO/DOCP (AMD) profilini açmak yeterli; para gerektirmez.",
                    "Oyunlarda ve işlemci yoğun işlerde %5–15 hız"));
            else
                advice.Add(new Advice(AdvicePriority.Info, GradeCategory.Memory, "Bellek, modülün hızının altında çalışıyor",
                    $"Modüller {m.RatedMts} MT/s destekliyor, sistem {m.ConfiguredMts} MT/s kullanıyor. Dizüstülerde bu sınırı genelde işlemci belirler; normaldir."));
        }

        if (max is { } maxGb && m.SlotCount > 0)
        {
            // bilgi amaçlı: anakartın sınırı
            if (total >= maxGb - 0.5)
                advice.Add(new Advice(AdvicePriority.Info, GradeCategory.Memory, "Anakartın bellek sınırına ulaşıldı",
                    $"Anakart en fazla {Formats.Gb(maxGb)} destekliyor; daha fazla bellek takılamaz."));
        }

        var reason = m.SlotCount > 0
            ? $"{Formats.Gb(total)} {type} · {m.UsedSlots}/{m.SlotCount} yuva dolu{(m.IsSingleChannel ? " · tek kanal" : "")}"
            : $"{Formats.Gb(total)}";
        return new Grade(GradeCategory.Memory, score, reason);
    }

    // ---------------- Depolama ----------------

    static Grade Storage(HardwareSnapshot s, List<Advice> advice, List<ShoppingItem> shopping)
    {
        var sys = s.SystemDisk;
        if (sys is null) return new Grade(GradeCategory.Storage, 3, "Disk bilgisi okunamadı");

        var score = sys.Kind switch
        {
            DiskKind.NvmeSsd => 5,
            DiskKind.SataSsd => 4,
            DiskKind.Hdd => 2,
            _ => 3,
        };

        if (sys.Kind == DiskKind.Hdd)
        {
            var size = sys.SizeGb >= 900 ? "1 TB" : "500 GB";
            var form = s.Machine.IsLaptop ? "2,5 inç SATA SSD (dizüstüde M.2 yuvası varsa NVMe)" : "M.2 NVMe SSD (anakartta M.2 yuvası yoksa 2,5 inç SATA SSD)";
            advice.Add(new Advice(AdvicePriority.Critical, GradeCategory.Storage, "Windows diskini SSD'ye geçirin",
                $"Windows dönen plakalı bir sabit diskte ({sys.Name}). {form} takıp sistemi kopyalamak, bu bilgisayara yapılabilecek en etkili yükseltme.",
                "Açılış 1 dakikadan 15 saniyeye, programlar 3–5 kat daha hızlı açılır"));
            shopping.Add(new ShoppingItem(1, $"{size} {(s.Machine.IsLaptop ? "2,5\" SATA" : "M.2 NVMe")} SSD", "Windows'u taşımak için"));
        }

        foreach (var d in s.Disks)
        {
            if (d.Health is DiskHealth.Warning or DiskHealth.Unhealthy)
            {
                score = Math.Min(score, 1);
                advice.Add(new Advice(AdvicePriority.Critical, GradeCategory.Storage, $"{d.Name} sorun bildiriyor: hemen yedek alın",
                    $"Disk kendi sağlık bilgisinde \"{d.HealthText}\" durumunu bildiriyor. Önemli dosyaları bugün başka bir diske ya da buluta kopyalayın, diski değiştirin.",
                    "Veri kaybının önüne geçer"));
                shopping.Add(new ShoppingItem(1, $"{Formats.Gb(Math.Max(500, d.SizeGb))} SSD", $"{d.Name} yerine"));
            }
            else if (d.WearPercent is >= 80)
            {
                advice.Add(new Advice(AdvicePriority.Recommended, GradeCategory.Storage, $"{d.Name} ömrünün sonuna yaklaşıyor",
                    $"SSD, öngörülen yazma ömrünün %{d.WearPercent}'ini kullanmış. Düzenli yedek alın ve değişimi planlayın."));
            }
            if (d.TemperatureC is >= 60)
                advice.Add(new Advice(AdvicePriority.Info, GradeCategory.Storage, $"{d.Name} sıcak çalışıyor ({d.TemperatureC} °C)",
                    "Kasa içi hava akışını kontrol edin; NVMe diskler için soğutucu levha işe yarar."));

            foreach (var v in d.Volumes.Where(x => x.SizeGb > 20))
            {
                if (v.FreeRatio < 0.10)
                {
                    if (d.IsSystem) score = Math.Max(1, score - 1);
                    advice.Add(new Advice(AdvicePriority.Critical, GradeCategory.Storage, $"{v.Letter} sürücüsü neredeyse dolu",
                        $"{Formats.Gb(v.SizeGb)} alanın yalnızca {Formats.Gb(v.FreeGb)}'ı boş ({Formats.Percent(v.FreeRatio)}). Windows güncellemeleri ve geçici dosyalar için en az %15 boş alan gerekir; Disk Temizleme ile başlayın.",
                        "Güncelleme hatalarını ve yavaşlamayı önler"));
                }
                else if (v.FreeRatio < 0.20)
                {
                    advice.Add(new Advice(AdvicePriority.Recommended, GradeCategory.Storage, $"{v.Letter} sürücüsünde yer azalıyor",
                        $"{Formats.Gb(v.FreeGb)} boş ({Formats.Percent(v.FreeRatio)}). Büyük indirmeleri ve eski kurulum dosyalarını temizlemek iyi olur."));
                }
            }
        }

        var reason = $"Windows {sys.KindText} üzerinde · {Formats.Gb(sys.SizeGb)}";
        if (s.Disks.Count > 1) reason += $" · {s.Disks.Count} disk";
        return new Grade(GradeCategory.Storage, score, reason);
    }

    // ---------------- Grafik ----------------

    static Grade Graphics(HardwareSnapshot s, List<Advice> advice)
    {
        var g = s.MainGpu;
        if (g is null) return new Grade(GradeCategory.Graphics, 2, "Ekran kartı okunamadı");
        int score;
        if (g.IsIntegrated) score = 2;
        else score = (g.VramGb ?? 0) switch
        {
            >= 7.5 => 5,
            >= 5.5 => 4,
            >= 3.5 => 4,
            _ => 3,
        };

        if (g.IsIntegrated && !s.Machine.IsLaptop)
            advice.Add(new Advice(AdvicePriority.Info, GradeCategory.Graphics, "Yalnızca tümleşik grafik var",
                "Ofis, internet ve film için yeterli. Oyun, video kurgusu ya da 3B çizim düşünülüyorsa PCIe yuvasına harici bir ekran kartı takılabilir; güç kaynağının gücünü kontrol edin."));
        if (g.DriverDate is { } dd && dd < DateOnly.FromDateTime(s.CapturedAt).AddYears(-2))
            advice.Add(new Advice(AdvicePriority.Info, GradeCategory.Graphics, "Ekran kartı sürücüsü eski",
                $"{g.Name} sürücüsü {Formats.Date(dd)} tarihli. Üreticinin sitesinden güncel sürücü, uyumluluk ve performans düzeltmeleri getirir."));

        var reason = g.IsIntegrated ? $"{g.Name} (tümleşik)" : $"{g.Name}{(g.VramGb is { } v ? $" · {Formats.Gb(v)}" : "")}";
        return new Grade(GradeCategory.Graphics, score, reason);
    }

    // ---------------- Pil ----------------

    static Grade Battery(BatteryInfo b, List<Advice> advice, List<ShoppingItem> shopping)
    {
        var wear = b.Wear;
        var score = wear switch
        {
            < 0.10 => 5,
            < 0.20 => 4,
            < 0.30 => 3,
            < 0.45 => 2,
            _ => 1,
        };
        if (wear >= 0.30)
        {
            advice.Add(new Advice(wear >= 0.45 ? AdvicePriority.Critical : AdvicePriority.Recommended, GradeCategory.Battery, "Pil yıpranmış: değişimi düşünün",
                $"Pil fabrika kapasitesinin {Formats.Percent(b.Health)}'ini tutabiliyor ({Formats.Number(b.FullWh, 1)} / {Formats.Number(b.DesignWh, 1)} Wh). Prizden uzakta kullanım süresi neredeyse yarıya inmiş olabilir.",
                "Pil süresi yeniden ilk günkü gibi"));
            shopping.Add(new ShoppingItem(1, $"Uyumlu pil ({Formats.Number(b.DesignWh, 0)} Wh)", "Yıpranan pilin yerine"));
        }
        else if (wear >= 0.20)
        {
            advice.Add(new Advice(AdvicePriority.Info, GradeCategory.Battery, "Pil yıpranmaya başlamış",
                $"Pil sağlığı {Formats.Percent(b.Health)}. Sürekli prizde kullanıyorsanız üreticinin şarjı %80'de kesen ayarı pil ömrünü uzatır."));
        }
        var reason = $"Sağlık {Formats.Percent(b.Health)}{(b.CycleCount is { } c && c > 0 ? $" · {c} döngü" : "")}";
        return new Grade(GradeCategory.Battery, score, reason);
    }

    // ---------------- Sistem ----------------

    static Grade SystemGrade(HardwareSnapshot s, DateTime now, List<Advice> advice)
    {
        var os = s.Os;
        var score = os.IsWindows11 ? 5 : os.IsWindows10 ? 3 : 2;
        if (os.IsWindows10)
            advice.Add(new Advice(AdvicePriority.Recommended, GradeCategory.System, "Windows 10'un ücretsiz desteği bitti",
                "Microsoft, Windows 10'a 14 Ekim 2025'te ücretsiz güvenlik güncellemesi vermeyi bıraktı. Donanım uygunsa Windows 11'e ücretsiz geçilebilir (Ayarlar › Windows Update).",
                "Güvenlik güncellemeleri devam eder"));
        if (s.Security.SecureBoot == false)
        {
            score = Math.Max(1, score - 1);
            advice.Add(new Advice(AdvicePriority.Info, GradeCategory.System, "Güvenli Önyükleme kapalı",
                "UEFI ayarlarından Secure Boot açılabilir; Windows 11 ve bazı oyunların hile korumaları bunu ister."));
        }
        if (s.Security.TpmVersion is { } tpm && !tpm.StartsWith('2'))
            advice.Add(new Advice(AdvicePriority.Info, GradeCategory.System, $"TPM {tpm}",
                "Windows 11 için TPM 2.0 gerekir; birçok anakartta UEFI ayarlarından (fTPM / PTT) açılır."));
        if (os.Uptime(now) is { } up && up.TotalDays >= 7)
            advice.Add(new Advice(AdvicePriority.Info, GradeCategory.System, $"Bilgisayar {(int)up.TotalDays} gündür yeniden başlatılmadı",
                "Bekleyen güncellemelerin kurulması ve belleğin tazelenmesi için ara sıra \"Yeniden başlat\" iyi gelir (Kapat'a basmak hızlı başlatma yüzünden aynı etkiyi vermez)."));
        var reason = $"{os.Name}{(s.Security.SecureBoot == true ? " · Güvenli Önyükleme açık" : "")}";
        return new Grade(GradeCategory.System, score, reason);
    }
}
