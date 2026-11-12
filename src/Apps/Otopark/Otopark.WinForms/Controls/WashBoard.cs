using Otopark.Application;
using Otopark.Domain;

namespace Otopark.WinForms.Controls;

/// <summary>
/// Yıkama panosu (kanban): Bekliyor → Yıkanıyor → Hazır → Bugün teslim. Her kart bir iş emri.
/// Tıklama seçer, çift tıklama bir sonraki aşamaya geçirir.
/// </summary>
sealed class WashBoard : OndaControl
{
    const TextFormatFlags RightAlign = TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
    const int HeaderH = 46, CardH = 112, CardGap = 10, ColGap = 14;
    static readonly WashStatus[] Columns = [WashStatus.Waiting, WashStatus.Washing, WashStatus.Ready, WashStatus.Delivered];

    IReadOnlyList<WashCard> _cards = [];
    readonly List<(Rectangle Rect, WashCard Card)> _hits = [];
    readonly int[] _scroll = new int[4];
    int? _hoverId, _selectedId;

    public event EventHandler<WashCard>? CardSelected;
    public event EventHandler<WashCard>? CardActivated;

    public WashCard? Selected => _cards.FirstOrDefault(c => c.Id == _selectedId);

    public void SetCards(IReadOnlyList<WashCard> cards)
    {
        _cards = cards;
        if (Selected is null) _selectedId = null;
        Invalidate();
    }

    public void Select(int id)
    {
        _selectedId = id;
        Invalidate();
        if (Selected is { } c) CardSelected?.Invoke(this, c);
    }

    int ColumnWidth => (Width - ColGap * (Columns.Length - 1)) / Columns.Length;

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.Smooth();
        _hits.Clear();
        var colW = ColumnWidth;

