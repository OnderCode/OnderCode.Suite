namespace Eczane.WinForms.Pages;

/// <summary>Satış listesi: dönem ve tür filtresi, reçeteli/serbest satış, iade, fiş ayrıntısı.</summary>
sealed class SalesPage : PageBase
{
    static readonly SalePeriod[] Periods = [SalePeriod.Today, SalePeriod.Last7, SalePeriod.Last30, SalePeriod.All];
    readonly OndaSegmented _period = new() { Items = ["Bugün", "7 gün", "30 gün", "Tümü"], SelectedIndex = 0 };
    readonly OndaSegmented _kind = new() { Items = ["Hepsi", "Reçete", "Serbest"], SelectedIndex = 0 };
    readonly OndaButton _rx = new() { Text = "Reçeteli satış", Icon = "\uE8A5", Shortcut = Keys.F2, Width = 165 };
    readonly OndaButton _retail = new() { Text = "Serbest satış", Icon = "\uE719", Kind = OndaButtonKind.Outline, Shortcut = Keys.F3, Width = 155 };
    readonly OndaButton _return = new() { Text = "İade al", Icon = "\uE7A7", Kind = OndaButtonKind.Outline, Width = 110 };
    readonly OndaButton _excel = new() { Action = OndaAction.Excel, Kind = OndaButtonKind.Outline, Width = 100 };
    readonly OndaGrid _grid = new() { RowHeight = 34, AutoGenerateColumns = false };

    public SalesPage() : base("Satışlar", "Yükleniyor…")
    {
        _grid.Columns.AddRange(
        [
            Col.Date(nameof(SaleRow.SoldAt), "Tarih", 120, "dd.MM HH:mm"),
            Col.Text(nameof(SaleRow.Number), "Fiş", 110),
            Col.Badge(nameof(SaleRow.KindText), "Tür", 110),
            Col.Text(nameof(SaleRow.Patient), "Hasta", 160),
            Col.Badge(nameof(SaleRow.InsuranceText), "Kurum", 100),
            Col.Text(nameof(SaleRow.Hospital), "Reçete kurumu", 190),
            Col.Text(nameof(SaleRow.Branch), "Branş", 130),
            Col.Badge(nameof(SaleRow.ColorText), "Renk", 80),
            Col.Int(nameof(SaleRow.Boxes), "Kutu", 60, true),
            Col.Money(nameof(SaleRow.PatientShare), "Hasta payı", 110, true),
            Col.Money(nameof(SaleRow.InstitutionShare), "Kurum payı", 110, true),
            Col.Money(nameof(SaleRow.Total), "Tutar", 110, true),
            Col.Money(nameof(SaleRow.Profit), "Kâr", 100, true),
            Col.Badge(nameof(SaleRow.PaymentText), "Ödeme", 80),
            Col.Badge(nameof(SaleRow.StatusText), "Durum", 100),
            Col.Text(nameof(SaleRow.SoldBy), "Satan", 130),
        ]);
        _grid.RowTint = o => o is SaleRow { Status: SaleStatus.Returned } ? Color.FromArgb(28, 239, 68, 68) : null;
        _period.SelectedIndexChanged += async (_, _) => await ReloadAsync();
        _kind.SelectedIndexChanged += async (_, _) => await ReloadAsync();
        _rx.Click += async (_, _) => { if (SaleForm.Open(FindForm(), SaleKind.Prescription) is not null) await ReloadAsync(); };
        _retail.Click += async (_, _) => { if (SaleForm.Open(FindForm(), SaleKind.Retail) is not null) await ReloadAsync(); };
        _return.Click += async (_, _) => await ReturnAsync();
        _excel.Click += (_, _) => _grid.ExportExcelWithDialog("Satışlar");
        _grid.RowActivated += async (_, e) => { if (e.Item is SaleRow row) await ShowSaleAsync(FindForm(), row.Id); };
        Controls.AddRange(new Control[] { _period, _kind, _rx, _retail, _return, _excel, _grid });
    }

