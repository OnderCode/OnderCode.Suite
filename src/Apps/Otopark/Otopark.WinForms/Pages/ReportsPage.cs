using OnderCode.Reporting;
using OnderCode.Reporting.Export;
using Otopark.Application;
using Otopark.WinForms.Reports;

namespace Otopark.WinForms.Pages;

/// <summary>Gelir, yoğunluk saatleri ve ciro tahmini; Excel, PDF ve CSV olarak dışa aktarma.</summary>
sealed class ReportsPage : PageBase
{
    public enum Kind { Revenue, PeakHours, Forecast }

    static readonly int[] Horizons = [7, 14, 30];

    readonly OndaSegmented _kind = new() { Items = ["Gelir", "Yoğunluk saatleri", "Ciro tahmini"] };
    readonly OndaSegmented _preset = new() { Items = ["Son 7 gün", "Son 28 gün", "Bu ay", "Geçen ay"] };
    readonly OndaSegmented _horizon = new() { Items = ["7 gün", "14 gün", "30 gün"], SelectedIndex = 1 };
    readonly OndaDatePicker _from = new() { Compact = true, Placeholder = "Başlangıç" };
    readonly OndaDatePicker _to = new() { Compact = true, Placeholder = "Bitiş" };
    readonly OndaButton _excel = new() { Text = "Excel", Icon = "", Kind = OndaButtonKind.Outline, Width = 110 };
    readonly OndaButton _pdf = new() { Text = "PDF", Icon = "", Kind = OndaButtonKind.Outline, Width = 100 };
    readonly OndaButton _csv = new() { Text = "CSV", Icon = "", Kind = OndaButtonKind.Outline, Width = 100 };

    readonly OndaStatCard[] _cards =
    [
        new() { Icon = "" },
        new() { Icon = "", IconColor = App.Violet },
        new() { Icon = "", IconColor = App.Cyan },
        new() { Icon = "", IconColor = Color.FromArgb(245, 158, 11) },
    ];
    readonly OndaChart _main = new() { ChartType = OndaChartType.Bar };
    readonly OndaChart _side = new() { ChartType = OndaChartType.Bar };
    readonly OndaGrid _grid = new() { RowHeight = 32, ShowFilterRow = false, AutoGenerateColumns = false };

    RevenueReport? _revenue;
    PeakHoursReport? _peak;
    RevenueForecastReport? _forecast;
    bool _syncing;

    public ReportsPage() : base("Raporlar", "Dönemi seçin; raporu Excel, PDF ya da CSV olarak kaydedin")
    {
        _kind.SelectedIndex = 0;
        _preset.SelectedIndex = 1;
        ApplyPreset();

        _kind.SelectedIndexChanged += async (_, _) => { Relayout(); await ReloadAsync(); };
        _preset.SelectedIndexChanged += async (_, _) => { ApplyPreset(); await ReloadAsync(); };
        _horizon.SelectedIndexChanged += async (_, _) => await ReloadAsync();
        _from.ValueChanged += async (_, _) => { if (!_syncing) await ReloadAsync(); };
        _to.ValueChanged += async (_, _) => { if (!_syncing) await ReloadAsync(); };
        _excel.Click += async (_, _) => await ReportSaver.SaveAsync(FindForm(), BuildDocument(), ReportExporters.Excel);
        _pdf.Click += async (_, _) => await ReportSaver.SaveAsync(FindForm(), BuildDocument(), ReportExporters.Pdf);
        _csv.Click += async (_, _) => await ReportSaver.SaveAsync(FindForm(), BuildDocument(), ReportExporters.Csv);

        Controls.AddRange(new Control[] { _kind, _preset, _horizon, _from, _to, _excel, _pdf, _csv, _main, _side, _grid });
        Controls.AddRange(_cards);
    }

    public Kind Current => (Kind)Math.Max(0, _kind.SelectedIndex);

    public async Task ShowAsync(Kind kind)
    {
        if (_kind.SelectedIndex != (int)kind) _kind.SelectedIndex = (int)kind;
        Relayout();
        await ReloadAsync();
    }

    public ReportDocument? BuildDocument() => Current switch
    {
        Kind.Revenue when _revenue is not null => OtoparkReports.Revenue(_revenue),
        Kind.PeakHours when _peak is not null => OtoparkReports.PeakHours(_peak),
        Kind.Forecast when _forecast is not null => OtoparkReports.Forecast(_forecast),
        _ => null,
    };

