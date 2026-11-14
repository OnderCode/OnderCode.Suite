namespace Donanim.WinForms.Controls;

/// <summary>Bir fiziksel disk: tür, bağlantı, sağlık, sıcaklık, çalışma süresi ve bölümlerin doluluğu.</summary>
sealed class DiskTile : AnimatedTile
{
    DiskInfo? _disk;

    public DiskTile() { HoverBorder = true; Size = new Size(600, 220); }

    public DiskInfo? Disk { get => _disk; set { _disk = value; Invalidate(); } }

    public static int HeightFor(DiskInfo d) => 150 + Math.Max(1, d.Volumes.Count) * 52;

    protected override void PaintContent(Graphics g, Rectangle r)
    {
        if (_disk is not { } d) return;
        var c = C;
        var col = App.DiskColor(d.Kind);
        Draw.IconBox(g, Glyph.Disk, col, new RectangleF(r.X, r.Y, 44, 44), OndaTheme.IconFontLarge);
        var x = r.X + 58;
        Gfx.Text(g, d.Name, Draw.Medium, new Rectangle(x, r.Y - 4, r.Width - 58, 28), c.Text);
        var cx = x;
        cx += Draw.Chip(g, d.KindText, col, cx, r.Y + 26) + 6;
        if (d.Bus is { Length: > 0 } bus && bus != "-") cx += Draw.Chip(g, bus, App.Slate, cx, r.Y + 26) + 6;
        var hc = d.Health switch { DiskHealth.Healthy => c.Success, DiskHealth.Warning => c.Warning, DiskHealth.Unhealthy => c.Danger, _ => App.Slate };
        cx += Draw.Chip(g, d.HealthText, hc, cx, r.Y + 26) + 6;
        if (d.IsSystem) Draw.Chip(g, "Windows burada", c.Primary, cx, r.Y + 26);

        // ölçüler
        var stats = new (string Label, string Value)[]
        {
            ("Kapasite", Formats.Gb(d.SizeGb)),
            ("Sıcaklık", d.TemperatureC is { } t ? $"{t} °C" : "-"),
            ("Çalışma süresi", d.PowerOnHours is { } h ? $"{Formats.Number(h)} saat" : "-"),
            ("Aşınma", d.WearPercent is { } w ? $"%{w}" : d.Kind == DiskKind.Hdd ? "mekanik" : "-"),
        };
        var sy = r.Y + 62;
        var sw = r.Width / stats.Length;
        for (var i = 0; i < stats.Length; i++)
        {
            Gfx.Text(g, stats[i].Label, OndaTheme.FontSmall, new Rectangle(r.X + i * sw, sy, sw - 8, 18), c.TextMuted);
            Gfx.Text(g, stats[i].Value, OndaTheme.FontSemiboldLarge, new Rectangle(r.X + i * sw, sy + 18, sw - 8, 26), c.Text);
        }

        var y = sy + 60;
        using (var sep = new Pen(c.Border)) g.DrawLine(sep, r.X, y - 8, r.Right, y - 8);
        if (d.Volumes.Count == 0)
        {
            Gfx.Text(g, "Bu diskte harf atanmış bölüm yok.", OndaTheme.FontSmall, new Rectangle(r.X, y, r.Width, 30), c.TextMuted);
            return;
        }
        foreach (var v in d.Volumes)
        {
            var used = v.SizeGb > 0 ? v.UsedGb / v.SizeGb : 0;
            var bc = v.FreeRatio < 0.10 ? c.Danger : v.FreeRatio < 0.20 ? c.Warning : col;
            Gfx.Text(g, v.DisplayName, OndaTheme.FontSemibold, new Rectangle(r.X, y, r.Width / 2, 20), c.Text);
            Draw.Text(g, $"{Formats.Gb(v.FreeGb)} boş / {Formats.Gb(v.SizeGb)}  ·  {v.FileSystem}", OndaTheme.FontSmall, new Rectangle(r.X + r.Width / 2, y, r.Width / 2, 20), c.TextMuted, Draw.Right);
            Draw.Bar(g, new RectangleF(r.X, y + 24, r.Width, 10), used * Progress, bc);
            y += 52;
        }
    }
}

/// <summary>Pil sağlığı halkası ve ayrıntıları.</summary>
sealed class BatteryTile : AnimatedTile
{
    BatteryInfo? _battery;
    bool _laptop;

    public BatteryTile() { Title = "Pil"; Icon = Glyph.Battery; IconColor = App.Amber; }

    public void SetData(BatteryInfo? battery, bool laptop) { _battery = battery; _laptop = laptop; Invalidate(); }

    protected override void PaintContent(Graphics g, Rectangle r)
    {
        var c = C;
        if (_battery is not { } b)
        {
            Draw.IconBox(g, _laptop ? Glyph.Battery : Glyph.Desktop, App.Slate, new RectangleF(r.X, r.Y + 4, 44, 44), OndaTheme.IconFontLarge);
            Draw.Text(g, _laptop ? "Pil bilgisi okunamadı." : "Masaüstü bilgisayar: pil yok. Kesintisiz güç kaynağı (UPS) ani elektrik kesintisinde disklerinizi korur.",
                OndaTheme.Font, new Rectangle(r.X + 58, r.Y + 6, r.Width - 58, r.Height), c.TextMuted, Draw.Wrap);
            return;
        }
        if (b.DesignWh <= 0)
        {
            Draw.Text(g, "Pil var ama kapasite bilgisi okunamadı (bazı üreticiler bu bilgiyi vermez).", OndaTheme.Font, r, c.TextMuted, Draw.Wrap);
            return;
        }
        var size = Math.Min(r.Height - 10, 150);
        var ring = new RectangleF(r.X, r.Y + (r.Height - size) / 2f, size, size);
        var hc = App.GradeColor(b.Health switch { >= 0.9 => 5, >= 0.8 => 4, >= 0.7 => 3, >= 0.55 => 2, _ => 1 });
        Draw.Ring(g, ring, b.Health * Progress, hc, 12);
        Draw.Text(g, Formats.Percent(b.Health * Progress), Draw.Large, Rectangle.Round(new RectangleF(ring.X, ring.Y + size / 2 - 30, size, 40)), c.Text, Gfx.Center);
        Draw.Text(g, "sağlık", OndaTheme.FontSmall, Rectangle.Round(new RectangleF(ring.X, ring.Y + size / 2 + 10, size, 18)), c.TextMuted, Gfx.Center);

        var x = (int)ring.Right + 28;
        var rows = new (string, string)[]
        {
            ("Fabrika kapasitesi", $"{Formats.Number(b.DesignWh, 1)} Wh"),
            ("Bugünkü tam şarj", $"{Formats.Number(b.FullWh, 1)} Wh"),
            ("Yıpranma", Formats.Percent(b.Wear)),
            ("Şarj döngüsü", b.CycleCount is { } cy ? Formats.Number(cy) : "-"),
            ("Şu anki şarj", b.ChargePercent is { } ch ? $"%{ch}{(b.Charging == true ? " · şarj oluyor" : "")}" : "-"),
        };
        var y = r.Y;
        foreach (var (l, v) in rows)
        {
            Gfx.Text(g, l, OndaTheme.Font, new Rectangle(x, y, 170, 30), c.TextMuted);
            Gfx.Text(g, v, OndaTheme.FontSemibold, new Rectangle(x + 170, y, r.Right - x - 170, 30), c.Text);
            y += 30;
        }
    }
}
