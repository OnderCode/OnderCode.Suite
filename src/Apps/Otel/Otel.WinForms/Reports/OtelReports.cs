using OnderCode.Reporting;
using Otel.Application;
using static OnderCode.Reporting.ReportFormat;

namespace Otel.WinForms.Reports;

/// <summary>Uygulama katmanının rapor sonuçlarını biçimden bağımsız <see cref="ReportDocument"/>'e çevirir (Excel, PDF, CSV).</summary>
static class OtelReports
{
    public const string Hotel = "OnderCode Otel";

    public static ReportDocument Revenue(RevenueReport r)
    {
        var doc = new ReportDocument
        {
            Title = "Gelir ve Doluluk Raporu",
            Subtitle = $"{Date(r.From)} – {Date(r.To)}  ·  {r.Days.Count} gün  ·  {r.RoomCount} oda",
            Organization = Hotel,
        };
        doc.Kpis.Add(new ReportKpi("Doluluk (OCC)", Percent((double)r.Current.Occupancy), Points(r.Current.Occupancy, r.Previous.Occupancy)));
        doc.Kpis.Add(new ReportKpi("ADR · ortalama gecelik fiyat", Money(r.Current.Adr), Change(r.Current.Adr, r.Previous.Adr)));
        doc.Kpis.Add(new ReportKpi("RevPAR · oda başına gelir", Money(r.Current.RevPar), Change(r.Current.RevPar, r.Previous.RevPar)));
        doc.Kpis.Add(new ReportKpi("Toplam gelir", Money(r.TotalRevenue), Change(r.TotalRevenue, r.PreviousTotalRevenue)));
        doc.Kpis.Add(new ReportKpi("Satılan oda-gece", Integer(r.Current.SoldNights), $"satılabilir {Integer(r.Current.AvailableNights)}"));
        doc.Kpis.Add(new ReportKpi("Oda geliri", Money(r.Current.RoomRevenue)));
        doc.Kpis.Add(new ReportKpi("Ekstra harcamalar", Money(r.ExtraRevenue), "folyo: restoran, spa, minibar…"));
        doc.Kpis.Add(new ReportKpi("Giriş · ortalama konaklama", $"{r.Arrivals} · {Number(r.AverageStay, 1)} gece", $"{r.Cancellations} iptal"));

        doc.Charts.Add(new ReportChart
        {
            Title = "Günlük doluluk",
            Subtitle = "Satılan oda / toplam oda (%)",
            Labels = [.. r.Days.Select(d => d.Date.ToString("dd.MM"))],
            Series = { new ReportSeries("Doluluk", [.. r.Days.Select(d => d.Occupancy * 100)]) },
            Min = 0,
            Max = 100,
            ValueFormat = v => "%" + v.ToString("0", Turkish),
        });
        doc.Charts.Add(new ReportChart
        {
            Title = "Günlük oda geliri",
            Subtitle = "Konaklama geliri gecelere eşit dağıtılarak (₺)",
            Kind = ChartKind.Bar,
            Labels = [.. r.Days.Select(d => d.Date.ToString("dd.MM"))],
            Series = { new ReportSeries("Oda geliri", [.. r.Days.Select(d => (double)d.RoomRevenue)], Color: "#14B8A6") },
        });

        doc.Tables.Add(ReportTable.For("Günlük döküm", r.Days)
            .Date("Tarih", d => d.Date)
            .Text("Gün", d => d.DayName, 6)
            .Integer("Satılan oda", d => d.SoldRooms, 10, total: true)
            .Integer("Satılabilir", d => d.AvailableRooms, 10, total: true)
            .Percent("Doluluk", d => d.Occupancy)
            .Money("Oda geliri", d => d.RoomRevenue, total: true)
            .Money("ADR", d => d.Adr, 12)
            .Money("RevPAR", d => d.RevPar, 12)
            .Build());
        doc.Tables.Add(ReportTable.For("Oda tipine göre", r.RoomTypes)
            .Text("Oda tipi", t => t.RoomType, 18)
            .Integer("Oda", t => t.Rooms, 7, total: true)
            .Integer("Satılan gece", t => t.SoldNights, 11, total: true)
            .Percent("Doluluk", t => t.Occupancy)
            .Money("Gelir", t => t.Revenue, total: true)
            .Money("ADR", t => t.Adr, 12)
            .Percent("Gelir payı", t => t.Share, total: true)
            .Build());
        doc.Tables.Add(ReportTable.For("Ekstra harcamalar", r.Charges)
            .Text("Kategori", c => c.Category, 18)
            .Integer("Adet", c => c.Count, 8, total: true)
            .Money("Tutar", c => c.Amount, total: true)
            .Percent("Pay", c => c.Share, total: true)
            .Build());

        doc.Notes.Add("OCC (doluluk) = satılan oda-gece / satılabilir oda-gece. ADR = oda geliri / satılan oda-gece. RevPAR = oda geliri / satılabilir oda-gece = OCC × ADR.");
        doc.Notes.Add("Konaklama geliri gecelere eşit dağıtılır; iptal edilen rezervasyonlar hesaba katılmaz. Karşılaştırmalar aynı uzunluktaki bir önceki döneme göredir.");
        doc.Notes.Add("Bütün misafirler ve rezervasyonlar örnek veridir.");
        return doc;
    }

