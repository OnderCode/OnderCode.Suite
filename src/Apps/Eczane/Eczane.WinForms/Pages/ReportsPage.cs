using OnderCode.Reporting;
using OnderCode.Reporting.Export;
using Eczane.WinForms.Reports;

namespace Eczane.WinForms.Pages;

/// <summary>Satış, hastane, ürün (ABC), ecza deposu, personel/nöbet, ciro tahmini ve demirbaş raporları; Excel, PDF ve CSV.</summary>
sealed class ReportsPage : PageBase
{
    public enum Kind { Sales, Hospitals, Products, Suppliers, Staff, Forecast, Assets }

    static readonly int[] Horizons = [7, 14, 30];

    readonly OndaSegmented _kind = new() { Items = ["Satış", "Hastaneler", "Ürünler (ABC)", "Ecza depoları", "Personel ve nöbet", "Ciro tahmini", "Demirbaş"] };
    readonly OndaSegmented _preset = new() { Items = ["Son 7 gün", "Son 30 gün", "Bu ay", "Geçen ay"] };
    readonly OndaSegmented _horizon = new() { Items = ["7 gün", "14 gün", "30 gün"], SelectedIndex = 1 };
    readonly OndaDatePicker _from = new() { Compact = true, Placeholder = "Başlangıç" };
    readonly OndaDatePicker _to = new() { Compact = true, Placeholder = "Bitiş" };
    readonly OndaLabel _note = new() { Style = OndaLabelStyle.Muted, AutoSize = true };
    readonly OndaButton _excel = new() { Text = "Excel", Icon = "\uE9F9", Kind = OndaButtonKind.Outline, Width = 110 };
    readonly OndaButton _pdf = new() { Text = "PDF", Icon = "\uEA90", Kind = OndaButtonKind.Outline, Width = 100 };
    readonly OndaButton _csv = new() { Text = "CSV", Icon = "\uE8A5", Kind = OndaButtonKind.Outline, Width = 100 };

    readonly Stat[] _cards =
    [
        new() { Icon = "\uE8C7" },
        new() { Icon = "\uE8A5", IconColor = App.Violet },
        new() { Icon = "\uE7B8", IconColor = App.Teal },
        new() { Icon = "\uE916", IconColor = App.Amber },
    ];
    readonly OndaChart _main = new() { ChartType = OndaChartType.Bar };
    readonly OndaChart _side = new() { ChartType = OndaChartType.Bar };
    readonly OndaGrid _grid = new() { RowHeight = 32, ShowFilterRow = false, AutoGenerateColumns = false };

    ReportDocument? _doc;
    bool _syncing;

    public ReportsPage() : base("Raporlar", "Raporu seçin; Excel, PDF ya da CSV olarak kaydedin")
    {
        _kind.SelectedIndex = 0;
        _preset.SelectedIndex = 1;
        ApplyPreset();
        _kind.SelectedIndexChanged += async (_, _) => { Relayout(); await ReloadAsync(); };
        _preset.SelectedIndexChanged += async (_, _) => { ApplyPreset(); await ReloadAsync(); };
        _horizon.SelectedIndexChanged += async (_, _) => await ReloadAsync();
        _from.ValueChanged += async (_, _) => { if (!_syncing) await ReloadAsync(); };
        _to.ValueChanged += async (_, _) => { if (!_syncing) await ReloadAsync(); };
        _excel.Click += async (_, _) => await ReportSaver.SaveAsync(FindForm(), _doc, ReportExporters.Excel);
        _pdf.Click += async (_, _) => await ReportSaver.SaveAsync(FindForm(), _doc, ReportExporters.Pdf);
        _csv.Click += async (_, _) => await ReportSaver.SaveAsync(FindForm(), _doc, ReportExporters.Csv);
        Controls.AddRange(new Control[] { _kind, _preset, _horizon, _from, _to, _note, _excel, _pdf, _csv, _main, _side, _grid });
        Controls.AddRange(_cards);
    }

