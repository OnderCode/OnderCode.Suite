using OnderCode.Reporting;
using Otopark.Application;
using static OnderCode.Reporting.ReportFormat;

namespace Otopark.WinForms.Reports;

/// <summary>Uygulama katmanının rapor sonuçlarını biçimden bağımsız <see cref="ReportDocument"/>'e çevirir (Excel, PDF, CSV).</summary>
static class OtoparkReports
{
    public const string Organization = "OnderCode Otopark";

    public static ReportDocument Revenue(RevenueReport r)
    {
        var doc = new ReportDocument
        {
            Title = "Gelir Raporu",
            Subtitle = $"{Date(r.From)} – {Date(r.To)}  ·  {r.Days.Count} gün  ·  {r.Capacity} park yeri",
            Organization = Organization,
        };
        doc.Kpis.Add(new ReportKpi("Toplam ciro", Money(r.Total), Change(r.Total, r.PreviousTotal)));
        doc.Kpis.Add(new ReportKpi("Otopark geliri", Money(r.Parking), Change(r.Parking, r.PreviousParking)));
        doc.Kpis.Add(new ReportKpi("Yıkama geliri", Money(r.Wash), Change(r.Wash, r.PreviousWash)));
        doc.Kpis.Add(new ReportKpi("Araç (çıkış)", Integer(r.Sessions), Change(r.Sessions, r.PreviousSessions)));
        doc.Kpis.Add(new ReportKpi("Ortalama kalış", $"{r.AverageStayMinutes:0} dk", $"önceki dönem {r.PreviousAverageStayMinutes:0} dk"));
        doc.Kpis.Add(new ReportKpi("Araç başı otopark geliri", Money(r.AveragePerSession), "abone girişleri hariç"));
        doc.Kpis.Add(new ReportKpi("Abone girişi", Integer(r.SubscriberVisits), "ücretsiz"));
        doc.Kpis.Add(new ReportKpi("Yıkama siparişi", Integer(r.WashOrders), r.WashOrders == 0 ? null : $"ortalama {Money(r.Wash / r.WashOrders)}"));

        var labels = r.Days.Select(d => d.Date.ToString("dd.MM")).ToArray();
        doc.Charts.Add(new ReportChart
        {
            Title = "Günlük ciro",
            Subtitle = "Otopark (çıkış günü) ve yıkama (teslim günü), ₺",
            Kind = ChartKind.Bar,
            Labels = labels,
            Series =
            {
                new ReportSeries("Otopark", [.. r.Days.Select(d => (double)d.ParkingRevenue)]),
                new ReportSeries("Yıkama", [.. r.Days.Select(d => (double)d.WashRevenue)], Color: "#06B6D4"),
            },
        });

        doc.Tables.Add(ReportTable.For("Günlük döküm", r.Days)
            .Date("Tarih", d => d.Date)
            .Text("Gün", d => d.DayName, 6)
            .Integer("Giriş", d => d.Entries, 8, total: true)
            .Integer("Çıkış", d => d.Exits, 8, total: true)
            .Integer("En yoğun an", d => d.PeakInside, 10)
            .Number("Ort. kalış (dk)", d => d.AverageStayMinutes, 0, 11)
            .Money("Otopark", d => d.ParkingRevenue, 13, total: true)
            .Money("Yıkama", d => d.WashRevenue, 13, total: true)
            .Money("Toplam", d => d.Total, 13, total: true)
            .Build());
        doc.Tables.Add(ReportTable.For("Araç sınıfına göre otopark", r.Vehicles)
            .Text("Araç", v => v.Vehicle, 16)
            .Integer("Araç sayısı", v => v.Sessions, 11, total: true)
            .Number("Ort. kalış (dk)", v => v.AverageStayMinutes, 0, 12)
            .Money("Gelir", v => v.Revenue, total: true)
            .Percent("Pay", v => v.Share, total: true)
            .Build());
        doc.Tables.Add(ReportTable.For("Yıkama paketleri (ABC)", r.Packages)
            .Description("Paketler gelire göre sıralanır; A sınıfı gelirin ilk %80'ini oluşturur.")
            .Text("Paket", p => p.Package, 18)
            .Integer("Sipariş", p => p.Orders, 9, total: true)
            .Money("Gelir", p => p.Revenue, total: true)
            .Percent("Pay", p => p.Share, total: true)
            .Percent("Kümülatif", p => p.Cumulative, 1, 10)
            .Text("Sınıf", p => p.Class, 6)
            .Build());
        doc.Tables.Add(ReportTable.For("Ödeme türleri", r.Payments)
            .Text("Ödeme", p => p.Method, 14)
            .Integer("Adet", p => p.Count, 8, total: true)
            .Money("Tutar", p => p.Amount, total: true)
            .Percent("Pay", p => p.Share, total: true)
            .Build());

        doc.Notes.Add("Otopark geliri aracın çıkış yaptığı güne, yıkama geliri aracın teslim edildiği güne yazılır. Karşılaştırmalar aynı uzunluktaki bir önceki döneme göredir.");
        doc.Notes.Add("Tarife: ilk 15 dakika ücretsiz, ilk saat ve başlayan her ek saat ücretli, günlük üst sınır var. Yıkama yaptıran araca ilk 2 saat otopark ücretsizdir.");
        doc.Notes.Add("Bütün plakalar ve kişiler örnek veridir.");
        return doc;
    }