    protected override async Task LoadAsync()
    {
        SaleKind? kind = _kind.SelectedIndex switch { 1 => SaleKind.Prescription, 2 => SaleKind.Retail, _ => null };
        var result = await App.Send(new ListSales { Period = Periods[Math.Max(0, _period.SelectedIndex)], Kind = kind, PageSize = 1000 });
        if (!result.IsSuccess) { App.ShowErrors(result); return; }
        var items = result.Value.Items;
        _grid.DataSource = items;
        var done = items.Where(s => s.Status == SaleStatus.Completed).ToList();
        Subtitle = $"{result.Value.TotalCount} satış  ·  ciro {App.Money(done.Sum(s => s.Total))}  ·  kâr {App.Money(done.Sum(s => s.Profit))}  ·  ayrıntı için satıra çift tıklayın";
    }

    async Task ReturnAsync()
    {
        if (_grid.SelectedItem is not SaleRow row) { OndaToast.Info("Önce bir satış seçin."); return; }
        if (row.Status != SaleStatus.Completed) { OndaToast.Info("Bu satış zaten iade edilmiş."); return; }
        if (!OndaMessageBox.Ask(FindForm(), $"{row.Number} numaralı satış ({App.Money(row.Total)}) iade alınsın mı? Kutular stoka geri döner.", "İade")) return;
        var r = await App.Send(new ReturnSale(row.Id));
        if (!r.IsSuccess) { App.ShowErrors(r, "İade alınamadı"); return; }
        OndaToast.Success($"{row.Number} iade alındı.");
        await ReloadAsync();
    }

    public static async Task ShowSaleAsync(IWin32Window? owner, int saleId)
    {
        var r = await App.Send(new GetSale(saleId));
        if (!r.IsSuccess) { App.ShowErrors(r); return; }
        var s = r.Value.Sale;
        var head = s.Kind == SaleKind.Prescription
            ? $"{s.SoldAt:dd.MM.yyyy HH:mm}  ·  {s.Patient} ({s.InsuranceText})  ·  {s.Hospital} · {s.Branch}  ·  {s.ColorText} reçete {s.PrescriptionNo}"
            : $"{s.SoldAt:dd.MM.yyyy HH:mm}  ·  serbest satış  ·  {s.PaymentText}";
        head += $"\nSatan: {s.SoldBy ?? "—"}  ·  durum: {s.StatusText}";
        using var f = new DetailForm($"Fiş {s.Number}", "\uE8A5", head, size: new Size(980, 640))
            .Card("Tutar", s.Total, "₺", $"{s.Boxes} kutu", "\uE8C7")
            .Card("Hasta payı", s.PatientShare, "₺", s.PaymentText, "\uE77B", App.Violet)
            .Card("Kurum payı", s.InstitutionShare, "₺", s.InsuranceText, "\uE95E", App.Teal)
            .Card("Kâr", s.Profit, "₺", s.Total == 0 ? null : $"marj {App.Percent((double)(s.Profit / s.Total))}", "\uE9D2", App.Amber)
            .Tab("Kalemler", r.Value.Lines,
                Col.Text(nameof(ReceiptLine.Product), "Ürün", 300),
                Col.Text(nameof(ReceiptLine.LotNo), "Parti", 110),
                Col.Day(nameof(ReceiptLine.Expiry), "SKT", 100),
                Col.Int(nameof(ReceiptLine.Quantity), "Kutu", 70, true),
                Col.Money(nameof(ReceiptLine.UnitPrice), "Birim", 110),
                Col.Money(nameof(ReceiptLine.Total), "Tutar", 120, true),
                Col.Check(nameof(ReceiptLine.Reimbursed), "Geri ödeme", 100));
        f.ShowDialog(owner);
    }

    protected override void LayoutPage(Rectangle r)
    {
        _period.SetBounds(r.X, r.Y, 300, 38);
        _kind.SetBounds(r.X + 316, r.Y, 230, 38);
        RightAlign(r.Right, r.Y, _excel, _return, _retail, _rx);
        _grid.SetBounds(r.X, r.Y + 54, r.Width, r.Height - 54);
    }
}
