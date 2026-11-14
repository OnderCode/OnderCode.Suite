namespace Donanim.WinForms.Controls;

/// <summary>Ortak çizim yardımcıları.</summary>
static class Draw
{
    public static readonly Font Big = new("Segoe UI Semibold", 30f);
    public static readonly Font Large = new("Segoe UI Semibold", 22f);
    public static readonly Font Medium = new("Segoe UI Semibold", 14f);

    public static float Ease(float t) { t = Math.Clamp(t, 0f, 1f); return 1 - (1 - t) * (1 - t) * (1 - t); }

    public const TextFormatFlags Wrap = TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
    public const TextFormatFlags Right = TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;

    public static int WrapHeight(string text, Font font, int width) =>
        string.IsNullOrEmpty(text) ? 0 : TextRenderer.MeasureText(text, font, new Size(Math.Max(10, width), int.MaxValue), Wrap).Height;

    public static void Text(Graphics g, string text, Font font, Rectangle r, Color color, TextFormatFlags flags) =>
        TextRenderer.DrawText(g, text, font, r, color, flags);

    /// <summary>Aşağıdan yukarı okunan dikey metin (GDI+; TextRenderer döndürmeyi desteklemez).</summary>
    public static void Vertical(Graphics g, string text, Font font, PointF center, Color color)
    {
        var st = g.Save();
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        g.TranslateTransform(center.X, center.Y);
        g.RotateTransform(-90);
        using var b = new SolidBrush(color);
        using var f = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(text, font, b, 0, 0, f);
        g.Restore(st);
    }

    public static Color Soft(Color c) => ColorX.Alpha(c, OndaTheme.Mode == OndaThemeMode.Light ? 30 : 55);

    /// <summary>Yuvarlak simge kutusu.</summary>
    public static void IconBox(Graphics g, string icon, Color color, RectangleF r, Font? font = null)
    {
        Gfx.FillRound(g, Soft(color), r, Math.Min(r.Width, r.Height) * 0.28f);
        Gfx.Text(g, icon, font ?? OndaTheme.IconFont, Rectangle.Round(r), ColorX.Readable(color), Gfx.Center);
    }

    /// <summary>Kapsül etiket; genişliğini döndürür.</summary>
    public static int Chip(Graphics g, string text, Color color, int x, int y, int h = 22)
    {
        var w = Gfx.Measure(text, OndaTheme.FontSemiboldSmall).Width + 16;
        var r = new RectangleF(x, y, w, h);
        Gfx.FillRound(g, Soft(color), r, h / 2f);
        Gfx.Text(g, text, OndaTheme.FontSemiboldSmall, Rectangle.Round(r), ColorX.Readable(color), Gfx.Center);
        return w;
    }

    /// <summary>Halka gösterge: oran 0..1.</summary>
    public static void Ring(Graphics g, RectangleF r, double ratio, Color color, float thickness)
    {
        var c = OndaTheme.Colors;
        var inset = thickness / 2;
        var rr = RectangleF.Inflate(r, -inset, -inset);
        using (var track = new Pen(c.SurfaceAlt, thickness))
            g.DrawEllipse(track, rr);
        if (ratio <= 0) return;
        using var pen = new Pen(color, thickness) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawArc(pen, rr, -90, (float)(360 * Math.Clamp(ratio, 0, 1)));
    }

    /// <summary>Yatay doluluk çubuğu.</summary>
    public static void Bar(Graphics g, RectangleF r, double ratio, Color color)
    {
        var c = OndaTheme.Colors;
        Gfx.FillRound(g, c.SurfaceAlt, r, r.Height / 2);
        if (ratio <= 0) return;
        var w = Math.Max(r.Height, (float)(r.Width * Math.Clamp(ratio, 0, 1)));
        Gfx.FillRound(g, color, new RectangleF(r.X, r.Y, w, r.Height), r.Height / 2);
    }
}

/// <summary>Kart zemini olan, isteğe bağlı başlıklı Onda kontrolü.</summary>
abstract class Tile : OndaControl
{
    protected const int HeaderH = 52;
    string _title = "", _icon = "";

    public string Title { get => _title; set { _title = value ?? ""; Invalidate(); } }
    public string Icon { get => _icon; set { _icon = value ?? ""; Invalidate(); } }
    public Color IconColor { get; set; } = Color.Empty;
    /// <summary>Başlığın sağındaki kısa not.</summary>
    public string Note { get; set; } = "";
    protected bool HoverBorder { get; set; }

    protected Rectangle Content => _title.Length > 0
        ? new Rectangle(16, HeaderH + 12, Width - 32, Height - HeaderH - 24)
        : new Rectangle(18, 16, Width - 36, Height - 32);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Smooth();
        var c = C;
        var card = new RectangleF(0, 0, Width - 1, Height - 1);
        float rad = OndaTheme.Radius + 4;
        Gfx.FillRound(g, c.Surface, card, rad);
        Gfx.DrawRound(g, HoverBorder ? ColorX.Blend(c.Border, c.Primary, HoverT * 0.5f) : c.Border, card, rad);
        if (_title.Length > 0)
        {
            var x = 16;
            if (_icon.Length > 0)
            {
                Draw.IconBox(g, _icon, IconColor.IsEmpty ? c.Primary : IconColor, new RectangleF(x, 12, 28, 28));
                x += 38;
            }
            var noteW = Note.Length > 0 ? Gfx.Measure(Note, OndaTheme.FontSmall).Width + 8 : 0;
            Gfx.Text(g, _title, OndaTheme.FontTitle, new Rectangle(x, 0, Width - x - 16 - noteW, HeaderH), c.Text);
            if (noteW > 0) Draw.Text(g, Note, OndaTheme.FontSmall, new Rectangle(Width - 16 - noteW, 0, noteW, HeaderH), c.TextMuted, Draw.Right);
            using var p = new Pen(c.Border);
            g.DrawLine(p, 1, HeaderH - 1, Width - 2, HeaderH - 1);
        }
        PaintContent(g, Content);
    }

    protected abstract void PaintContent(Graphics g, Rectangle r);
}

/// <summary>Bir kez oynayan giriş animasyonu (0→1).</summary>
abstract class AnimatedTile : Tile
{
    float _t = 1f;
    protected float Progress => Draw.Ease(_t);

    public void Replay() { _t = 0f; Animate(); }

    protected override bool OnAnimate()
    {
        if (_t >= 1f) return false;
        _t = Math.Min(1f, _t + 0.045f);
        return _t < 1f;
    }
}