    public static ReportDocument PeakHours(PeakHoursReport r)
    {
        var doc = new ReportDocument
        {
            Title = "Yoğunluk Saatleri",
            Subtitle = $"{Date(r.From)} – {Date(r.To)}  ·  haftanın günü ve saate göre ortalama içerideki araç  ·  {r.Capacity} park yeri",
            Organization = Organization,
            Landscape = true,
        };
        var top = r.TopSlots.FirstOrDefault();
        doc.Kpis.Add(new ReportKpi("En yoğun dilim", top?.Text ?? "—", top is null ? null : $"ortalama {top.Inside:0} araç · %{top.Inside * 100 / Math.Max(1, r.Capacity):0} doluluk"));
        doc.Kpis.Add(new ReportKpi("En yoğun saat (ortalama)", $"{r.BusiestAverage:0.0} araç", "bütün günlerin ortalaması"));
        doc.Kpis.Add(new ReportKpi("En sakin saat (ortalama)", $"{r.QuietestAverage:0.0} araç"));
        doc.Kpis.Add(new ReportKpi("Kapasite", $"{r.Capacity} yer"));
        doc.Kpis.Add(new ReportKpi("İlk 5 dilim", string.Join(", ", r.TopSlots.Select(s => $"{s.DayName} {s.Hour:00}")), "en yoğun gün-saat"));

        var (weekday, weekend) = WeekSplit(r);
        doc.Charts.Add(new ReportChart
        {
            Title = "Saatlik ortalama doluluk",
            Subtitle = "Hafta içi ve hafta sonu, içerideki araç",
            Labels = [.. r.Hours.Select(h => h.Hour[..2])],
            Series =
            {
                new ReportSeries("Hafta içi", weekday),
                new ReportSeries("Hafta sonu", weekend, Color: "#F59E0B"),
            },
            Min = 0,
            Max = r.Capacity,
        });
        doc.Charts.Add(new ReportChart
        {
            Title = "Saatlik ortalama giriş",
            Subtitle = "Günde, o saatte giren araç",
            Kind = ChartKind.Bar,
            Labels = [.. r.Hours.Select(h => h.Hour[..2])],
            Series = { new ReportSeries("Giriş", [.. r.Hours.Select(h => h.AverageEntries)], Color: "#14B8A6") },
        });

        doc.Tables.Add(ReportTable.For("Gün × saat (ortalama içerideki araç)", r.Hours)
            .Text("Saat", h => h.Hour, 7)
            .Number("Pzt", h => h.Mon, 1, 7)
            .Number("Sal", h => h.Tue, 1, 7)
            .Number("Çar", h => h.Wed, 1, 7)
            .Number("Per", h => h.Thu, 1, 7)
            .Number("Cum", h => h.Fri, 1, 7)
            .Number("Cmt", h => h.Sat, 1, 7)
            .Number("Paz", h => h.Sun, 1, 7)
            .Number("Ortalama", h => h.Average, 1, 9)
            .Number("Giriş / gün", h => h.AverageEntries, 1, 10)
            .Build());
        doc.Notes.Add("Her saat diliminin ortasında içeride bulunan araçlar sayılır; aynı günün bütün haftalarının ortalaması alınır. Bugün hesaba katılmaz.");
        doc.Notes.Add("Personel ve yıkama bölmesi planlaması için en yoğun dilimlere bakın.");
        return doc;
    }

