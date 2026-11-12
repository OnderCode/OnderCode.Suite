using OnderCode.Reporting;
using static OnderCode.Reporting.ReportFormat;

namespace Eczane.WinForms.Reports;

/// <summary>Uygulama katmanının rapor sonuçlarını biçimden bağımsız <see cref="ReportDocument"/>'e çevirir (Excel, PDF, CSV).</summary>
static class EczaneReports
{
    public const string Organization = "OnderCode Eczane";
    const string Privacy = "Raporlarda T.C. kimlik numarası, telefon ve adres yer almaz. Bütün ilaç, hasta, hastane ve depo adları örnek veridir.";

    static string Period(DateOnly from, DateOnly to) => $"{Date(from)} – {Date(to)}  ·  {to.DayNumber - from.DayNumber + 1} gün";

    public static ReportDocument Sales(SalesReport r)
    {
        var doc = new ReportDocument { Title = "Satış Raporu", Subtitle = Period(r.From, r.To), Organization = Organization };
        doc.Kpis.Add(new ReportKpi("Ciro", Money(r.Total), Change(r.Total, r.PreviousTotal)));
        doc.Kpis.Add(new ReportKpi("Brüt kâr", Money(r.Profit), $"marj {Percent(r.Margin)} · {Change(r.Profit, r.PreviousProfit)}"));
        doc.Kpis.Add(new ReportKpi("İşlem", Integer(r.Count), Change(r.Count, r.PreviousCount)));
        doc.Kpis.Add(new ReportKpi("Reçete", Integer(r.Prescriptions), $"reçete cirosu {Money(r.Rx)}"));
        doc.Kpis.Add(new ReportKpi("Serbest satış", Money(r.Retail), r.Total == 0 ? null : $"cironun {Percent((double)(r.Retail / r.Total))}"));
        doc.Kpis.Add(new ReportKpi("Kurum payı", Money(r.Institution), "SGK + özel sigorta, faturalanacak"));
        doc.Kpis.Add(new ReportKpi("Ortalama sepet", Money(r.AverageBasket)));

        doc.Charts.Add(new ReportChart
        {
            Title = "Günlük ciro",
            Subtitle = "Reçete ve serbest satış (₺)",
            Kind = ChartKind.Bar,
            Labels = [.. r.Days.Select(d => d.Date.ToString("dd.MM"))],
            Series =
            {
                new ReportSeries("Reçete", [.. r.Days.Select(d => (double)d.RxRevenue)]),
                new ReportSeries("Serbest satış", [.. r.Days.Select(d => (double)d.RetailRevenue)], Color: "#8B5CF6"),
            },
        });
        doc.Tables.Add(ReportTable.For("Günlük döküm", r.Days)
            .Date("Tarih", d => d.Date)
            .Text("Gün", d => d.DayName, 6)
            .Integer("Reçete", d => d.Prescriptions, 8, total: true)
            .Integer("Serbest", d => d.Retail, 8, total: true)
            .Money("Reçete cirosu", d => d.RxRevenue, 13, total: true)
            .Money("Serbest ciro", d => d.RetailRevenue, 13, total: true)
            .Money("Hasta payı", d => d.PatientShare, 13, total: true)
            .Money("Kurum payı", d => d.InstitutionShare, 13, total: true)
            .Money("Kâr", d => d.Profit, 12, total: true)
            .Build());
        doc.Tables.Add(ReportTable.For("Ürün gruplarına göre", r.Categories)
            .Text("Grup", c => c.Category, 18)
            .Integer("Kutu", c => c.Boxes, 9, total: true)
            .Money("Ciro", c => c.Revenue, total: true)
            .Money("Kâr", c => c.Profit, total: true)
            .Percent("Marj", c => c.Margin)
            .Percent("Pay", c => c.Share, total: true)
            .Build());
        doc.Tables.Add(ReportTable.For("Kuruma göre reçete", r.Insurances)
            .Text("Kurum", c => c.Category, 18)
            .Integer("Kutu", c => c.Boxes, 9, total: true)
            .Money("Ciro", c => c.Revenue, total: true)
            .Percent("Pay", c => c.Share, total: true)
            .Build());
        doc.Tables.Add(ReportTable.For("Ödeme türleri", r.Payments)
            .Text("Ödeme", p => p.Method, 14)
            .Integer("Adet", p => p.Count, 8, total: true)
            .Money("Tutar", p => p.Amount, total: true)
            .Percent("Pay", p => p.Share, total: true)
            .Build());
        doc.Tables.Add(ReportTable.For("Personele göre satış", r.Sellers)
            .Text("Personel", s => s.Name, 20)
            .Integer("Satış", s => s.Sales, 8, total: true)
            .Money("Ciro", s => s.Revenue, total: true)
            .Money("Ortalama sepet", s => s.AverageBasket)
            .Build());
        doc.Notes.Add("Ciro iadeler düşüldükten sonra hasta payı + kurum payıdır. Kâr, satılan partilerin alış maliyetine göre (FEFO) hesaplanır.");
        doc.Notes.Add("Reçeteli ilaçta hasta katılım payı: ilk 3 kutu 3 ₺, sonrası 1 ₺ + çalışanlarda %20, emeklilerde %10; kronik raporda yüzdelik pay alınmaz.");
        doc.Notes.Add(Privacy);
        return doc;
    }