        for (var c = 0; c < Columns.Length; c++)
        {
            var status = Columns[c];
            var x = c * (colW + ColGap);
            var color = App.ColorOf(status);
            var items = _cards.Where(k => k.Status == status)
                .OrderBy(k => status == WashStatus.Delivered ? -(k.DeliveredAt ?? k.OrderedAt).Ticks : k.OrderedAt.Ticks).ToList();

            var col = new Rectangle(x, 0, colW, Height);
            Gfx.FillRound(g, ColorX.Blend(C.Background, color, 0.05f), col, OndaTheme.Radius + 4);

            var title = status == WashStatus.Delivered ? "Bugün teslim" : Texts.Of(status);
            using (var b = new SolidBrush(color)) g.FillEllipse(b, x + 16, 18, 10, 10);
            Gfx.Text(g, title, OndaTheme.FontSemibold, new Rectangle(x + 32, 8, colW - 90, 30), C.Text);
            var count = items.Count.ToString();
            var cw = Gfx.Measure(count, OndaTheme.FontSemiboldSmall).Width + 16;
            var pill = new Rectangle(x + colW - cw - 14, 13, cw, 20);
            Gfx.FillRound(g, ColorX.Alpha(color, 40), pill, 10);
            Gfx.Text(g, count, OndaTheme.FontSemiboldSmall, pill, ColorX.Readable(color), Gfx.Center);

            var state = g.Save();
            g.SetClip(new Rectangle(x, HeaderH, colW, Height - HeaderH));
            var y = HeaderH - _scroll[c];
            foreach (var card in items)
            {
                var rect = new Rectangle(x + 10, y, colW - 20, CardH);
                if (rect.Bottom > HeaderH && rect.Top < Height) DrawCard(g, rect, card, color);
                _hits.Add((Rectangle.Intersect(rect, new Rectangle(x, HeaderH, colW, Height - HeaderH)), card));
                y += CardH + CardGap;
            }
            if (items.Count == 0)
                Gfx.Text(g, status == WashStatus.Waiting ? "Sırada araç yok" : "—", OndaTheme.FontSmall, new Rectangle(x + 16, HeaderH + 6, colW - 32, 24), C.TextMuted);
            g.Restore(state);
        }
    }

    void DrawCard(Graphics g, Rectangle r, WashCard k, Color color)
    {
        var hover = _hoverId == k.Id;
        var selected = _selectedId == k.Id;
        var radius = OndaTheme.Radius + 2;
        Gfx.FillRound(g, hover ? ColorX.Blend(C.Surface, color, 0.06f) : C.Surface, r, radius);
        Gfx.DrawRound(g, selected ? color : C.Border, r, radius, selected ? 2f : 1f);

        Gfx.Text(g, k.Plate, OndaTheme.FontSemiboldLarge, new Rectangle(r.X + 14, r.Y + 8, r.Width - 110, 26), C.Text);
        var price = $"{k.Price:N0} ₺";
        Gfx.Text(g, price, OndaTheme.FontSemibold, new Rectangle(r.Right - 100, r.Y + 10, 86, 22), C.Text, RightAlign);
        Gfx.Text(g, $"{k.PackageName} · {k.VehicleText}", OndaTheme.FontSmall, new Rectangle(r.X + 14, r.Y + 36, r.Width - 28, 18), C.TextMuted);

        var timing = k.Status switch
        {
            WashStatus.Waiting => $"Sırada · tahmini bitiş {k.EstimatedReady:HH:mm}",
            WashStatus.Washing => $"{k.Washer ?? "Yıkamacı"} · bitiş ~{k.EstimatedReady:HH:mm}",
            WashStatus.Ready => $"Hazır {At(k.ReadyAt)}" + (k.InLot ? " · çıkışta teslim" : " · müşteri bekleniyor"),
            _ => $"Teslim {At(k.DeliveredAt)}",
        };
        Gfx.Text(g, timing, OndaTheme.FontSmall, new Rectangle(r.X + 14, r.Y + 56, r.Width - 28, 18), C.Text);

        var place = k.SpotCode is { } spot ? $"Otoparkta · {spot}" : "Ayakta müşteri";
        var pw = Gfx.Measure(place, OndaTheme.FontSemiboldSmall).Width + 16;
        var pill = new Rectangle(r.X + 12, r.Bottom - 30, pw, 20);
        var pc = k.InLot ? OndaTheme.Colors.Primary : OndaTheme.Colors.TextMuted;
        Gfx.FillRound(g, ColorX.Alpha(pc, 32), pill, 10);
        Gfx.Text(g, place, OndaTheme.FontSemiboldSmall, pill, ColorX.Readable(pc), Gfx.Center);
        if (!string.IsNullOrEmpty(k.Note))
            Gfx.Text(g, k.Note, OndaTheme.FontSmall, new Rectangle(pill.Right + 8, r.Bottom - 30, r.Right - pill.Right - 20, 20), C.TextMuted);
    }

    /// <summary>Bugünse saat, dünse "dün 17:20", daha eskiyse tarih ve saat.</summary>
    static string At(DateTime? t) => t is not { } v ? "" :
        v.Date == App.Now.Date ? v.ToString("HH:mm") :
        v.Date == App.Now.Date.AddDays(-1) ? "dün " + v.ToString("HH:mm") : v.ToString("dd.MM HH:mm");

    WashCard? HitTest(Point p) => _hits.FirstOrDefault(h => h.Rect.Contains(p)).Card;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var id = HitTest(e.Location)?.Id;
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
        if (HitTest(e.Location) is { } c) Select(c.Id);
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (HitTest(e.Location) is { } c) CardActivated?.Invoke(this, c);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        var col = Math.Clamp(e.X / Math.Max(1, ColumnWidth + ColGap), 0, Columns.Length - 1);
        var count = _cards.Count(k => k.Status == Columns[col]);
        var max = Math.Max(0, HeaderH + count * (CardH + CardGap) - Height);
        _scroll[col] = Math.Clamp(_scroll[col] - e.Delta / 2, 0, max);
        Invalidate();
    }
}
