namespace Eczane.WinForms.Pages;

/// <summary>İlaç ve ürün listesi: stok, en yakın SKT, kaç günlük stok kaldığı, raf; ürün kartı (partiler, hareketler, depo fiyatları).</summary>
sealed class ProductsPage : PageBase
{
    static readonly StockFilter[] Filters = [StockFilter.All, StockFilter.Low, StockFilter.Out, StockFilter.Expiring];
    readonly OndaSegmented _filter = new() { Items = ["Tümü", "Kritik", "Stokta yok", "SKT yakın"], SelectedIndex = 0 };
    readonly OndaComboBox _category = new() { Compact = true, Placeholder = "Bütün gruplar", ShowClearButton = true };
    readonly OndaTextBox _search = new() { Compact = true, Placeholder = "Ad, barkod, etken madde…", Icon = "\uE721", ShowClearButton = true };
    readonly OndaButton _new = new() { Action = OndaAction.New, Text = "Yeni ürün", Width = 140 };
    readonly OndaButton _card = new() { Text = "Ürün kartı", Icon = "\uE8F1", Kind = OndaButtonKind.Outline, Width = 140 };
    readonly OndaButton _excel = new() { Action = OndaAction.Excel, Kind = OndaButtonKind.Outline, Width = 100 };
    readonly OndaGrid _grid = new() { RowHeight = 34, AutoGenerateColumns = false };
    readonly System.Windows.Forms.Timer _debounce = new() { Interval = 350 };

    public ProductsPage() : base("İlaçlar ve stok", "Yükleniyor…")
    {
        foreach (var c in Enum.GetValues<ProductCategory>()) _category.Items.Add(Texts.Of(c));
        _grid.Columns.AddRange(
        [
            Col.Text(nameof(ProductRow.Barcode), "Barkod", 130),
            Col.Text(nameof(ProductRow.Name), "Ürün", 270),
            Col.Badge(nameof(ProductRow.CategoryText), "Grup", 130),
            Col.Text(nameof(ProductRow.Group), "Tedavi grubu", 140),
            Col.Badge(nameof(ProductRow.ColorText), "Reçete", 80),
            Col.Check(nameof(ProductRow.ColdChain), "Soğuk", 60),
            Col.Int(nameof(ProductRow.Stock), "Stok", 70, true),
            Col.Int(nameof(ProductRow.MinStock), "En az", 60),
            Col.Badge(nameof(ProductRow.StockStatus), "Stok durumu", 110),
            Col.Int(nameof(ProductRow.Sold30), "Satış 30 g.", 90, true),
            Col.Int(nameof(ProductRow.DaysOfCover), "Kaç günlük", 90),
            Col.Day(nameof(ProductRow.NearestExpiry), "En yakın SKT", 110),
            Col.Badge(nameof(ProductRow.ExpiryText), "Miad", 90),
            Col.Money(nameof(ProductRow.PurchasePrice), "Alış", 100),
            Col.Money(nameof(ProductRow.SalePrice), "Satış", 100),
            Col.Pct(nameof(ProductRow.Margin), "Marj", 70),
            Col.Money(nameof(ProductRow.StockValue), "Stok değeri", 120, true),
            Col.Text(nameof(ProductRow.Shelf), "Raf", 70),
        ]);
        _grid.RowTint = o => o is ProductRow p ? p.Stock == 0 ? Color.FromArgb(26, 239, 68, 68) : p.Stock <= p.MinStock ? Color.FromArgb(26, 245, 158, 11) : null : null;
        _filter.SelectedIndexChanged += async (_, _) => await ReloadAsync();
        _category.SelectedValueChanged += async (_, _) => await ReloadAsync();
        _search.TextChanged += (_, _) => { _debounce.Stop(); _debounce.Start(); };
        _debounce.Tick += async (_, _) => { _debounce.Stop(); await ReloadAsync(); };
        _new.Click += async (_, _) => { if (ProductForm.Open(FindForm()) is not null) await ReloadAsync(); };
        _card.Click += async (_, _) => { if (_grid.SelectedItem is ProductRow p) await OpenCardAsync(p.Id); else OndaToast.Info("Önce bir ürün seçin."); };
        _excel.Click += (_, _) => _grid.ExportExcelWithDialog("İlaçlar ve stok");
        _grid.RowActivated += async (_, e) => { if (e.Item is ProductRow p) await OpenCardAsync(p.Id); };
        Controls.AddRange(new Control[] { _filter, _category, _search, _new, _card, _excel, _grid });
    }

    protected override async Task LoadAsync()
    {
        ProductCategory? cat = _category.SelectedItem is string s ? Enum.GetValues<ProductCategory>().First(c => Texts.Of(c) == s) : null;
        var result = await App.Send(new ListProducts(_search.Text, cat, Filters[Math.Max(0, _filter.SelectedIndex)]));
        if (!result.IsSuccess) { App.ShowErrors(result); return; }
        var rows = result.Value;
        _grid.DataSource = rows;
        Subtitle = $"{rows.Count} ürün  ·  {rows.Sum(r => r.Stock):N0} kutu  ·  stok değeri {App.Money(rows.Sum(r => r.StockValue))}  ·  {rows.Count(r => r.Stock == 0)} ürün stokta yok, {rows.Count(r => r.Stock > 0 && r.Stock <= r.MinStock)} kritik  ·  ürün kartı için çift tıklayın";
    }

