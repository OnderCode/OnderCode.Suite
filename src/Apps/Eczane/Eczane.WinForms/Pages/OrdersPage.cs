namespace Eczane.WinForms.Pages;

/// <summary>Depo siparişleri: yoldakiler, teslim alınanlar; teslim alma (parti no ve SKT girişi), iptal, ayrıntı.</summary>
sealed class OrdersPage : PageBase
{
    static readonly OrderFilter[] Filters = [OrderFilter.Open, OrderFilter.Received, OrderFilter.All];
    readonly OndaSegmented _filter = new() { Items = ["Açık", "Teslim alınan", "Tümü"], SelectedIndex = 0 };
    readonly OndaButton _receive = new() { Text = "Teslim al", Icon = "\uE73E", Width = 140 };
    readonly OndaButton _cancel = new() { Text = "İptal et", Icon = "\uE711", Kind = OndaButtonKind.Outline, Width = 120 };
    readonly OndaButton _excel = new() { Action = OndaAction.Excel, Kind = OndaButtonKind.Outline, Width = 100 };
    readonly OndaGrid _grid = new() { RowHeight = 34, AutoGenerateColumns = false };

    public OrdersPage() : base("Siparişler", "Yükleniyor…")
    {
        _grid.Columns.AddRange(
        [
            Col.Text(nameof(OrderRow.Number), "Sipariş", 120),
            Col.Text(nameof(OrderRow.Wholesaler), "Depo", 170),
            Col.Badge(nameof(OrderRow.StatusText), "Durum", 120),
            Col.Date(nameof(OrderRow.OrderedAt), "Sipariş", 120, "dd.MM HH:mm"),
            Col.Date(nameof(OrderRow.ReceivedAt), "Teslim", 120, "dd.MM HH:mm"),
            Col.Int(nameof(OrderRow.Lines), "Kalem", 70, true),
            Col.Int(nameof(OrderRow.Boxes), "Kutu", 70, true),
            Col.Money(nameof(OrderRow.Total), "Tutar", 120, true),
            Col.Money(nameof(OrderRow.FreeValue), "MF değeri", 110, true),
            Col.Day(nameof(OrderRow.DueDate), "Vade", 100),
        ]);
        _filter.SelectedIndexChanged += async (_, _) => await ReloadAsync();
        _receive.Click += async (_, _) => await ReceiveAsync();
        _cancel.Click += async (_, _) => await CancelAsync();
        _excel.Click += (_, _) => _grid.ExportExcelWithDialog("Siparişler");
        _grid.RowActivated += async (_, e) => { if (e.Item is OrderRow o) await ShowOrderAsync(FindForm(), o.Id); };
        Controls.AddRange(new Control[] { _filter, _receive, _cancel, _excel, _grid });
    }

    protected override async Task LoadAsync()
    {
        var result = await App.Send(new ListOrders(Filters[Math.Max(0, _filter.SelectedIndex)]));
        if (!result.IsSuccess) { App.ShowErrors(result); return; }
        var rows = result.Value;
        _grid.DataSource = rows;
        var open = rows.Where(o => o.Status is OrderStatus.Ordered or OrderStatus.Draft).ToList();
        Subtitle = $"{rows.Count} sipariş  ·  yolda {open.Count} sipariş, {open.Sum(o => o.Boxes)} kutu, {App.Money(open.Sum(o => o.Total))}  ·  ayrıntı için çift tıklayın";
    }

    async Task ReceiveAsync()
    {
        if (_grid.SelectedItem is not OrderRow row || row.Status is not (OrderStatus.Ordered or OrderStatus.Draft)) { OndaToast.Info("Teslim alınacak (yoldaki) bir sipariş seçin."); return; }
        if (await ReceiveForm.OpenAsync(FindForm(), row.Id)) await ReloadAsync();
    }

    async Task CancelAsync()
    {
        if (_grid.SelectedItem is not OrderRow row || row.Status is not (OrderStatus.Ordered or OrderStatus.Draft)) { OndaToast.Info("İptal edilecek açık bir sipariş seçin."); return; }
        if (!OndaMessageBox.Ask(FindForm(), $"{row.Number} ({row.Wholesaler}, {App.Money(row.Total)}) iptal edilsin mi?", "Sipariş iptali")) return;
        var r = await App.Send(new CancelOrder(row.Id));
        if (!r.IsSuccess) { App.ShowErrors(r); return; }
        OndaToast.Success($"{row.Number} iptal edildi.");
        await ReloadAsync();
    }

    public static async Task ShowOrderAsync(IWin32Window? owner, int orderId)
    {
        var r = await App.Send(new GetOrder(orderId));
        if (!r.IsSuccess) { App.ShowErrors(r); return; }
        var o = r.Value.Order;
        var head = $"{o.Wholesaler}  ·  {o.StatusText}  ·  oluşturma {o.CreatedOn:dd.MM.yyyy HH:mm}" +
                   (o.ReceivedAt is { } ra ? $"  ·  teslim {ra:dd.MM.yyyy HH:mm}" : "") + (o.DueDate is { } dd ? $"  ·  vade {dd:dd.MM.yyyy}" : "");
        using var f = new DetailForm($"Sipariş {o.Number}", "\uE7B8", head, size: new Size(1040, 680))
            .Card("Tutar", o.Total, "₺", $"{o.Lines} kalem", "\uE8C7")
            .Card("Kutu", o.Boxes, "", "mal fazlası hariç", "\uE7B8", App.Teal)
            .Card("Mal fazlası", o.FreeValue, "₺", "bedelsiz gelen değer", "\uE734", App.Violet)
            .Tab("Kalemler", r.Value.Lines,
                Col.Text(nameof(OrderLineRow.Product), "Ürün", 280),
                Col.Text(nameof(OrderLineRow.Barcode), "Barkod", 130),
                Col.Int(nameof(OrderLineRow.Quantity), "Adet", 70, true),
                Col.Text(nameof(OrderLineRow.FreeText), "MF", 60),
                Col.Money(nameof(OrderLineRow.UnitPrice), "Birim", 110),
                Col.Money(nameof(OrderLineRow.LineTotal), "Tutar", 120, true),
                Col.Check(nameof(OrderLineRow.ColdChain), "Soğuk", 70));
        f.ShowDialog(owner);
    }

    protected override void LayoutPage(Rectangle r)
    {
        _filter.SetBounds(r.X, r.Y, 380, 38);
        RightAlign(r.Right, r.Y, _excel, _cancel, _receive);
        _grid.SetBounds(r.X, r.Y + 54, r.Width, r.Height - 54);
    }
}