    void ApplyPreset()
    {
        var today = App.Today;
        var month = new DateOnly(today.Year, today.Month, 1);
        var (from, to) = _preset.SelectedIndex switch
        {
            0 => (today.AddDays(-7), today.AddDays(-1)),
            2 => (month, today.AddDays(-1)),
            3 => (month.AddMonths(-1), month.AddDays(-1)),
            _ => (today.AddDays(-28), today.AddDays(-1)),
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
        var from = _from.Value is { } f ? DateOnly.FromDateTime(f) : today.AddDays(-28);
        var to = _to.Value is { } t ? DateOnly.FromDateTime(t) : today.AddDays(-1);
        return (from, to);
    }

    protected override async Task LoadAsync()
    {
        var (from, to) = Period();
        switch (Current)
        {
            case Kind.Revenue:
            {
                var result = await App.Send(new GetRevenueReport(from, to));
                if (!result.IsSuccess) { App.ShowErrors(result); return; }
                ShowRevenue(_revenue = result.Value);
                break;
            }
            case Kind.PeakHours:
            {
                var result = await App.Send(new GetPeakHours(from, to));
                if (!result.IsSuccess) { App.ShowErrors(result); return; }
                ShowPeak(_peak = result.Value);
                break;
            }
            default:
            {
                var result = await App.Send(new GetRevenueForecast(Horizons[Math.Max(0, _horizon.SelectedIndex)]));
                if (!result.IsSuccess) { App.ShowErrors(result); return; }
                ShowForecast(_forecast = result.Value);
                break;
            }
        }
        foreach (var card in _cards) card.Replay();
        _main.Replay();
        _side.Replay();
    }

    void ShowRevenue(RevenueReport r)
    {
        Subtitle = $"{r.From:dd.MM.yyyy} – {r.To:dd.MM.yyyy}  ·  {r.Days.Count} gün  ·  önceki {r.Days.Count} günle karşılaştırma";
        Card(0, "Toplam ciro", r.Total, "₺", 0, Change(r.Total, r.PreviousTotal), "önceki döneme göre");
        Card(1, "Otopark geliri", r.Parking, "₺", 0, Change(r.Parking, r.PreviousParking), $"{r.Sessions:N0} araç");
        Card(2, "Yıkama geliri", r.Wash, "₺", 0, Change(r.Wash, r.PreviousWash), $"{r.WashOrders:N0} sipariş");
        Card(3, "Ortalama kalış", (decimal)Math.Round(r.AverageStayMinutes, 0), "", 0,
            r.PreviousAverageStayMinutes == 0 ? null : (r.AverageStayMinutes - r.PreviousAverageStayMinutes) / r.PreviousAverageStayMinutes * 100, "dk · önceki döneme göre");
        _cards[0].Sparkline = [.. r.Days.Select(d => (double)d.Total)];
        _cards[1].Sparkline = [.. r.Days.Select(d => (double)d.ParkingRevenue)];
        _cards[2].Sparkline = [.. r.Days.Select(d => (double)d.WashRevenue)];
        _cards[3].Sparkline = [.. r.Days.Select(d => d.AverageStayMinutes)];

        Chart(_main, OndaChartType.Bar, "Günlük ciro", "Otopark ve yıkama (₺)", [.. r.Days.Select(d => d.Date.ToString("dd.MM"))],
            new OndaSeries("Otopark", [.. r.Days.Select(d => (double)d.ParkingRevenue)]),
            new OndaSeries("Yıkama", [.. r.Days.Select(d => (double)d.WashRevenue)], App.Cyan));
        Chart(_side, OndaChartType.Bar, "Yıkama paketleri", "Dönem geliri (₺), ABC sırasıyla", [.. r.Packages.Select(p => p.Package.Split(' ')[0])],
            new OndaSeries("Gelir", [.. r.Packages.Select(p => (double)p.Revenue)], App.Cyan));
        _main.ValueFormat = OndaChart.Compact;
        _side.ValueFormat = OndaChart.Compact;

        Columns(
            new OndaGridColumn(nameof(DailyRow.Date), "Tarih", 110) { Format = "dd.MM.yyyy" },
            new OndaGridColumn(nameof(DailyRow.DayName), "Gün", 70),
            new OndaGridColumn(nameof(DailyRow.Entries), "Giriş", 90, OndaColumnKind.Number) { ShowTotal = true },
            new OndaGridColumn(nameof(DailyRow.Exits), "Çıkış", 90, OndaColumnKind.Number) { ShowTotal = true },
            new OndaGridColumn(nameof(DailyRow.PeakInside), "En yoğun an", 120, OndaColumnKind.Number),
            new OndaGridColumn(nameof(DailyRow.AverageStayMinutes), "Ort. kalış (dk)", 130, OndaColumnKind.Number),
            new OndaGridColumn(nameof(DailyRow.ParkingRevenue), "Otopark", 140, OndaColumnKind.Money) { ShowTotal = true },
            new OndaGridColumn(nameof(DailyRow.WashRevenue), "Yıkama", 130, OndaColumnKind.Money) { ShowTotal = true },
            new OndaGridColumn(nameof(DailyRow.Total), "Toplam", 140, OndaColumnKind.Money) { ShowTotal = true });
        _grid.DataSource = r.Days;
    }

    void ShowPeak(PeakHoursReport r)
    {
        var top = r.TopSlots.FirstOrDefault();
        Subtitle = $"{r.From:dd.MM.yyyy} – {r.To:dd.MM.yyyy}  ·  ortalama içerideki araç  ·  en yoğun dilimler: {string.Join(", ", r.TopSlots.Take(3).Select(s => s.Text))}";
        Card(0, "En yoğun dilim · ortalama", (decimal)(top?.Inside ?? 0), "", 0, null, top?.Text ?? "—");
        Card(1, "Doluluk · en yoğun dilim", (decimal)Math.Round((top?.Inside ?? 0) * 100 / Math.Max(1, r.Capacity), 0), "%", 0, null, $"{r.Capacity} park yeri");
        Card(2, "En yoğun saat (tüm günler)", (decimal)r.BusiestAverage, "", 1, null, "ortalama içerideki araç");
        Card(3, "En sakin saat", (decimal)r.QuietestAverage, "", 1, null, "ortalama içerideki araç");
        foreach (var c in _cards) c.Sparkline = null;

        var (weekday, weekend) = OtoparkReports.WeekSplit(r);
        var hours = r.Hours.Select(h => h.Hour[..2]).ToArray();
        Chart(_main, OndaChartType.Line, "Saatlik ortalama doluluk", "Hafta içi ve hafta sonu, içerideki araç", hours,
            new OndaSeries("Hafta içi", weekday),
            new OndaSeries("Hafta sonu", weekend, Color.FromArgb(245, 158, 11)));
        Chart(_side, OndaChartType.Bar, "Saatlik giriş", "Günde ortalama giren araç", hours,
            new OndaSeries("Giriş", [.. r.Hours.Select(h => h.AverageEntries)], Color.FromArgb(20, 184, 166)));
        _main.ValueFormat = v => v.ToString("0");
        _side.ValueFormat = v => v.ToString("0");

        Columns(
            new OndaGridColumn(nameof(HourRow.Hour), "Saat", 80),
            new OndaGridColumn(nameof(HourRow.Mon), "Pzt", 80, OndaColumnKind.Number) { Formatter = One },
            new OndaGridColumn(nameof(HourRow.Tue), "Sal", 80, OndaColumnKind.Number) { Formatter = One },
            new OndaGridColumn(nameof(HourRow.Wed), "Çar", 80, OndaColumnKind.Number) { Formatter = One },
            new OndaGridColumn(nameof(HourRow.Thu), "Per", 80, OndaColumnKind.Number) { Formatter = One },
            new OndaGridColumn(nameof(HourRow.Fri), "Cum", 80, OndaColumnKind.Number) { Formatter = One },
            new OndaGridColumn(nameof(HourRow.Sat), "Cmt", 80, OndaColumnKind.Number) { Formatter = One },
            new OndaGridColumn(nameof(HourRow.Sun), "Paz", 80, OndaColumnKind.Number) { Formatter = One },
            new OndaGridColumn(nameof(HourRow.Average), "Ortalama", 100, OndaColumnKind.Number) { Formatter = One },
            new OndaGridColumn(nameof(HourRow.AverageEntries), "Giriş / gün", 110, OndaColumnKind.Number) { Formatter = One });
        _grid.DataSource = r.Hours;
    }

    void ShowForecast(RevenueForecastReport r)
    {
        Subtitle = $"{r.Today:dd.MM.yyyy} itibarıyla önümüzdeki {r.Days.Count} gün  ·  yöntem: {r.Method} (son dönem üzerinde yarıştırılarak seçildi)";
        Card(0, $"{r.Days.Count} günlük beklenen ciro", (decimal)r.ForecastTotal, "₺", 0, null, "otopark + yıkama");
        Card(1, "Günlük ortalama (tahmin)", (decimal)r.ForecastAverage, "₺", 0,
            r.HistoryAverage == 0 ? null : (r.ForecastAverage - r.HistoryAverage) / r.HistoryAverage * 100, "son dönem ortalamasına göre");
        Card(2, $"Son {r.HistoryRevenue.Length} gün ortalaması", (decimal)r.HistoryAverage, "₺", 0, null, "gerçekleşen");
        Card(3, "Model hatası (MAE)", (decimal)r.Mae, "₺", 0, null, r.Mape is { } m ? $"ortalama %{m * 100:0.0} sapma" : "bir gün sonrası tahminde");
        _cards[0].Sparkline = [.. r.Days.Select(d => d.Forecast)];
        _cards[1].Sparkline = [.. r.Days.Select(d => d.Forecast)];
        _cards[2].Sparkline = [.. r.HistoryRevenue.TakeLast(28)];
        _cards[3].Sparkline = null;

        var (labels, actual, model) = OtoparkReports.ChartSeries(r, 28);
        // Ekran grafiği boşluk çizmez: gelecekte gerçekleşen çizgisi tahmine bağlanır
        var realized = actual.Select((v, i) => double.IsNaN(v) ? model[i] : v).ToArray();
        var fitted = model.Select((v, i) => double.IsNaN(v) ? realized[i] : v).ToArray();
        Chart(_main, OndaChartType.Line, "Gerçekleşen ve tahmini günlük ciro", $"Son 28 gün ve önümüzdeki {r.Days.Count} gün (₺)", labels,
            new OndaSeries("Gerçekleşen / tahmin", realized),
            new OndaSeries("Model", fitted, Color.FromArgb(20, 184, 166)));
        Chart(_side, OndaChartType.Bar, "Beklenen giriş", "Günlük araç sayısı", [.. r.Days.Select(d => d.Date.ToString("dd.MM"))],
            new OndaSeries("Giriş", [.. r.Days.Select(d => d.ExpectedEntries)], App.Violet));
        _main.ValueFormat = OndaChart.Compact;
        _side.ValueFormat = v => v.ToString("0");

        Columns(
            new OndaGridColumn(nameof(ForecastDay.Date), "Tarih", 120) { Format = "dd.MM.yyyy" },
            new OndaGridColumn(nameof(ForecastDay.DayName), "Gün", 80),
            new OndaGridColumn(nameof(ForecastDay.Forecast), "Tahmini ciro", 150, OndaColumnKind.Number) { Formatter = Lira, ShowTotal = true },
            new OndaGridColumn(nameof(ForecastDay.Lower), "Alt sınır", 130, OndaColumnKind.Number) { Formatter = Lira },
            new OndaGridColumn(nameof(ForecastDay.Upper), "Üst sınır", 130, OndaColumnKind.Number) { Formatter = Lira },
            new OndaGridColumn(nameof(ForecastDay.ExpectedEntries), "Beklenen giriş", 140, OndaColumnKind.Number) { Formatter = v => v is double d ? d.ToString("N0") : "" });
        _grid.DataSource = r.Days;
    }

    /// <summary>Ekrandaki raporu verilen klasöre yazar (örnek dosyalar için).</summary>
    public void SaveAll(string directory, string baseName, params IReportExporter[] exporters)
        => ReportSaver.SaveAll(BuildDocument() ?? throw new InvalidOperationException("Rapor yüklenmedi."), directory, baseName, exporters);

    void Card(int i, string title, decimal value, string prefix, int decimals, double? delta, string deltaText)
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

    void Columns(params OndaGridColumn[] columns)
    {
        _grid.DataSource = null;
        _grid.Columns.Clear();
        _grid.Columns.AddRange(columns);
    }

    static double? Change(decimal now, decimal before) => before == 0 ? null : (double)((now - before) / before * 100);

    static string One(object? v) => v is double d ? d.ToString("0.0") : "";

    static string Lira(object? v) => v is double d ? d.ToString("N0") + " ₺" : "";

    protected override void LayoutPage(Rectangle r)
    {
        _kind.SetBounds(r.X, r.Y, 520, 38);
        RightAlign(r.Right, r.Y, _excel, _pdf, _csv);

        var y2 = r.Y + 52;
        var forecast = Current == Kind.Forecast;
        _preset.Visible = _from.Visible = _to.Visible = !forecast;
        _horizon.Visible = forecast;
        if (forecast) _horizon.SetBounds(r.X, y2, 300, 38);
        else
        {
            _preset.SetBounds(r.X, y2, 440, 38);
            _from.SetBounds(r.X + 456, y2 + 2, 150, _from.Height);
            _to.SetBounds(r.X + 616, y2 + 2, 150, _to.Height);
        }

        var y = y2 + 38 + Gap;
        const int cardH = 132;
        var cw = (r.Width - Gap * 3) / 4;
        for (var i = 0; i < 4; i++) _cards[i].SetBounds(r.X + i * (cw + Gap), y, cw, cardH);

        y += cardH + Gap;
        var chartH = Math.Max(220, Math.Min(290, (r.Bottom - y) / 2));
        var leftW = (int)((r.Width - Gap) * 0.62);
        _main.SetBounds(r.X, y, leftW, chartH);
        _side.SetBounds(r.X + leftW + Gap, y, r.Width - leftW - Gap, chartH);

        y += chartH + Gap;
        _grid.SetBounds(r.X, y, r.Width, Math.Max(140, r.Bottom - y));
    }
}
