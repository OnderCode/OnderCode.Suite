using Otopark.Application;

namespace Otopark.WinForms.Pages;

/// <summary>Aylık aboneler: kalan gün, son 30 gündeki giriş sayısı; uzatma ve yeni abonelik.</summary>
sealed class SubscriptionsPage : PageBase
{
    static readonly SubscriptionFilter[] Filters = [SubscriptionFilter.Active, SubscriptionFilter.Expiring, SubscriptionFilter.Expired, SubscriptionFilter.All];
    readonly OndaSegmented _filter = new() { Items = ["Aktif", "Yakında bitiyor", "Bitti", "Tümü"], SelectedIndex = 0 };
    readonly OndaButton _new = new() { Action = OndaAction.New, Text = "Yeni abonelik", Width = 170 };
    readonly OndaButton _extend = new() { Text = "1 ay uzat", Icon = "", Kind = OndaButtonKind.Outline, Width = 140 };
    readonly OndaButton _excel = new() { Action = OndaAction.Excel, Kind = OndaButtonKind.Outline, Width = 120 };
    readonly OndaGrid _grid = new() { RowHeight = 34, EmptyText = "Kayıt yok" };

    public SubscriptionsPage() : base("Abonelikler", "Yükleniyor…")
    {
        _grid.Columns.Add(new OndaGridColumn(nameof(SubscriptionRow.Plate), "Plaka", 130));
        _grid.Columns.Add(new OndaGridColumn(nameof(SubscriptionRow.OwnerName), "Abone", 200));
        _grid.Columns.Add(new OndaGridColumn(nameof(SubscriptionRow.VehicleText), "Araç", 120, OndaColumnKind.Badge) { BadgeColor = App.BadgeColor });
        _grid.Columns.Add(new OndaGridColumn(nameof(SubscriptionRow.StartDate), "Başlangıç", 110) { Format = "dd.MM.yyyy" });
        _grid.Columns.Add(new OndaGridColumn(nameof(SubscriptionRow.EndDate), "Bitiş", 110) { Format = "dd.MM.yyyy" });
        _grid.Columns.Add(new OndaGridColumn(nameof(SubscriptionRow.DaysLeft), "Kalan gün", 100, OndaColumnKind.Number));
        _grid.Columns.Add(new OndaGridColumn(nameof(SubscriptionRow.StatusText), "Durum", 140, OndaColumnKind.Badge) { BadgeColor = App.BadgeColor });
        _grid.Columns.Add(new OndaGridColumn(nameof(SubscriptionRow.VisitsLast30), "Giriş (30 gün)", 120, OndaColumnKind.Number));
        _grid.Columns.Add(new OndaGridColumn(nameof(SubscriptionRow.MonthlyFee), "Aylık", 110, OndaColumnKind.Money));
        _grid.Columns.Add(new OndaGridColumn(nameof(SubscriptionRow.TotalPaid), "Ödenen", 120, OndaColumnKind.Money) { ShowTotal = true });

        _filter.SelectedIndexChanged += async (_, _) => await ReloadAsync();
        _new.Click += async (_, _) => { if (SubscriptionForm.Open(FindForm()) is not null) await ReloadAsync(); };
        _extend.Click += async (_, _) => await ExtendAsync();
        _excel.Click += (_, _) => _grid.ExportExcelWithDialog("Abonelikler");
        Controls.AddRange(new Control[] { _filter, _new, _extend, _excel, _grid });
    }

    protected override async Task LoadAsync()
    {
        var result = await App.Send(new ListSubscriptions(Filters[Math.Max(0, _filter.SelectedIndex)]));
        if (!result.IsSuccess) { App.ShowErrors(result); return; }
        _grid.DataSource = result.Value;
        var all = (await App.Send(new ListSubscriptions(SubscriptionFilter.Active))).Value;
        Subtitle = $"{all.Count} aktif abone  ·  {all.Count(s => s.StatusText == "Yakında bitiyor")} abonelik bu hafta bitiyor  ·  aylık gelir {all.Sum(s => s.MonthlyFee):N0} ₺";
    }

    async Task ExtendAsync()
    {
        if (_grid.SelectedItem is not SubscriptionRow row) { OndaToast.Info("Önce bir abone seçin."); return; }
        if (!OndaMessageBox.Ask(FindForm(), $"{row.Plate} ({row.OwnerName}) aboneliği 1 ay uzatılsın mı? Ücret: {App.Money(row.MonthlyFee)}", "Uzatma")) return;
        var r = await App.Send(new ExtendSubscription(row.Id, 1));
        if (!r.IsSuccess) { App.ShowErrors(r); return; }
        OndaToast.Success($"{row.Plate} aboneliği uzatıldı.");
        await ReloadAsync();
    }

    protected override void LayoutPage(Rectangle r)
    {
        _filter.SetBounds(r.X, r.Y, 480, 38);
        RightAlign(r.Right, r.Y, _excel, _extend, _new);
        _grid.SetBounds(r.X, r.Y + 54, r.Width, r.Height - 54);
    }
}
