namespace Eczane.WinForms.Pages;

/// <summary>Günün özeti: ciro ve kâr, reçete sayısı, stok ve miad uyarıları, depo borcu, nöbet, en çok reçete gelen hastaneler.</summary>
sealed class DashboardPage : PageBase
{
    readonly Stat _revenue = new() { Title = "Bugünkü ciro", Icon = "\uE8C7", Prefix = "₺", DeltaText = "dün bu saate göre" };
    readonly Stat _rx = new() { Title = "Bugün işlem", Icon = "\uE7BF", IconColor = App.Violet };
    readonly Stat _stock = new() { Title = "Stok uyarısı", Icon = "\uE7BA", IconColor = App.Amber };
    readonly Stat _payable = new() { Title = "Depo borcu · 7 gün içinde", Icon = "\uE825", Prefix = "₺", IconColor = App.Teal };

    readonly OndaChart _trend = new() { Title = "Son 30 gün ciro", Subtitle = "Reçete ve serbest satış (₺)", ChartType = OndaChartType.Bar };
    readonly OndaChart _hours = new() { Title = "Saat saat satış", Subtitle = "Bugün ve son 4 haftanın aynı günü (işlem)", ChartType = OndaChartType.Bar };
    readonly OndaLabel _gridTitle = new() { Text = "Son satışlar", Style = OndaLabelStyle.Title };
    readonly OndaGrid _grid = new() { ShowFilterRow = false, RowHeight = 34, EmptyText = "Bugün satış yok", AutoGenerateColumns = false };
    readonly OndaLabel _hospTitle = new() { Text = "En çok reçete gelen kurumlar · 30 gün", Style = OndaLabelStyle.Title };
    readonly OndaGrid _hosp = new() { ShowFilterRow = false, RowHeight = 32, ShowFooter = false, AutoGenerateColumns = false };
    readonly OndaCard _alerts = new() { Title = "Dikkat", Icon = "\uE7BA" };
    readonly OndaLabel _alertText = new() { Style = OndaLabelStyle.Body, AutoSize = true };

    public DashboardPage() : base("Gösterge paneli", "Yükleniyor…")
    {
        _grid.Columns.AddRange(
        [
            Col.Date(nameof(DashboardSale.At), "Saat", 70, "HH:mm"),
            Col.Text(nameof(DashboardSale.Number), "Fiş", 110),
            Col.Badge(nameof(DashboardSale.KindText), "Tür", 110),
            Col.Text(nameof(DashboardSale.Patient), "Hasta", 160),
            Col.Text(nameof(DashboardSale.Hospital), "Kurum", 190),
            Col.Int(nameof(DashboardSale.Boxes), "Kutu", 60, true),
            Col.Badge(nameof(DashboardSale.PaymentText), "Ödeme", 80),
            Col.Money(nameof(DashboardSale.Amount), "Tutar", 110, true),
        ]);
        _hosp.Columns.AddRange(
        [
            Col.Text(nameof(TopHospital.Name), "Kurum", 210),
            Col.Int(nameof(TopHospital.Prescriptions), "Reçete", 70),
            Col.Progress(nameof(TopHospital.Share), "Pay", 110),
        ]);
        _trend.ValueFormat = OndaChart.Compact;
        _hours.ValueFormat = v => v.ToString("0");
        _alerts.Controls.Add(_alertText);
        Controls.AddRange(new Control[] { _revenue, _rx, _stock, _payable, _trend, _hours, _gridTitle, _grid, _hospTitle, _hosp, _alerts });
    }

