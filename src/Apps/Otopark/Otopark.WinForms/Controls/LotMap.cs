using Otopark.Application;
using Otopark.Domain;

namespace Otopark.WinForms.Controls;

/// <summary>
/// Otopark haritası: park yerleri bölge bölge kutucuk olarak çizilir. Doluysa plaka, süre ve o anki ücret görünür;
/// yıkamadaki araçta küçük durum noktası çıkar. Tıklama seçer, çift tıklama ana işlemi tetikler, tekerlek kaydırır.
/// </summary>
sealed class LotMap : OndaControl
{
    const int MinTileW = 104, MaxTileW = 150, TileH = 86, TileGap = 10, ZoneW = 92, LegendH = 44;

    static readonly Dictionary<string, string> ZoneNames = new()
    {
        ["A"] = "A · Zemin",
        ["B"] = "B · -1. kat",
        ["C"] = "C · Büyük araç",
        ["M"] = "M · Motosiklet",
    };

    IReadOnlyList<SpotTile> _tiles = [];
    readonly List<(Rectangle Rect, SpotTile Tile)> _hits = [];
    int? _hoverId, _selectedId;
    int _scroll, _contentH;

    public event EventHandler<SpotTile>? SpotSelected;
    public event EventHandler<SpotTile>? SpotActivated;

    public SpotTile? Selected => _tiles.FirstOrDefault(t => t.SpotId == _selectedId);

    public void SetTiles(IReadOnlyList<SpotTile> tiles)
    {
        _tiles = tiles;
        if (Selected is null) _selectedId = null;
        ClampScroll();
        Invalidate();
    }

    public void Select(int spotId)
    {
        _selectedId = spotId;
        Invalidate();
        if (Selected is { } t) SpotSelected?.Invoke(this, t);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.Smooth();
        _hits.Clear();

        // Lejant
        var free = _tiles.Count(t => !t.Occupied && !t.OutOfService);
        var legend = new (string Text, Color Color)[]
        {
            ($"Boş  {free}", OndaTheme.Colors.Success),
            ($"Dolu  {_tiles.Count(t => t.Occupied && !t.IsSubscriber)}", OndaTheme.Colors.Primary),
            ($"Abone  {_tiles.Count(t => t.IsSubscriber)}", App.Violet),
            ($"Kullanım dışı  {_tiles.Count(t => t.OutOfService)}", OndaTheme.Colors.Danger),
            ($"Yıkamada  {_tiles.Count(t => t.Wash is WashStatus.Waiting or WashStatus.Washing)}", App.Cyan),
        };
        var x = 0;
        foreach (var (text, color) in legend)
        {
            var w = Gfx.Measure(text, OndaTheme.FontSemibold).Width + 34;
            var pill = new Rectangle(x, 6 - _scroll, w, 30);
            Gfx.FillRound(g, ColorX.Blend(C.Surface, color, 0.12f), pill, 15);
            using (var b = new SolidBrush(color)) g.FillEllipse(b, pill.X + 12, pill.Y + 10, 10, 10);
            Gfx.Text(g, text, OndaTheme.FontSemibold, new Rectangle(pill.X + 28, pill.Y, w - 30, pill.Height), C.Text);
            x += w + 10;
        }

        var avail = Width - ZoneW - 10;
        var cols = Math.Max(1, (avail + TileGap) / (MinTileW + TileGap));
        var tileW = Math.Min(MaxTileW, (avail - TileGap * (cols - 1)) / cols);

        var y = LegendH + 8 - _scroll;
        foreach (var zone in _tiles.GroupBy(t => t.Zone).OrderBy(z => z.Key, StringComparer.Ordinal))
        {
            var zoneFree = zone.Count(t => !t.Occupied && !t.OutOfService);
            var name = ZoneNames.GetValueOrDefault(zone.Key, zone.Key);
            Gfx.Text(g, name, OndaTheme.FontSemibold, new Rectangle(0, y + 4, ZoneW - 8, 40), C.Text);
            Gfx.Text(g, $"{zoneFree} / {zone.Count()} boş", OndaTheme.FontSmall, new Rectangle(0, y + 44, ZoneW - 8, 20), C.TextMuted);
            var i = 0;
            var rowY = y;
            foreach (var tile in zone.OrderBy(t => t.Code, StringComparer.Ordinal))
            {
                if (i > 0 && i % cols == 0) rowY += TileH + TileGap;
                var rect = new Rectangle(ZoneW + (i % cols) * (tileW + TileGap), rowY, tileW, TileH);
                if (rect.Bottom > 0 && rect.Top < Height) DrawTile(g, rect, tile);
                _hits.Add((rect, tile));
                i++;
            }
            y = rowY + TileH + TileGap + 14;
        }
        _contentH = y + _scroll;

        if (_contentH > Height)
        {
            var h = Math.Max(30, Height * Height / _contentH);
            var top = (Height - h) * _scroll / Math.Max(1, _contentH - Height);
            Gfx.FillRound(g, ColorX.Alpha(C.TextMuted, 90), new Rectangle(Width - 5, top, 4, h), 2);
        }
    }

