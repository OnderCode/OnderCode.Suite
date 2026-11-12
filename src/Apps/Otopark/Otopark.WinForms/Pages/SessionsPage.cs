using Otopark.Application;

namespace Otopark.WinForms.Pages;

/// <summary>Araç hareketleri: içeridekiler, bugün, tümü; plaka araması; satıra çift tıklama içerideki araç için çıkış.</summary>
sealed class SessionsPage : PageBase
{
    static readonly SessionFilter[] Filters = [SessionFilter.Inside, SessionFilter.Today, SessionFilter.All];
    readonly OndaSegmented _filter = new() { Items = ["İçeridekiler", "Bugün", "Son 1000 kayıt"], SelectedIndex = 0 };
    readonly OndaTextBox _search = new() { Compact = true, Placeholder = "Plaka ara…", Icon = "", ShowClearButton = true, Width = 240 };
    readonly OndaButton _excel = new() { Action = OndaAction.Excel, Kind = OndaButtonKind.Outline, Width = 120 };
    readonly OndaGrid _grid = new() { RowHeight = 34 };
    readonly System.Windows.Forms.Timer _debounce = new() { Interval = 350 };

    public SessionsPage() : base("Araç hareketleri", "Yükleniyor…")
    {
        _grid.Columns.Add(new OndaGridColumn(nameof(SessionRow.Plate), "Plaka", 130));
        _grid.Columns.Add(new OndaGridColumn(nameof(SessionRow.VehicleText), "Araç", 120, OndaColumnKind.Badge) { BadgeColor = App.BadgeColor });
        _grid.Columns.Add(new OndaGridColumn(nameof(SessionRow.SpotCode), "Yer", 70));
        _grid.Columns.Add(new OndaGridColumn(nameof(SessionRow.EntryAt), "Giriş", 130, OndaColumnKind.Date) { Format = "dd.MM HH:mm" });
        _grid.Columns.Add(new OndaGridColumn(nameof(SessionRow.ExitAt), "Çıkış", 130, OndaColumnKind.Date) { Format = "dd.MM HH:mm" });
        _grid.Columns.Add(new OndaGridColumn(nameof(SessionRow.DurationText), "Süre", 110));
        _grid.Columns.Add(new OndaGridColumn(nameof(SessionRow.StatusText), "Durum", 100, OndaColumnKind.Badge) { BadgeColor = App.BadgeColor });
        _grid.Columns.Add(new OndaGridColumn(nameof(SessionRow.PaymentText), "Ödeme", 100, OndaColumnKind.Badge) { BadgeColor = App.BadgeColor });
        _grid.Columns.Add(new OndaGridColumn(nameof(SessionRow.ParkingFee), "Otopark", 120, OndaColumnKind.Money) { ShowTotal = true });
        _grid.Columns.Add(new OndaGridColumn(nameof(SessionRow.WashFee), "Yıkama", 110, OndaColumnKind.Money) { ShowTotal = true });
        _grid.Columns.Add(new OndaGridColumn(nameof(SessionRow.Total), "Toplam", 120, OndaColumnKind.Money) { ShowTotal = true });

        _filter.SelectedIndexChanged += async (_, _) => await ReloadAsync();
        _search.TextChanged += (_, _) => { _debounce.Stop(); _debounce.Start(); };
        _debounce.Tick += async (_, _) => { _debounce.Stop(); await ReloadAsync(); };
        _excel.Click += (_, _) => _grid.ExportExcelWithDialog("Araç hareketleri");
        _grid.RowActivated += async (_, e) =>
        {
            if (e.Item is SessionRow { StatusText: "İçeride" } row && await ExitForm.OpenAsync(FindForm(), row.Id)) await ReloadAsync();
        };
        Controls.AddRange(new Control[] { _filter, _search, _excel, _grid });
    }

    protected override async Task LoadAsync()
    {
        var result = await App.Send(new ListSessions { Filter = Filters[Math.Max(0, _filter.SelectedIndex)], Search = _search.Text, PageSize = 1000 });
        if (!result.IsSuccess) { App.ShowErrors(result); return; }
        _grid.DataSource = result.Value.Items;
        Subtitle = $"{result.Value.TotalCount} kayıt  ·  içerideki araca çift tıklayın: çıkış ve tahsilat";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _debounce.Dispose();
        base.Dispose(disposing);
    }

    protected override void LayoutPage(Rectangle r)
    {
        _filter.SetBounds(r.X, r.Y, 440, 38);
        _search.SetBounds(r.X + 456, r.Y + 2, 260, _search.Height);
        RightAlign(r.Right, r.Y, _excel);
        _grid.SetBounds(r.X, r.Y + 54, r.Width, r.Height - 54);
    }
}