    public static ReportDocument GuestValue(GuestParetoReport r)
    {
        var doc = new ReportDocument
        {
            Title = "Misafir Değeri Raporu (ABC)",
            Subtitle = $"Girişi {Date(r.From)} – {Date(r.To)} arasında olan konaklamalar  ·  {r.GuestCount} misafir",
            Organization = Hotel,
            Landscape = true,
        };
        doc.Kpis.Add(new ReportKpi("Misafir", Integer(r.GuestCount), $"A {r.CountA}  ·  B {r.CountB}  ·  C {r.CountC}"));
        doc.Kpis.Add(new ReportKpi("Toplam gelir", Money(r.Total), "oda + folyo harcamaları"));
        doc.Kpis.Add(new ReportKpi("A sınıfı", $"{r.CountA} misafir", $"misafirlerin {Percent(r.CountShareA)}'i, gelirin {Percent(r.ValueShareA)}'i"));
        doc.Kpis.Add(new ReportKpi("Ortalama misafir değeri", Money(r.GuestCount == 0 ? 0 : r.Total / r.GuestCount)));
        doc.Kpis.Add(new ReportKpi("B ve C sınıfı payı", $"{Percent(r.ValueShareB)} · {Percent(r.ValueShareC)}", "gelir payı"));

        var n = r.Guests.Count;
        doc.Charts.Add(new ReportChart
        {
            Title = "Gelir yoğunlaşması (Pareto eğrisi)",
            Subtitle = "En değerli misafirden başlayarak kümülatif gelir payı (%)",
            Labels = [.. r.Guests.Select(g => g.Rank.ToString(Turkish))],
            Series =
            {
                new ReportSeries("Kümülatif gelir payı", [.. r.Guests.Select(g => g.Cumulative * 100)]),
                new ReportSeries("Eşit dağılım", [.. r.Guests.Select((_, i) => (i + 1) * 100.0 / n)], Dashed: true, Color: "#94A3B8"),
            },
            Min = 0,
            Max = 100,
            MarkerIndex = r.CountA > 0 ? r.CountA - 1 : null,
            MarkerLabel = "A sınıfı sınırı",
            ValueFormat = v => "%" + v.ToString("0", Turkish),
        });

        doc.Tables.Add(ReportTable.For("Misafirler", r.Guests)
            .Integer("Sıra", g => g.Rank, 5)
            .Text("Misafir", g => g.GuestName, 20)
            .Text("Seviye", g => g.TierText, 8)
            .Integer("Konaklama", g => g.Stays, 9, total: true)
            .Integer("Gece", g => g.Nights, 6, total: true)
            .Money("Oda geliri", g => g.RoomRevenue, 13, total: true)
            .Money("Harcama", g => g.ExtraRevenue, 12, total: true)
            .Money("Toplam", g => g.Total, 13, total: true)
            .Percent("Pay", g => g.Share, 2, 7, total: true)
            .Percent("Kümülatif", g => g.Cumulative, 1, 9)
            .Text("Sınıf", g => g.Class, 5)
            .Build());

        doc.Notes.Add("A sınıfı: gelirin ilk %80'ini oluşturan misafirler. B: sonraki %15. C: kalan %5. Sınırı aşan misafir üst sınıfta sayılır.");
        doc.Notes.Add("Yalnızca gerçekleşen (giriş yapılmış) konaklamalar; oda ücreti ve folyoya yazılan harcamalar dahildir.");
        return doc;
    }