    public Kind Current => (Kind)Math.Max(0, _kind.SelectedIndex);
    bool UsesPeriod => Current is Kind.Sales or Kind.Hospitals or Kind.Products or Kind.Suppliers;

    public async Task ShowAsync(Kind kind)
    {
        if (_kind.SelectedIndex != (int)kind) _kind.SelectedIndex = (int)kind;
        Relayout();
        await ReloadAsync();
    }

    public void SaveAll(string directory, string baseName, params IReportExporter[] exporters)
        => ReportSaver.SaveAll(_doc ?? throw new InvalidOperationException("Rapor yüklenmedi."), directory, baseName, exporters);

    void ApplyPreset()
    {
        var today = App.Today;
        var month = new DateOnly(today.Year, today.Month, 1);
        var (from, to) = _preset.SelectedIndex switch
        {
            0 => (today.AddDays(-7), today.AddDays(-1)),
            2 => (month, today.AddDays(-1)),
            3 => (month.AddMonths(-1), month.AddDays(-1)),
            _ => (today.AddDays(-30), today.AddDays(-1)),
        };
        if (to < from) to = from;
        _syncing = true;
        _from.Value = from.ToDateTime(TimeOnly.MinValue);
        _to.Value = to.ToDateTime(TimeOnly.MinValue);
        _syncing = false;
    }

    (DateOnly From, DateOnly To) Period()
    {
        var today = App.Today;
        return (_from.Value is { } f ? DateOnly.FromDateTime(f) : today.AddDays(-30), _to.Value is { } t ? DateOnly.FromDateTime(t) : today.AddDays(-1));
    }

    protected override async Task LoadAsync()
    {
        var (from, to) = Period();
        switch (Current)
        {
            case Kind.Sales:
            {
                var r = await App.Send(new GetSalesReport(from, to));
                if (!r.IsSuccess) { App.ShowErrors(r); return; }
                ShowSales(r.Value);
                break;
            }
            case Kind.Hospitals:
            {
                var r = await App.Send(new GetHospitalReport(from, to));
                if (!r.IsSuccess) { App.ShowErrors(r); return; }
                ShowHospitals(r.Value);
                break;
            }
            case Kind.Products:
            {
                var r = await App.Send(new GetProductReport(from, to));
                if (!r.IsSuccess) { App.ShowErrors(r); return; }
                ShowProducts(r.Value);
                break;
            }
            case Kind.Suppliers:
            {
                var r = await App.Send(new GetSupplierReport(from, to));
                if (!r.IsSuccess) { App.ShowErrors(r); return; }
                ShowSuppliers(r.Value);
                break;
            }
            case Kind.Staff:
            {
                var r = await App.Send(new GetStaffReport(App.Today.Year));
                if (!r.IsSuccess) { App.ShowErrors(r); return; }
                ShowStaff(r.Value);
                break;
            }
            case Kind.Forecast:
            {
                var r = await App.Send(new GetRevenueForecast(Horizons[Math.Max(0, _horizon.SelectedIndex)]));
                if (!r.IsSuccess) { App.ShowErrors(r); return; }
                ShowForecast(r.Value);
                break;
            }
            default:
            {
                var a = await App.Send(new ListAssets(IncludeRetired: true));
                var m = await App.Send(new ListMaintenance());
                if (!a.IsSuccess) { App.ShowErrors(a); return; }
                ShowAssets(a.Value, m.IsSuccess ? m.Value : []);
                break;
            }
        }
        foreach (var card in _cards) card.Replay();
        _main.Replay();
        _side.Replay();
    }