    void DrawTile(Graphics g, Rectangle r, SpotTile t)
    {
        var color = App.ColorOf(t);
        var hover = _hoverId == t.SpotId;
        var selected = _selectedId == t.SpotId;
        var dark = OndaTheme.Mode == OndaThemeMode.Dark;
        var bg = ColorX.Blend(C.Surface, color, (dark ? 0.16f : 0.08f) + (hover ? 0.06f : 0f) + (t.Occupied ? 0.04f : 0f));
        var radius = OndaTheme.Radius + 2;

        Gfx.FillRound(g, bg, r, radius);
        Gfx.DrawRound(g, selected ? color : ColorX.Blend(C.Border, color, 0.35f), r, radius, selected ? 2f : 1f);
        Gfx.FillRound(g, color, new Rectangle(r.X + 1, r.Y + 10, 4, r.Height - 20), 2);

        Gfx.Text(g, t.Code, OndaTheme.FontSemibold, new Rectangle(r.X + 12, r.Y + 6, 60, 20), C.Text);
        if (t.Kind != SpotKind.Standard)
        {
            var kind = t.KindText;
            var kw = Gfx.Measure(kind, OndaTheme.FontSemiboldSmall).Width + 12;
            var pill = new Rectangle(r.Right - kw - 6, r.Y + 7, kw, 18);
            var kc = t.Kind switch { SpotKind.Electric => OndaTheme.Colors.Success, SpotKind.Disabled => OndaTheme.Colors.Primary, _ => OndaTheme.Colors.TextMuted };
            Gfx.FillRound(g, ColorX.Alpha(kc, dark ? 60 : 30), pill, 9);
            Gfx.Text(g, kind, OndaTheme.FontSemiboldSmall, pill, ColorX.Readable(kc), Gfx.Center);
        }

        if (t.OutOfService)
        {
            Gfx.Text(g, t.ServiceNote ?? "Kullanım dışı", OndaTheme.FontSmall, new Rectangle(r.X + 12, r.Y + 32, r.Width - 18, 40), C.TextMuted);
            return;
        }
        if (!t.Occupied)
        {
            Gfx.Text(g, "Boş", OndaTheme.FontSemibold, new Rectangle(r.X + 12, r.Y + 34, r.Width - 18, 22), ColorX.Readable(color));
            return;
        }

        Gfx.Text(g, t.Plate ?? "", OndaTheme.FontSemibold, new Rectangle(r.X + 12, r.Y + 30, r.Width - 18, 22), C.Text);
        var elapsed = t.EntryAt is { } entry ? Short(App.Now - entry) : "";
        var right = t.IsSubscriber ? "abone" : $"{t.CurrentFee:N0} ₺";
        var rw = Gfx.Measure(right, OndaTheme.FontSmall).Width;
        Gfx.Text(g, elapsed, OndaTheme.FontSmall, new Rectangle(r.X + 12, r.Y + 56, r.Width - rw - 34, 18), C.TextMuted);
        Gfx.Text(g, right, OndaTheme.FontSmall, new Rectangle(r.Right - rw - 22, r.Y + 56, rw + 4, 18), t.IsSubscriber ? App.Violet : C.Text);

        if (t.Wash is { } wash)
        {
            var wc = App.ColorOf(wash);
            using var b = new SolidBrush(wc);
            g.FillEllipse(b, r.Right - 18, r.Bottom - 20, 10, 10);
        }
    }

    /// <summary>Kutucuğa sığan kısa süre: "45 dk", "2 sa 17", "1 g 3 sa".</summary>
    static string Short(TimeSpan d) => d.TotalDays >= 1 ? $"{(int)d.TotalDays} g {d.Hours} sa"
        : d.TotalHours >= 1 ? $"{(int)d.TotalHours} sa {d.Minutes:00}" : $"{Math.Max(0, d.Minutes)} dk";

    SpotTile? HitTest(Point p) => _hits.FirstOrDefault(h => h.Rect.Contains(p)).Tile;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var id = HitTest(e.Location)?.SpotId;
        if (id != _hoverId)
        {
            _hoverId = id;
            Cursor = id is null ? Cursors.Default : Cursors.Hand;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hoverId = null;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (HitTest(e.Location) is { } t) Select(t.SpotId);
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (HitTest(e.Location) is { } t) SpotActivated?.Invoke(this, t);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        _scroll -= e.Delta / 2;
        ClampScroll();
        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        ClampScroll();
    }

    void ClampScroll() => _scroll = Math.Clamp(_scroll, 0, Math.Max(0, _contentH - Height));
}