    public static ReportDocument Hospitals(HospitalReport r)
    {
        var doc = new ReportDocument { Title = "Hastane ve Reçete Kaynağı Raporu", Subtitle = Period(r.From, r.To), Organization = Organization };
        doc.Kpis.Add(new ReportKpi("Reçete", Integer(r.Prescriptions), Change(r.Prescriptions, r.PreviousPrescriptions)));
        doc.Kpis.Add(new ReportKpi("Hasta", Integer(r.Patients)));
        doc.Kpis.Add(new ReportKpi("En çok reçete gelen", r.Leader ?? "—", r.Hospitals.FirstOrDefault() is { } h ? $"payı {Percent(h.Share)}" : null));
        doc.Kpis.Add(new ReportKpi("İlk 3 kurumun payı", Percent(r.TopThreeShare)));
        doc.Kpis.Add(new ReportKpi("En sık branş", r.Branches.FirstOrDefault()?.Name ?? "—", r.Branches.FirstOrDefault() is { } b ? Percent(b.Share) : null));

        doc.Charts.Add(new ReportChart
        {
            Title = "Haftalık reçete · ilk 5 kurum",
            Subtitle = "Kurum başına reçete sayısı",
            Kind = ChartKind.Line,
            Labels = r.WeekLabels,
            Series = { },
        });
        var palette = new[] { "#2563EB", "#8B5CF6", "#14B8A6", "#F59E0B", "#EC4899" };
        for (var i = 0; i < r.TopTrend.Count; i++) doc.Charts[0].Series.Add(new ReportSeries(r.TopTrend[i].Name, r.TopTrend[i].Values, Color: palette[i % palette.Length]));

        doc.Tables.Add(ReportTable.For("Kurumlar (Pareto)", r.Hospitals)
            .Description("Kurumlar reçete cirosuna göre sıralanır; A sınıfı cironun ilk %80'ini getirir.")
            .Text("Kurum", h => h.Hospital, 30)
            .Text("Tür", h => h.Kind, 16)
            .Integer("Reçete", h => h.Prescriptions, 8, total: true)
            .Integer("Önceki dönem", h => h.PreviousPrescriptions, 10, total: true)
            .Integer("Hasta", h => h.Patients, 8)
            .Money("Ciro", h => h.Revenue, total: true)
            .Percent("Pay", h => h.Share, total: true)
            .Percent("Kümülatif", h => h.Cumulative, 1, 10)
            .Text("Sınıf", h => h.Class, 6)
            .Text("En sık branş", h => h.TopBranch, 16)
            .Build());
        doc.Tables.Add(Group("Kurum türleri", r.Kinds, "Tür"));
        doc.Tables.Add(Group("Hekim branşları", r.Branches, "Branş"));
        doc.Tables.Add(Group("Tedavi grupları", r.Groups, "Grup"));
        doc.Tables.Add(Group("Reçete renkleri", r.Colors, "Renk"));
        doc.Notes.Add("Reçetenin kaynağı, reçetenin yazıldığı kurumdur. Ciro hasta payı + kurum payıdır. Karşılaştırma aynı uzunluktaki bir önceki döneme göredir.");
        doc.Notes.Add(Privacy);
        return doc;
    }