    void ShowSales(SalesReport r)
    {
        _doc = EczaneReports.Sales(r);
        Subtitle = $"{r.From:dd.MM.yyyy} – {r.To:dd.MM.yyyy}  ·  {r.Days.Count} gün  ·  önceki {r.Days.Count} günle karşılaştırma";
        Card(0, "Ciro", r.Total, "₺", Change(r.Total, r.PreviousTotal), "önceki döneme göre");
        Card(1, "Brüt kâr", r.Profit, "₺", Change(r.Profit, r.PreviousProfit), $"marj {App.Percent(r.Margin)}");
        Card(2, "İşlem", r.Count, "", Change(r.Count, r.PreviousCount), $"{r.Prescriptions} reçete");
        Card(3, "Ortalama sepet", r.AverageBasket, "₺", null, $"kurum payı {App.Money(r.Institution)}");
        _cards[0].Sparkline = [.. r.Days.Select(d => (double)d.Total)];
        _cards[1].Sparkline = [.. r.Days.Select(d => (double)d.Profit)];
        _cards[2].Sparkline = [.. r.Days.Select(d => (double)d.Count)];
        _cards[3].Sparkline = null;
        Chart(_main, OndaChartType.Bar, "Günlük ciro", "Reçete ve serbest satış (₺)", [.. r.Days.Select(d => d.Date.ToString("dd.MM"))],
            new OndaSeries("Reçete", [.. r.Days.Select(d => (double)d.RxRevenue)]),
            new OndaSeries("Serbest satış", [.. r.Days.Select(d => (double)d.RetailRevenue)], App.Violet));
        Chart(_side, OndaChartType.Bar, "Ürün gruplarına göre ciro", "₺", [.. r.Categories.Select(c => c.Category.Split(' ')[0])],
            new OndaSeries("Ciro", [.. r.Categories.Select(c => (double)c.Revenue)], App.Teal));
        _main.ValueFormat = _side.ValueFormat = OndaChart.Compact;
        Columns(r.Days,
            Col.Day(nameof(SalesDay.Date), "Tarih", 110),
            Col.Text(nameof(SalesDay.DayName), "Gün", 60),
            Col.Int(nameof(SalesDay.Prescriptions), "Reçete", 80, true),
            Col.Int(nameof(SalesDay.Retail), "Serbest", 80, true),
            Col.Money(nameof(SalesDay.RxRevenue), "Reçete cirosu", 130, true),
            Col.Money(nameof(SalesDay.RetailRevenue), "Serbest ciro", 130, true),
            Col.Money(nameof(SalesDay.PatientShare), "Hasta payı", 120, true),
            Col.Money(nameof(SalesDay.InstitutionShare), "Kurum payı", 120, true),
            Col.Money(nameof(SalesDay.Total), "Toplam", 130, true),
            Col.Money(nameof(SalesDay.Profit), "Kâr", 120, true));
    }

