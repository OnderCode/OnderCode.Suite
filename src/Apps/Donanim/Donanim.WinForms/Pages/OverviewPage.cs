using Donanim.WinForms.Controls;

namespace Donanim.WinForms.Pages;

/// <summary>Karne: bilgisayarın özeti, her bileşenin notu, öncelikli öneriler ve alışveriş listesi.</summary>
sealed class OverviewPage : KarnePage
{
    readonly OndaSegmented _source = new() { Width = 560, Height = 36 };
    readonly OndaButton _refresh = new() { Text = "Yeniden tara", Icon = Glyph.Refresh, Kind = OndaButtonKind.Outline, Width = 150 };
    readonly HeroTile _hero = new() { Title = "Bilgisayar", Icon = Glyph.Karne };
    readonly List<GradeTile> _grades = [];
    readonly AdviceList _advice = new() { Title = "Öncelikli öneriler", Icon = Glyph.Bulb, IconColor = OndaTheme.Colors.Warning, Compact = true };
    readonly ShoppingList _shopping = new();
    bool _binding;

    public event EventHandler<GradeCategory>? GradeClicked;

    public OverviewPage() : base("Donanım karnesi", "Yükleniyor…")
    {
        _source.Items = App.Sources.Select(s => s == App.ThisComputer ? "Bu bilgisayar" : s.Replace("Örnek: ", "Örnek · ")).ToArray();
        _source.SelectedIndexChanged += async (_, _) =>
        {
            if (_binding) return;
            await App.LoadAsync(App.Sources[_source.SelectedIndex]);
        };
        _refresh.Click += async (_, _) => await App.LoadAsync(App.Source);
        for (var i = 0; i < 6; i++)
        {
            var t = new GradeTile();
            t.Click += (_, _) => { if (t.Grade is { } g) GradeClicked?.Invoke(this, g.Category); };
            _grades.Add(t);
        }
        Controls.AddRange(new Control[] { _source, _refresh, _hero, _advice, _shopping });
        Controls.AddRange(_grades.ToArray());
    }

    public OndaSegmented SourceSelector => _source;
    public GradeTile GradeTileAt(int i) => _grades[i];

    protected override void Bind(Karne k)
    {
        _binding = true;
        _source.SelectedIndex = Math.Max(0, App.Sources.ToList().IndexOf(App.Source));
        _binding = false;
        var s = k.Snapshot;
        var crit = k.CriticalCount > 0 ? $"{k.CriticalCount} önemli, " : "";
        Subtitle = $"{MachineLine(s)}  ·  genel ortalama {Formats.Number(k.Average, 1)} ({k.AverageLabel})  ·  {crit}{k.RecommendedCount} önerilen iyileştirme";
        _hero.Karne = k;
        for (var i = 0; i < _grades.Count; i++)
        {
            _grades[i].Visible = i < k.Grades.Count;
            _grades[i].Grade = i < k.Grades.Count ? k.Grades[i] : null;
        }
        _advice.Items = k.Advice;
        _advice.Note = $"{k.Advice.Count} öneri · tümü için Öneriler";
        _shopping.Items = k.Shopping;
    }

    protected override void LayoutPage(Rectangle r)
    {
        _source.Location = new Point(r.Right - _source.Width - _refresh.Width - 12, 26);
        _refresh.Location = new Point(r.Right - _refresh.Width, 26);
        _refresh.Height = 36;

        var topH = Math.Max(300, Math.Min(330, (r.Height - Gap) * 50 / 100));
        var heroW = (int)(r.Width * 0.46);
        _hero.SetBounds(r.X, r.Y, heroW, topH);

        var gx = r.X + heroW + Gap;
        var gw = r.Right - gx;
        var n = _grades.Count(t => t.Visible);
        var th = (topH - Gap) / 2;
        var perRow1 = 3;
        for (int i = 0, vis = 0; i < _grades.Count; i++)
        {
            if (!_grades[i].Visible) continue;
            var row = vis < perRow1 ? 0 : 1;
            var cols = row == 0 ? perRow1 : n - perRow1;
            var col = row == 0 ? vis : vis - perRow1;
            var tw = (gw - Gap * (cols - 1)) / cols;
            _grades[i].SetBounds(gx + col * (tw + Gap), r.Y + row * (th + Gap), tw, th);
            vis++;
        }

        var y = r.Y + topH + Gap;
        var h = r.Bottom - y;
        var aw = (int)(r.Width * 0.62);
        _advice.SetBounds(r.X, y, aw, h);
        _shopping.SetBounds(r.X + aw + Gap, y, r.Width - aw - Gap, h);
    }
}