    async Task OpenCardAsync(int id)
    {
        if (await ShowCardAsync(FindForm(), id)) await ReloadAsync();
    }

    /// <summary>Ürün kartını gösterir; kart içinden düzenleme yapılırsa true döner.</summary>
    public static async Task<bool> ShowCardAsync(IWin32Window? owner, int productId)
    {
        var r = await App.Send(new GetProductCard(productId));
        if (!r.IsSuccess) { App.ShowErrors(r); return false; }
        var c = r.Value;
        var chart = new OndaChart { Title = "Günlük satış", Subtitle = "Son 60 gün (kutu)", ChartType = OndaChartType.Bar, Labels = c.DayLabels, ShowLegend = false };
        chart.Series.Add(new OndaSeries("Kutu", c.DailySales, App.Teal));
        chart.ValueFormat = v => v.ToString("0");
        var head = $"{c.Barcode}  ·  {c.CategoryText}{(c.Category == ProductCategory.Rx ? $" ({Texts.Of(c.Color)} reçete)" : "")}  ·  {c.ActiveIngredient ?? "—"}  ·  {c.Group ?? "—"}" +
                   $"\nRaf {c.Shelf ?? "—"}  ·  en az stok {c.MinStock}  ·  {(c.ColdChain ? "soğuk zincir  ·  " : "")}{(c.Reimbursed ? "SGK geri ödemeli" : "geri ödeme dışı")}  ·  {(c.IsActive ? "satışta" : "pasif")}";
        using var f = new DetailForm(c.Name, "\uE8F1", head, chart, new Size(1160, 820))
            .Card("Stok", c.Stock, "", $"{c.Lots.Count} parti", "\uE7B8")
            .Card("Satış · 30 gün", c.Sold30, "", $"90 günde {c.Sold90} kutu", "\uE7BF", App.Teal)
            .Card("Ciro · 90 gün", c.Revenue90, "₺", null, "\uE9D2", App.Violet)
            .Card("Satış fiyatı", c.SalePrice, "₺", $"alış {App.Money(c.PurchasePrice)} · marj {App.Percent(c.Margin)}", "\uE8EC", App.Amber, 2)
            .Tab("Partiler (SKT)", c.Lots,
                Col.Text(nameof(LotRow.LotNo), "Parti no", 120),
                Col.Day(nameof(LotRow.Expiry), "SKT", 110),
                Col.Badge(nameof(LotRow.LevelText), "Miad", 100),
                Col.Int(nameof(LotRow.Quantity), "Kalan", 80, true),
                Col.Int(nameof(LotRow.InitialQuantity), "Giren", 80),
                Col.Money(nameof(LotRow.UnitCost), "Birim maliyet", 120),
                Col.Money(nameof(LotRow.Value), "Değer", 120, true),
                Col.Date(nameof(LotRow.ReceivedAt), "Giriş", 120, "dd.MM.yyyy HH:mm"),
                Col.Text(nameof(LotRow.Supplier), "Depo", 160))
            .Tab("Stok hareketleri", c.Movements,
                Col.Date(nameof(CardMovement.At), "Tarih", 130, "dd.MM.yyyy HH:mm"),
                Col.Badge(nameof(CardMovement.Type), "Hareket", 120),
                Col.Text(nameof(CardMovement.Reference), "Belge", 160),
                Col.Int(nameof(CardMovement.In), "Giriş", 80, true),
                Col.Int(nameof(CardMovement.Out), "Çıkış", 80, true),
                Col.Money(nameof(CardMovement.UnitValue), "Birim", 110))
            .Tab("Depo alışları", c.Purchases,
                Col.Text(nameof(SupplierPrice.Supplier), "Depo", 180),
                Col.Date(nameof(SupplierPrice.ReceivedAt), "Teslim", 130, "dd.MM.yyyy HH:mm"),
                Col.Int(nameof(SupplierPrice.Quantity), "Adet", 80, true),
                Col.Int(nameof(SupplierPrice.Free), "MF", 70, true),
                Col.Money(nameof(SupplierPrice.UnitPrice), "Birim fiyat", 120),
                Col.Money(nameof(SupplierPrice.EffectiveCost), "Net maliyet", 120));
        var edit = new OndaButton { Action = OndaAction.Edit, Text = "Düzenle", Kind = OndaButtonKind.Outline, Width = 130 };
        var changed = false;
        edit.Click += (_, _) => { if (ProductForm.Open(f, c) is not null) { changed = true; f.Close(); } };
        f.AddFooterButton(edit);
        f.ShowDialog(owner);
        return changed;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _debounce.Dispose();
        base.Dispose(disposing);
    }

    protected override void LayoutPage(Rectangle r)
    {
        _filter.SetBounds(r.X, r.Y, 340, 38);
        _category.SetBounds(r.X + 352, r.Y + 2, 170, _category.Height);
        _search.SetBounds(r.X + 534, r.Y + 2, Math.Max(160, r.Right - 410 - (r.X + 534)), _search.Height);
        RightAlign(r.Right, r.Y, _excel, _card, _new);
        _grid.SetBounds(r.X, r.Y + 54, r.Width, r.Height - 54);
    }
}