    void ShowHospitals(HospitalReport r)
    {
        _doc = EczaneReports.Hospitals(r);
        var lead = r.Hospitals.FirstOrDefault();
        Subtitle = $"{r.From:dd.MM.yyyy} – {r.To:dd.MM.yyyy}  ·  en çok reçete gelen kurum: {lead?.Hospital ?? "—"}  ·  ilk üç kurum %{r.TopThreeShare * 100:0.0}";
        Card(0, "Reçete", r.Prescriptions, "", Change(r.Prescriptions, r.PreviousPrescriptions), "önceki döneme göre");
        Card(1, "Hasta", r.Patients, "", null, "farklı hasta");
        Card(2, "Lider kurumun payı", (decimal)Math.Round((lead?.Share ?? 0) * 100, 1), "%", null, lead?.Hospital ?? "—", 1);
        Card(3, "En sık branş", (decimal)Math.Round((r.Branches.FirstOrDefault()?.Share ?? 0) * 100, 1), "%", null, r.Branches.FirstOrDefault()?.Name ?? "—", 1);
        foreach (var c in _cards) c.Sparkline = null;
        var palette = new[] { OndaTheme.Colors.Primary, App.Violet, App.Teal, App.Amber, App.Pink };
        Chart(_main, OndaChartType.Line, "Haftalık reçete · ilk 5 kurum", "Kurum başına reçete", r.WeekLabels,
            [.. r.TopTrend.Select((t, i) => new OndaSeries(Short(t.Name), t.Values, palette[i % palette.Length]))]);
        var br = r.Branches.Take(8).ToList();
        Chart(_side, OndaChartType.Bar, "Hekim branşları", "Reçete sayısı", [.. br.Select(b => b.Name.Split(' ')[0])],
            new OndaSeries("Reçete", [.. br.Select(b => (double)b.Prescriptions)], App.Violet));
        _main.ValueFormat = _side.ValueFormat = v => v.ToString("0");
        Columns(r.Hospitals,
            Col.Text(nameof(HospitalShare.Hospital), "Kurum", 260),
            Col.Badge(nameof(HospitalShare.Kind), "Tür", 160),
            Col.Int(nameof(HospitalShare.Prescriptions), "Reçete", 80, true),
            Col.Int(nameof(HospitalShare.PreviousPrescriptions), "Önceki", 80, true),
            Col.Pct(nameof(HospitalShare.Growth), "Değişim", 80),
            Col.Int(nameof(HospitalShare.Patients), "Hasta", 70),
            Col.Money(nameof(HospitalShare.Revenue), "Ciro", 130, true),
            Col.Progress(nameof(HospitalShare.Share), "Pay", 110),
            Col.Pct(nameof(HospitalShare.Cumulative), "Kümülatif", 90),
            Col.Badge(nameof(HospitalShare.Class), "Sınıf", 60),
            Col.Text(nameof(HospitalShare.TopBranch), "En sık branş", 140));
    }

    void ShowProducts(ProductReport r)
    {
        _doc = EczaneReports.Products(r);
        Subtitle = $"{r.From:dd.MM.yyyy} – {r.To:dd.MM.yyyy}  ·  ürünlerin %{r.ACountShare * 100:0} kadarı cironun %{r.AShareOfRevenue * 100:0}'ini getiriyor (A sınıfı)";
        Card(0, "A sınıfı ürün", r.ACount, "", null, $"cironun %{r.AShareOfRevenue * 100:0.0}'i");
        Card(1, "Stok değeri", r.StockValue, "₺", null, "alış maliyetiyle");
        Card(2, "Stok devir hızı", (decimal)Math.Round(r.Turnover, 1), "", null, $"yıllık · ~{r.DaysOfInventory} günlük stok", 1);
        Card(3, "Yavaş dönen", r.SlowMovers.Count, "", null, $"bağlı stok {App.Money(r.SlowMovers.Sum(s => s.StockValue))}");
        foreach (var c in _cards) c.Sparkline = null;
        var top = r.Products.Take(12).ToList();
        Chart(_main, OndaChartType.Bar, "En çok ciro getiren ürünler", "₺", [.. top.Select(p => p.Product.Split(' ')[0])],
            new OndaSeries("Ciro", [.. top.Select(p => (double)p.Revenue)], App.Teal));
        Chart(_side, OndaChartType.Bar, "Gruplara göre stok değeri", "₺", [.. r.StockByCategory.Select(s => s.Category.Split(' ')[0])],
            new OndaSeries("Stok", [.. r.StockByCategory.Select(s => (double)s.Value)], App.Amber));
        _main.ValueFormat = _side.ValueFormat = OndaChart.Compact;
        Columns(r.Products,
            Col.Text(nameof(ProductShare.Product), "Ürün", 260),
            Col.Badge(nameof(ProductShare.Category), "Grup", 130),
            Col.Text(nameof(ProductShare.Group), "Tedavi grubu", 140),
            Col.Int(nameof(ProductShare.Boxes), "Kutu", 70, true),
            Col.Money(nameof(ProductShare.Revenue), "Ciro", 120, true),
            Col.Money(nameof(ProductShare.Profit), "Kâr", 110, true),
            Col.Pct(nameof(ProductShare.Margin), "Marj", 70),
            Col.Progress(nameof(ProductShare.Share), "Pay", 100, 0.1),
            Col.Pct(nameof(ProductShare.Cumulative), "Kümülatif", 90),
            Col.Badge(nameof(ProductShare.Class), "Sınıf", 60),
            Col.Int(nameof(ProductShare.Stock), "Stok", 70, true));
    }

