using Otel.Application;
using Otel.Domain;

namespace Otel.WinForms.Controls;

/// <summary>
/// Etkileşimli oda haritası: odalar kat kat, durum rengiyle kutucuk olarak çizilir.
/// Tıklama seçer, çift tıklama ana işlemi tetikler, tekerlek kaydırır.
/// </summary>
sealed class RoomMap : OndaControl
{
    const int MinTileW = 132, MaxTileW = 210, TileH = 108, TileGap = 12, FloorW = 64, LegendH = 44;

    IReadOnlyList<RoomTile> _tiles = [];
    readonly List<(Rectangle Rect, RoomTile Tile)> _hits = [];
    int? _hoverId, _selectedId;
    int _scroll, _contentH;

    public event EventHandler<RoomTile>? RoomSelected;
    public event EventHandler<RoomTile>? RoomActivated;

    public RoomTile? Selected => _tiles.FirstOrDefault(t => t.RoomId == _selectedId);

    public void SetTiles(IReadOnlyList<RoomTile> tiles)
    {
        _tiles = tiles;
        if (Selected is null) _selectedId = null;
        ClampScroll();
        Invalidate();
    }

    public void Select(int roomId)
    {
        _selectedId = roomId;
        Invalidate();
        if (Selected is { } t) RoomSelected?.Invoke(this, t);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.Smooth();
        _hits.Clear();

        // Lejant: durum başına adet
        var x = 0;
        foreach (var status in Enum.GetValues<RoomStatus>())
        {
            var count = _tiles.Count(t => t.Status == status);
            var color = App.ColorOf(status);
            var text = $"{Texts.Of(status)}  {count}";
            var w = Gfx.Measure(text, OndaTheme.FontSemibold).Width + 34;
            var pill = new Rectangle(x, 6 - _scroll, w, 30);
            Gfx.FillRound(g, ColorX.Blend(C.Surface, color, 0.12f), pill, 15);
            using (var b = new SolidBrush(color)) g.FillEllipse(b, pill.X + 12, pill.Y + 10, 10, 10);
            Gfx.Text(g, text, OndaTheme.FontSemibold, new Rectangle(pill.X + 28, pill.Y, w - 30, pill.Height), C.Text);
            x += w + 10;
        }

        // Kutucuk genişliği alana göre: mümkünse her kat tek satıra sığar
        var avail = Width - FloorW - 10;
        var perFloor = _tiles.GroupBy(t => t.Floor).Select(f => f.Count()).DefaultIfEmpty(1).Max();
        var cols = Math.Clamp((avail + TileGap) / (MinTileW + TileGap), 1, perFloor);
        var tileW = Math.Min(MaxTileW, (avail - TileGap * (cols - 1)) / cols);

        var y = LegendH + 8 - _scroll;
        foreach (var floor in _tiles.GroupBy(t => t.Floor).OrderByDescending(f => f.Key))
        {
            Gfx.Text(g, $"{floor.Key}. kat", OndaTheme.FontSemibold, new Rectangle(0, y, FloorW, TileH), C.TextMuted);
            var i = 0;
            var rowY = y;
            foreach (var tile in floor.OrderBy(t => t.Number))
            {
                if (i > 0 && i % cols == 0) rowY += TileH + TileGap;
                var rect = new Rectangle(FloorW + (i % cols) * (tileW + TileGap), rowY, tileW, TileH);
                if (rect.Bottom > 0 && rect.Top < Height) DrawTile(g, rect, tile);
                _hits.Add((rect, tile));
                i++;
            }
            y = rowY + TileH + TileGap + 8;
        }
        _contentH = y + _scroll;

        // İnce kaydırma göstergesi
        if (_contentH > Height)
        {
            var h = Math.Max(30, Height * Height / _contentH);
            var top = (Height - h) * _scroll / Math.Max(1, _contentH - Height);
            Gfx.FillRound(g, ColorX.Alpha(C.TextMuted, 90), new Rectangle(Width - 5, top, 4, h), 2);
        }
    }

    void DrawTile(Graphics g, Rectangle r, RoomTile t)
    {
        var color = App.ColorOf(t.Status);
        var hover = _hoverId == t.RoomId;
        var selected = _selectedId == t.RoomId;
        var dark = OndaTheme.Mode == OndaThemeMode.Dark;
        var bg = ColorX.Blend(C.Surface, color, (dark ? 0.16f : 0.08f) + (hover ? 0.06f : 0f));
        var radius = OndaTheme.Radius + 2;

        Gfx.FillRound(g, bg, r, radius);
        Gfx.DrawRound(g, selected ? color : ColorX.Blend(C.Border, color, 0.35f), r, radius, selected ? 2f : 1f);
        Gfx.FillRound(g, color, new Rectangle(r.X + 1, r.Y + 12, 4, r.Height - 24), 2);

        Gfx.Text(g, t.Number, OndaTheme.FontSemiboldLarge, new Rectangle(r.X + 16, r.Y + 10, 60, 24), C.Text);

        var status = t.StatusText;
        var sw = Gfx.Measure(status, OndaTheme.FontSemiboldSmall).Width + 16;
        var pill = new Rectangle(r.Right - sw - 10, r.Y + 12, sw, 20);
        Gfx.FillRound(g, ColorX.Alpha(color, dark ? 70 : 38), pill, 10);
        Gfx.Text(g, status, OndaTheme.FontSemiboldSmall, pill, ColorX.Readable(color), Gfx.Center);

        Gfx.Text(g, $"{t.RoomType} · {t.Capacity} kişilik", OndaTheme.FontSmall, new Rectangle(r.X + 16, r.Y + 36, r.Width - 26, 18), C.TextMuted);

        var main = t.Status switch
        {
            RoomStatus.Occupied => t.GuestName ?? "Misafir",
            RoomStatus.Dirty => "Temizlik bekliyor",
            RoomStatus.Maintenance => t.MaintenanceNote ?? "Bakım",
            _ => t.ArrivalGuest is null ? "Müsait" : t.ArrivalGuest,
        };
        Gfx.Text(g, main, OndaTheme.FontSemibold, new Rectangle(r.X + 16, r.Y + 56, r.Width - 26, 22), C.Text);

        var foot = t.Status switch
        {
            RoomStatus.Occupied when t.CheckOut is { } co => co == App.Today ? "Bugün çıkış" : $"Çıkış {co:dd.MM}",
            RoomStatus.Available when t.ArrivalGuest is not null => "Bugün giriş bekleniyor",
            RoomStatus.Available => $"{t.BaseRate:N0} ₺ / gece",
            RoomStatus.Dirty => "Kat hizmetlerine iletildi",
            _ => "Satışa kapalı",
        };
        var footColor = t.Status == RoomStatus.Available && t.ArrivalGuest is not null ? App.Violet : C.TextMuted;
        Gfx.Text(g, foot, OndaTheme.FontSmall, new Rectangle(r.X + 16, r.Y + 80, r.Width - 26, 18), footColor);
    }

    RoomTile? HitTest(Point p) => _hits.FirstOrDefault(h => h.Rect.Contains(p)).Tile;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var id = HitTest(e.Location)?.RoomId;
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
        if (HitTest(e.Location) is { } t) Select(t.RoomId);
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (HitTest(e.Location) is { } t) RoomActivated?.Invoke(this, t);
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
