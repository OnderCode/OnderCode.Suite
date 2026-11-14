namespace Donanim.WinForms.Controls;

/// <summary>Öneri listesi: öncelik simgesi, başlık, açıklama ve kazanım etiketi.</summary>
sealed class AdviceList : Tile
{
    IReadOnlyList<Advice> _items = [];
    const int IconW = 40, Pad = 14;

    public IReadOnlyList<Advice> Items { get => _items; set { _items = value; Invalidate(); } }
    /// <summary>0: hepsi. Sığmayanlar çizilmez.</summary>
    public int MaxItems { get; set; }
    public bool Compact { get; set; }

    IEnumerable<Advice> Shown => MaxItems > 0 ? _items.Take(MaxItems) : _items;

    static readonly int LineH = TextRenderer.MeasureText("Ağ", OndaTheme.FontSmall).Height;

    int DetailHeight(Advice a, int tw)
    {
        var full = Draw.WrapHeight(a.Detail, OndaTheme.FontSmall, tw);
        return Compact ? Math.Min(full, LineH * 2) : full;
    }

    static int BenefitHeight(Advice a, int tw) => a.Benefit is null ? 0 : Draw.WrapHeight(a.Benefit, OndaTheme.FontSemiboldSmall, tw - 20) + 10;

    int ItemHeight(Advice a, int width)
    {
        var tw = width - IconW - 12;
        var h = 26 + DetailHeight(a, tw) + 4;
        if (!Compact) h += BenefitHeight(a, tw);
        return Math.Max(IconW, h) + Pad * 2;
    }

    /// <summary>Verilen genişlikte bütün öğeler için gereken yükseklik.</summary>
    public int PreferredHeightFor(int width)
    {
        var inner = width - 32;
        var h = (Title.Length > 0 ? HeaderH + 4 : 8) + 8;
        foreach (var a in Shown) h += ItemHeight(a, inner);
        return h;
    }

    protected override void PaintContent(Graphics g, Rectangle r)
    {
        var c = C;
        var y = Title.Length > 0 ? HeaderH : 8;
        var first = true;
        foreach (var a in Shown)
        {
            var h = ItemHeight(a, r.Width);
            if (y + h > Height - 4 && !first) break;
            if (!first)
            {
                using var sep = new Pen(c.Border);
                g.DrawLine(sep, r.X, y, r.Right, y);
            }
            first = false;
            var col = App.PriorityColor(a.Priority);
            var top = y + Pad;
            Draw.IconBox(g, App.PriorityIcon(a.Priority), col, new RectangleF(r.X, top, IconW, IconW));
            var tx = r.X + IconW + 12;
            var tw = r.Right - tx;
            var chipW = Gfx.Measure(a.PriorityText, OndaTheme.FontSemiboldSmall).Width + 16;
            Gfx.Text(g, a.Title, OndaTheme.FontSemibold, new Rectangle(tx, top, tw - chipW - 8, 22), c.Text);
            Draw.Chip(g, a.PriorityText, col, r.Right - chipW, top);
            var dy = top + 26;
            var detailH = DetailHeight(a, tw);
            Draw.Text(g, a.Detail, OndaTheme.FontSmall, new Rectangle(tx, dy, tw, detailH), c.TextMuted, Draw.Wrap | (Compact ? TextFormatFlags.EndEllipsis : 0));
            if (a.Benefit is { } b && !Compact)
            {
                var by = dy + detailH + 8;
                Gfx.Text(g, Glyph.Speed, OndaTheme.IconFontSmall, new Rectangle(tx, by, 16, 18), ColorX.Readable(c.Success), Gfx.Center);
                Draw.Text(g, b, OndaTheme.FontSemiboldSmall, new Rectangle(tx + 20, by, tw - 20, BenefitHeight(a, tw)), ColorX.Readable(c.Success), Draw.Wrap);
            }
            y += h;
        }
        if (_items.Count == 0)
            Gfx.Text(g, "Öneri yok.", OndaTheme.Font, r, c.TextMuted, Gfx.Center);
    }
}

/// <summary>"Bu bilgisayara ne alınır?" alışveriş listesi.</summary>
sealed class ShoppingList : Tile
{
    IReadOnlyList<ShoppingItem> _items = [];

    public ShoppingList() { Title = "Bu bilgisayara ne alınır?"; Icon = Glyph.Cart; }

    public IReadOnlyList<ShoppingItem> Items { get => _items; set { _items = value; Invalidate(); } }

    protected override void PaintContent(Graphics g, Rectangle r)
    {
        var c = C;
        if (_items.Count == 0)
        {
            Draw.IconBox(g, Glyph.Check, c.Success, new RectangleF(r.X, r.Y + 4, 36, 36));
            Draw.Text(g, "Şu an alınması gereken bir parça yok. Bu donanım, kullanım amacına uygun görünüyor.", OndaTheme.Font, new Rectangle(r.X + 48, r.Y + 4, r.Width - 48, r.Height), c.TextMuted, Draw.Wrap);
            return;
        }
        var y = r.Y;
        foreach (var it in _items)
        {
            if (y + 50 > r.Bottom + 8) break;
            var qr = new RectangleF(r.X, y + 2, 36, 36);
            Gfx.FillRound(g, Draw.Soft(App.Teal), qr, 10);
            Gfx.Text(g, it.Quantity + "×", OndaTheme.FontSemibold, Rectangle.Round(qr), ColorX.Readable(App.Teal), Gfx.Center);
            Gfx.Text(g, it.Part, OndaTheme.FontSemibold, new Rectangle(r.X + 48, y, r.Width - 48, 22), c.Text);
            Gfx.Text(g, it.Why, OndaTheme.FontSmall, new Rectangle(r.X + 48, y + 21, r.Width - 48, 20), c.TextMuted);
            y += 52;
        }
    }
}

/// <summary>Etiket – değer satırları.</summary>
sealed class InfoList : Tile
{
    readonly List<(string Label, string Value, Color? Color)> _rows = [];

    public int RowHeight { get; set; } = 34;
    public int LabelWidth { get; set; } = 170;

    public void SetRows(IEnumerable<(string Label, string Value, Color? Color)> rows)
    {
        _rows.Clear();
        _rows.AddRange(rows);
        Invalidate();
    }

    public int PreferredHeight => (Title.Length > 0 ? HeaderH + 12 : 16) + _rows.Count * RowHeight + 12;

    protected override void PaintContent(Graphics g, Rectangle r)
    {
        var c = C;
        var y = r.Y - 4;
        for (var i = 0; i < _rows.Count; i++)
        {
            var (label, value, color) = _rows[i];
            if (y + RowHeight > Height) break;
            Gfx.Text(g, label, OndaTheme.Font, new Rectangle(r.X, y, LabelWidth, RowHeight), c.TextMuted);
            var vc = color is { } col ? ColorX.Readable(col) : c.Text;
            Gfx.Text(g, value, OndaTheme.FontSemibold, new Rectangle(r.X + LabelWidth, y, r.Width - LabelWidth, RowHeight), vc);
            if (i < _rows.Count - 1)
            {
                using var p = new Pen(ColorX.Alpha(c.Border, 160));
                g.DrawLine(p, r.X, y + RowHeight - 1, r.Right, y + RowHeight - 1);
            }
            y += RowHeight;
        }
    }
}
