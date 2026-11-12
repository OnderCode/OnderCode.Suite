using FluentValidation;
using OnderCode.Analytics;
using OnderCode.Application.Messaging;
using OnderCode.Application.Results;
using OnderCode.Kernel.Persistence;
using OnderCode.Kernel.Specifications;
using Otopark.Domain;

namespace Otopark.Application;

// ---------- Gelir raporu ----------

public sealed record DailyRow(DateOnly Date, string DayName, long Entries, long Exits, decimal ParkingRevenue, decimal WashRevenue, double AverageStayMinutes, long PeakInside)
{
    public decimal Total => ParkingRevenue + WashRevenue;
}

public sealed record VehicleClassRow(string Vehicle, long Sessions, decimal Revenue, double AverageStayMinutes, double Share);

public sealed record PackageRow(string Package, long Orders, decimal Revenue, double Share, double Cumulative, string Class);

public sealed record PaymentRow(string Method, long Count, decimal Amount, double Share);

public sealed record RevenueReport(
    DateOnly From, DateOnly To, int Capacity,
    decimal Parking, decimal Wash, decimal PreviousParking, decimal PreviousWash,
    long Sessions, long PreviousSessions, double AverageStayMinutes, double PreviousAverageStayMinutes,
    long SubscriberVisits, long WashOrders,
    IReadOnlyList<DailyRow> Days, IReadOnlyList<VehicleClassRow> Vehicles, IReadOnlyList<PackageRow> Packages, IReadOnlyList<PaymentRow> Payments)
{
    public decimal Total => Parking + Wash;
    public decimal PreviousTotal => PreviousParking + PreviousWash;

    /// <summary>Ücretli araç başına ortalama otopark geliri.</summary>
    public decimal AveragePerSession => Sessions - SubscriberVisits <= 0 ? 0 : Parking / (Sessions - SubscriberVisits);
}

/// <summary>
/// [From, To] dönemi (iki uç dahil). Otopark geliri çıkış gününe, yıkama geliri teslim gününe yazılır.
/// Karşılaştırma aynı uzunluktaki önceki dönemle yapılır. Yıkama paketleri gelire göre Pareto (ABC) sınıflanır.
/// </summary>
public sealed record GetRevenueReport(DateOnly From, DateOnly To) : IQuery<RevenueReport>;

public sealed class GetRevenueReportValidator : AbstractValidator<GetRevenueReport>
{
    public GetRevenueReportValidator()
    {
        RuleFor(x => x.To).GreaterThanOrEqualTo(x => x.From).WithMessage("Bitiş tarihi başlangıçtan önce olamaz.");
        RuleFor(x => x).Must(x => x.To.DayNumber - x.From.DayNumber < 366).WithName("Dönem").WithMessage("Dönem en çok bir yıl olabilir.");
    }
}

