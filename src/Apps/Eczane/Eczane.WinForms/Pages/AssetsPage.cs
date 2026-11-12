namespace Eczane.WinForms.Pages;

/// <summary>Demirbaş: alış bedeli, birikmiş amortisman, net defter değeri, bu yılın amortismanı; bakım takvimi, tamir ve hurdaya ayırma.</summary>
sealed class AssetsPage : PageBase
{
    readonly OndaSegmented _view = new() { Items = ["Demirbaşlar", "Bakım geçmişi"], SelectedIndex = 0 };
    readonly OndaSwitch _retired = new() { Text = "Hurdaları göster" };
    readonly OndaButton _new = new() { Action = OndaAction.New, Text = "Yeni demirbaş", Width = 150 };
    readonly OndaButton _maint = new() { Text = "Bakım kaydı", Icon = "\uE90F", Kind = OndaButtonKind.Outline, Width = 130 };
    readonly OndaButton _repair = new() { Text = "Tamire gönder", Icon = "\uE90F", Kind = OndaButtonKind.Outline, Width = 140 };
    readonly OndaButton _retire = new() { Text = "Hurdaya ayır", Icon = "\uE74D", Kind = OndaButtonKind.Outline, Width = 130 };
    readonly OndaButton _excel = new() { Action = OndaAction.Excel, Kind = OndaButtonKind.Outline, Width = 90 };
    readonly Stat _cost = new() { Title = "Alış bedeli", Icon = "\uE772", Prefix = "₺" };
    readonly Stat _book = new() { Title = "Net defter değeri", Icon = "\uE9D2", Prefix = "₺", IconColor = App.Teal };
    readonly Stat _year = new() { Title = "Bu yılın amortismanı", Icon = "\uE787", Prefix = "₺", IconColor = App.Violet };
    readonly Stat _due = new() { Title = "Bakımı yaklaşan", Icon = "\uE90F", IconColor = App.Amber };
    readonly OndaGrid _grid = new() { RowHeight = 34, AutoGenerateColumns = false };
    readonly OndaGrid _history = new() { RowHeight = 32, AutoGenerateColumns = false, Visible = false };
    IReadOnlyList<AssetRow> _rows = [];

    public AssetsPage() : base("Demirbaş", "Yükleniyor…")
    {
        _grid.Columns.AddRange(
        [
            Col.Text(nameof(AssetRow.Code), "Kod", 90),
            Col.Text(nameof(AssetRow.Name), "Ad", 230),
            Col.Badge(nameof(AssetRow.CategoryText), "Grup", 150),
            Col.Text(nameof(AssetRow.Location), "Konum", 120),
            Col.Badge(nameof(AssetRow.StatusText), "Durum", 118),
            Col.Day(nameof(AssetRow.PurchaseDate), "Alış", 100),
            Col.Int(nameof(AssetRow.UsefulLifeYears), "Ömür", 60),
            Col.Money(nameof(AssetRow.Cost), "Bedel", 120, true),
            Col.Money(nameof(AssetRow.Accumulated), "Birikmiş amort.", 130, true),
            Col.Progress(nameof(AssetRow.DepreciatedShare), "Yıpranma", 110),
            Col.Money(nameof(AssetRow.BookValue), "Net değer", 120, true),
            Col.Money(nameof(AssetRow.ThisYear), "Bu yıl", 110, true),
            Col.Day(nameof(AssetRow.NextMaintenance), "Sonraki bakım", 110),
            Col.Text(nameof(AssetRow.MaintenanceText), "Bakım durumu", 130),
            Col.Money(nameof(AssetRow.MaintenanceCost), "Bakım gideri", 110, true),
            Col.Text(nameof(AssetRow.SerialNo), "Seri no", 120),
        ]);
        _grid.RowTint = o => o is AssetRow a ? a.Status == AssetStatus.InRepair ? Color.FromArgb(26, 245, 158, 11) : a.NextMaintenance is not null && a.MaintenanceDays < 0 ? Color.FromArgb(26, 239, 68, 68) : null : null;
        _history.Columns.AddRange(
        [
            Col.Day(nameof(MaintenanceRow.Date), "Tarih", 110),
            Col.Text(nameof(MaintenanceRow.Asset), "Demirbaş", 260),
            Col.Badge(nameof(MaintenanceRow.KindText), "İşlem", 140),
            Col.Money(nameof(MaintenanceRow.Cost), "Maliyet", 120, true),
            Col.Text(nameof(MaintenanceRow.Note), "Açıklama", 360),
        ]);
        _view.SelectedIndexChanged += (_, _) => Relayout();
        _retired.CheckedChanged += async (_, _) => await ReloadAsync();
        _new.Click += async (_, _) => { if (AssetForm.Open(FindForm(), NextCode())) await ReloadAsync(); };
        _maint.Click += async (_, _) => { if (_grid.SelectedItem is AssetRow a && a.Status != AssetStatus.Retired) { if (MaintenanceForm.Open(FindForm(), a)) await ReloadAsync(); } else OndaToast.Info("Önce kullanımdaki bir demirbaş seçin."); };
        _repair.Click += async (_, _) => await ChangeAsync();
        _retire.Click += async (_, _) => await RetireAsync();
        _excel.Click += (_, _) => { if (_view.SelectedIndex == 0) _grid.ExportExcelWithDialog("Demirbaş"); else _history.ExportExcelWithDialog("Bakım geçmişi"); };
        _grid.SelectionChanged += (_, _) => _repair.Text = _grid.SelectedItem is AssetRow { Status: AssetStatus.InRepair } ? "Hizmete al" : "Tamire gönder";
        Controls.AddRange(new Control[] { _view, _retired, _new, _maint, _repair, _retire, _excel, _cost, _book, _year, _due, _grid, _history });
    }