    void ShowSuppliers(SupplierReport r)
    {
        _doc = EczaneReports.Suppliers(r);
        Subtitle = $"{r.From:dd.MM.yyyy} – {r.To:dd.MM.yyyy}  ·  {r.Wholesalers.Count} depo  ·  mal fazlası alışın %{(r.TotalPurchases == 0 ? 0 : r.FreeGoods / r.TotalPurchases * 100):0.0}'i";
        Card(0, "Alış", r.TotalPurchases, "₺", null, $"{r.Wholesalers.Sum(w => w.Orders)} sipariş");
        Card(1, "Mal fazlası", r.FreeGoods, "₺", null, "bedelsiz gelen ürün değeri");
        Card(2, "Ödenen", r.Paid, "₺", null, "dönem içinde");
        Card(3, "Güncel borç", r.Balance, "₺", null, r.Overdue > 0 ? $"{App.Money(r.Overdue)} vadesi geçmiş" : "vadesi geçmiş yok");
        foreach (var c in _cards) c.Sparkline = null;
        var palette = new[] { OndaTheme.Colors.Primary, App.Violet, App.Teal, App.Amber, App.Pink };
        Chart(_main, OndaChartType.Bar, "Haftalık alış", "Depo başına (₺)", r.WeekLabels,
            [.. r.WeeklyPurchases.Select((w, i) => new OndaSeries(w.Name, w.Values, palette[i % palette.Length]))]);
        Chart(_side, OndaChartType.Bar, "Ortalama teslim süresi", "Siparişten teslime (saat)", [.. r.Wholesalers.Select(w => w.Wholesaler.Split(' ')[0])],
            new OndaSeries("Saat", [.. r.Wholesalers.Select(w => w.AverageLeadHours)], App.Teal));
        _main.ValueFormat = OndaChart.Compact;
        _side.ValueFormat = v => v.ToString("0.0");
        Columns(r.Wholesalers,
            Col.Text(nameof(SupplierRow.Wholesaler), "Depo", 170),
            Col.Int(nameof(SupplierRow.Orders), "Sipariş", 80, true),
            Col.Int(nameof(SupplierRow.Boxes), "Kutu", 80, true),
            Col.Int(nameof(SupplierRow.FreeBoxes), "MF kutu", 80, true),
            Col.Pct(nameof(SupplierRow.FreeShare), "MF oranı", 80),
            Col.Money(nameof(SupplierRow.Purchases), "Alış", 130, true),
            Col.Money(nameof(SupplierRow.FreeGoodsValue), "MF değeri", 120, true),
            Col.Dbl(nameof(SupplierRow.AverageLeadHours), "Teslim (sa)", 90),
            Col.Money(nameof(SupplierRow.Paid), "Ödenen", 120, true),
            Col.Money(nameof(SupplierRow.Balance), "Bakiye", 120, true),
            Col.Money(nameof(SupplierRow.Overdue), "Vadesi geçen", 120, true),
            Col.Progress(nameof(SupplierRow.Share), "Pay", 100));
    }

