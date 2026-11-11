using Otel.Application;
using Otel.Domain;

namespace Otel.WinForms.Pages;

sealed class ReservationsPage : PageBase
{
    static readonly ReservationStatus?[] Filters = [null, ReservationStatus.Confirmed, ReservationStatus.CheckedIn, ReservationStatus.CheckedOut, ReservationStatus.Cancelled];
    readonly OndaSegmented _filter = new() { Items = ["Tümü", "Onaylı", "Konaklıyor", "Çıkış yaptı", "İptal"] };
    readonly OndaButton _new = new() { Action = OndaAction.New, Text = "Yeni rezervasyon", Width = 180, Shortcut = Keys.F3 };
    readonly OndaButton _excel = new() { Action = OndaAction.Excel, Kind = OndaButtonKind.Outline, Width = 120 };
    readonly OndaGrid _grid = new() { RowHeight = 36 };

    public ReservationsPage() : base("Rezervasyonlar", "Filtre satırına yazarak arayın; satıra çift tıklayın: detay ve folyo")
    {
        _grid.Columns.Add(new OndaGridColumn(nameof(ReservationRow.Code), "Kod", 110));
        _grid.Columns.Add(new OndaGridColumn(nameof(ReservationRow.GuestName), "Misafir", 210));
        _grid.Columns.Add(new OndaGridColumn(nameof(ReservationRow.RoomNumber), "Oda", 70));
        _grid.Columns.Add(new OndaGridColumn(nameof(ReservationRow.RoomType), "Oda tipi", 130));
        _grid.Columns.Add(new OndaGridColumn(nameof(ReservationRow.CheckIn), "Giriş", 105) { Format = "dd.MM.yyyy" });
        _grid.Columns.Add(new OndaGridColumn(nameof(ReservationRow.CheckOut), "Çıkış", 105) { Format = "dd.MM.yyyy" });
        _grid.Columns.Add(new OndaGridColumn(nameof(ReservationRow.Nights), "Gece", 70, OndaColumnKind.Number) { ShowTotal = true });
        _grid.Columns.Add(new OndaGridColumn(nameof(ReservationRow.Adults), "Kişi", 65, OndaColumnKind.Number));
        _grid.Columns.Add(new OndaGridColumn(nameof(ReservationRow.StatusText), "Durum", 125, OndaColumnKind.Badge) { BadgeColor = App.BadgeColor });
        _grid.Columns.Add(new OndaGridColumn(nameof(ReservationRow.RoomTotal), "Konaklama", 135, OndaColumnKind.Money) { ShowTotal = true });

        _filter.SelectedIndexChanged += async (_, _) => await ReloadAsync();
        _new.Click += async (_, _) => { if (ReservationForm.Open(FindForm()) is not null) await ReloadAsync(); };
        _excel.Click += (_, _) => _grid.ExportExcelWithDialog("Rezervasyonlar");
        _grid.RowActivated += async (_, e) =>
        {
            if (e.Item is ReservationRow row && await ReservationDetailForm.OpenAsync(FindForm(), row.Id)) await ReloadAsync();
        };
        Controls.AddRange(new Control[] { _filter, _new, _excel, _grid });
    }

    protected override async Task LoadAsync()
    {
        var result = await App.Send(new ListReservations { PageSize = 1000, Status = Filters[Math.Max(0, _filter.SelectedIndex)] });
        if (!result.IsSuccess) { App.ShowErrors(result); return; }
        _grid.DataSource = result.Value.Items;
        Subtitle = $"{result.Value.TotalCount} kayıt  ·  filtre satırına yazarak arayın, satıra çift tıklayın: detay ve folyo";
    }

    protected override void LayoutPage(Rectangle r)
    {
        _filter.SetBounds(r.X, r.Y, 520, 38);
        RightAlign(r.Right, r.Y, _excel, _new);
        _grid.SetBounds(r.X, r.Y + 54, r.Width, r.Height - 54);
    }
}
