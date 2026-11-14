using Donanim.Windows;
using Donanim.WinForms.Controls;

namespace Donanim.WinForms.Pages;

/// <summary>Bellek: anakart üzerinde yuvaların çizimi, dolu/boş yuvalar, modül ayrıntıları, en fazla kapasite ve anlık kullanım.</summary>
sealed class MemoryPage : KarnePage
{
    readonly ValueTile _total = new() { Label = "Toplam bellek", Icon = Glyph.Memory, IconColor = App.Violet };
    readonly ValueTile _slots = new() { Label = "Dolu yuva", Icon = Glyph.Memory, IconColor = App.Teal };
    readonly ValueTile _max = new() { Label = "Anakartın sınırı", Icon = Glyph.Speed, IconColor = App.Amber };
    readonly ValueTile _speed = new() { Label = "Çalışma hızı", Icon = Glyph.Speed, IconColor = App.Pink };
    readonly ValueTile _usage = new() { Label = "Şu an kullanılan", Icon = Glyph.Clock, IconColor = App.Cyan };
    readonly SlotBoard _board = new() { Title = "Anakart ve bellek yuvaları", Icon = Glyph.Memory, IconColor = App.Violet };
    readonly InfoList _detail = new() { Title = "Yuva ayrıntısı", Icon = Glyph.Info, LabelWidth = 120, RowHeight = 32 };
    readonly AdviceList _advice = new() { Title = "Bellek önerileri", Icon = Glyph.Bulb, IconColor = OndaTheme.Colors.Warning };
    readonly System.Windows.Forms.Timer _timer = new() { Interval = 1500 };
    MemoryInfo? _memory;

    public MemoryPage() : base("Bellek (RAM)", "Yükleniyor…")
    {
        _board.HoverChanged += (_, _) => ShowSlot(_board.HoverIndex);
        _timer.Tick += (_, _) => UpdateUsage();
        VisibleChanged += (_, _) => _timer.Enabled = Visible && App.Snapshot is { IsSample: false };
        Controls.AddRange(new Control[] { _total, _slots, _max, _speed, _usage, _board, _detail, _advice });
    }

    public SlotBoard Board => _board;

    protected override void Bind(Karne k)
    {
        var s = k.Snapshot;
        var m = s.Memory;
        _memory = m;
        Subtitle = $"{MachineLine(s)}  ·  {KarneReport.MemoryLine(s)}  ·  yuvanın üzerine gelin";
        var type = Formats.MemoryType(m.Type);
        _total.Set(Formats.Gb(m.TotalGb), m.Type != MemoryType.Unknown ? $"{type} · {Formats.FormFactor(m.FormFactor)}" : "");
        _slots.Set(m.SlotCount > 0 ? $"{m.UsedSlots} / {m.SlotCount}" : "-", m.FreeSlots > 0 ? $"{m.FreeSlots} yuva boş" : m.SlotCount > 0 ? "bütün yuvalar dolu" : "",
            m.FreeSlots > 0 ? (Color?)OndaTheme.Colors.Success : null);
        _max.Set(m.MaxCapacityGb is { } mx ? Formats.Gb(mx) : "-", m.MaxCapacityGb is { } mx2 ? $"{Formats.Gb(Math.Max(0, mx2 - m.TotalGb))} daha eklenebilir" : "BIOS bildirmiyor");
        var slow = m.RatedMts > 0 && m.ConfiguredMts > 0 && m.ConfiguredMts < m.RatedMts * 0.9;
        _speed.Set(m.ConfiguredMts > 0 ? $"{m.ConfiguredMts} MT/s" : "-", slow ? $"modül {m.RatedMts} MT/s destekliyor" : m.IsSingleChannel ? "tek kanal" : m.UsedSlots >= 2 ? "çift kanal" : "",
            slow ? (Color?)OndaTheme.Colors.Warning : null);
        _board.SetData(m, s.Cpu.Name, s.Cpu.Socket, s.Machine.IsLaptop);
        _advice.Items = k.Advice.Where(a => a.Category == GradeCategory.Memory).ToList();
        if (_advice.Items.Count == 0) _advice.Items = [new Advice(AdvicePriority.Good, GradeCategory.Memory, "Bellek yeterli", "Bu bilgisayarın belleği için bir öneri yok.")];
        UpdateUsage();
        ShowSlot(-1);
        _timer.Enabled = Visible && !s.IsSample;
    }