    public static ReportDocument Forecast(RevenueForecastReport r, int historyShown = 28)
    {
        var doc = new ReportDocument
        {
            Title = "Ciro Tahmini",
            Subtitle = $"{Date(r.Today)} itibarıyla önümüzdeki {r.Days.Count} gün  ·  yöntem: {r.Method}",
            Organization = Organization,
        };
        doc.Kpis.Add(new ReportKpi($"{r.Days.Count} günlük beklenen ciro", Money((decimal)r.ForecastTotal)));
        doc.Kpis.Add(new ReportKpi("Günlük ortalama (tahmin)", Money((decimal)r.ForecastAverage), Change((decimal)r.ForecastAverage, (decimal)r.HistoryAverage, "son dönem ortalamasına göre")));
        doc.Kpis.Add(new ReportKpi($"Son {r.HistoryRevenue.Length} gün ortalaması", Money((decimal)r.HistoryAverage), "gerçekleşen"));
        doc.Kpis.Add(new ReportKpi("Model hatası (MAE)", Money((decimal)r.Mae), r.Mape is { } m ? $"ortalama %{m * 100:0.0} sapma" : "bir gün sonrası tahminde"));

        var (labels, actual, model) = ChartSeries(r, historyShown);
        doc.Charts.Add(new ReportChart
        {
            Title = "Gerçekleşen ve tahmini günlük ciro",
            Subtitle = $"Son {historyShown} gün ve önümüzdeki {r.Days.Count} gün (₺)",
            Labels = labels,
            Series =
            {
                new ReportSeries("Gerçekleşen", actual),
                new ReportSeries("Model / tahmin", model, Dashed: true, Color: "#14B8A6"),
            },
            MarkerIndex = Math.Min(historyShown, r.HistoryRevenue.Length),
            MarkerLabel = "Bugün",
        });
        doc.Tables.Add(ReportTable.For("Günlük tahmin", r.Days)
            .Date("Tarih", d => d.Date)
            .Text("Gün", d => d.DayName, 6)
            .Money("Tahmin", d => (decimal)d.Forecast, 14, total: true)
            .Money("Alt sınır", d => (decimal)d.Lower, 14)
            .Money("Üst sınır", d => (decimal)d.Upper, 14)
            .Number("Beklenen giriş", d => d.ExpectedEntries, 0, 12)
            .Build());
        doc.Notes.Add($"Tahmin son {r.HistoryRevenue.Length} günün günlük cirosundan (otopark + yıkama) kurulur ve haftalık deseni (7 gün) dikkate alır. Yöntem; naif, doğrusal trend, Holt ve Holt-Winters arasından serinin son bölümünü en az hatayla tahmin edene göre seçilir.");
        doc.Notes.Add("Alt ve üst sınır yaklaşık %80 tahmin aralığıdır.");
        return doc;
    }

    /// <summary>Son N günün gerçekleşeni + gelecek günlerin tahmini (grafik serileri).</summary>
    public static (string[] Labels, double[] Actual, double[] Model) ChartSeries(RevenueForecastReport r, int historyShown)
    {
        var h = Math.Min(historyShown, r.HistoryRevenue.Length);
        var start = r.HistoryRevenue.Length - h;
        var total = h + r.Days.Count;
        var labels = new string[total];
        var actual = new double[total];
        var model = new double[total];
        for (var i = 0; i < h; i++)
        {
            labels[i] = r.HistoryDates[start + i].ToString("dd.MM");
            actual[i] = r.HistoryRevenue[start + i];
            model[i] = r.HistoryFitted[start + i];
        }
        for (var i = 0; i < r.Days.Count; i++)
        {
            labels[h + i] = r.Days[i].Date.ToString("dd.MM");
            actual[h + i] = double.NaN;
            model[h + i] = r.Days[i].Forecast;
        }
        return (labels, actual, model);
    }

    /// <summary>Hafta içi (Pzt-Cum) ve hafta sonu (Cmt-Paz) saatlik ortalamaları.</summary>
    public static (double[] Weekday, double[] Weekend) WeekSplit(PeakHoursReport r)
    {
        var weekday = new double[24];
        var weekend = new double[24];
        for (var h = 0; h < 24; h++)
        {
            weekday[h] = Math.Round(Enumerable.Range(0, 5).Average(d => r.Matrix[d, h]), 1);
            weekend[h] = Math.Round((r.Matrix[5, h] + r.Matrix[6, h]) / 2, 1);
        }
        return (weekday, weekend);
    }

    static string Change(decimal now, decimal before, string suffix = "önceki döneme göre")
    {
        if (before == 0) return "önceki dönem verisi yok";
        var change = (double)((now - before) / before);
        return $"{suffix} {(change >= 0 ? "+" : "")}{Percent(change)}";
    }

    static string Change(long now, long before) => Change((decimal)now, before);
}