    static ReportTable Group(string title, IReadOnlyList<GroupShare> rows, string header) => ReportTable.For(title, rows)
        .Text(header, g => g.Name, 24)
        .Integer("Reçete", g => g.Prescriptions, 9, total: true)
        .Money("Ciro", g => g.Revenue, total: true)
        .Percent("Pay", g => g.Share, total: true)
        .Build();

    public static ReportDocument Products(ProductReport r)
    {
        var doc = new ReportDocument { Title = "Ürün ve Stok Raporu (ABC)", Subtitle = Period(r.From, r.To), Organization = Organization };
        doc.Kpis.Add(new ReportKpi("A sınıfı ürün", Integer(r.ACount), $"ürünlerin {Percent(r.ACountShare)}'i cironun {Percent(r.AShareOfRevenue)}'ini getiriyor"));
        doc.Kpis.Add(new ReportKpi("Stok değeri", Money(r.StockValue), "alış maliyetiyle"));
        doc.Kpis.Add(new ReportKpi("Satılan malın maliyeti", Money(r.CostOfSales)));
        doc.Kpis.Add(new ReportKpi("Stok devir hızı", Number(r.Turnover, 1), $"yıllık · ortalama {r.DaysOfInventory} günlük stok"));
        doc.Kpis.Add(new ReportKpi("Yavaş dönen ürün", Integer(r.SlowMovers.Count), $"bağlı stok {Money(r.SlowMovers.Sum(s => s.StockValue))}"));

        var top = r.Products.Take(15).ToList();
        doc.Charts.Add(new ReportChart
        {
            Title = "En çok ciro getiren 15 ürün",
            Subtitle = "Dönem cirosu (₺)",
            Kind = ChartKind.Bar,
            Labels = [.. top.Select(p => p.Product.Split(' ')[0])],
            Series = { new ReportSeries("Ciro", [.. top.Select(p => (double)p.Revenue)], Color: "#14B8A6") },
        });
        doc.Tables.Add(ReportTable.For("Ürünler (ABC)", r.Products)
            .Text("Ürün", p => p.Product, 30)
            .Text("Grup", p => p.Category, 16)
            .Text("Tedavi grubu", p => p.Group, 16)
            .Integer("Kutu", p => p.Boxes, 8, total: true)
            .Money("Ciro", p => p.Revenue, total: true)
            .Money("Kâr", p => p.Profit, total: true)
            .Percent("Marj", p => p.Margin)
            .Percent("Pay", p => p.Share, total: true)
            .Percent("Kümülatif", p => p.Cumulative, 1, 10)
            .Text("Sınıf", p => p.Class, 6)
            .Integer("Stok", p => p.Stock, 8, total: true)
            .Build());
        doc.Tables.Add(ReportTable.For("Yavaş dönen ürünler", r.SlowMovers)
            .Description("Stokta olup dönem içinde hiç satılmayan ürünler.")
            .Text("Ürün", s => s.Product, 30)
            .Text("Grup", s => s.Category, 16)
            .Integer("Stok", s => s.Stock, 8, total: true)
            .Money("Stok değeri", s => s.StockValue, total: true)
            .Text("Son satış", s => s.LastSale is { } d ? ReportFormat.DateTime(d) : "hiç", 16)
            .Text("En yakın SKT", s => s.NearestExpiry is { } e ? Date(e) : "", 12)
            .Build());
        doc.Tables.Add(ReportTable.For("Gruplara göre stok", r.StockByCategory)
            .Text("Grup", s => s.Category, 18)
            .Integer("Ürün", s => s.Products, 8, total: true)
            .Integer("Kutu", s => s.Boxes, 9, total: true)
            .Money("Değer", s => s.Value, total: true)
            .Percent("Pay", s => s.Share, total: true)
            .Build());
        doc.Notes.Add("ABC: ürünler ciroya göre sıralanır; cironun ilk %80'i A, sonraki %15'i B, kalan C sınıfıdır. Stok değeri parti alış maliyetiyle hesaplanır.");
        doc.Notes.Add(Privacy);
        return doc;
    }

