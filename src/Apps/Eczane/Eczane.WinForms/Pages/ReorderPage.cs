namespace Eczane.WinForms.Pages;

/// <summary>
/// Sipariş önerisi: satış hızı, en az stok, eldeki ve yoldaki miktara göre ne kadar alınacağı; her ürün için toplamda en ucuz depo
/// (iskonto + mal fazlası). Seçilen satırlar depo başına tek siparişe dönüştürülür.
/// </summary>
sealed class ReorderPage : PageBase
{
    /// <summary>Ekranda işaretlenebilen öneri satırı.</summary>
    sealed class Pick(SuggestionRow row)
    {
        public SuggestionRow Row { get; } = row;
        public bool Selected { get; set; } = true;
        public string Product => Row.Product;
        public string CategoryText => Row.CategoryText;
        public int Stock => Row.Stock;
        public int OnOrder => Row.OnOrder;
        public int MinStock => Row.MinStock;
        public double DailyUsage => Row.DailyUsage;
        public int? DaysOfCover => Row.DaysOfCover;
        public int Suggested => Row.Suggested;
        public int Free => Row.Free;
        public string Wholesaler => Row.Wholesaler;
        public decimal UnitPrice => Row.UnitPrice;
        public decimal EffectiveUnitCost => Row.EffectiveUnitCost;
        public decimal LineTotal => Row.LineTotal;
        public string? Campaign => Row.Campaign;
    }

    static readonly double[] Safety = [3, 7, 14];
    readonly OndaSegmented _safety = new() { Items = ["3 gün emniyet", "7 gün", "14 gün"], SelectedIndex = 1 };
    readonly OndaButton _all = new() { Text = "Tümünü seç", Kind = OndaButtonKind.Outline, Width = 130 };
    readonly OndaButton _none = new() { Text = "Temizle", Kind = OndaButtonKind.Outline, Width = 110 };
    readonly OndaButton _order = new() { Text = "Siparişleri oluştur", Icon = "\uE724", Width = 200 };
    readonly OndaGrid _grid = new() { RowHeight = 34, AutoGenerateColumns = false, AllowEdit = true };
    readonly OndaGrid _summary = new() { RowHeight = 32, ShowFilterRow = false, AutoGenerateColumns = false };
    List<Pick> _picks = [];

    sealed record WholesalerTotal(string Wholesaler, int Lines, int Boxes, int Free, decimal Total);

    public ReorderPage() : base("Sipariş önerisi", "Yükleniyor…")
    {
        _grid.Columns.AddRange(
        [
            Col.Check(nameof(Pick.Selected), "Al", 50),
            Ro(Col.Text(nameof(Pick.Product), "Ürün", 260)),
            Ro(Col.Badge(nameof(Pick.CategoryText), "Grup", 120)),
            Ro(Col.Int(nameof(Pick.Stock), "Stok", 60)),
            Ro(Col.Int(nameof(Pick.OnOrder), "Yolda", 60)),
            Ro(Col.Int(nameof(Pick.MinStock), "En az", 60)),
            Ro(Col.Dbl(nameof(Pick.DailyUsage), "Günlük", 70, "0.0")),
            Ro(Col.Int(nameof(Pick.DaysOfCover), "Kaç günlük", 90)),
            Ro(Col.Int(nameof(Pick.Suggested), "Sipariş", 80, true)),
            Ro(Col.Int(nameof(Pick.Free), "MF", 60, true)),
            Ro(Col.Text(nameof(Pick.Wholesaler), "En uygun depo", 140)),
            Ro(Col.Money(nameof(Pick.UnitPrice), "Birim", 100)),
            Ro(Col.Money(nameof(Pick.EffectiveUnitCost), "Net birim", 100)),
            Ro(Col.Money(nameof(Pick.LineTotal), "Tutar", 110, true)),
            Ro(Col.Text(nameof(Pick.Campaign), "Kampanya", 140)),
        ]);
        _summary.Columns.AddRange(
        [
            Col.Text(nameof(WholesalerTotal.Wholesaler), "Depo", 170),
            Col.Int(nameof(WholesalerTotal.Lines), "Kalem", 70, true),
            Col.Int(nameof(WholesalerTotal.Boxes), "Kutu", 70, true),
            Col.Int(nameof(WholesalerTotal.Free), "MF", 60, true),
            Col.Money(nameof(WholesalerTotal.Total), "Tutar", 120, true),
        ]);
        _grid.RowTint = o => o is Pick { Stock: 0 } ? Color.FromArgb(26, 239, 68, 68) : null;
        _grid.CellValueChanged += (_, _) => ShowSummary();
        _safety.SelectedIndexChanged += async (_, _) => await ReloadAsync();
        _all.Click += (_, _) => SetAll(true);
        _none.Click += (_, _) => SetAll(false);
        _order.Click += async (_, _) => await OrderAsync();
        Controls.AddRange(new Control[] { _safety, _all, _none, _order, _grid, _summary });
    }