public sealed class GetRevenueReportHandler(IUnitOfWorkFactory uowFactory, TimeProvider time) : IQueryHandler<GetRevenueReport, RevenueReport>
{
    public async Task<Result<RevenueReport>> HandleAsync(GetRevenueReport q, CancellationToken cancellationToken)
    {
        var now = time.Now();
        var length = q.To.DayNumber - q.From.DayNumber + 1;
        var from = q.From.Start();
        var end = q.To.AddDays(1).Start();
        var prevFrom = from.AddDays(-length);

        await using var uow = uowFactory.Create();
        var capacity = await uow.Repository<ParkingSpot>().CountAsync(cancellationToken: cancellationToken);
        var sessions = await uow.Repository<ParkingSession>().ListAsync(
            new Specification<ParkingSession>().Where(s => s.EntryAt < end && (s.ExitAt == null || s.ExitAt >= prevFrom)),
            s => new { s.EntryAt, s.ExitAt, s.Vehicle, s.ParkingFee, s.WashFee, s.Payment, s.SubscriptionId },
            cancellationToken);
        var washes = await uow.Repository<WashOrder>().ListAsync(
            new Specification<WashOrder>().Where(w => w.Status == WashStatus.Delivered && w.DeliveredAt >= prevFrom && w.DeliveredAt < end),
            w => new { w.DeliveredAt, w.Price, Package = w.Package.Name, w.Package.SortOrder },
            cancellationToken);

        var closed = sessions.Where(s => s.ExitAt >= from && s.ExitAt < end).ToList();
        var prevClosed = sessions.Where(s => s.ExitAt >= prevFrom && s.ExitAt < from).ToList();
        var curWash = washes.Where(w => w.DeliveredAt >= from).ToList();
        var prevWash = washes.Where(w => w.DeliveredAt < from).ToList();
        var spans = sessions.Select(s => new StaySpan(s.EntryAt, s.ExitAt, s.Vehicle)).ToList();

        var days = Enumerable.Range(0, length).Select(i =>
        {
            var d = q.From.AddDays(i);
            var start = d.Start();
            var stop = start.AddDays(1);
            var exits = closed.Where(s => s.ExitAt >= start && s.ExitAt < stop).ToList();
            return new DailyRow(d, Texts.DayName(d),
                sessions.Count(s => s.EntryAt >= start && s.EntryAt < stop),
                exits.Count,
                exits.Sum(s => s.ParkingFee),
                curWash.Where(w => w.DeliveredAt >= start && w.DeliveredAt < stop).Sum(w => w.Price),
                exits.Count == 0 ? 0 : Math.Round(exits.Average(s => (s.ExitAt!.Value - s.EntryAt).TotalMinutes), 0),
                LotKpi.PeakConcurrent(start, stop < now ? stop : now, spans, now));
        }).ToList();

        var parking = closed.Sum(s => s.ParkingFee);
        var vehicles = Enum.GetValues<VehicleClass>().Select(v =>
        {
            var list = closed.Where(s => s.Vehicle == v).ToList();
            var revenue = list.Sum(s => s.ParkingFee);
            return new VehicleClassRow(Texts.Of(v), list.Count, revenue,
                list.Count == 0 ? 0 : Math.Round(list.Average(s => (s.ExitAt!.Value - s.EntryAt).TotalMinutes), 0),
                parking == 0 ? 0 : (double)(revenue / parking));
        }).ToList();

        var byPackage = curWash.GroupBy(w => w.Package).Select(g => (Name: g.Key, Count: g.Count(), Revenue: g.Sum(w => w.Price))).ToList();
        var pareto = Pareto.Analyze(byPackage, p => p.Revenue);
        var packages = pareto.Items.Select(i => new PackageRow(i.Item.Name, i.Item.Count, i.Value, i.Share, i.Cumulative, i.Class.ToString())).ToList();

        var paid = closed.Sum(s => s.ParkingFee + s.WashFee);
        var payments = closed.GroupBy(s => s.Payment ?? PaymentMethod.Cash).OrderBy(g => g.Key)
            .Select(g => new PaymentRow(Texts.Of(g.Key), g.Count(), g.Sum(s => s.ParkingFee + s.WashFee), paid == 0 ? 0 : (double)(g.Sum(s => s.ParkingFee + s.WashFee) / paid)))
            .ToList();

        static double AvgStay<T>(IReadOnlyCollection<T> list, Func<T, double> minutes) => list.Count == 0 ? 0 : list.Average(minutes);

        return new RevenueReport(q.From, q.To, capacity,
            parking, curWash.Sum(w => w.Price), prevClosed.Sum(s => s.ParkingFee), prevWash.Sum(w => w.Price),
            closed.Count, prevClosed.Count,
            AvgStay(closed, s => (s.ExitAt!.Value - s.EntryAt).TotalMinutes), AvgStay(prevClosed, s => (s.ExitAt!.Value - s.EntryAt).TotalMinutes),
            closed.Count(s => s.SubscriptionId != null), curWash.Count,
            days, vehicles, packages, payments);
    }
}

// ---------- Yoğunluk saatleri ----------

public sealed record HourRow(string Hour, double Mon, double Tue, double Wed, double Thu, double Fri, double Sat, double Sun, double Average, double AverageEntries);

public sealed record PeakSlot(string DayName, int Hour, double Inside)
{
    public string Text => $"{DayName} {Hour:00}:00-{Hour + 1:00}:00";
}