    public static ReportDocument Suppliers(SupplierReport r)
    {
        var doc = new ReportDocument { Title = "Ecza Deposu Raporu", Subtitle = Period(r.From, r.To), Organization = Organization };
        doc.Kpis.Add(new ReportKpi("Alış", Money(r.TotalPurchases)));
        doc.Kpis.Add(new ReportKpi("Mal fazlası", Money(r.FreeGoods), r.TotalPurchases == 0 ? null : $"alışın {Percent((double)(r.FreeGoods / r.TotalPurchases))}'i kadar bedelsiz ürün"));
        doc.Kpis.Add(new ReportKpi("Ödenen", Money(r.Paid)));
        doc.Kpis.Add(new ReportKpi("Güncel borç", Money(r.Balance), r.Overdue > 0 ? $"{Money(r.Overdue)} vadesi geçmiş" : "vadesi geçmiş borç yok"));

        var palette = new[] { "#2563EB", "#8B5CF6", "#14B8A6", "#F59E0B", "#EC4899" };
        var chart = new ReportChart { Title = "Haftalık alış", Subtitle = "Depo başına (₺)", Kind = ChartKind.Bar, Labels = r.WeekLabels };
        for (var i = 0; i < r.WeeklyPurchases.Count; i++) chart.Series.Add(new ReportSeries(r.WeeklyPurchases[i].Name, r.WeeklyPurchases[i].Values, Color: palette[i % palette.Length]));
        doc.Charts.Add(chart);
        doc.Tables.Add(ReportTable.For("Depolar", r.Wholesalers)
            .Text("Depo", w => w.Wholesaler, 18)
            .Integer("Sipariş", w => w.Orders, 8, total: true)
            .Integer("Kutu", w => w.Boxes, 8, total: true)
            .Integer("MF kutu", w => w.FreeBoxes, 8, total: true)
            .Percent("MF oranı", w => w.FreeShare)
            .Money("Alış", w => w.Purchases, total: true)
            .Money("MF değeri", w => w.FreeGoodsValue, total: true)
            .Number("Ort. teslim (saat)", w => w.AverageLeadHours, 1, 12)
            .Money("Ödenen", w => w.Paid, total: true)
            .Money("Bakiye", w => w.Balance, total: true)
            .Money("Vadesi geçen", w => w.Overdue, total: true)
            .Percent("Pay", w => w.Share, total: true)
            .Build());
        doc.Notes.Add("Mal fazlası (MF): kampanya eşiğindeki alımda bedelsiz gelen kutular; değeri alış birim fiyatıyla hesaplanır. Ödemeler en eski vadeli borçtan başlayarak kapatılır.");
        doc.Notes.Add(Privacy);
        return doc;
    }

