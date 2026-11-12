namespace Eczane.WinForms.Pages;

/// <summary>Kurum faturaları: SGK ve özel sigorta payları ay ay; biten ayın faturasını kesme ve tahsilat işaretleme.</summary>
sealed class InvoicesPage : PageBase
{
    readonly OndaButton _issue = new() { Text = "Fatura kes", Icon = "\uE8A5", Width = 140 };
    readonly OndaButton _paid = new() { Text = "Tahsil edildi", Icon = "\uE73E", Kind = OndaButtonKind.Outline, Width = 150 };
    readonly OndaButton _excel = new() { Action = OndaAction.Excel, Kind = OndaButtonKind.Outline, Width = 100 };
    readonly Stat _pending = new() { Title = "Tahsil bekleyen", Icon = "\uE823", Prefix = "₺", IconColor = App.Amber };
    readonly Stat _uninvoiced = new() { Title = "Faturalanmamış", Icon = "\uE8A5", Prefix = "₺", IconColor = App.Violet };
    readonly Stat _collected = new() { Title = "Bu yıl tahsil edilen", Icon = "\uE73E", Prefix = "₺", IconColor = App.Teal };
    readonly OndaChart _chart = new() { Title = "Aylık kurum payı", Subtitle = "SGK ve özel sigorta (₺)", ChartType = OndaChartType.Bar };
    readonly OndaGrid _grid = new() { RowHeight = 34, ShowFilterRow = false, AutoGenerateColumns = false };

    public InvoicesPage() : base("Kurum faturaları", "Yükleniyor…")
    {
        _grid.Columns.AddRange(
        [
            Col.Text(nameof(InvoiceRow.Period), "Dönem", 140),
            Col.Badge(nameof(InvoiceRow.InstitutionText), "Kurum", 130),
            Col.Int(nameof(InvoiceRow.Prescriptions), "Reçete", 90, true),
            Col.Money(nameof(InvoiceRow.Amount), "Tutar", 140, true),
            Col.Day(nameof(InvoiceRow.IssuedOn), "Fatura tarihi", 120),
            Col.Day(nameof(InvoiceRow.PaidOn), "Tahsil tarihi", 120),
            Col.Badge(nameof(InvoiceRow.StatusText), "Durum", 130),
        ]);
        _chart.ValueFormat = OndaChart.Compact;
        _issue.Click += async (_, _) => await IssueAsync();
        _paid.Click += async (_, _) => await PaidAsync();
        _excel.Click += (_, _) => _grid.ExportExcelWithDialog("Kurum faturaları");
        Controls.AddRange(new Control[] { _issue, _paid, _excel, _pending, _uninvoiced, _collected, _chart, _grid });
    }

    protected override async Task LoadAsync()
    {
        var r = await App.Send(new ListInvoices());
        if (!r.IsSuccess) { App.ShowErrors(r); return; }
        var rows = r.Value;
        _grid.DataSource = rows;
        var pending = rows.Where(x => x.Id is not null && x.PaidOn is null).ToList();
        var open = rows.Where(x => x.Id is null).ToList();
        var year = rows.Where(x => x.PaidOn is { } p && p.Year == App.Today.Year).ToList();
        _pending.Value = pending.Sum(x => x.Amount);
        _pending.DeltaText = $"{pending.Count} fatura";
        _uninvoiced.Value = open.Sum(x => x.Amount);
        _uninvoiced.DeltaText = $"{open.Sum(x => x.Prescriptions)} reçete · içinde bulunulan ay dahil";
        _collected.Value = year.Sum(x => x.Amount);
        _collected.DeltaText = $"{year.Count} fatura";

        var months = rows.Select(x => (x.Year, x.Month)).Distinct().OrderBy(m => m).TakeLast(6).ToList();
        _chart.Labels = [.. months.Select(m => new DateTime(m.Year, m.Month, 1).ToString("MMM yy"))];
        _chart.Series.Clear();
        _chart.Series.Add(new OndaSeries("SGK", [.. months.Select(m => (double)rows.Where(x => x.Institution == Institution.Sgk && x.Year == m.Year && x.Month == m.Month).Sum(x => x.Amount))]));
        _chart.Series.Add(new OndaSeries("Özel sigorta", [.. months.Select(m => (double)rows.Where(x => x.Institution == Institution.PrivateInsurance && x.Year == m.Year && x.Month == m.Month).Sum(x => x.Amount))], App.Violet));
        Subtitle = "Reçetelerin kurum payı ay sonunda faturalanır  ·  SGK ödemesi ortalama 45, özel sigorta 30 gün sürer";
        foreach (var c in new[] { _pending, _uninvoiced, _collected }) c.Replay();
        _chart.Replay();
    }

    async Task IssueAsync()
    {
        if (_grid.SelectedItem is not InvoiceRow { Id: null } row) { OndaToast.Info("Faturalanmamış bir dönem seçin."); return; }
        if (!OndaMessageBox.Ask(FindForm(), $"{row.Period} {row.InstitutionText} faturası kesilsin mi? {row.Prescriptions} reçete, {App.Money(row.Amount)}", "Fatura")) return;
        var r = await App.Send(new IssueInvoice(row.Institution, row.Year, row.Month));
        if (!r.IsSuccess) { App.ShowErrors(r, "Fatura kesilemedi"); return; }
        OndaToast.Success($"{row.Period} · {row.InstitutionText}", "Fatura kesildi");
        await ReloadAsync();
    }

    async Task PaidAsync()
    {
        if (_grid.SelectedItem is not InvoiceRow { Id: int id, PaidOn: null } row) { OndaToast.Info("Tahsil bekleyen bir fatura seçin."); return; }
        var r = await App.Send(new MarkInvoicePaid(id, App.Today));
        if (!r.IsSuccess) { App.ShowErrors(r); return; }
        OndaToast.Success($"{row.Period} · {row.InstitutionText} · {App.Money(row.Amount)}", "Tahsilat kaydedildi");
        await ReloadAsync();
    }

    protected override void LayoutPage(Rectangle r)
    {
        RightAlign(r.Right, r.Y, _excel, _paid, _issue);
        var y = r.Y + 52;
        var leftW = (int)((r.Width - Gap) * 0.42);
        var cards = new[] { _pending, _uninvoiced, _collected };
        for (var i = 0; i < 3; i++) cards[i].SetBounds(r.X, y + i * (132 + 12), leftW, 132);
        var h = 132 * 3 + 24;
        _chart.SetBounds(r.X + leftW + Gap, y, r.Width - leftW - Gap, h);
        y += h + Gap;
        _grid.SetBounds(r.X, y, r.Width, r.Bottom - y);
    }
}
