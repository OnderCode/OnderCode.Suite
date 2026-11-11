using Otel.Application;

namespace Otel.WinForms.Pages;

sealed class GuestsPage : PageBase
{
    readonly OndaGrid _grid = new() { RowHeight = 36 };
    readonly OndaLabel _hint = new() { Style = OndaLabelStyle.Muted, Text = "Sadakat: her gece 10 puan · 200 puan Gümüş · 500 puan Altın" };

    public GuestsPage() : base("Misafirler", "Misafir kartları ve sadakat programı")
    {
        _grid.Columns.Add(new OndaGridColumn(nameof(GuestRow.FullName), "Misafir", 230));
        _grid.Columns.Add(new OndaGridColumn(nameof(GuestRow.TierText), "Seviye", 110, OndaColumnKind.Badge) { BadgeColor = App.BadgeColor });
        _grid.Columns.Add(new OndaGridColumn(nameof(GuestRow.LoyaltyPoints), "Puan", 90, OndaColumnKind.Number));
        _grid.Columns.Add(new OndaGridColumn(nameof(GuestRow.StayCount), "Konaklama", 100, OndaColumnKind.Number) { ShowTotal = true });
        _grid.Columns.Add(new OndaGridColumn(nameof(GuestRow.TotalSpent), "Toplam konaklama", 160, OndaColumnKind.Money) { ShowTotal = true });
        _grid.Columns.Add(new OndaGridColumn(nameof(GuestRow.LastStay), "Son çıkış", 120) { Format = "dd.MM.yyyy" });
        _grid.Columns.Add(new OndaGridColumn(nameof(GuestRow.Nationality), "Uyruk", 80));
        _grid.Columns.Add(new OndaGridColumn(nameof(GuestRow.Email), "E-posta", 240));
        Controls.AddRange(new Control[] { _grid, _hint });
    }

    protected override async Task LoadAsync()
    {
        var result = await App.Send(new ListGuests { PageSize = 1000 });
        if (!result.IsSuccess) { App.ShowErrors(result); return; }
        _grid.DataSource = result.Value.Items;
        Subtitle = $"{result.Value.TotalCount} misafir  ·  puanı en yüksek olanlar üstte";
    }

    protected override void LayoutPage(Rectangle r)
    {
        _hint.Location = new Point(r.X, r.Y + 6);
        _grid.SetBounds(r.X, r.Y + 40, r.Width, r.Height - 40);
    }
}
