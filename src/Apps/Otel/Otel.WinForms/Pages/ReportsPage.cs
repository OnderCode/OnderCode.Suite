using System.Diagnostics;
using OnderCode.Reporting;
using OnderCode.Reporting.Export;
using Otel.Application;
using Otel.WinForms.Reports;

namespace Otel.WinForms.Pages;

/// <summary>Gelir ve doluluk, misafir değeri (Pareto/ABC) ve doluluk tahmini; Excel, PDF ve CSV olarak dışa aktarma.</summary>
sealed class ReportsPage : PageBase
{
    public enum Kind { Revenue, GuestValue, Forecast }

    static readonly int[] Horizons = [7, 14, 30];

    readonly OndaSegmented _kind = new() { Items = ["Gelir ve doluluk", "Misafir değeri (ABC)", "Doluluk tahmini"] };
    readonly OndaSegmented _preset = new() { Items = ["Son 30 gün", "Bu ay", "Geçen ay", "Son 90 gün"] };
    readonly OndaSegmented _horizon = new() { Items = ["7 gün", "14 gün", "30 gün"], SelectedIndex = 1 };
    readonly OndaDatePicker _from = new() { Compact = true, Placeholder = "Başlangıç" };
    readonly OndaDatePicker _to = new() { Compact = true, Placeholder = "Bitiş" };
    readonly OndaButton _excel = new() { Text = "Excel", Icon = "", Kind = OndaButtonKind.Outline, Width = 110 };
    readonly OndaButton _pdf = new() { Text = "PDF", Icon = "", Kind = OndaButtonKind.Outline, Width = 100 };
    readonly OndaButton _csv = new() { Text = "CSV", Icon = "", Kind = OndaButtonKind.Outline, Width = 100 };

    readonly OndaStatCard[] _cards =
    [
        new() { Icon = "" },
        new() { Icon = "", IconColor = App.Violet },
        new() { Icon = "", IconColor = Color.FromArgb(20, 184, 166) },
        new() { Icon = "", IconColor = Color.FromArgb(245, 158, 11) },
    ];
    readonly OndaChart _main = new() { ChartType = OndaChartType.Area };
    readonly OndaChart _side = new() { ChartType = OndaChartType.Bar };
    readonly OndaGrid _grid = new() { RowHeight = 34, ShowFilterRow = false, AutoGenerateColumns = false };

    RevenueReport? _revenue;
    GuestParetoReport? _pareto;
    OccupancyForecastReport? _forecast;
    bool _syncing;

    public ReportsPage() : base("Raporlar", "Dönemi seçin; raporu Excel, PDF ya da CSV olarak kaydedin")
    {
        _kind.SelectedIndex = 0;
        _preset.SelectedIndex = 0;
        ApplyPreset();

        _kind.SelectedIndexChanged += async (_, _) => { Relayout(); await ReloadAsync(); };
        _preset.SelectedIndexChanged += async (_, _) => { ApplyPreset(); await ReloadAsync(); };
        _horizon.SelectedIndexChanged += async (_, _) => await ReloadAsync();
        _from.ValueChanged += async (_, _) => { if (!_syncing) await ReloadAsync(); };
        _to.ValueChanged += async (_, _) => { if (!_syncing) await ReloadAsync(); };
        _excel.Click += async (_, _) => await ExportAsync(ReportExporters.Excel);
        _pdf.Click += async (_, _) => await ExportAsync(ReportExporters.Pdf);
        _csv.Click += async (_, _) => await ExportAsync(ReportExporters.Csv);

        Controls.AddRange(new Control[] { _kind, _preset, _horizon, _from, _to, _excel, _pdf, _csv, _main, _side, _grid });
        Controls.AddRange(_cards);
    }

    public Kind Current => (Kind)Math.Max(0, _kind.SelectedIndex);

    /// <summary>Rapor türünü değiştirip veriyi yükler (ekran görüntüsü otomasyonu da kullanır).</summary>
    public async Task ShowAsync(Kind kind)
    {
        if (_kind.SelectedIndex != (int)kind) _kind.SelectedIndex = (int)kind;
        Relayout();
        await ReloadAsync();
    }

    /// <summary>Ekrandaki raporun biçimden bağımsız belgesi (henüz yüklenmediyse null).</summary>
    public ReportDocument? BuildDocument() => Current switch
    {
        Kind.Revenue when _revenue is not null => OtelReports.Revenue(_revenue),
        Kind.GuestValue when _pareto is not null => OtelReports.GuestValue(_pareto),
        Kind.Forecast when _forecast is not null => OtelReports.Forecast(_forecast),
        _ => null,
    };