public sealed record PeakHoursReport(DateOnly From, DateOnly To, int Capacity, IReadOnlyList<HourRow> Hours, IReadOnlyList<PeakSlot> TopSlots, double BusiestAverage, double QuietestAverage)
{
    /// <summary>Haftanın günü (Pzt=0) × saat ortalama içerideki araç.</summary>
    public double[,] Matrix { get; init; } = new double[7, 24];
}

/// <summary>Haftanın her günü ve her saati için ortalama içerideki araç sayısı ve saatlik giriş; en yoğun dilimler.</summary>
public sealed record GetPeakHours(DateOnly From, DateOnly To) : IQuery<PeakHoursReport>;

public sealed class GetPeakHoursValidator : AbstractValidator<GetPeakHours>
{
    public GetPeakHoursValidator() => RuleFor(x => x.To).GreaterThanOrEqualTo(x => x.From).WithMessage("Bitiş tarihi başlangıçtan önce olamaz.");
}

public sealed class GetPeakHoursHandler(IUnitOfWorkFactory uowFactory, TimeProvider time) : IQueryHandler<GetPeakHours, PeakHoursReport>
{
    public async Task<Result<PeakHoursReport>> HandleAsync(GetPeakHours q, CancellationToken cancellationToken)
    {
        var now = time.Now();
        var from = q.From.Start();
        var end = q.To.AddDays(1).Start();
        if (end > now) end = now.Date; // yarım günü ortalamaya katma
        await using var uow = uowFactory.Create();
        var capacity = await uow.Repository<ParkingSpot>().CountAsync(cancellationToken: cancellationToken);
        var rows = await uow.Repository<ParkingSession>().ListAsync(
            new Specification<ParkingSession>().Where(s => s.EntryAt < end && (s.ExitAt == null || s.ExitAt >= from)),
            s => new { s.EntryAt, s.ExitAt, s.Vehicle }, cancellationToken);
        var spans = rows.Select(r => new StaySpan(r.EntryAt, r.ExitAt, r.Vehicle)).ToList();

        var totalHours = Math.Max(0, (int)(end - from).TotalHours);
        var hourly = LotKpi.HourlyOccupancy(from, totalHours, spans, now);
        var sum = new double[7, 24];
        var entries = new double[24];
        var count = new int[7, 24];
        for (var h = 0; h < totalHours; h++)
        {
            var at = from.AddHours(h);
            var dow = ((int)at.DayOfWeek + 6) % 7; // Pzt = 0
            sum[dow, at.Hour] += hourly[h];
            count[dow, at.Hour]++;
        }
        foreach (var r in rows.Where(r => r.EntryAt >= from && r.EntryAt < end)) entries[r.EntryAt.Hour]++;
        var dayCount = Math.Max(1, (end - from).Days);

        var matrix = new double[7, 24];
        for (var d = 0; d < 7; d++)
            for (var h = 0; h < 24; h++)
                matrix[d, h] = count[d, h] == 0 ? 0 : Math.Round(sum[d, h] / count[d, h], 1);

        var names = new[] { "Pzt", "Sal", "Çar", "Per", "Cum", "Cmt", "Paz" };
        var hours = Enumerable.Range(0, 24).Select(h => new HourRow($"{h:00}:00",
            matrix[0, h], matrix[1, h], matrix[2, h], matrix[3, h], matrix[4, h], matrix[5, h], matrix[6, h],
            Math.Round(Enumerable.Range(0, 7).Average(d => matrix[d, h]), 1),
            Math.Round(entries[h] / dayCount, 1))).ToList();
        var top = Enumerable.Range(0, 7).SelectMany(d => Enumerable.Range(0, 24).Select(h => new PeakSlot(names[d], h, matrix[d, h])))
            .OrderByDescending(s => s.Inside).Take(5).ToList();

        return new PeakHoursReport(q.From, q.To, capacity, hours, top,
            hours.Max(h => h.Average), hours.Where(h => h.Average > 0).Select(h => h.Average).DefaultIfEmpty(0).Min()) { Matrix = matrix };
    }
}

// ---------- Gelir tahmini ----------