    void ShowStaff(StaffReport r)
    {
        _doc = EczaneReports.Staff(r);
        Subtitle = $"{r.Year} yılı  ·  nöbet puanı: gece 1, pazar 2, bayram 3  ·  eczacılar arası denge %{r.Fairness * 100:0}";
        Card(0, "Nöbet günü", r.Duties, "", null, r.Unstaffed > 0 ? $"{r.Unstaffed} atanmamış" : "hepsi atanmış");
        Card(1, "Nöbet dengesi", (decimal)Math.Round(r.Fairness * 100, 0), "%", null, "100 = puanlar eşit");
        Card(2, "Personel gideri", r.PersonnelCost, "₺", null, "yıllık brüt");
        Card(3, "Kullanılan izin", r.Staff.Sum(s => s.LeaveUsed), "", null, $"gün · {r.Staff.Sum(s => s.SickDays)} gün rapor");
        foreach (var c in _cards) c.Sparkline = null;
        var palette = new[] { OndaTheme.Colors.Primary, App.Violet, App.Teal, App.Amber, App.Pink, App.Slate };
        Chart(_main, OndaChartType.Bar, "Aylık nöbet", "Personel başına", r.MonthLabels,
            [.. r.DutiesByMonth.Select((d, i) => new OndaSeries(d.Name.Split(' ')[0], d.Values, palette[i % palette.Length]))]);
        Chart(_side, OndaChartType.Bar, "Nöbet puanı", "Yıl başından beri", [.. r.Staff.Where(s => s.Points > 0).Select(s => s.Name.Split(' ')[0])],
            new OndaSeries("Puan", [.. r.Staff.Where(s => s.Points > 0).Select(s => (double)s.Points)], App.Violet));
        _main.ValueFormat = _side.ValueFormat = v => v.ToString("0");
        Columns(r.Staff,
            Col.Text(nameof(StaffLine.Name), "Personel", 170),
            Col.Badge(nameof(StaffLine.Role), "Görev", 160),
            Col.Int(nameof(StaffLine.Duties), "Nöbet", 70, true),
            Col.Int(nameof(StaffLine.Points), "Puan", 70, true),
            Col.Int(nameof(StaffLine.Sundays), "Pazar/bayram", 100, true),
            Col.Int(nameof(StaffLine.LeaveEntitlement), "İzin hakkı", 90),
            Col.Int(nameof(StaffLine.LeaveUsed), "Kullanılan", 90, true),
            Col.Int(nameof(StaffLine.LeaveLeft), "Kalan", 70, true),
            Col.Int(nameof(StaffLine.SickDays), "Rapor", 70, true),
            Col.Int(nameof(StaffLine.Sales), "Satış", 80, true),
            Col.Money(nameof(StaffLine.Revenue), "Ciro", 130, true),
            Col.Money(nameof(StaffLine.AnnualCost), "Yıllık brüt", 130, true));
    }