    protected override async Task LoadAsync()
    {
        var result = await App.Send(new GetDashboard());
        if (!result.IsSuccess) { App.ShowErrors(result); return; }
        var d = result.Value;

        var duty = d.NextDuty is { } nd ? $"sıradaki nöbet {nd:dd.MM ddd} ({d.NextDutyKind}, {d.NextDutyPharmacist ?? "atanmadı"})" : "planlı nöbet yok";
        var leave = d.OnLeaveToday.Count > 0 ? $"  ·  izinli: {string.Join(", ", d.OnLeaveToday)}" : "";
        Subtitle = $"{d.Now:dd MMMM yyyy, dddd HH:mm}  ·  bugünkü kâr {App.Money(d.TodayProfit)}  ·  {duty}{leave}  ·  F1 hakkında, F2 reçeteli satış, F3 serbest satış";

        _revenue.Value = d.TodayRevenue;
        _revenue.Delta = d.YesterdayToNow == 0 ? null : (double)((d.TodayRevenue - d.YesterdayToNow) / d.YesterdayToNow * 100);
        _revenue.Sparkline = d.RxTrend.Zip(d.RetailTrend, (a, b) => a + b).TakeLast(14).ToArray();
        _revenue.DeltaText = "dün bu saate göre";

        _rx.Value = d.TodayCount;
        _rx.DeltaText = $"{d.TodayPrescriptions} reçete · {d.TodayRetail} serbest · sepet {App.Money(d.AverageBasket)}";

        _stock.Value = d.OutOfStock + d.LowStock + d.ExpiringLots;
        _stock.DeltaText = $"{d.OutOfStock} yok · {d.LowStock} kritik · {d.ExpiringLots} parti miadı yakın";

        _payable.Value = d.PayableNext7 + d.PayableOverdue;
        _payable.DeltaText = d.PayableOverdue > 0 ? $"{App.Money(d.PayableOverdue)} vadesi geçmiş · SGK alacağı {App.Money(d.InstitutionReceivable)}" : $"kurum alacağı {App.Money(d.InstitutionReceivable)}";

        _trend.Labels = d.TrendLabels;
        _trend.Series.Clear();
        _trend.Series.Add(new OndaSeries("Reçete", d.RxTrend));
        _trend.Series.Add(new OndaSeries("Serbest satış", d.RetailTrend, App.Violet));

        _hours.Labels = d.HourLabels;
        _hours.Series.Clear();
        _hours.Series.Add(new OndaSeries("Bugün", d.SalesByHourToday));
        _hours.Series.Add(new OndaSeries("Tipik gün", d.SalesByHourTypical, App.Slate));

        _grid.DataSource = d.RecentSales;
        _hosp.DataSource = d.TopHospitals;
        var alerts = d.Alerts.Count == 0 ? ["Her şey yolunda."] : d.Alerts;
        _alertText.Text = string.Join(Environment.NewLine, alerts.Select(a => "•  " + a));

        foreach (var card in new[] { _revenue, _rx, _stock, _payable }) card.Replay();
        _trend.Replay();
        _hours.Replay();
        Relayout();
    }

    protected override void LayoutPage(Rectangle r)
    {
        var cards = new[] { _revenue, _rx, _stock, _payable };
        var cw = (r.Width - Gap * 3) / 4;
        for (var i = 0; i < 4; i++) cards[i].SetBounds(r.X + i * (cw + Gap), r.Y, cw, 150);

        var y = r.Y + 150 + Gap;
        var chartH = Math.Max(210, Math.Min(260, (r.Bottom - y) / 2 - 20));
        var leftW = (int)((r.Width - Gap) * 0.6);
        _trend.SetBounds(r.X, y, leftW, chartH);
        _hours.SetBounds(r.X + leftW + Gap, y, r.Width - leftW - Gap, chartH);

        y += chartH + Gap;
        var rightX = r.X + leftW + Gap;
        var rightW = r.Width - leftW - Gap;
        _gridTitle.Location = new Point(r.X - 2, y + 2);
        _hospTitle.Location = new Point(rightX - 2, y + 2);
        y += 36;
        var h = Math.Max(160, r.Bottom - y);
        _grid.SetBounds(r.X, y, leftW, h);
        var hospH = Math.Min(h - 110, 32 * 6 + 40);
        _hosp.SetBounds(rightX, y, rightW, Math.Max(120, hospH));
        _alerts.SetBounds(rightX, _hosp.Bottom + 12, rightW, Math.Max(90, r.Bottom - _hosp.Bottom - 12));
        _alertText.MaximumSize = new Size(rightW - 32, 0);
        _alertText.Location = new Point(16, _alerts.ContentTop);
    }
}
