namespace Eczane.WinForms.Pages;

/// <summary>Ecza depoları: iskonto, vade, mal fazlası, zamanında teslim oranı, bakiye ve vadesi geçmiş borç; seçili deponun cari ekstresi ve ödeme girişi.</summary>
sealed class WholesalersPage : PageBase
{
    readonly OndaButton _pay = new() { Text = "Ödeme yap", Icon = "\uE8C7", Width = 140 };
    readonly OndaButton _excel = new() { Text = "Ekstre Excel", Icon = "\uE9F9", Kind = OndaButtonKind.Outline, Width = 140 };
    readonly OndaGrid _grid = new() { RowHeight = 36, ShowFilterRow = false, AutoGenerateColumns = false };
    readonly OndaLabel _stTitle = new() { Text = "Cari ekstre", Style = OndaLabelStyle.Title };
    readonly OndaGrid _statement = new() { RowHeight = 32, AutoGenerateColumns = false, EmptyText = "Bir depo seçin" };

    public WholesalersPage() : base("Ecza depoları", "Yükleniyor…")
    {
        _grid.Columns.AddRange(
        [
            Col.Text(nameof(WholesalerRow.Name), "Depo", 170),
            Col.Int(nameof(WholesalerRow.DeliveriesPerDay), "Sefer/gün", 80),
            Col.Text(nameof(WholesalerRow.TermText), "Vade", 70),
            Col.Text(nameof(WholesalerRow.DiscountText), "İskonto", 70),
            Col.Int(nameof(WholesalerRow.Campaigns), "MF kampanya", 100),
            Col.Int(nameof(WholesalerRow.Orders90), "Sipariş 90 g.", 100, true),
            Col.Money(nameof(WholesalerRow.Purchases90), "Alış 90 g.", 130, true),
            Col.Money(nameof(WholesalerRow.FreeGoods90), "MF değeri", 110, true),
            Col.Progress(nameof(WholesalerRow.OnTimeRate), "Zamanında", 110),
            Col.Date(nameof(WholesalerRow.LastDelivery), "Son teslim", 120, "dd.MM HH:mm"),
            Col.Money(nameof(WholesalerRow.Balance), "Bakiye", 130, true),
            Col.Money(nameof(WholesalerRow.Overdue), "Vadesi geçen", 120, true),
            Col.Day(nameof(WholesalerRow.NextDue), "Sonraki vade", 110),
            Col.Money(nameof(WholesalerRow.NextDueAmount), "Vade tutarı", 120),
        ]);
        _grid.RowTint = o => o is WholesalerRow { Overdue: > 0 } ? Color.FromArgb(26, 239, 68, 68) : null;
        _statement.Columns.AddRange(
        [
            Col.Date(nameof(LedgerEntry.At), "Tarih", 130, "dd.MM.yyyy HH:mm"),
            Col.Badge(nameof(LedgerEntry.Type), "İşlem", 120),
            Col.Text(nameof(LedgerEntry.Reference), "Belge", 160),
            Col.Money(nameof(LedgerEntry.Debit), "Borç", 130, true),
            Col.Money(nameof(LedgerEntry.Credit), "Ödeme", 130, true),
            Col.Day(nameof(LedgerEntry.DueDate), "Vade", 110),
            Col.Money(nameof(LedgerEntry.FreeValue), "MF değeri", 110, true),
            Col.Dbl(nameof(LedgerEntry.LeadHours), "Teslim (saat)", 110, "0.0"),
        ]);
        _grid.SelectionChanged += async (_, _) => await LoadStatementAsync();
        _pay.Click += async (_, _) => await PayAsync();
        _excel.Click += (_, _) => _statement.ExportExcelWithDialog(_grid.SelectedItem is WholesalerRow w ? $"{w.Name} ekstre" : "Ekstre");
        Controls.AddRange(new Control[] { _pay, _excel, _grid, _stTitle, _statement });
    }

    protected override async Task LoadAsync()
    {
        var keep = (_grid.SelectedItem as WholesalerRow)?.Id;
        var result = await App.Send(new ListWholesalers());
        if (!result.IsSuccess) { App.ShowErrors(result); return; }
        var rows = result.Value;
        _grid.DataSource = rows;
        _grid.SelectedItem = rows.FirstOrDefault(r => r.Id == keep) ?? rows.OrderByDescending(r => r.Overdue).ThenByDescending(r => r.Balance).FirstOrDefault();
        Subtitle = $"{rows.Count} depo  ·  toplam borç {App.Money(rows.Sum(r => r.Balance))}  ·  vadesi geçen {App.Money(rows.Sum(r => r.Overdue))}  ·  son 90 günde {App.Money(rows.Sum(r => r.Purchases90))} alış, {App.Money(rows.Sum(r => r.FreeGoods90))} mal fazlası";
        await LoadStatementAsync();
    }

    async Task LoadStatementAsync()
    {
        if (_grid.SelectedItem is not WholesalerRow w) { _statement.DataSource = null; return; }
        var r = await App.Send(new GetWholesalerStatement(w.Id));
        if (!r.IsSuccess) return;
        _statement.DataSource = r.Value.Entries;
        _stTitle.Text = $"Cari ekstre · {w.Name}  ·  bakiye {App.Money(r.Value.Balance)}" + (r.Value.Overdue > 0 ? $"  ·  vadesi geçen {App.Money(r.Value.Overdue)}" : "");
    }

    async Task PayAsync()
    {
        if (_grid.SelectedItem is not WholesalerRow w) { OndaToast.Info("Önce bir depo seçin."); return; }
        if (PaymentForm.Open(FindForm(), w)) await ReloadAsync();
    }

    protected override void LayoutPage(Rectangle r)
    {
        RightAlign(r.Right, r.Y, _excel, _pay);
        var y = r.Y + 54;
        var top = Math.Max(200, Math.Min(36 * 5 + 80, (r.Bottom - y) / 2));
        _grid.SetBounds(r.X, y, r.Width, top);
        y += top + Gap;
        _stTitle.Location = new Point(r.X - 2, y);
        y += 36;
        _statement.SetBounds(r.X, y, r.Width, r.Bottom - y);
    }
}