    void ShowForecast(RevenueForecastReport r)
    {
        _doc = EczaneReports.Forecast(r);
        Subtitle = $"{r.Today:dd.MM.yyyy} itibarıyla önümüzdeki {r.Days.Count} gün  ·  yöntem: {r.Method}  ·  haftalık desen: pazar kapalı, pazartesi yoğun";
        Card(0, $"{r.Days.Count} günlük beklenen ciro", (decimal)r.ForecastTotal, "₺", null, "reçete + serbest satış");
        Card(1, "Günlük ortalama (tahmin)", (decimal)r.ForecastAverage, "₺", r.HistoryAverage == 0 ? null : (r.ForecastAverage - r.HistoryAverage) / r.HistoryAverage * 100, "son dönem ortalamasına göre");
        Card(2, $"Son {r.HistoryRevenue.Length} gün ortalaması", (decimal)r.HistoryAverage, "₺", null, "gerçekleşen");
        Card(3, "Model hatası (MAE)", (decimal)r.Mae, "₺", null, r.Mape is { } m ? $"ortalama %{m * 100:0.0} sapma" : "bir gün sonrası tahminde");
        _cards[0].Sparkline = [.. r.Days.Select(d => d.Forecast)];
        _cards[1].Sparkline = [.. r.Days.Select(d => d.Forecast)];
        _cards[2].Sparkline = [.. r.HistoryRevenue.TakeLast(28)];
        _cards[3].Sparkline = null;
        var (labels, actual, model) = EczaneReports.ChartSeries(r, 28);
        var realized = actual.Select((v, i) => double.IsNaN(v) ? model[i] : v).ToArray();
        var fitted = model.Select((v, i) => double.IsNaN(v) ? realized[i] : v).ToArray();
        Chart(_main, OndaChartType.Line, "Gerçekleşen ve tahmini günlük ciro", $"Son 28 gün ve önümüzdeki {r.Days.Count} gün (₺)", labels,
            new OndaSeries("Gerçekleşen / tahmin", realized),
            new OndaSeries("Model", fitted, App.Teal));
        Chart(_side, OndaChartType.Bar, "Beklenen reçete", "Günlük", [.. r.Days.Select(d => d.Date.ToString("dd.MM"))],
            new OndaSeries("Reçete", [.. r.Days.Select(d => d.Prescriptions)], App.Violet));
        _main.ValueFormat = OndaChart.Compact;
        _side.ValueFormat = v => v.ToString("0");
        Columns(r.Days,
            Col.Day(nameof(ForecastDay.Date), "Tarih", 120),
            Col.Text(nameof(ForecastDay.DayName), "Gün", 70),
            new OndaGridColumn(nameof(ForecastDay.Forecast), "Tahmini ciro", 150, OndaColumnKind.Number) { Formatter = Lira, ShowTotal = true },
            new OndaGridColumn(nameof(ForecastDay.Lower), "Alt sınır", 130, OndaColumnKind.Number) { Formatter = Lira },
            new OndaGridColumn(nameof(ForecastDay.Upper), "Üst sınır", 130, OndaColumnKind.Number) { Formatter = Lira },
            Col.Dbl(nameof(ForecastDay.Prescriptions), "Beklenen reçete", 140, "0"));
    }

    void ShowAssets(IReadOnlyList<AssetRow> rows, IReadOnlyList<MaintenanceRow> history)
    {
        var year = App.Today.Year;
        _doc = EczaneReports.Assets(rows, history, year);
        var active = rows.Where(a => a.Status != AssetStatus.Retired).ToList();
        Subtitle = $"{year} yılı  ·  {active.Count} demirbaş  ·  doğrusal amortisman";
        Card(0, "Alış bedeli", active.Sum(a => a.Cost), "₺", null, $"{active.Count} demirbaş");
        Card(1, "Net defter değeri", active.Sum(a => a.BookValue), "₺", null, $"birikmiş {App.Money(active.Sum(a => a.Accumulated))}");
        Card(2, $"{year} amortismanı", active.Sum(a => a.ThisYear), "₺", null, "gider olarak yazılır");
        Card(3, "Bakım / onarım", history.Where(m => m.Date.Year == year).Sum(m => m.Cost), "₺", null, $"{history.Count(m => m.Date.Year == year)} işlem");
        foreach (var c in _cards) c.Sparkline = null;
        var groups = active.GroupBy(a => a.CategoryText).OrderByDescending(g => g.Sum(a => a.Cost)).ToList();
        Chart(_main, OndaChartType.Bar, "Gruplara göre değer", "Alış bedeli ve net değer (₺)", [.. groups.Select(g => g.Key.Split(' ')[0])],
            new OndaSeries("Alış bedeli", [.. groups.Select(g => (double)g.Sum(a => a.Cost))]),
            new OndaSeries("Net değer", [.. groups.Select(g => (double)g.Sum(a => a.BookValue))], App.Teal));
        var months = Enumerable.Range(1, 12).ToList();
        Chart(_side, OndaChartType.Bar, "Aylık bakım gideri", $"{year} (₺)", [.. months.Select(m => Texts.Turkish.DateTimeFormat.GetAbbreviatedMonthName(m))],
            new OndaSeries("Gider", [.. months.Select(m => (double)history.Where(h => h.Date.Year == year && h.Date.Month == m).Sum(h => h.Cost))], App.Amber));
        _main.ValueFormat = _side.ValueFormat = OndaChart.Compact;
        Columns(rows,
            Col.Text(nameof(AssetRow.Code), "Kod", 90),
            Col.Text(nameof(AssetRow.Name), "Ad", 230),
            Col.Badge(nameof(AssetRow.CategoryText), "Grup", 150),
            Col.Badge(nameof(AssetRow.StatusText), "Durum", 118),
            Col.Day(nameof(AssetRow.PurchaseDate), "Alış", 100),
            Col.Money(nameof(AssetRow.Cost), "Bedel", 120, true),
            Col.Money(nameof(AssetRow.Accumulated), "Birikmiş", 120, true),
            Col.Money(nameof(AssetRow.BookValue), "Net değer", 120, true),
            Col.Money(nameof(AssetRow.ThisYear), "Bu yıl", 110, true),
            Col.Day(nameof(AssetRow.NextMaintenance), "Sonraki bakım", 110));
    }

