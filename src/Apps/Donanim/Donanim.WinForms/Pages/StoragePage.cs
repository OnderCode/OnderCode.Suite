using Donanim.WinForms.Controls;

namespace Donanim.WinForms.Pages;

/// <summary>Depolama: diskler (NVMe, SATA SSD, HDD), sağlık, sıcaklık, çalışma süresi ve bölümlerin doluluğu.</summary>
sealed class StoragePage : KarnePage
{
    readonly ValueTile _capacity = new() { Label = "Toplam kapasite", Icon = Glyph.Disk, IconColor = App.Teal };
    readonly ValueTile _free = new() { Label = "Boş alan", Icon = Glyph.Disk, IconColor = App.Cyan };
    readonly ValueTile _system = new() { Label = "Windows diski", Icon = Glyph.Speed, IconColor = App.Violet };
    readonly ValueTile _health = new() { Label = "Sağlık", Icon = Glyph.Shield, IconColor = App.Lime };
    readonly OndaPanel _host = ScrollHost();
    readonly AdviceList _advice = new() { Title = "Depolama önerileri", Icon = Glyph.Bulb, IconColor = OndaTheme.Colors.Warning };
    readonly List<DiskTile> _disks = [];

    public StoragePage() : base("Depolama", "Yükleniyor…")
    {
        Controls.AddRange(new Control[] { _capacity, _free, _system, _health, _host, _advice });
    }

    protected override void Bind(Karne k)
    {
        var s = k.Snapshot;
        var vols = s.Disks.SelectMany(d => d.Volumes).ToList();
        Subtitle = $"{MachineLine(s)}  ·  {s.Disks.Count} disk, {vols.Count} bölüm";
        _capacity.Set(Formats.Gb(s.Disks.Sum(d => d.SizeGb)), string.Join(" + ", s.Disks.Select(d => Formats.Gb(d.SizeGb))));
        var free = vols.Sum(v => v.FreeGb);
        var size = vols.Sum(v => v.SizeGb);
        _free.Set(Formats.Gb(free), size > 0 ? $"{Formats.Percent(free / size)} boş" : "");
        if (s.SystemDisk is { } sys)
            _system.Set(sys.KindText, sys.Kind == DiskKind.Hdd ? "SSD'ye geçmek en büyük kazanç" : sys.Kind == DiskKind.NvmeSsd ? "en hızlı tür" : "hızlı", sys.Kind == DiskKind.Hdd ? OndaTheme.Colors.Danger : OndaTheme.Colors.Success);
        var bad = s.Disks.Count(d => d.Health is DiskHealth.Warning or DiskHealth.Unhealthy);
        var unknown = s.Disks.All(d => d.Health == DiskHealth.Unknown);
        _health.Set(bad > 0 ? $"{bad} disk sorunlu" : unknown ? "Bilinmiyor" : "Hepsi sağlıklı", s.Disks.Any(d => d.TemperatureC is not null) ? $"en sıcak {s.Disks.Max(d => d.TemperatureC ?? 0)} °C" : "sıcaklık için yönetici olarak açın",
            bad > 0 ? OndaTheme.Colors.Danger : unknown ? null : (Color?)OndaTheme.Colors.Success);

        _host.SuspendLayout();
        foreach (var t in _disks) { _host.Controls.Remove(t); t.Dispose(); }
        _disks.Clear();
        foreach (var d in s.Disks)
        {
            var t = new DiskTile { Disk = d };
            _disks.Add(t);
            _host.Controls.Add(t);
        }
        _host.ResumeLayout();
        var adv = k.Advice.Where(a => a.Category == GradeCategory.Storage).ToList();
        _advice.Items = adv.Count > 0 ? adv : [new Advice(AdvicePriority.Good, GradeCategory.Storage, "Diskler yolunda", "Yer, tür ve sağlık açısından bir sorun bulunmadı.")];
    }

    protected override void LayoutPage(Rectangle r)
    {
        var cards = new[] { _capacity, _free, _system, _health };
        var cw = (r.Width - Gap * 3) / 4;
        for (var i = 0; i < 4; i++) cards[i].SetBounds(r.X + i * (cw + Gap), r.Y, cw, 106);
        var y = r.Y + 106 + Gap;
        var h = r.Bottom - y;
        var lw = (int)(r.Width * 0.62);
        _host.SetBounds(r.X, y, lw, h);
        var ty = 0;
        var inner = lw - (_disks.Sum(d => DiskTile.HeightFor(d.Disk!) + Gap) > h ? SystemInformation.VerticalScrollBarWidth + 4 : 0);
        foreach (var t in _disks)
        {
            var th = DiskTile.HeightFor(t.Disk!);
            t.SetBounds(0, ty + _host.AutoScrollPosition.Y, inner, th);
            ty += th + Gap;
        }
        _advice.SetBounds(r.X + lw + Gap, y, r.Width - lw - Gap, h);
    }
}