    void ApplyPreset()
    {
        var today = App.Today;
        var (from, to) = _preset.SelectedIndex switch
        {
            1 => (new DateOnly(today.Year, today.Month, 1), today.AddDays(-1)),
            2 => (new DateOnly(today.Year, today.Month, 1).AddMonths(-1), new DateOnly(today.Year, today.Month, 1).AddDays(-1)),
            3 => (today.AddDays(-90), today.AddDays(-1)),
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
        var from = _from.Value is { } f ? DateOnly.FromDateTime(f) : today.AddDays(-30);
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
            case Kind.GuestValue:
            {
                var result = await App.Send(new GetGuestPareto(from, to));
                if (!result.IsSuccess) { App.ShowErrors(result); return; }
                ShowPareto(_pareto = result.Value);
                break;
            }
            default:
            {
                var result = await App.Send(new GetOccupancyForecast(Horizons[Math.Max(0, _horizon.SelectedIndex)]));
                if (!result.IsSuccess) { App.ShowErrors(result); return; }
                ShowForecast(_forecast = result.Value);
                break;
            }
        }
        foreach (var card in _cards) card.Replay();
        _main.Replay();
        _side.Replay();
    }

    // ---- görünümler ----

    void ShowRevenue(RevenueReport r)
    {
        Subtitle = $"{r.From:dd.MM.yyyy} – {r.To:dd.MM.yyyy}  ·  {r.Days.Count} gün  ·  önceki {r.Days.Count} günle karşılaştırma";
        Card(0, "", "Doluluk (OCC)", r.Current.Occupancy * 100, "%", 1, (double)((r.Current.Occupancy - r.Previous.Occupancy) * 100), "puan fark");
        Card(1, "", "ADR · ortalama gecelik", r.Current.Adr, "₺", 0, Change(r.Current.Adr, r.Previous.Adr), "önceki döneme göre");
        Card(2, "", "RevPAR · oda başına gelir", r.Current.RevPar, "₺", 0, Change(r.Current.RevPar, r.Previous.RevPar), "önceki döneme göre");
        Card(3, "", "Toplam gelir (oda + ekstra)", r.TotalRevenue, "₺", 0, Change(r.TotalRevenue, r.PreviousTotalRevenue), "önceki döneme göre");
        _cards[0].Sparkline = [.. r.Days.Select(d => d.Occupancy * 100)];
        _cards[3].Sparkline = [.. r.Days.Select(d => (double)d.RoomRevenue)];

        Chart(_main, OndaChartType.Area, "Günlük doluluk", "Satılan oda / toplam oda (%)", [.. r.Days.Select(d => d.Date.ToString("dd.MM"))],
            new OndaSeries("Doluluk", [.. r.Days.Select(d => Math.Round(d.Occupancy * 100, 1))]));
        Chart(_side, OndaChartType.Bar, "Oda tipine göre", "Dönem oda geliri (₺)", [.. r.RoomTypes.Select(t => t.RoomType)],
            new OndaSeries("Oda geliri", [.. r.RoomTypes.Select(t => (double)t.Revenue)], Color.FromArgb(20, 184, 166)));
        _side.ValueFormat = OndaChart.Compact;
        _main.ValueFormat = v => "%" + v.ToString("0");

        Columns(
            new OndaGridColumn(nameof(DailyRevenueRow.Date), "Tarih", 120) { Format = "dd.MM.yyyy" },
            new OndaGridColumn(nameof(DailyRevenueRow.DayName), "Gün", 80),
            new OndaGridColumn(nameof(DailyRevenueRow.SoldRooms), "Satılan oda", 120, OndaColumnKind.Number) { ShowTotal = true },
            new OndaGridColumn(nameof(DailyRevenueRow.Occupancy), "Doluluk", 110, OndaColumnKind.Number) { Formatter = Pct },
            new OndaGridColumn(nameof(DailyRevenueRow.RoomRevenue), "Oda geliri", 160, OndaColumnKind.Money) { ShowTotal = true },
            new OndaGridColumn(nameof(DailyRevenueRow.Adr), "ADR", 140, OndaColumnKind.Money),
            new OndaGridColumn(nameof(DailyRevenueRow.RevPar), "RevPAR", 140, OndaColumnKind.Money));
        _grid.DataSource = r.Days;
    }