    static string Short(string name) => App.ShortHospital(name);

    void Card(int i, string title, decimal value, string prefix, double? delta, string deltaText, int decimals = 0)
    {
        var c = _cards[i];
        c.Title = title;
        c.Value = value;
        c.Prefix = prefix;
        c.Decimals = decimals;
        c.Delta = delta;
        c.DeltaText = deltaText;
    }

    static void Chart(OndaChart chart, OndaChartType type, string title, string subtitle, string[] labels, params OndaSeries[] series)
    {
        chart.ChartType = type;
        chart.Title = title;
        chart.Subtitle = subtitle;
        chart.Labels = labels;
        chart.ShowLegend = series.Length > 1;
        chart.Series.Clear();
        chart.Series.AddRange(series);
    }

    void Columns(object data, params OndaGridColumn[] columns)
    {
        _grid.DataSource = null;
        _grid.Columns.Clear();
        _grid.Columns.AddRange(columns);
        _grid.DataSource = data;
    }

    static double? Change(decimal now, decimal before) => before == 0 ? null : (double)((now - before) / before * 100);

    static string Lira(object? v) => v is double d ? d.ToString("N0") + " ₺" : "";

    protected override void LayoutPage(Rectangle r)
    {
        _kind.SetBounds(r.X, r.Y, Math.Min(r.Width, 860), 38);
        var y2 = r.Y + 50;
        RightAlign(r.Right, y2, _excel, _pdf, _csv);
        _preset.Visible = _from.Visible = _to.Visible = UsesPeriod;
        _horizon.Visible = Current == Kind.Forecast;
        _note.Visible = Current is Kind.Staff or Kind.Assets;
        if (UsesPeriod)
        {
            _preset.SetBounds(r.X, y2, 440, 38);
            _from.SetBounds(r.X + 456, y2 + 2, 150, _from.Height);
            _to.SetBounds(r.X + 616, y2 + 2, 150, _to.Height);
        }
        else if (Current == Kind.Forecast) _horizon.SetBounds(r.X, y2, 300, 38);
        else
        {
            _note.Text = Current == Kind.Staff ? $"{App.Today.Year} yılı başından bugüne" : "Bugünkü durum ve bu yılın amortismanı";
            _note.Location = new Point(r.X, y2 + 10);
        }

        var y = y2 + 38 + Gap;
        const int cardH = 140;
        var cw = (r.Width - Gap * 3) / 4;
        for (var i = 0; i < 4; i++) _cards[i].SetBounds(r.X + i * (cw + Gap), y, cw, cardH);
        y += cardH + Gap;
        var chartH = Math.Max(220, Math.Min(290, (r.Bottom - y) / 2));
        var leftW = (int)((r.Width - Gap) * 0.6);
        _main.SetBounds(r.X, y, leftW, chartH);
        _side.SetBounds(r.X + leftW + Gap, y, r.Width - leftW - Gap, chartH);
        y += chartH + Gap;
        _grid.SetBounds(r.X, y, r.Width, Math.Max(140, r.Bottom - y));
    }
}