    public static ReportDocument Staff(StaffReport r)
    {
        var doc = new ReportDocument { Title = "Personel ve Nöbet Raporu", Subtitle = $"{r.Year} yılı", Organization = Organization };
        doc.Kpis.Add(new ReportKpi("Nöbet günü", Integer(r.Duties), r.Unstaffed > 0 ? $"{r.Unstaffed} nöbet atanmamış" : "hepsi atanmış"));
        doc.Kpis.Add(new ReportKpi("Nöbet dengesi", Percent(r.Fairness), "eczacılar arası puan dengesi (100% = eşit)"));
        doc.Kpis.Add(new ReportKpi("Personel gideri", Money(r.PersonnelCost), "yıllık brüt"));
        doc.Kpis.Add(new ReportKpi("Çalışan", Integer(r.Staff.Count)));

        var palette = new[] { "#2563EB", "#8B5CF6", "#14B8A6", "#F59E0B", "#EC4899", "#64748B" };
        var chart = new ReportChart { Title = "Aylık nöbet sayısı", Subtitle = "Personel başına", Kind = ChartKind.Bar, Labels = r.MonthLabels };
        for (var i = 0; i < r.DutiesByMonth.Count; i++) chart.Series.Add(new ReportSeries(r.DutiesByMonth[i].Name, r.DutiesByMonth[i].Values, Color: palette[i % palette.Length]));
        doc.Charts.Add(chart);
        doc.Tables.Add(ReportTable.For("Personel", r.Staff)
            .Text("Personel", s => s.Name, 20)
            .Text("Görev", s => s.Role, 18)
            .Integer("Nöbet", s => s.Duties, 7, total: true)
            .Integer("Puan", s => s.Points, 7, total: true)
            .Integer("Pazar/bayram", s => s.Sundays, 10, total: true)
            .Integer("İzin hakkı", s => s.LeaveEntitlement, 9)
            .Integer("Kullanılan", s => s.LeaveUsed, 9, total: true)
            .Integer("Kalan", s => s.LeaveLeft, 7, total: true)
            .Integer("Rapor", s => s.SickDays, 7, total: true)
            .Integer("Satış", s => s.Sales, 8, total: true)
            .Money("Ciro", s => s.Revenue, total: true)
            .Money("Yıllık brüt", s => s.AnnualCost, total: true)
            .Build());
        doc.Notes.Add("Nöbet puanı: gece 1, pazar 2, bayram 3. Otomatik plan yıl boyu puanı en düşük olanı seçer; izinliler ve son iki gün içinde nöbet tutanlar atlanır.");
        doc.Notes.Add("Yıllık izin hakkı kıdeme göre: 1-5 yıl 14, 5-15 yıl 20, 15 yıl ve üstü 26 iş günü. Personel kartında kimlik ve iletişim bilgisi tutulmaz.");
        return doc;
    }

