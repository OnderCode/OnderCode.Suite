namespace Donanim.WinForms.Controls;

/// <summary>Karnedeki bir ders: simge, bileşen adı, büyük not, derece, beş nokta ve gerekçe.</summary>
sealed class GradeTile : AnimatedTile
{
    Grade? _grade;

    public GradeTile() { HoverBorder = true; Cursor = Cursors.Hand; Size = new Size(260, 150); }

    public Grade? Grade { get => _grade; set { _grade = value; Invalidate(); } }

    protected override void PaintContent(Graphics g, Rectangle r)
    {
        if (_grade is null) return;
        var c = C;
        var col = App.CategoryColor(_grade.Category);
        Draw.IconBox(g, App.CategoryIcon(_grade.Category), col, new RectangleF(r.X, r.Y, 36, 36));
        Gfx.Text(g, _grade.Title, OndaTheme.FontSemibold, new Rectangle(r.X + 46, r.Y, r.Width - 46 - 34, 36), c.TextMuted);

        var gc = App.GradeColor(_grade.Score);
        var p = Progress;
        var shown = Math.Max(1, (int)Math.Round(_grade.Score * p));
        var scoreRect = new Rectangle(r.Right - 40, r.Y - 6, 44, 52);
        Draw.Text(g, shown.ToString(), Draw.Big, scoreRect, ColorX.Readable(gc), TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        var y = r.Y + 48;
        Gfx.Text(g, _grade.Label, OndaTheme.FontSemiboldLarge, new Rectangle(r.X, y, 120, 26), ColorX.Readable(gc));
        // beş nokta
        for (var i = 0; i < 5; i++)
        {
            var on = i < _grade.Score * p;
            var dot = new RectangleF(r.Right - 5 * 16 + i * 16 + 4, y + 8, 10, 10);
            using var b = new SolidBrush(on ? gc : c.SurfaceAlt);
            g.FillEllipse(b, dot);
        }
        Draw.Text(g, _grade.Reason, OndaTheme.FontSmall, new Rectangle(r.X, y + 32, r.Width, r.Bottom - y - 32), c.TextMuted, Draw.Wrap | TextFormatFlags.EndEllipsis);
    }
}

/// <summary>Karnenin başı: bilgisayarın adı, türü, temel özellikleri ve genel ortalama halkası.</summary>
sealed class HeroTile : AnimatedTile
{
    Karne? _karne;

    public HeroTile() => Size = new Size(520, 300);

    public Karne? Karne { get => _karne; set { _karne = value; Invalidate(); } }

    protected override void PaintContent(Graphics g, Rectangle r)
    {
        if (_karne is null) return;
        var c = C;
        var s = _karne.Snapshot;

        var ringSize = Math.Min(136, r.Height - 50);
        var ring = new RectangleF(r.Right - ringSize - 6, r.Y + (r.Height - ringSize) / 2f - 10, ringSize, ringSize);
        var avg = _karne.Average;
        var gc = App.GradeColor(avg);
        Draw.Ring(g, ring, avg / 5 * Progress, gc, 14);
        var shown = avg * Progress;
        Draw.Text(g, Formats.Number(shown, 1), Draw.Large, Rectangle.Round(new RectangleF(ring.X, ring.Y + ring.Height / 2 - 34, ring.Width, 44)), c.Text, Gfx.Center);
        Draw.Text(g, _karne.AverageLabel, OndaTheme.FontSemibold, Rectangle.Round(new RectangleF(ring.X, ring.Y + ring.Height / 2 + 8, ring.Width, 22)), ColorX.Readable(gc), Gfx.Center);
        Draw.Text(g, "genel ortalama", OndaTheme.FontSmall, Rectangle.Round(new RectangleF(ring.X, ring.Bottom + 6, ring.Width, 20)), c.TextMuted, Gfx.Center);

        var leftW = (int)ring.X - r.X - 14;
        Draw.IconBox(g, s.Machine.IsLaptop ? Glyph.Laptop : Glyph.Desktop, c.Primary, new RectangleF(r.X, r.Y, 48, 48), OndaTheme.IconFontLarge);
        var nameFont = Gfx.Measure(s.Machine.DisplayName, Draw.Medium).Width <= leftW - 60 ? Draw.Medium : OndaTheme.FontSemiboldLarge;
        Gfx.Text(g, s.Machine.DisplayName, nameFont, new Rectangle(r.X + 60, r.Y - 2, leftW - 60, 30), c.Text);
        var sub = s.IsSample ? $"{s.Machine.KindText} · kurgusal örnek" : $"{s.Machine.KindText} · {s.CapturedAt:dd.MM.yyyy HH:mm} taraması";
        Gfx.Text(g, sub, OndaTheme.FontSmall, new Rectangle(r.X + 60, r.Y + 28, leftW - 60, 20), c.TextMuted);

        var rows = new (string Icon, string Label, string Value)[]
        {
            (Glyph.Cpu, "İşlemci", s.Cpu.ShortName),
            (Glyph.Memory, "Bellek", $"{Formats.Gb(s.Memory.TotalGb)} {(s.Memory.Type != MemoryType.Unknown ? Formats.MemoryType(s.Memory.Type) + (s.Memory.ConfiguredMts > 0 ? "-" + s.Memory.ConfiguredMts : "") : "")}".Trim()),
            (Glyph.Disk, "Windows diski", s.SystemDisk is { } d ? $"{Formats.Gb(d.SizeGb)} {d.KindText}" : "-"),
            (Glyph.Monitor, "Ekran kartı", s.MainGpu?.Name ?? "-"),
            (Glyph.System, "Sistem", s.Os.ShortName),
        };
        var y = r.Y + 66;
        var rowH = Math.Max(30, Math.Min(38, (r.Bottom - y) / rows.Length));
        foreach (var (icon, label, value) in rows)
        {
            Gfx.Text(g, icon, OndaTheme.IconFont, new Rectangle(r.X + 2, y, 22, rowH), c.TextMuted, Gfx.Center);
            Gfx.Text(g, label, OndaTheme.FontSmall, new Rectangle(r.X + 32, y, 96, rowH), c.TextMuted);
            Gfx.Text(g, value, OndaTheme.FontSemibold, new Rectangle(r.X + 128, y, leftW - 128, rowH), c.Text);
            y += rowH;
        }
    }
}