    void ShowPareto(GuestParetoReport r)
    {
        Subtitle = $"Girişi {r.From:dd.MM.yyyy} – {r.To:dd.MM.yyyy} arasında olan konaklamalar  ·  misafirlerin %{r.CountShareA * 100:0}'i gelirin %{r.ValueShareA * 100:0}'ini getiriyor";
        Card(0, "", "Misafir", r.GuestCount, "", 0, null, $"A {r.CountA}  ·  B {r.CountB}  ·  C {r.CountC}");
        Card(1, "", "Toplam gelir", r.Total, "₺", 0, null, "oda + folyo harcamaları");
        Card(2, "", "A sınıfı misafir", r.CountA, "", 0, null, $"gelirin %{r.ValueShareA * 100:0.0}'i");
        Card(3, "", "Ortalama misafir değeri", r.GuestCount == 0 ? 0 : r.Total / r.GuestCount, "₺", 0, null, "dönem içinde");
        foreach (var c in _cards) c.Sparkline = null;

        var n = r.Guests.Count;
        Chart(_main, OndaChartType.Line, "Gelir yoğunlaşması", "Kümülatif gelir payı, en değerli misafirden başlayarak (%)",
            [.. r.Guests.Select(g => g.Rank.ToString())],
            new OndaSeries("Kümülatif pay", [.. r.Guests.Select(g => Math.Round(g.Cumulative * 100, 1))]),
            new OndaSeries("Eşit dağılım", [.. r.Guests.Select((_, i) => Math.Round((i + 1) * 100.0 / n, 1))], Color.FromArgb(148, 163, 184)));
        Chart(_side, OndaChartType.Bar, "Sınıflar", "Misafir ve gelir payı (%)", ["A", "B", "C"],
            new OndaSeries("Misafir payı", [Share(r.CountA, n), Share(r.CountB, n), Share(r.CountC, n)], Color.FromArgb(148, 163, 184)),
            new OndaSeries("Gelir payı", [Math.Round(r.ValueShareA * 100, 1), Math.Round(r.ValueShareB * 100, 1), Math.Round(r.ValueShareC * 100, 1)]));
        _main.ValueFormat = v => "%" + v.ToString("0");
        _side.ValueFormat = v => "%" + v.ToString("0");

        Columns(
            new OndaGridColumn(nameof(GuestValueRow.Rank), "Sıra", 70, OndaColumnKind.Number),
            new OndaGridColumn(nameof(GuestValueRow.GuestName), "Misafir", 210),
            new OndaGridColumn(nameof(GuestValueRow.TierText), "Seviye", 100, OndaColumnKind.Badge) { BadgeColor = App.BadgeColor },
            new OndaGridColumn(nameof(GuestValueRow.Stays), "Konaklama", 100, OndaColumnKind.Number) { ShowTotal = true },
            new OndaGridColumn(nameof(GuestValueRow.Nights), "Gece", 80, OndaColumnKind.Number) { ShowTotal = true },
            new OndaGridColumn(nameof(GuestValueRow.Total), "Toplam gelir", 150, OndaColumnKind.Money) { ShowTotal = true },
            new OndaGridColumn(nameof(GuestValueRow.Share), "Pay", 90, OndaColumnKind.Number) { Formatter = v => Pct(v, 2) },
            new OndaGridColumn(nameof(GuestValueRow.Cumulative), "Kümülatif", 110, OndaColumnKind.Number) { Formatter = Pct },
            new OndaGridColumn(nameof(GuestValueRow.Class), "Sınıf", 80, OndaColumnKind.Badge) { BadgeColor = App.BadgeColor });
        _grid.DataSource = r.Guests;
    }

