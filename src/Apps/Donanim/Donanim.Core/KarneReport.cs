using System.Text;
using OnderCode.Reporting;

namespace Donanim.Core;

/// <summary>Karneyi PDF/Excel raporuna ve ikinci el ilanı için düz metne çevirir.</summary>
public static class KarneReport
{
    public static ReportDocument Build(Karne k)
    {
        var s = k.Snapshot;
        var doc = new ReportDocument
        {
            Title = "Donanım karnesi",
            Subtitle = $"{s.Machine.DisplayName} · {s.Machine.KindText} · {s.CapturedAt:dd.MM.yyyy HH:mm}",
            Organization = "OnderCode Donanım",
            GeneratedAt = s.CapturedAt,
        };
        doc.Kpis.Add(new ReportKpi("Genel ortalama", Formats.Number(k.Average, 1), k.AverageLabel));
        foreach (var g in k.Grades) doc.Kpis.Add(new ReportKpi(g.Title, g.Score.ToString(), g.Label));

        doc.Tables.Add(ReportTable.For("Karne", k.Grades)
            .Text("Bileşen", g => g.Title, 14).Integer("Not", g => g.Score, 6).Text("Derece", g => g.Label, 10).Text("Gerekçe", g => g.Reason, 60)
            .Build());

        doc.Tables.Add(ReportTable.For("Bileşenler", Components(s))
            .Text("Bileşen", r => r.Name, 18).Text("Bilgi", r => r.Value, 72)
            .Build());

        if (s.Memory.SlotCount > 0)
            doc.Tables.Add(ReportTable.For("Bellek yuvaları", s.Memory.Slots)
                .Text("Yuva", x => x.Locator, 18)
                .Text("Modül", x => x.Module is { } m ? m.ShortText : "Boş", 20)
                .Text("Üretici", x => x.Module?.Manufacturer ?? "", 16)
                .Text("Parça no", x => x.Module?.PartNumber ?? "", 22)
                .Text("Hız", x => x.Module?.SpeedText ?? "", 20)
                .Build());

        doc.Tables.Add(ReportTable.For("Diskler", s.Disks)
            .Text("Disk", d => d.Name, 28).Text("Tür", d => d.KindText, 10).Text("Kapasite", d => Formats.Gb(d.SizeGb), 10)
            .Text("Sağlık", d => d.HealthText, 10).Text("Bölümler", d => string.Join(", ", d.Volumes.Select(v => $"{v.Letter} {Formats.Gb(v.FreeGb)} boş")), 30)
            .Build());

        doc.Tables.Add(ReportTable.For("Öneriler", k.Advice)
            .Text("Öncelik", a => a.PriorityText, 10).Text("Öneri", a => a.Title, 34).Text("Açıklama", a => a.Detail, 56)
            .Build());

        if (k.Shopping.Count > 0)
            doc.Tables.Add(ReportTable.For("Alışveriş listesi", k.Shopping)
                .Integer("Adet", x => x.Quantity, 6).Text("Parça", x => x.Part, 40).Text("Neden", x => x.Why, 44)
                .Build());

        doc.Notes.Add("Bilgiler Windows'un donanım yönetim arayüzünden (WMI) okunmuştur; seri numarası ve MAC adresi gibi kimlik bilgileri okunmaz.");
        if (s.IsSample) doc.Notes.Add("Bu rapor kurgusal bir örnek bilgisayara aittir.");
        return doc;
    }

    public sealed record Row(string Name, string Value);

    public static IReadOnlyList<Row> Components(HardwareSnapshot s)
    {
        var rows = new List<Row>
        {
            new("Bilgisayar", $"{s.Machine.DisplayName} ({s.Machine.KindText})"),
            new("İşlemci", $"{s.Cpu.Name} · {s.Cpu.CoreText} · {s.Cpu.ClockText}"),
            new("Anakart", $"{s.Board.DisplayName} · BIOS {s.Board.BiosVersion} ({Formats.Date(s.Board.BiosDate)})"),
            new("Bellek", MemoryLine(s)),
        };
        foreach (var d in s.Disks) rows.Add(new("Disk", $"{d.Name} · {d.KindText} · {Formats.Gb(d.SizeGb)} · {d.HealthText}"));
        foreach (var g in s.Gpus) rows.Add(new("Ekran kartı", $"{g.Name} · {g.KindText}{(g.VramGb is { } v && !g.IsIntegrated ? " · " + Formats.Gb(v) : "")}"));
        foreach (var m in s.Monitors) rows.Add(new("Ekran", $"{m.Name}{(m.DiagonalInch is { } d2 ? $" · {d2:0.#} inç" : "")}"));
        if (s.Battery is { } b) rows.Add(new("Pil", $"Sağlık {Formats.Percent(b.Health)} · {Formats.Number(b.FullWh, 1)} / {Formats.Number(b.DesignWh, 1)} Wh"));
        rows.Add(new("İşletim sistemi", $"{s.Os.Name} ({s.Os.Version}) · {s.Os.Architecture}"));
        return rows;
    }

    public static string MemoryLine(HardwareSnapshot s)
    {
        var m = s.Memory;
        var parts = new List<string> { Formats.Gb(m.TotalGb) };
        if (m.Type != MemoryType.Unknown) parts.Add(Formats.MemoryType(m.Type) + (m.ConfiguredMts > 0 ? $"-{m.ConfiguredMts}" : ""));
        if (m.SlotCount > 0) parts.Add($"{m.UsedSlots}/{m.SlotCount} yuva");
        if (m.MaxCapacityGb is { } mx) parts.Add($"en fazla {Formats.Gb(mx)}");
        return string.Join(" · ", parts);
    }

    /// <summary>İkinci el ilanı için kısa ve dürüst özellik metni.</summary>
    public static string Listing(Karne k)
    {
        var s = k.Snapshot;
        var sb = new StringBuilder();
        sb.AppendLine($"{s.Machine.DisplayName} ({s.Machine.KindText})");
        sb.AppendLine();
        sb.AppendLine($"• İşlemci: {s.Cpu.Name} ({s.Cpu.Cores} çekirdek / {s.Cpu.Threads} iş parçacığı)");
        sb.AppendLine($"• Bellek: {MemoryLine(s)}");
        foreach (var d in s.Disks) sb.AppendLine($"• Disk: {Formats.Gb(d.SizeGb)} {d.KindText} ({d.HealthText.ToLowerInvariant()})");
        if (s.MainGpu is { } g) sb.AppendLine($"• Ekran kartı: {g.Name}{(g.IsIntegrated ? " (tümleşik)" : g.VramGb is { } v ? $" {Formats.Gb(v)}" : "")}");
        foreach (var m in s.Monitors.Where(x => x.IsInternal && x.DiagonalInch is not null)) sb.AppendLine($"• Ekran: {m.DiagonalInch:0.#} inç, {s.DisplayResolution}{(s.DisplayRefreshHz > 60 ? $", {s.DisplayRefreshHz} Hz" : "")}");
        if (s.Battery is { } b) sb.AppendLine($"• Pil sağlığı: {Formats.Percent(b.Health)}");
        sb.AppendLine($"• {s.Os.Name}");
        sb.AppendLine();
        sb.AppendLine($"Donanım karnesi genel ortalaması: {Formats.Number(k.Average, 1)} ({k.AverageLabel}).");
        sb.Append("Bilgiler OnderCode Donanım ile Windows'tan okunmuştur.");
        return sb.ToString();
    }
}