    void UpdateUsage()
    {
        if (App.Snapshot is not { } s) return;
        if (s.IsSample)
        {
            var ratio = s.Memory.TotalGb <= 8 ? 0.83 : 0.41;
            _usage.Set(Formats.Percent(ratio), $"{Formats.Gb(s.Memory.TotalGb * ratio)} / {Formats.Gb(s.Memory.TotalGb)} (örnek)", ratio > 0.8 ? (Color?)OndaTheme.Colors.Danger : null);
            return;
        }
        var (r, used, total) = LiveUsage.Memory();
        _usage.Set(Formats.Percent(r), $"{Formats.Number(used, 1)} / {Formats.Number(total, 1)} GB", r > 0.85 ? OndaTheme.Colors.Danger : r > 0.7 ? (Color?)OndaTheme.Colors.Warning : null);
    }

    void ShowSlot(int index)
    {
        if (_memory is null) return;
        if (index < 0) index = _memory.Slots.ToList().FindIndex(x => !x.IsEmpty);
        if (index < 0 || index >= _memory.Slots.Count)
        {
            _detail.SetRows([("Yuva", "-", null)]);
            return;
        }
        var slot = _memory.Slots[index];
        _detail.Title = $"Yuva {index + 1} · {slot.Locator}";
        if (slot.Module is not { } mod)
        {
            var hint = _memory.Modules.FirstOrDefault() is { } first
                ? $"{Formats.Gb(first.CapacityGb)} {first.TypeText}-{Math.Max(first.RatedMts, first.ConfiguredMts)} {Formats.FormFactorShort(first.FormFactor)}"
                : "-";
            _detail.SetRows(
            [
                ("Durum", "Boş", OndaTheme.Colors.Success),
                ("Takılabilir", hint, null),
                ("Neden aynısı", "Aynı boyut ve hız çift kanal sağlar", null),
                ("Banka", slot.Bank.Length > 0 ? slot.Bank : "-", null),
            ]);
            return;
        }
        _detail.SetRows(
        [
            ("Durum", "Dolu", App.Violet),
            ("Kapasite", Formats.Gb(mod.CapacityGb), null),
            ("Tür", $"{mod.TypeText} · {Formats.FormFactorShort(mod.FormFactor)}", null),
            ("Hız", mod.SpeedText, mod.ConfiguredMts < mod.RatedMts * 0.9 ? (Color?)OndaTheme.Colors.Warning : null),
            ("Üretici", mod.Manufacturer.Length > 0 ? mod.Manufacturer : "-", null),
            ("Parça no", mod.PartNumber.Length > 0 ? mod.PartNumber : "-", null),
            ("Banka", slot.Bank.Length > 0 ? slot.Bank : "-", null),
        ]);
    }

    protected override void LayoutPage(Rectangle r)
    {
        var cards = new[] { _total, _slots, _max, _speed, _usage };
        var cw = (r.Width - Gap * (cards.Length - 1)) / cards.Length;
        for (var i = 0; i < cards.Length; i++) cards[i].SetBounds(r.X + i * (cw + Gap), r.Y, cw, 106);
        var y = r.Y + 106 + Gap;
        var h = r.Bottom - y;
        var bw = (int)(r.Width * 0.64);
        _board.SetBounds(r.X, y, bw, h);
        var rx = r.X + bw + Gap;
        var rw = r.Width - bw - Gap;
        var dh = Math.Min(h / 2 + 30, 300);
        _detail.SetBounds(rx, y, rw, dh);
        _advice.SetBounds(rx, y + dh + Gap, rw, h - dh - Gap);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
        base.Dispose(disposing);
    }
}