public sealed record ForecastDay(DateOnly Date, string DayName, double Forecast, double Lower, double Upper, double ExpectedEntries);

public sealed record RevenueForecastReport(
    DateOnly Today, string Method, double Mae, double? Mape,
    IReadOnlyList<DateOnly> HistoryDates, double[] HistoryRevenue, double[] HistoryFitted,
    IReadOnlyList<ForecastDay> Days, double HistoryAverage)
{
    public double ForecastTotal => Days.Sum(d => d.Forecast);
    public double ForecastAverage => Days.Count == 0 ? 0 : Days.Average(d => d.Forecast);
}

/// <summary>Günlük toplam ciro (otopark + yıkama) için tahmin. Haftalık desen (7 gün) dikkate alınır, yöntem son dönemde yarıştırılarak seçilir.</summary>
public sealed record GetRevenueForecast(int Horizon = 14, int HistoryDays = 56) : IQuery<RevenueForecastReport>;

public sealed class GetRevenueForecastValidator : AbstractValidator<GetRevenueForecast>
{
    public GetRevenueForecastValidator()
    {
        RuleFor(x => x.Horizon).InclusiveBetween(1, 60).WithName("Tahmin süresi");
        RuleFor(x => x.HistoryDays).InclusiveBetween(21, 365).WithName("Geçmiş gün sayısı");
    }
}

public sealed class GetRevenueForecastHandler(IUnitOfWorkFactory uowFactory, TimeProvider time) : IQueryHandler<GetRevenueForecast, RevenueForecastReport>
{
    public async Task<Result<RevenueForecastReport>> HandleAsync(GetRevenueForecast q, CancellationToken cancellationToken)
    {
        var today = time.Today();
        var histFrom = today.AddDays(-q.HistoryDays);
        var from = histFrom.Start();
        var end = today.Start();
        await using var uow = uowFactory.Create();
        var parking = await uow.Repository<ParkingSession>().ListAsync(
            new Specification<ParkingSession>().Where(s => s.ExitAt >= from && s.ExitAt < end), s => new { s.ExitAt, s.ParkingFee, s.EntryAt }, cancellationToken);
        var entries = await uow.Repository<ParkingSession>().ListAsync(
            new Specification<ParkingSession>().Where(s => s.EntryAt >= from && s.EntryAt < end), s => s.EntryAt, cancellationToken);
        var washes = await uow.Repository<WashOrder>().ListAsync(
            new Specification<WashOrder>().Where(w => w.Status == WashStatus.Delivered && w.DeliveredAt >= from && w.DeliveredAt < end),
            w => new { w.DeliveredAt, w.Price }, cancellationToken);

        var dates = Enumerable.Range(0, q.HistoryDays).Select(i => histFrom.AddDays(i)).ToList();
        var revenue = dates.Select(d =>
        {
            var s = d.Start();
            var e = s.AddDays(1);
            return (double)(parking.Where(p => p.ExitAt >= s && p.ExitAt < e).Sum(p => p.ParkingFee)
                            + washes.Where(w => w.DeliveredAt >= s && w.DeliveredAt < e).Sum(w => w.Price));
        }).ToArray();
        var entryCounts = dates.Select(d => (double)entries.Count(t => DateOnly.FromDateTime(t) == d)).ToArray();

        var model = Forecast.Auto(revenue, q.Horizon, seasonLength: 7).Clamp(0, double.MaxValue);
        var entryModel = Forecast.Auto(entryCounts, q.Horizon, seasonLength: 7).Clamp(0, double.MaxValue);

        var days = Enumerable.Range(0, q.Horizon).Select(i =>
        {
            var d = today.AddDays(i);
            return new ForecastDay(d, Texts.DayName(d), Math.Round(model.Forecast[i], 0), Math.Round(model.Lower[i], 0), Math.Round(model.Upper[i], 0),
                Math.Round(entryModel.Forecast[i], 0));
        }).ToList();

        return new RevenueForecastReport(today, model.MethodName, model.Mae, model.Mape, dates, revenue, model.Fitted, days,
            revenue.Length == 0 ? 0 : revenue.Average());
    }
}