    public static ReportDocument Forecast(RevenueForecastReport r, int historyShown = 28)
    {
        var doc = new ReportDocument { Title = "Ciro Tahmini", Subtitle = $"{Date(r.Today)} itibarıyla önümüzdeki {r.Days.Count} gün  ·  yöntem: {r.Method}", Organization = Organization };
        doc.Kpis.Add(new ReportKpi($"{r.Days.Count} günlük beklenen ciro", Money((decimal)r.ForecastTotal)));
        doc.Kpis.Add(new ReportKpi("Günlük ortalama (tahmin)", Money((decimal)r.ForecastAverage), Change((decimal)r.ForecastAverage, (decimal)r.HistoryAverage, "son dönem ortalamasına göre")));
        doc.Kpis.Add(new ReportKpi($"Son {r.HistoryRevenue.Length} gün ortalaması", Money((decimal)r.HistoryAverage), "gerçekleşen"));
        doc.Kpis.Add(new ReportKpi("Model hatası (MAE)", Money((decimal)r.Mae), r.Mape is { } m ? $"ortalama %{m * 100:0.0} sapma" : null));
        var (labels, actual, model) = ChartSeries(r, historyShown);
        doc.Charts.Add(new ReportChart
        {
            Title = "Gerçekleşen ve tahmini günlük ciro",
            Subtitle = $"Son {historyShown} gün ve önümüzdeki {r.Days.Count} gün (₺)",
            Labels = labels,
            Series = { new ReportSeries("Gerçekleşen", actual), new ReportSeries("Model / tahmin", model, Dashed: true, Color: "#14B8A6") },
            MarkerIndex = Math.Min(historyShown, r.HistoryRevenue.Length),
            MarkerLabel = "Bugün",
        });
        doc.Tables.Add(ReportTable.For("Günlük tahmin", r.Days)
            .Date("Tarih", d => d.Date)
            .Text("Gün", d => d.DayName, 6)
            .Money("Tahmin", d => (decimal)d.Forecast, 14, total: true)
            .Money("Alt sınır", d => (decimal)d.Lower, 14)
            .Money("Üst sınır", d => (decimal)d.Upper, 14)
            .Number("Beklenen reçete", d => d.Prescriptions, 0, 12)
            .Build());
        doc.Notes.Add($"Tahmin son {r.HistoryRevenue.Length} günün cirosundan kurulur Holt-Winters yöntemiyle kurulur; haftalık deseni (pazar kapalı, pazartesi yoğun) ve eğilimi dikkate alır. Parametreler geçmişteki bir gün sonrası tahmin hatasını en aza indirecek şekilde seçilir.");
        doc.Notes.Add("Alt ve üst sınır yaklaşık %80 tahmin aralığıdır.");
        return doc;
    }

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
            model[i] = r.Fitted[start + i];
        }
        for (var i = 0; i < r.Days.Count; i++)
        {
            labels[h + i] = r.Days[i].Date.ToString("dd.MM");
            actual[h + i] = double.NaN;
            model[h + i] = r.Days[i].Forecast;
        }
        return (labels, actual, model);
    }

    public static ReportDocument Assets(IReadOnlyList<AssetRow> rows, IReadOnlyList<MaintenanceRow> history, int year)
    {
        var active = rows.Where(a => a.Status != AssetStatus.Retired).ToList();
        var doc = new ReportDocument { Title = "Demirbaş ve Amortisman Raporu", Subtitle = $"{year} yılı  ·  {active.Count} demirbaş", Organization = Organization };
        doc.Kpis.Add(new ReportKpi("Alış bedeli", Money(active.Sum(a => a.Cost))));
        doc.Kpis.Add(new ReportKpi("Birikmiş amortisman", Money(active.Sum(a => a.Accumulated))));
        doc.Kpis.Add(new ReportKpi("Net defter değeri", Money(active.Sum(a => a.BookValue))));
        doc.Kpis.Add(new ReportKpi($"{year} amortismanı", Money(active.Sum(a => a.ThisYear)), "doğrusal yöntem"));
        var yearMaint = history.Where(m => m.Date.Year == year).ToList();
        doc.Kpis.Add(new ReportKpi("Bakım / onarım gideri", Money(yearMaint.Sum(m => m.Cost)), $"{yearMaint.Count} işlem"));

        var groups = active.GroupBy(a => a.CategoryText).Select(g => (Name: g.Key, Cost: g.Sum(a => a.Cost), Book: g.Sum(a => a.BookValue))).OrderByDescending(g => g.Cost).ToList();
        doc.Charts.Add(new ReportChart
        {
            Title = "Gruplara göre değer",
            Subtitle = "Alış bedeli ve net defter değeri (₺)",
            Kind = ChartKind.Bar,
            Labels = [.. groups.Select(g => g.Name)],
            Series = { new ReportSeries("Alış bedeli", [.. groups.Select(g => (double)g.Cost)]), new ReportSeries("Net değer", [.. groups.Select(g => (double)g.Book)], Color: "#14B8A6") },
        });
        doc.Tables.Add(ReportTable.For("Demirbaş listesi", rows)
            .Text("Kod", a => a.Code, 9)
            .Text("Ad", a => a.Name, 26)
            .Text("Grup", a => a.CategoryText, 16)
            .Text("Konum", a => a.Location, 12)
            .Text("Durum", a => a.StatusText, 10)
            .Date("Alış", a => a.PurchaseDate)
            .Integer("Ömür", a => a.UsefulLifeYears, 6)
            .Money("Bedel", a => a.Cost, total: true)
            .Money("Birikmiş", a => a.Accumulated, total: true)
            .Money("Net değer", a => a.BookValue, total: true)
            .Money($"{year} amort.", a => a.ThisYear, total: true)
            .Text("Sonraki bakım", a => a.NextMaintenance is { } d ? Date(d) : "", 12)
            .Build());
        doc.Tables.Add(ReportTable.For("Bakım ve onarım geçmişi", history)
            .Date("Tarih", m => m.Date)
            .Text("Demirbaş", m => m.Asset, 26)
            .Text("İşlem", m => m.KindText, 14)
            .Money("Maliyet", m => m.Cost, total: true)
            .Text("Açıklama", m => m.Note, 30)
            .Build());
        doc.Notes.Add("Amortisman aylık doğrusal hesaplanır: (bedel − hurda değeri) / (faydalı ömür × 12). Hurdaya ayrılan demirbaşlar toplamlara katılmaz.");
        return doc;
    }

    static string Change(decimal now, decimal before, string suffix = "önceki döneme göre")
    {
        if (before == 0) return "önceki dönem verisi yok";
        var change = (double)((now - before) / before);
        return $"{suffix} {(change >= 0 ? "+" : "")}{Percent(change)}";
    }

    static string Change(long now, long before) => Change((decimal)now, before);
}
