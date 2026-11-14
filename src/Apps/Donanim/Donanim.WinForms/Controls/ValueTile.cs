namespace Donanim.WinForms.Controls;

/// <summary>Küçük ölçü kartı: simge, etiket, büyük değer ve alt not.</summary>
sealed class ValueTile : Tile
{
    public ValueTile() => Size = new Size(240, 110);

    public string Label { get; set; } = "";
    public string Value { get; set; } = "";
    public string Sub { get; set; } = "";
    public Color ValueColor { get; set; } = Color.Empty;

    public void Set(string value, string sub = "", Color? color = null)
    {
        Value = value;
        Sub = sub;
        ValueColor = color ?? Color.Empty;
        Invalidate();
    }

    protected override void PaintContent(Graphics g, Rectangle r)
    {
        var c = C;
        var col = IconColor.IsEmpty ? c.Primary : IconColor;
        var x = r.X;
        if (Icon.Length > 0 && Title.Length == 0)
        {
            Draw.IconBox(g, Icon, col, new RectangleF(r.X, r.Y, 32, 32));
            x += 42;
        }
        Gfx.Text(g, Label, OndaTheme.FontSemiboldSmall, new Rectangle(x, r.Y, r.Right - x, 32), c.TextMuted);
        var vc = ValueColor.IsEmpty ? c.Text : ColorX.Readable(ValueColor);
        Gfx.Text(g, Value, Draw.Medium, new Rectangle(r.X, r.Y + 36, r.Width, 30), vc);
        if (Sub.Length > 0) Gfx.Text(g, Sub, OndaTheme.FontSmall, new Rectangle(r.X, r.Y + 66, r.Width, 18), c.TextMuted);
    }
}

/// <summary>Başlıklı, kaydırılmadan okunan düz metin kartı.</summary>
sealed class TextTile : Tile
{
    string _text = "";
    public string Body { get => _text; set { _text = value ?? ""; Invalidate(); } }
    public bool Mono { get; set; }

    protected override void PaintContent(Graphics g, Rectangle r)
    {
        var font = Mono ? OndaTheme.FontMono : OndaTheme.Font;
        Draw.Text(g, _text, font, r, C.Text, Draw.Wrap | TextFormatFlags.EndEllipsis);
    }
}