    public static ReportDocument Forecast(OccupancyForecastReport r, int historyShown = 28)
    {
        var doc = new ReportDocument
        {
            Title = "Doluluk Tahmini",
            Subtitle = $"{Date(r.Today)} itibarıyla önümüzdeki {r.Days.Count} gün  ·  yöntem: {r.Method}",
            Organization = Hotel,
        };
        doc.Kpis.Add(new ReportKpi("Beklenen ortalama doluluk", Percent(r.ForecastAverage), $"≈ {Math.Round(r.ForecastAverage * r.RoomCount):0} dolu oda / gün"));
        doc.Kpis.Add(new ReportKpi("Kesin rezervasyon (OTB)", Percent(r.OnTheBooksAverage), "bugün itibarıyla satılmış"));
        doc.Kpis.Add(new ReportKpi($"Son {r.HistoryActual.Length} gün ortalaması", Percent(r.HistoryAverage), "gerçekleşen"));
        doc.Kpis.Add(new ReportKpi("Model hatası (MAE)", $"{Number(r.Mae * 100, 1)} puan", "bir gün sonrası tahminde"));

        var (labels, actual, model, booked) = ChartSeries(r, historyShown);
        doc.Charts.Add(new ReportChart
        {
            Title = "Gerçekleşen ve beklenen doluluk",
            Subtitle = $"Son {historyShown} gün ve önümüzdeki {r.Days.Count} gün (%)",
            Labels = labels,
            Series =
            {
                new ReportSeries("Gerçekleşen", actual),
                new ReportSeries("Kesin rezervasyon", booked, Color: "#F59E0B"),
                new ReportSeries("Model / tahmin", model, Dashed: true, Color: "#14B8A6"),
            },
            Min = 0,
            Max = 100,
            MarkerIndex = Math.Min(historyShown, r.HistoryActual.Length),
            MarkerLabel = "Bugün",
            ValueFormat = v => "%" + v.ToString("0", Turkish),
        });

        doc.Tables.Add(ReportTable.For("Günlük tahmin", r.Days)
            .Date("Tarih", d => d.Date)
            .Text("Gün", d => d.DayName, 6)
            .Percent("Kesin rezervasyon", d => d.OnTheBooks, 1, 13)
            .Percent("Tahmin", d => d.Forecast, 1, 9)
            .Percent("Alt sınır", d => d.Lower, 1, 9)
            .Percent("Üst sınır", d => d.Upper, 1, 9)
            .Percent("Beklenen", d => d.Expected, 1, 9)
            .Integer("Beklenen dolu oda", d => d.ExpectedRooms, 13)
            .Build());

        doc.Notes.Add($"Tahmin son {r.HistoryActual.Length} günün gerçekleşen doluluğundan kurulur ve haftalık deseni (7 gün) dikkate alır. Yöntem; naif, doğrusal trend, Holt ve Holt-Winters arasından, serinin son bölümünü en az hatayla tahmin edene göre seçilir.");
        doc.Notes.Add("Alt ve üst sınır yaklaşık %80 tahmin aralığıdır. Beklenen doluluk, kesin rezervasyon ile tahminin büyüğüdür: satılmış oda geri gelmez, tahmin yalnızca ek talebi gösterir.");
        return doc;
    }

    /// <summary>Grafik için son N günün gerçekleşeni + gelecek günlerin kesin rezervasyonu ve model eğrisi (yüzde).</summary>
    public static (string[] Labels, double[] Actual, double[] Model, double[] Booked) ChartSeries(OccupancyForecastReport r, int historyShown)
    {
        var h = Math.Min(historyShown, r.HistoryActual.Length);
        var start = r.HistoryActual.Length - h;
        var total = h + r.Days.Count;
        var labels = new string[total];
        var actual = new double[total];
        var model = new double[total];
        var booked = new double[total];
        for (var i = 0; i < h; i++)
        {
            labels[i] = r.HistoryDates[start + i].ToString("dd.MM");
            actual[i] = r.HistoryActual[start + i] * 100;
            model[i] = r.HistoryFitted[start + i] * 100;
            booked[i] = double.NaN;
        }
        for (var i = 0; i < r.Days.Count; i++)
        {
            var d = r.Days[i];
            labels[h + i] = d.Date.ToString("dd.MM");
            actual[h + i] = double.NaN;
            model[h + i] = d.Forecast * 100;
            booked[h + i] = d.OnTheBooks * 100;
        }
        return (labels, actual, model, booked);
    }

    static string Points(decimal now, decimal before)
    {
        var diff = (double)(now - before) * 100;
        return $"önceki döneme göre {(diff >= 0 ? "+" : "-")}{Math.Abs(diff).ToString("0.0", Turkish)} puan";
    }

    static string Change(decimal now, decimal before)
    {
        if (before == 0) return "önceki dönem verisi yok";
        var change = (double)((now - before) / before);
        return $"önceki döneme göre {(change >= 0 ? "+" : "")}{Percent(change)}";
    }
}