    void ShowForecast(OccupancyForecastReport r)
    {
        Subtitle = $"{r.Today:dd.MM.yyyy} itibarıyla önümüzdeki {r.Days.Count} gün  ·  yöntem: {r.Method} (son dönem üzerinde yarıştırılarak seçildi)";
        Card(0, "", "Beklenen ortalama doluluk", (decimal)(r.ForecastAverage * 100), "%", 1, null, $"≈ {Math.Round(r.ForecastAverage * r.RoomCount):0} dolu oda / gün");
        Card(1, "", "Kesin rezervasyon (OTB)", (decimal)(r.OnTheBooksAverage * 100), "%", 1, null, "bugün itibarıyla satılmış");
        Card(2, "", $"Son {r.HistoryActual.Length} gün ortalaması", (decimal)(r.HistoryAverage * 100), "%", 1, null, "gerçekleşen doluluk");
        Card(3, "", "Model hatası (MAE)", (decimal)(r.Mae * 100), "", 1, null, "puan, bir gün sonrası tahminde");
        _cards[0].Sparkline = [.. r.Days.Select(d => d.Expected * 100)];
        _cards[1].Sparkline = [.. r.Days.Select(d => d.OnTheBooks * 100)];
        _cards[2].Sparkline = [.. r.HistoryActual.TakeLast(28).Select(v => v * 100)];
        _cards[3].Sparkline = null;

        var (labels, actual, model, booked) = OtelReports.ChartSeries(r, 28);
        // Ekran grafiği boşluk çizmez: gerçekleşen geçmişte, kesin rezervasyon gelecekte aynı çizgide gösterilir
        var realized = actual.Select((v, i) => Math.Round(double.IsNaN(v) ? booked[i] : v, 1)).ToArray();
        var fitted = model.Select((v, i) => Math.Round(double.IsNaN(v) ? realized[i] : v, 1)).ToArray();
        Chart(_main, OndaChartType.Line, "Gerçekleşen ve beklenen doluluk", $"Son 28 gün gerçekleşen, önümüzdeki {r.Days.Count} gün kesin rezervasyon (%)", labels,
            new OndaSeries("Gerçekleşen / kesin", realized),
            new OndaSeries("Model / tahmin", fitted, Color.FromArgb(20, 184, 166)));
        Chart(_side, OndaChartType.Bar, "Önümüzdeki günler", "Kesin rezervasyon ve beklenen (%)", [.. r.Days.Select(d => d.Date.ToString("dd.MM"))],
            new OndaSeries("Kesin", [.. r.Days.Select(d => Math.Round(d.OnTheBooks * 100, 1))], Color.FromArgb(245, 158, 11)),
            new OndaSeries("Beklenen", [.. r.Days.Select(d => Math.Round(d.Expected * 100, 1))]));
        _main.ValueFormat = v => "%" + v.ToString("0");
        _side.ValueFormat = v => "%" + v.ToString("0");

        Columns(
            new OndaGridColumn(nameof(ForecastDayRow.Date), "Tarih", 120) { Format = "dd.MM.yyyy" },
            new OndaGridColumn(nameof(ForecastDayRow.DayName), "Gün", 80),
            new OndaGridColumn(nameof(ForecastDayRow.OnTheBooks), "Kesin rezervasyon", 160, OndaColumnKind.Number) { Formatter = Pct },
            new OndaGridColumn(nameof(ForecastDayRow.Forecast), "Tahmin", 110, OndaColumnKind.Number) { Formatter = Pct },
            new OndaGridColumn(nameof(ForecastDayRow.Lower), "Alt sınır", 110, OndaColumnKind.Number) { Formatter = Pct },
            new OndaGridColumn(nameof(ForecastDayRow.Upper), "Üst sınır", 110, OndaColumnKind.Number) { Formatter = Pct },
            new OndaGridColumn(nameof(ForecastDayRow.Expected), "Beklenen", 110, OndaColumnKind.Number) { Formatter = Pct },
            new OndaGridColumn(nameof(ForecastDayRow.ExpectedRooms), "Beklenen dolu oda", 160, OndaColumnKind.Number));
        _grid.DataSource = r.Days;
    }

    // ---- dışa aktarma ----

    async Task ExportAsync(IReportExporter exporter)
    {
        var doc = BuildDocument();
        if (doc is null) { OndaToast.Info("Rapor henüz yüklenmedi."); return; }

        using var dialog = new SaveFileDialog
        {
            Title = "Raporu kaydet",
            Filter = exporter.Filter,
            FileName = ReportExporters.SuggestFileName(doc, exporter),
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;

        try
        {
            await Task.Run(() => ReportExporters.Save(doc, dialog.FileName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            OndaToast.Error(ex.Message, "Rapor kaydedilemedi");
            return;
        }
        OndaToast.Success(Path.GetFileName(dialog.FileName), "Rapor kaydedildi");
        try { Process.Start(new ProcessStartInfo(dialog.FileName) { UseShellExecute = true }); } catch { }
    }

    /// <summary>Ekrandaki raporu verilen klasöre bütün biçimlerde yazar (örnek dosyalar için).</summary>
    public void SaveAll(string directory, string baseName, params IReportExporter[] exporters)
    {
        var doc = BuildDocument() ?? throw new InvalidOperationException("Rapor yüklenmedi.");
        Directory.CreateDirectory(directory);
        foreach (var e in exporters) ReportExporters.Save(doc, Path.Combine(directory, baseName + e.Extension));
    }

    // ---- yardımcılar ----

    void Card(int i, string icon, string title, decimal value, string prefix, int decimals, double? delta, string deltaText)
    {
        var c = _cards[i];
        c.Icon = icon;
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

    static double Share(int part, int total) => total == 0 ? 0 : Math.Round(part * 100.0 / total, 1);

    static string Pct(object? v) => Pct(v, 1);

    static string Pct(object? v, int decimals) => v is double d ? ReportFormat.Percent(d, decimals) : "";

    protected override void LayoutPage(Rectangle r)
    {
        _kind.SetBounds(r.X, r.Y, 560, 38);
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