    static OndaGridColumn Ro(OndaGridColumn c) { c.ReadOnly = true; return c; }

    protected override async Task LoadAsync()
    {
        var result = await App.Send(new GetReorderSuggestions(Safety[Math.Max(0, _safety.SelectedIndex)]));
        if (!result.IsSuccess) { App.ShowErrors(result); return; }
        _picks = [.. result.Value.Select(r => new Pick(r))];
        _grid.DataSource = _picks;
        ShowSummary();
    }

    void SetAll(bool on)
    {
        foreach (var p in _picks) p.Selected = on;
        _grid.Invalidate();
        ShowSummary();
    }

    void ShowSummary()
    {
        var chosen = _picks.Where(p => p.Selected).ToList();
        _summary.DataSource = chosen.GroupBy(p => p.Wholesaler)
            .Select(g => new WholesalerTotal(g.Key, g.Count(), g.Sum(p => p.Suggested), g.Sum(p => p.Free), g.Sum(p => p.LineTotal)))
            .OrderByDescending(t => t.Total).ToList();
        Subtitle = $"{_picks.Count} ürün sipariş gerektiriyor ({_picks.Count(p => p.Stock == 0)} tanesi stokta yok)  ·  seçili {chosen.Count} kalem, {chosen.Sum(p => p.Suggested)} kutu + {chosen.Sum(p => p.Free)} MF, {App.Money(chosen.Sum(p => p.LineTotal))}  ·  son 28 günlük satış hızına göre";
    }

    async Task OrderAsync()
    {
        var groups = _picks.Where(p => p.Selected).GroupBy(p => (p.Row.WholesalerId, p.Wholesaler)).ToList();
        if (groups.Count == 0) { OndaToast.Info("Sipariş edilecek ürün seçin."); return; }
        if (!OndaMessageBox.Ask(FindForm(), $"{groups.Count} depoya toplam {groups.Sum(g => g.Count())} kalem sipariş geçilsin mi?\n\n" +
            string.Join("\n", groups.Select(g => $"•  {g.Key.Wholesaler}: {g.Count()} kalem, {App.Money(g.Sum(p => p.LineTotal))}")), "Sipariş")) return;
        var created = 0;
        foreach (var g in groups)
        {
            var r = await App.Send(new CreatePurchaseOrder(g.Key.WholesalerId, [.. g.Select(p => new OrderItem(p.Row.ProductId, p.Suggested))]));
            if (!r.IsSuccess) { App.ShowErrors(r, $"{g.Key.Wholesaler} siparişi oluşturulamadı"); continue; }
            created++;
        }
        if (created > 0) OndaToast.Success($"{created} sipariş depolara iletildi. Teslim alındığında Siparişler sayfasından girişini yapın.", "Sipariş verildi");
        await ReloadAsync();
    }

    protected override void LayoutPage(Rectangle r)
    {
        _safety.SetBounds(r.X, r.Y, 360, 38);
        RightAlign(r.Right, r.Y, _all, _none, _order);
        var y = r.Y + 54;
        const int sumH = 210;
        _grid.SetBounds(r.X, y, r.Width, Math.Max(200, r.Bottom - y - sumH - Gap));
        _summary.SetBounds(r.X, _grid.Bottom + Gap, Math.Min(r.Width, 560), r.Bottom - _grid.Bottom - Gap);
    }
}
