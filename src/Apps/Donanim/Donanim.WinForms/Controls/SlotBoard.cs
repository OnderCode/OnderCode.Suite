namespace Donanim.WinForms.Controls;

/// <summary>
/// Anakart üzerindeki bellek yuvalarının çizimi: dolu yuvada modül (yonga, altın bacaklar, etiket),
/// boş yuvada kesik çizgili çerçeve. Masaüstünde dikey DIMM, dizüstünde yatay SO-DIMM yuvaları.
/// Fareyle üzerine gelinen yuva vurgulanır ve <see cref="HoverChanged"/> tetiklenir.
/// </summary>
sealed class SlotBoard : AnimatedTile
{
    MemoryInfo? _memory;
    string _cpu = "", _socket = "";
    bool _laptop;
    int _hover = -1;
    readonly List<RectangleF> _slots = [];

    public event EventHandler? HoverChanged;

    public SlotBoard() { Size = new Size(700, 380); Cursor = Cursors.Hand; }

    public int HoverIndex => _hover;
    /// <summary>Video/ekran görüntüsü için dışarıdan vurgulama.</summary>
    public void SetHover(int index) { if (_hover == index) return; _hover = index; Invalidate(); HoverChanged?.Invoke(this, EventArgs.Empty); }
    public RectangleF SlotRect(int i) => i >= 0 && i < _slots.Count ? _slots[i] : RectangleF.Empty;

    public void SetData(MemoryInfo memory, string cpu, string socket, bool laptop)
    {
        _memory = memory;
        _cpu = cpu;
        _socket = socket;
        _laptop = laptop;
        _hover = -1;
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var h = _slots.FindIndex(r => RectangleF.Inflate(r, 6, 6).Contains(e.Location));
        if (h != _hover) SetHover(h);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        SetHover(-1);
    }

    static readonly Color Pcb = Color.FromArgb(22, 101, 70);
    static readonly Color PcbDark = Color.FromArgb(14, 72, 50);
    static readonly Color Gold = Color.FromArgb(214, 170, 72);
    static readonly Color ChipColor = Color.FromArgb(28, 30, 36);

    protected override void PaintContent(Graphics g, Rectangle r)
    {
        _slots.Clear();
        if (_memory is null) return;
        var c = C;
        var dark = OndaTheme.Mode == OndaThemeMode.Dark;

        // anakart zemini
        var board = new RectangleF(r.X, r.Y, r.Width, r.Height);
        var boardColor = dark ? Color.FromArgb(24, 34, 30) : Color.FromArgb(236, 244, 239);
        Gfx.FillRound(g, boardColor, board, 12);
        using (var grid = new Pen(ColorX.Alpha(Pcb, dark ? 40 : 22)))
        {
            for (var x = board.X + 24; x < board.Right; x += 24) g.DrawLine(grid, x, board.Y + 6, x, board.Bottom - 6);
            for (var y = board.Y + 24; y < board.Bottom; y += 24) g.DrawLine(grid, board.X + 6, y, board.Right - 6, y);
        }
        Gfx.DrawRound(g, ColorX.Alpha(Pcb, dark ? 120 : 70), board, 12);

        // işlemci yuvası
        var cpuSize = Math.Min(130f, board.Height * 0.42f);
        var cpu = new RectangleF(board.X + 34, board.Y + (board.Height - cpuSize) / 2f, cpuSize, cpuSize);
        Gfx.FillRound(g, dark ? Color.FromArgb(52, 56, 64) : Color.FromArgb(200, 205, 212), RectangleF.Inflate(cpu, 10, 10), 8);
        Gfx.FillRound(g, dark ? Color.FromArgb(150, 156, 166) : Color.FromArgb(176, 182, 192), cpu, 6);
        using (var pins = new Pen(ColorX.Alpha(Color.Black, 40)))
            for (var i = 1; i < 6; i++)
            {
                var d = cpu.Width * i / 6f;
                g.DrawLine(pins, cpu.X + d, cpu.Y + 6, cpu.X + d, cpu.Bottom - 6);
            }
        Draw.Text(g, "CPU", OndaTheme.FontSemiboldLarge, Rectangle.Round(new RectangleF(cpu.X, cpu.Y + cpu.Height / 2 - 24, cpu.Width, 24)), Color.FromArgb(40, 44, 52), Gfx.Center);
        Draw.Text(g, _socket.Length > 0 ? _socket : "", OndaTheme.FontSmall, Rectangle.Round(new RectangleF(cpu.X, cpu.Y + cpu.Height / 2, cpu.Width, 20)), Color.FromArgb(60, 64, 72), Gfx.Center);
        Draw.Text(g, ShortCpu(_cpu), OndaTheme.FontSmall, Rectangle.Round(new RectangleF(cpu.X - 20, cpu.Bottom + 16, cpu.Width + 40, 34)), c.TextMuted, Gfx.Center | TextFormatFlags.WordBreak);

        var slots = _memory.Slots;
        if (slots.Count == 0)
        {
            Draw.Text(g, "Bellek yuvası bilgisi okunamadı", OndaTheme.Font, Rectangle.Round(new RectangleF(cpu.Right + 40, board.Y, board.Right - cpu.Right - 60, board.Height)), c.TextMuted, Gfx.Center);
            return;
        }

        var area = new RectangleF(cpu.Right + 60, board.Y + 30, board.Right - cpu.Right - 90, board.Height - 60);
        var p = Progress;
        if (_laptop)
        {
            // SO-DIMM: yatay, üst üste
            const float gap = 58f;
            var h = Math.Min(70f, (area.Height - (slots.Count - 1) * gap) / slots.Count);
            var w = Math.Min(area.Width, 480f);
            var y0 = area.Y + (area.Height - (slots.Count * h + (slots.Count - 1) * gap)) / 2;
            for (var i = 0; i < slots.Count; i++)
            {
                var slot = new RectangleF(area.X + (area.Width - w) / 2, y0 + i * (h + gap), w, h);
                _slots.Add(slot);
                PaintSlot(g, slot, slots[i], i, horizontal: true, p);
            }
        }
        else
        {
            // DIMM: dikey, yan yana
            var gap = 26f;
            var w = Math.Min(54f, (area.Width - (slots.Count - 1) * gap) / slots.Count);
            var total = slots.Count * w + (slots.Count - 1) * gap;
            var x0 = area.X + (area.Width - total) / 2;
            for (var i = 0; i < slots.Count; i++)
            {
                var slot = new RectangleF(x0 + i * (w + gap), area.Y, w, area.Height - 24);
                _slots.Add(slot);
                PaintSlot(g, slot, slots[i], i, horizontal: false, p);
            }
        }
    }

