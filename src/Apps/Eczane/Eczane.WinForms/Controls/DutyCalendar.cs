namespace Eczane.WinForms.Controls;

/// <summary>
/// Nöbet çizelgesi takvimi: ayın günleri haftalık ızgara olarak çizilir. Nöbet günlerinde tür (gece, pazar, bayram),
/// nöbetçi eczacı ve yardımcı personel görünür; ataması yapılmamış nöbet kırmızı çerçevelidir. İzinli personel gün altında listelenir.
/// Tıklama seçer, çift tıklama atama penceresini açar.
/// </summary>
sealed class DutyCalendar : OndaControl
{
    static readonly string[] DayHeaders = ["Pazartesi", "Salı", "Çarşamba", "Perşembe", "Cuma", "Cumartesi", "Pazar"];
    const int HeaderH = 34, CellGap = 6;

    DutyMonth? _month;
    readonly List<(Rectangle Rect, DutyCell Cell)> _hits = [];
    DateOnly? _selected, _hover;

    public event EventHandler<DutyCell>? DaySelected;
    public event EventHandler<DutyCell>? DayActivated;

    public DutyCell? Selected => _month?.Days.FirstOrDefault(d => d.Date == _selected);

    public void SetMonth(DutyMonth month)
    {
        _month = month;
        if (_selected is { } s && (s.Year != month.Year || s.Month != month.Month)) _selected = null;
        Invalidate();
    }

    public void Select(DateOnly date)
    {
        _selected = date;
        Invalidate();
        if (Selected is { } c) DaySelected?.Invoke(this, c);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.Smooth();
        _hits.Clear();
        if (_month is null) return;

        var first = new DateOnly(_month.Year, _month.Month, 1);
        var offset = ((int)first.DayOfWeek + 6) % 7; // pazartesi = 0
        var days = DateTime.DaysInMonth(_month.Year, _month.Month);
        var weeks = (int)Math.Ceiling((offset + days) / 7.0);
        var cw = (Width - CellGap * 6) / 7;
        var ch = Math.Max(70, (Height - HeaderH - CellGap * (weeks - 1)) / weeks);
        var today = App.Today;
        var dark = OndaTheme.Mode == OndaThemeMode.Dark;

        for (var i = 0; i < 7; i++)
            Gfx.Text(g, DayHeaders[i], OndaTheme.FontSemiboldSmall, new Rectangle(i * (cw + CellGap) + 6, 0, cw - 6, HeaderH - 6), i == 6 ? App.Pink : C.TextMuted);

        foreach (var cell in _month.Days)
        {
            var idx = offset + cell.Date.Day - 1;
            var r = new Rectangle((idx % 7) * (cw + CellGap), HeaderH + (idx / 7) * (ch + CellGap), cw, ch);
            _hits.Add((r, cell));
            DrawCell(g, r, cell, cell.Date == today, dark);
        }
    }

    void DrawCell(Graphics g, Rectangle r, DutyCell c, bool today, bool dark)
    {
        var color = c.Kind switch { DutyKind.Sunday => App.Pink, DutyKind.Holiday => App.Amber, DutyKind.Night => App.Violet, _ => C.TextMuted };
        var selected = _selected == c.Date;
        var hover = _hover == c.Date;
        var past = c.Date < App.Today;
        var bg = c.IsDuty ? ColorX.Blend(C.Surface, color, (dark ? 0.16f : 0.08f) + (hover ? 0.05f : 0f)) : hover ? ColorX.Blend(C.Surface, C.TextMuted, 0.06f) : C.Surface;
        var radius = OndaTheme.Radius + 2;
        Gfx.FillRound(g, bg, r, radius);
        var border = c.Unstaffed ? OndaTheme.Colors.Danger : selected ? OndaTheme.Colors.Primary : today ? OndaTheme.Colors.Primary : C.Border;
        Gfx.DrawRound(g, border, r, radius, selected || c.Unstaffed ? 2f : today ? 1.5f : 1f);

        var dayColor = past ? C.TextMuted : c.Date.DayOfWeek == DayOfWeek.Sunday ? App.Pink : C.Text;
        Gfx.Text(g, c.Date.Day.ToString(), OndaTheme.FontSemiboldLarge, new Rectangle(r.X + 10, r.Y + 6, 40, 22), dayColor);
        if (today) Gfx.Text(g, "bugün", OndaTheme.FontSemiboldSmall, new Rectangle(r.X + 40, r.Y + 8, 60, 18), OndaTheme.Colors.Primary);

        if (c.IsDuty)
        {
            var kind = c.KindText;
            var kw = Gfx.Measure(kind, OndaTheme.FontSemiboldSmall).Width + 14;
            var pill = new Rectangle(r.Right - kw - 8, r.Y + 7, kw, 20);
            Gfx.FillRound(g, ColorX.Alpha(color, dark ? 70 : 36), pill, 10);
            Gfx.Text(g, kind, OndaTheme.FontSemiboldSmall, pill, ColorX.Readable(color), Gfx.Center);

            var y = r.Y + 32;
            if (c.Pharmacist is { } ph)
            {
                Gfx.Text(g, "\uE77B", OndaTheme.IconFontSmall, new Rectangle(r.X + 10, y, 16, 18), color);
                Gfx.Text(g, Fit(ph, r.Width - 34, OndaTheme.FontSemibold), OndaTheme.FontSemibold, new Rectangle(r.X + 28, y, r.Width - 34, 18), C.Text);
            }
            else Gfx.Text(g, "Eczacı atanmadı", OndaTheme.FontSemibold, new Rectangle(r.X + 10, y, r.Width - 16, 18), OndaTheme.Colors.Danger);
            y += 20;
            if (c.Assistant is { } a && r.Bottom - y > 18)
                Gfx.Text(g, "+ " + Fit(a, r.Width - 46, OndaTheme.FontSmall), OndaTheme.FontSmall, new Rectangle(r.X + 28, y, r.Width - 34, 16), C.TextMuted);
        }
        if (c.OnLeave.Count > 0 && r.Height > 92)
            Gfx.Text(g, "İzinli: " + string.Join(", ", c.OnLeave.Select(n => n.Split(' ')[0])), OndaTheme.FontSmall, new Rectangle(r.X + 10, r.Bottom - 20, r.Width - 16, 16), OndaTheme.Colors.Warning);
        else if (c.Note is { Length: > 0 } note && r.Height > 92)
            Gfx.Text(g, note, OndaTheme.FontSmall, new Rectangle(r.X + 10, r.Bottom - 20, r.Width - 16, 16), C.TextMuted);
    }

    /// <summary>Ad sığmıyorsa "Ad S." biçimine kısaltır.</summary>
    static string Fit(string name, int width, Font font)
    {
        if (Gfx.Measure(name, font).Width <= width) return name;
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 1 ? $"{parts[0]} {parts[^1][0]}." : name;
    }

    DutyCell? HitTest(Point p) => _hits.FirstOrDefault(h => h.Rect.Contains(p)).Cell;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var d = HitTest(e.Location)?.Date;
        if (d != _hover)
        {
            _hover = d;
            Cursor = d is null ? Cursors.Default : Cursors.Hand;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = null;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (HitTest(e.Location) is { } c) Select(c.Date);
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (HitTest(e.Location) is { } c) DayActivated?.Invoke(this, c);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Invalidate();
    }
}