    string NextCode()
    {
        var max = _rows.Select(a => int.TryParse(new string(a.Code.Where(char.IsDigit).ToArray()), out var n) ? n : 0).DefaultIfEmpty(0).Max();
        return $"DMR-{max + 1:000}";
    }

    protected override async Task LoadAsync()
    {
        var r = await App.Send(new ListAssets(_retired.Checked));
        if (!r.IsSuccess) { App.ShowErrors(r); return; }
        _rows = r.Value;
        _grid.DataSource = _rows;
        var h = await App.Send(new ListMaintenance());
        if (h.IsSuccess) _history.DataSource = h.Value;
        var active = _rows.Where(a => a.Status != AssetStatus.Retired).ToList();
        _cost.Value = active.Sum(a => a.Cost);
        _cost.DeltaText = $"{active.Count} demirbaş";
        _book.Value = active.Sum(a => a.BookValue);
        _book.DeltaText = $"birikmiş amortisman {App.Money(active.Sum(a => a.Accumulated))}";
        _year.Value = active.Sum(a => a.ThisYear);
        _year.DeltaText = $"{App.Today.Year} · doğrusal yöntem";
        var due = active.Where(a => a.NextMaintenance is not null && a.MaintenanceDays <= 30).ToList();
        _due.Value = due.Count;
        _due.DeltaText = $"{due.Count(a => a.MaintenanceDays < 0)} gecikmiş · {active.Count(a => a.Status == AssetStatus.InRepair)} tamirde";
        Subtitle = $"Amortisman aylık doğrusal hesaplanır: (bedel − hurda değeri) / faydalı ömür  ·  soğuk zincir dolapları ve terazi yılda bir kalibre edilir";
        foreach (var c in new[] { _cost, _book, _year, _due }) c.Replay();
        Relayout();
    }

    async Task ChangeAsync()
    {
        if (_grid.SelectedItem is not AssetRow a || a.Status == AssetStatus.Retired) { OndaToast.Info("Önce kullanımdaki bir demirbaş seçin."); return; }
        var action = a.Status == AssetStatus.InRepair ? AssetAction.BackInService : AssetAction.Repair;
        var r = await App.Send(new ChangeAssetStatus(a.Id, action));
        if (!r.IsSuccess) { App.ShowErrors(r); return; }
        OndaToast.Success(a.Name, action == AssetAction.Repair ? "Tamire gönderildi" : "Hizmete alındı");
        await ReloadAsync();
    }

    async Task RetireAsync()
    {
        if (_grid.SelectedItem is not AssetRow a || a.Status == AssetStatus.Retired) { OndaToast.Info("Önce bir demirbaş seçin."); return; }
        if (!OndaMessageBox.Ask(FindForm(), $"{a.Code} {a.Name} hurdaya ayrılsın mı? Net defter değeri {App.Money(a.BookValue)}.", "Hurdaya ayır")) return;
        var r = await App.Send(new ChangeAssetStatus(a.Id, AssetAction.Retire));
        if (!r.IsSuccess) { App.ShowErrors(r); return; }
        await ReloadAsync();
    }

    protected override void LayoutPage(Rectangle r)
    {
        var list = _view.SelectedIndex <= 0;
        _view.SetBounds(r.X, r.Y, 260, 38);
        _retired.SetBounds(r.X + 272, r.Y + 8, 150, 24);
        RightAlign(r.Right, r.Y, _excel, _retire, _repair, _maint, _new);
        var y = r.Y + 52;
        var cards = new[] { _cost, _book, _year, _due };
        var cw = (r.Width - Gap * 3) / 4;
        for (var i = 0; i < 4; i++) cards[i].SetBounds(r.X + i * (cw + Gap), y, cw, 140);
        y += 140 + Gap;
        _grid.Visible = list;
        _history.Visible = !list;
        (list ? _grid : _history).SetBounds(r.X, y, r.Width, r.Bottom - y);
    }
}