    void PaintSlot(Graphics g, RectangleF slot, MemorySlot s, int index, bool horizontal, float p)
    {
        var c = C;
        var dark = OndaTheme.Mode == OndaThemeMode.Dark;
        var hover = index == _hover;
        var rail = dark ? Color.FromArgb(40, 44, 52) : Color.FromArgb(58, 62, 72);
        var accent = hover ? c.Primary : Color.Empty;

        // yuva rayı
        var railRect = horizontal ? RectangleF.Inflate(slot, 6, 6) : RectangleF.Inflate(slot, 6, 8);
        Gfx.FillRound(g, ColorX.Alpha(rail, s.IsEmpty ? 70 : 200), railRect, 6);
        if (hover) Gfx.DrawRound(g, accent, RectangleF.Inflate(railRect, 3, 3), 8, 2.4f);
        // mandallar
        using (var clip = new SolidBrush(dark ? Color.FromArgb(200, 204, 212) : Color.FromArgb(235, 238, 242)))
        {
            if (horizontal)
            {
                g.FillRectangle(clip, railRect.X - 4, railRect.Y + 4, 6, railRect.Height - 8);
                g.FillRectangle(clip, railRect.Right - 2, railRect.Y + 4, 6, railRect.Height - 8);
            }
            else
            {
                g.FillRectangle(clip, railRect.X + 4, railRect.Y - 4, railRect.Width - 8, 6);
                g.FillRectangle(clip, railRect.X + 4, railRect.Bottom - 2, railRect.Width - 8, 6);
            }
        }

        // etiket (yuva adı)
        var label = Locator(s, index, horizontal ? 32 : 12);
        if (horizontal)
            Draw.Text(g, label, OndaTheme.FontSmall, Rectangle.Round(new RectangleF(slot.X, slot.Y - 30, slot.Width, 18)), hover ? ColorX.Readable(c.Primary) : c.TextMuted, Gfx.Left);
        else
            Draw.Text(g, label, OndaTheme.FontSmall, Rectangle.Round(new RectangleF(slot.X - 20, slot.Bottom + 12, slot.Width + 40, 18)), hover ? ColorX.Readable(c.Primary) : c.TextMuted, Gfx.Center);

        if (s.Module is not { } m)
        {
            using var dash = new Pen(ColorX.Alpha(c.TextMuted, 150), 1.6f) { DashStyle = DashStyle.Dash };
            using var path = Gfx.Round(RectangleF.Inflate(slot, -3, -3), 5);
            g.DrawPath(dash, path);
            var txt = horizontal ? "Boş yuva  +" : "Boş";
            if (horizontal)
                Draw.Text(g, txt, OndaTheme.FontSemibold, Rectangle.Round(slot), c.TextMuted, Gfx.Center);
            else
            {
                Draw.Vertical(g, "Boş yuva", OndaTheme.FontSemibold, new PointF(slot.X + slot.Width / 2, slot.Y + slot.Height / 2), c.TextMuted);
            }
            return;
        }

        // modül: animasyonla yuvaya oturur
        var offset = (1 - p) * 40;
        var stick = horizontal ? new RectangleF(slot.X + 2, slot.Y + 2 - offset, slot.Width - 4, slot.Height - 4) : new RectangleF(slot.X + 2, slot.Y + 2 - offset, slot.Width - 4, slot.Height - 4);
        var state = g.Save();
        g.SetClip(RectangleF.Inflate(railRect, 2, 2 + 40));
        using (var pcb = new LinearGradientBrush(stick, Pcb, PcbDark, horizontal ? LinearGradientMode.Vertical : LinearGradientMode.Horizontal))
        using (var path = Gfx.Round(stick, 3))
            g.FillPath(pcb, path);

        // altın bacaklar
        using (var gold = new SolidBrush(Gold))
        {
            if (horizontal)
            {
                var gy = stick.Bottom - 7;
                for (var x = stick.X + 6; x < stick.Right - 6; x += 5) g.FillRectangle(gold, x, gy, 3, 6);
            }
            else
            {
                var gx = stick.X;
                for (var y = stick.Y + 6; y < stick.Bottom - 6; y += 5) g.FillRectangle(gold, gx, y, 6, 3);
            }
        }

        // yongalar
        using (var chip = new SolidBrush(ChipColor))
        {
            var n = horizontal ? 4 : 8;
            for (var i = 0; i < n; i++)
            {
                RectangleF cr;
                if (horizontal)
                {
                    var cw = (stick.Width - 30) / n;
                    cr = new RectangleF(stick.X + 15 + i * cw + 3, stick.Y + 6, cw - 6, stick.Height - 20);
                }
                else
                {
                    var ch = (stick.Height - 30) / n;
                    cr = new RectangleF(stick.X + 12, stick.Y + 15 + i * ch + 3, stick.Width - 18, ch - 6);
                }
                g.FillRectangle(chip, cr);
            }
        }

        // etiket
        var text = $"{Formats.Gb(m.CapacityGb)}";
        var sub = $"{m.TypeText}-{Math.Max(m.RatedMts, m.ConfiguredMts)}";
        if (horizontal)
        {
            var lr = new RectangleF(stick.X + stick.Width * 0.30f, stick.Y + 4, stick.Width * 0.40f, stick.Height - 18);
            Gfx.FillRound(g, Color.FromArgb(245, 246, 248), lr, 3);
            Draw.Text(g, $"{text}  {sub}", OndaTheme.FontSemibold, Rectangle.Round(lr), Color.FromArgb(25, 28, 34), Gfx.Center);
        }
        else
        {
            var lr = new RectangleF(stick.X + 10, stick.Y + stick.Height * 0.32f, stick.Width - 14, stick.Height * 0.36f);
            Gfx.FillRound(g, Color.FromArgb(245, 246, 248), lr, 3);
            Draw.Vertical(g, $"{text} · {sub}", OndaTheme.FontSemiboldSmall, new PointF(lr.X + lr.Width / 2, lr.Y + lr.Height / 2), Color.FromArgb(25, 28, 34));
        }
        g.Restore(state);
    }

    static string Locator(MemorySlot s, int index, int max)
    {
        var l = s.Locator.Trim();
        if (l.Length == 0 || l.StartsWith("Boş", StringComparison.Ordinal)) return $"Yuva {index + 1}";
        if (l.Length > max) l = l.Replace("Controller", "C").Replace("Channel", "K");
        return l.Length > max ? l[..max] : l;
    }

    static string ShortCpu(string name) => name.Replace(" Processor", "").Replace("-Core", " çekirdek").Replace(" CPU", "");
}
