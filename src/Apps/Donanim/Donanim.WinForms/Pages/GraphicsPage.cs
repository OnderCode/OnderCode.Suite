using Donanim.WinForms.Controls;

namespace Donanim.WinForms.Pages;

/// <summary>Ekran kartı ve ekranlar: tümleşik/harici ayrımı, video belleği, sürücü, çözünürlük ve bağlı monitörler.</summary>
sealed class GraphicsPage : KarnePage
{
    readonly ValueTile _gpu = new() { Label = "Ana ekran kartı", Icon = Glyph.Monitor, IconColor = App.Pink };
    readonly ValueTile _vram = new() { Label = "Video belleği", Icon = Glyph.Memory, IconColor = App.Violet };
    readonly ValueTile _res = new() { Label = "Çözünürlük", Icon = Glyph.Monitor, IconColor = App.Cyan };
    readonly ValueTile _screens = new() { Label = "Ekran", Icon = Glyph.Monitor, IconColor = App.Teal };
    readonly List<InfoList> _cards = [];
    readonly OndaLabel _monTitle = new() { Text = "Bağlı ekranlar", Style = OndaLabelStyle.Title };
    readonly OndaGrid _monitors = new() { ShowFilterRow = false, ShowFooter = false, RowHeight = 36, AutoGenerateColumns = false, EmptyText = "Ekran bilgisi okunamadı" };
    readonly AdviceList _advice = new() { Title = "Grafik önerileri", Icon = Glyph.Bulb, IconColor = OndaTheme.Colors.Warning };

    sealed record MonitorRow(string Name, string Maker, string Size, string Year, string Kind);

    public GraphicsPage() : base("Ekran kartı ve ekran", "Yükleniyor…")
    {
        _monitors.Columns.AddRange(
        [
            new OndaGridColumn(nameof(MonitorRow.Name), "Ekran", 200),
            new OndaGridColumn(nameof(MonitorRow.Maker), "Üretici", 130),
            new OndaGridColumn(nameof(MonitorRow.Size), "Boyut", 100),
            new OndaGridColumn(nameof(MonitorRow.Year), "Yıl", 80),
            new OndaGridColumn(nameof(MonitorRow.Kind), "Bağlantı", 110, OndaColumnKind.Badge) { BadgeColor = v => v as string == "Dahili" ? App.Teal : OndaTheme.Colors.Primary },
        ]);
        Controls.AddRange(new Control[] { _gpu, _vram, _res, _screens, _monTitle, _monitors, _advice });
    }

    protected override void Bind(Karne k)
    {
        var s = k.Snapshot;
        var main = s.MainGpu;
        Subtitle = $"{MachineLine(s)}  ·  {s.Gpus.Count} ekran kartı, {s.Monitors.Count} ekran";
        _gpu.Set(main is null ? "-" : main.KindText, main?.Name ?? "", main is { IsIntegrated: false } ? (Color?)OndaTheme.Colors.Success : null);
        _vram.Set(main is { IsIntegrated: false, VramGb: { } v } ? Formats.Gb(v) : main is { IsIntegrated: true } ? "Paylaşımlı" : "-",
            main is { IsIntegrated: true } ? "sistem belleğinden kullanır" : "");
        _res.Set(s.DisplayResolution, s.DisplayRefreshHz > 0 ? $"{s.DisplayRefreshHz} Hz" : "");
        var biggest = s.Monitors.Where(m => m.DiagonalInch is not null).OrderByDescending(m => m.DiagonalInch).FirstOrDefault();
        _screens.Set(s.Monitors.Count == 0 ? "-" : $"{s.Monitors.Count} ekran", biggest is null ? "" : $"en büyüğü {biggest.DiagonalInch:0.#} inç");

        foreach (var c in _cards) { Controls.Remove(c); c.Dispose(); }
        _cards.Clear();
        foreach (var g in s.Gpus.Take(3))
        {
            var card = new InfoList { Title = g.Name, Icon = Glyph.Monitor, IconColor = g.IsIntegrated ? App.Slate : App.Pink, LabelWidth = 150, RowHeight = 32 };
            card.SetRows(
            [
                ("Tür", g.IsIntegrated ? "Tümleşik (işlemcinin içinde)" : "Harici ekran kartı", g.IsIntegrated ? null : (Color?)OndaTheme.Colors.Success),
                ("Video belleği", g.IsIntegrated ? "Sistem belleğini paylaşır" : g.VramGb is { } vr ? Formats.Gb(vr) : "-", null),
                ("Sürücü sürümü", g.DriverVersion.Length > 0 ? g.DriverVersion : "-", null),
                ("Sürücü tarihi", Formats.Date(g.DriverDate), null),
                ("Çözünürlük", g.Resolution == "-" ? "Ekranı diğer kart sürüyor" : g.Resolution + (g.RefreshHz > 0 ? $" · {g.RefreshHz} Hz" : ""), null),
            ]);
            _cards.Add(card);
            Controls.Add(card);
        }
        _monitors.DataSource = s.Monitors.Select(m => new MonitorRow(m.Name, m.Manufacturer, m.DiagonalInch is { } d ? $"{d:0.#} inç" : "-", m.Year?.ToString() ?? "-", m.IsInternal ? "Dahili" : "Harici")).ToList();
        var adv = k.Advice.Where(a => a.Category == GradeCategory.Graphics).ToList();
        _advice.Items = adv.Count > 0 ? adv : [new Advice(AdvicePriority.Good, GradeCategory.Graphics, "Grafik tarafı yolunda", main is { IsIntegrated: true } ? "Tümleşik grafik ofis, internet ve film için yeterli." : "Ekran kartı ve sürücüsü için bir öneri yok.")];
    }

    protected override void LayoutPage(Rectangle r)
    {
        var tiles = new[] { _gpu, _vram, _res, _screens };
        var cw = (r.Width - Gap * 3) / 4;
        for (var i = 0; i < 4; i++) tiles[i].SetBounds(r.X + i * (cw + Gap), r.Y, cw, 106);
        var y = r.Y + 106 + Gap;
        var lw = (int)(r.Width * 0.62);
        var n = Math.Max(1, _cards.Count);
        var cardW = (lw - Gap * (n - 1)) / n;
        var ch = _cards.Count > 0 ? _cards[0].PreferredHeight : 0;
        for (var i = 0; i < _cards.Count; i++) _cards[i].SetBounds(r.X + i * (cardW + Gap), y, cardW, ch);
        var my = y + ch + Gap;
        _monTitle.Location = new Point(r.X - 2, my + 2);
        _monitors.SetBounds(r.X, my + 36, lw, Math.Max(120, r.Bottom - my - 36));
        _advice.SetBounds(r.X + lw + Gap, y, r.Width - lw - Gap, r.Bottom - y);
    }
}
