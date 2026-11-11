using System.Globalization;
using Otel.Application;
using Otel.Domain;

namespace Otel.WinForms.Controls;

/// <summary>
/// Rezervasyon çizelgesi: satırlar odalar, sütunlar günler. Konaklama çubukları giriş gününün ortasından
/// çıkış gününün ortasına çizilir; aynı gün çıkış ve giriş yan yana görünür, çakışma görsel olarak da yoktur.
/// </summary>
sealed class ReservationTimeline : OndaControl
{
    const int RoomColW = 150, HeaderH = 56, RowH = 36;
    static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    TimelineView? _view;
    readonly List<(RectangleF Rect, TimelineBar Bar)> _bars = [];
    int? _hoverBar;
    int _scroll;

    public event EventHandler<int>? ReservationClicked;
    public event EventHandler<(int RoomId, DateOnly Date)>? EmptyCellDoubleClicked;

    public void SetView(TimelineView view)
    {
        _view = view;
        ClampScroll();
        Invalidate();
    }

    float CellW => _view is null ? 40 : Math.Max(28f, (Width - RoomColW - 2) / (float)_view.Days);

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.Smooth();
        var radius = OndaTheme.Radius + 2;
        var card = new Rectangle(0, 0, Width - 1, Height - 1);
        Gfx.FillRound(g, C.Surface, card, radius);
        _bars.Clear();
        if (_view is not { } v) { Gfx.DrawRound(g, C.Border, card, radius); return; }

        var cw = CellW;
        var body = new Rectangle(0, HeaderH, Width, Height - HeaderH);
        var rowIndex = v.Rooms.Select((r, i) => (r.RoomId, i)).ToDictionary(x => x.RoomId, x => x.i);

        g.SetClip(body);
        // Hafta sonu ve bugün sütunları
        for (var i = 0; i < v.Days; i++)
        {
            var d = v.From.AddDays(i);
            var x = RoomColW + i * cw;
            if (d == v.Today)
                using (var b = new SolidBrush(ColorX.Alpha(C.Primary, 26))) g.FillRectangle(b, x, HeaderH, cw, body.Height);
            else if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                using (var b = new SolidBrush(ColorX.Alpha(C.TextMuted, 14))) g.FillRectangle(b, x, HeaderH, cw, body.Height);
        }

        using var line = new Pen(ColorX.Alpha(C.Border, 160));
        for (var r = 0; r < v.Rooms.Count; r++)
        {
            var room = v.Rooms[r];
            var y = HeaderH + r * RowH - _scroll;
            if (y + RowH < HeaderH || y > Height) continue;
            g.DrawLine(line, 0, y + RowH, Width, y + RowH);
            var dot = App.ColorOf(room.Status);
            using (var b = new SolidBrush(dot)) g.FillEllipse(b, 14, y + RowH / 2 - 4, 8, 8);
            Gfx.Text(g, room.Number, OndaTheme.FontSemibold, new Rectangle(28, y, 44, RowH), C.Text);
            Gfx.Text(g, room.RoomType, OndaTheme.FontSmall, new Rectangle(72, y, RoomColW - 76, RowH), C.TextMuted);
        }
        for (var i = 0; i <= v.Days; i++)
        {
            var x = RoomColW + i * cw;
            g.DrawLine(line, x, HeaderH, x, Height);
        }

        // Konaklama çubukları
        foreach (var bar in v.Bars)
        {
            if (!rowIndex.TryGetValue(bar.RoomId, out var r)) continue;
            var y = HeaderH + r * RowH - _scroll;
            var x1 = RoomColW + (bar.CheckIn.DayNumber - v.From.DayNumber + 0.5f) * cw;
            var x2 = RoomColW + (bar.CheckOut.DayNumber - v.From.DayNumber + 0.5f) * cw - 3;
            x1 = Math.Max(x1, RoomColW + 2);
            x2 = Math.Min(x2, Width - 3);
            if (x2 - x1 < 4) continue;
            var rect = new RectangleF(x1, y + 6, x2 - x1, RowH - 12);
            var color = App.ColorOf(bar.Status);
            var hover = _hoverBar == bar.ReservationId;
            Gfx.FillRound(g, hover ? ColorX.Shade(color, -0.12f) : color, rect, 6);
            if (rect.Width > 26)
                Gfx.Text(g, bar.GuestName, OndaTheme.FontSemiboldSmall,
                    Rectangle.Round(new RectangleF(rect.X + 8, rect.Y, rect.Width - 12, rect.Height)), Color.White);
            _bars.Add((rect, bar));
        }
        g.ResetClip();

        // Başlık: ay, gün numarası, gün adı
        using (var hb = new SolidBrush(C.Surface)) g.FillRectangle(hb, 1, 1, Width - 3, HeaderH - 1);
        g.DrawLine(line, 0, HeaderH, Width, HeaderH);
        Gfx.Text(g, v.From.ToString("MMMM yyyy", Tr), OndaTheme.FontSemibold, new Rectangle(14, 0, RoomColW - 16, HeaderH), C.Text);
        for (var i = 0; i < v.Days; i++)
        {
            var d = v.From.AddDays(i);
            var x = (int)(RoomColW + i * cw);
            var today = d == v.Today;
            var color = today ? C.Primary : d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? C.TextMuted : C.Text;
            if (today) Gfx.FillRound(g, ColorX.Alpha(C.Primary, 30), new Rectangle(x + 3, 8, (int)cw - 6, HeaderH - 14), 6);
            Gfx.Text(g, d.Day.ToString(), OndaTheme.FontSemibold, new Rectangle(x, 8, (int)cw, 22), color, Gfx.Center);
            Gfx.Text(g, d.ToString("ddd", Tr), OndaTheme.FontSmall, new Rectangle(x, 28, (int)cw, 18), today ? C.Primary : C.TextMuted, Gfx.Center);
        }
        Gfx.DrawRound(g, C.Border, card, radius);
    }

    TimelineBar? BarAt(Point p) => _bars.LastOrDefault(b => b.Rect.Contains(p)).Bar;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var id = BarAt(e.Location)?.ReservationId;
        if (id != _hoverBar)
        {
            _hoverBar = id;
            Cursor = id is null ? Cursors.Default : Cursors.Hand;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hoverBar = null;
        Invalidate();
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (BarAt(e.Location) is { } bar) ReservationClicked?.Invoke(this, bar.ReservationId);
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (_view is not { } v || BarAt(e.Location) is not null || e.Y < HeaderH || e.X < RoomColW) return;
        var row = (e.Y - HeaderH + _scroll) / RowH;
        var col = (int)((e.X - RoomColW) / CellW);
        if (row < 0 || row >= v.Rooms.Count || col < 0 || col >= v.Days) return;
        EmptyCellDoubleClicked?.Invoke(this, (v.Rooms[row].RoomId, v.From.AddDays(col)));
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        _scroll -= e.Delta / 3;
        ClampScroll();
        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        ClampScroll();
    }

    void ClampScroll()
    {
        var content = (_view?.Rooms.Count ?? 0) * RowH;
        _scroll = Math.Clamp(_scroll, 0, Math.Max(0, content - (Height - HeaderH)));
    }
}
