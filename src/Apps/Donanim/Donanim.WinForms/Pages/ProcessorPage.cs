using Donanim.Windows;
using Donanim.WinForms.Controls;

namespace Donanim.WinForms.Pages;

/// <summary>İşlemci ve anakart: çekirdek, saat hızı, önbellek, sanallaştırma, anakart ve BIOS; canlı işlemci/bellek grafiği.</summary>
sealed class ProcessorPage : KarnePage
{
    const int Points = 60;
    readonly ValueTile _cores = new() { Label = "Çekirdek / iş parçacığı", Icon = Glyph.Cpu };
    readonly ValueTile _clock = new() { Label = "Temel saat hızı", Icon = Glyph.Speed, IconColor = App.Pink };
    readonly ValueTile _cache = new() { Label = "Önbellek (L2 / L3)", Icon = Glyph.Memory, IconColor = App.Violet };
    readonly ValueTile _virt = new() { Label = "Sanallaştırma", Icon = Glyph.Shield, IconColor = App.Teal };
    readonly InfoList _cpu = new() { Title = "İşlemci", Icon = Glyph.Cpu, LabelWidth = 150 };
    readonly InfoList _board = new() { Title = "Anakart ve BIOS", Icon = Glyph.System, IconColor = App.Cyan, LabelWidth = 150 };
    readonly OndaChart _live = new() { Title = "Anlık kullanım", Subtitle = "Son 1,5 dakika · işlemci ve bellek (%)", ChartType = OndaChartType.Area };
    readonly System.Windows.Forms.Timer _timer = new() { Interval = 1500 };
    readonly double[] _cpuHist = new double[Points];
    readonly double[] _memHist = new double[Points];
    LiveUsage? _usage;
    readonly Random _rnd = new(7);

    public ProcessorPage() : base("İşlemci ve anakart", "Yükleniyor…")
    {
        _live.Labels = Enumerable.Range(0, Points).Select(i => i % 10 == 0 ? $"-{(Points - i) * 3 / 2} sn" : "").ToArray();
        _live.ValueFormat = v => "%" + v.ToString("0");
        _live.Series.Add(new OndaSeries("İşlemci", _cpuHist));
        _live.Series.Add(new OndaSeries("Bellek", _memHist, App.Violet));
        _timer.Tick += (_, _) => Tick();
        VisibleChanged += (_, _) => _timer.Enabled = Visible;
        Controls.AddRange(new Control[] { _cores, _clock, _cache, _virt, _cpu, _board, _live });
    }

    protected override void Bind(Karne k)
    {
        var s = k.Snapshot;
        var c = s.Cpu;
        var b = s.Board;
        Subtitle = $"{MachineLine(s)}  ·  {c.Name}";
        _cores.Set($"{c.Cores} / {c.Threads}", c.Threads >= 12 ? "çoklu işte güçlü" : c.Threads >= 8 ? "günlük işler için rahat" : "yoğun işte zorlanabilir");
        _clock.Set(c.ClockText, "Windows'un bildirdiği en yüksek temel hız");
        _cache.Set($"{Kb(c.L2CacheKb)} / {Kb(c.L3CacheKb)}", "işlemcinin içindeki hızlı bellek");
        _virt.Set(Formats.YesNo(c.Virtualization), c.Virtualization == false ? "BIOS'ta açılabilir" : "sanal makine, WSL, öykünücü",
            c.Virtualization switch { true => OndaTheme.Colors.Success, false => OndaTheme.Colors.Warning, _ => null });
        _cpu.SetRows(
        [
            ("Model", c.Name, null),
            ("Üretici", c.Manufacturer.Length > 0 ? c.Manufacturer : "-", null),
            ("Çekirdek", Formats.Number(c.Cores), null),
            ("İş parçacığı", Formats.Number(c.Threads), null),
            ("Saat hızı", c.ClockText, null),
            ("Soket", c.Socket.Length > 0 ? c.Socket : "-", null),
        ]);
        var biosAge = b.BiosDate is { } bd ? (DateOnly.FromDateTime(s.CapturedAt).DayNumber - bd.DayNumber) / 365.0 : 0;
        _board.SetRows(
        [
            ("Bilgisayar", s.Machine.DisplayName, null),
            ("Anakart", b.DisplayName.Length > 0 ? b.DisplayName : "-", null),
            ("BIOS üreticisi", b.BiosVendor.Length > 0 ? b.BiosVendor : "-", null),
            ("BIOS sürümü", b.BiosVersion.Length > 0 ? b.BiosVersion : "-", null),
            ("BIOS tarihi", Formats.Date(b.BiosDate) + (biosAge >= 3 ? $" ({(int)biosAge} yıl önce)" : ""), biosAge >= 3 ? (Color?)OndaTheme.Colors.Warning : null),
            ("Tür", s.Machine.KindText, null),
        ]);
        Seed(s);
        _live.Replay();
        _timer.Enabled = Visible;
    }

    static string Kb(int kb) => kb <= 0 ? "-" : kb >= 1024 ? $"{kb / 1024.0:0.#} MB" : $"{kb} KB";

    void Seed(HardwareSnapshot s)
    {
        _usage = s.IsSample ? null : new LiveUsage();
        var baseCpu = s.Cpu.Threads <= 8 ? 34 : 12;
        var baseMem = s.Memory.TotalGb <= 8 ? 82 : 41;
        for (var i = 0; i < Points; i++)
        {
            _cpuHist[i] = s.IsSample ? Math.Clamp(baseCpu + 18 * Math.Sin(i / 6.0) + _rnd.Next(-8, 9), 2, 100) : 0;
            _memHist[i] = s.IsSample ? Math.Clamp(baseMem + _rnd.Next(-2, 3), 0, 100) : 0;
        }
        if (!s.IsSample)
        {
            var (r, _, _) = LiveUsage.Memory();
            Array.Fill(_memHist, r * 100);
        }
    }

    void Tick()
    {
        if (App.Snapshot is not { } s) return;
        Array.Copy(_cpuHist, 1, _cpuHist, 0, Points - 1);
        Array.Copy(_memHist, 1, _memHist, 0, Points - 1);
        if (_usage is not null)
        {
            _cpuHist[^1] = _usage.Cpu() * 100;
            _memHist[^1] = LiveUsage.Memory().Ratio * 100;
        }
        else
        {
            var baseCpu = s.Cpu.Threads <= 8 ? 34 : 12;
            _cpuHist[^1] = Math.Clamp(baseCpu + 18 * Math.Sin(Environment.TickCount64 / 9000.0) + _rnd.Next(-8, 9), 2, 100);
            _memHist[^1] = Math.Clamp(_memHist[^2] + _rnd.Next(-1, 2), 0, 100);
        }
        _live.Invalidate();
    }

    protected override void LayoutPage(Rectangle r)
    {
        var cards = new[] { _cores, _clock, _cache, _virt };
        var cw = (r.Width - Gap * 3) / 4;
        for (var i = 0; i < 4; i++) cards[i].SetBounds(r.X + i * (cw + Gap), r.Y, cw, 106);
        var y = r.Y + 106 + Gap;
        var lh = _cpu.PreferredHeight;
        var half = (r.Width - Gap) / 2;
        _cpu.SetBounds(r.X, y, half, lh);
        _board.SetBounds(r.X + half + Gap, y, r.Width - half - Gap, lh);
        y += lh + Gap;
        _live.SetBounds(r.X, y, r.Width, Math.Max(200, r.Bottom - y));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
        base.Dispose(disposing);
    }
}
