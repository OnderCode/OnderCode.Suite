using System.Globalization;
using FluentValidation;
using OnderCode.Analytics;
using OnderCode.Application.Messaging;
using OnderCode.Application.Results;
using OnderCode.Kernel.Persistence;
using OnderCode.Kernel.Specifications;
using Otel.Domain;

namespace Otel.Application;

// ---------- Gelir ve doluluk raporu ----------

public sealed record DailyRevenueRow(DateOnly Date, string DayName, long SoldRooms, long AvailableRooms, double Occupancy, decimal RoomRevenue, decimal Adr, decimal RevPar);

public sealed record RoomTypeRevenueRow(string RoomType, long Rooms, long SoldNights, double Occupancy, decimal Revenue, decimal Adr, double Share);

public sealed record ChargeCategoryRow(string Category, long Count, decimal Amount, double Share);

public sealed record RevenueReport(
    DateOnly From,
    DateOnly To,
    int RoomCount,
    KpiSet Current,
    KpiSet Previous,
    decimal ExtraRevenue,
    decimal PreviousExtraRevenue,
    int Arrivals,
    int Cancellations,
    double AverageStay,
    IReadOnlyList<DailyRevenueRow> Days,
    IReadOnlyList<RoomTypeRevenueRow> RoomTypes,
    IReadOnlyList<ChargeCategoryRow> Charges)
{
    public decimal TotalRevenue => Current.RoomRevenue + ExtraRevenue;
    public decimal PreviousTotalRevenue => Previous.RoomRevenue + PreviousExtraRevenue;
}

/// <summary>[From, To] (iki uç dahil) dönemi için OCC / ADR / RevPAR, günlük döküm, oda tipi ve ekstra harcama kırılımı; aynı uzunluktaki önceki dönemle karşılaştırma.</summary>
public sealed record GetRevenueReport(DateOnly From, DateOnly To) : IQuery<RevenueReport>;

public sealed class GetRevenueReportValidator : AbstractValidator<GetRevenueReport>
{
    public GetRevenueReportValidator()
    {
        RuleFor(x => x.To).GreaterThanOrEqualTo(x => x.From).WithMessage("Bitiş tarihi başlangıçtan önce olamaz.");
        RuleFor(x => x).Must(x => x.To.DayNumber - x.From.DayNumber < 366).WithName("Dönem").WithMessage("Dönem en çok bir yıl olabilir.");
    }
}

public sealed class GetRevenueReportHandler(IUnitOfWorkFactory uowFactory) : IQueryHandler<GetRevenueReport, RevenueReport>
{
    public async Task<Result<RevenueReport>> HandleAsync(GetRevenueReport q, CancellationToken cancellationToken)
    {
        var from = q.From;
        var end = q.To.AddDays(1);
        var length = end.DayNumber - from.DayNumber;
        var prevFrom = from.AddDays(-length);

        await using var uow = uowFactory.Create();
        var rooms = await uow.Repository<Room>().ListAsync(new Specification<Room>(), r => new { Type = r.RoomType.Name }, cancellationToken);
        var reservations = await uow.Repository<Reservation>().ListAsync(
            new Specification<Reservation>().Where(r => r.CheckIn < end && prevFrom < r.CheckOut),
            r => new { r.CheckIn, r.CheckOut, r.RoomTotal, r.Status, Type = r.Room.RoomType.Name },
            cancellationToken);
        var charges = await uow.Repository<FolioItem>().ListAsync(
            new Specification<FolioItem>().Where(f => f.PostedAt >= prevFrom.ToDateTime(TimeOnly.MinValue) && f.PostedAt < end.ToDateTime(TimeOnly.MinValue)),
            f => new { f.Category, f.Amount, f.PostedAt },
            cancellationToken);

        var roomCount = rooms.Count;
        var stays = reservations.Where(r => r.Status != ReservationStatus.Cancelled).Select(r => new StaySlice(r.CheckIn, r.CheckOut, r.RoomTotal, r.Type)).ToList();
        var current = HotelKpi.Calculate(roomCount, from, end, stays);
        var previous = HotelKpi.Calculate(roomCount, prevFrom, from, stays);

        var days = Enumerable.Range(0, length).Select(i =>
        {
            var d = from.AddDays(i);
            var k = HotelKpi.Calculate(roomCount, d, d.AddDays(1), stays);
            return new DailyRevenueRow(d, Days.Name(d), k.SoldNights, k.AvailableNights, (double)k.Occupancy, k.RoomRevenue, k.Adr, k.RevPar);
        }).ToList();

        var types = rooms.GroupBy(r => r.Type).OrderBy(g => g.Key, StringComparer.Create(Days.Turkish, true)).Select(g =>
        {
            var k = HotelKpi.Calculate(g.Count(), from, end, stays.Where(s => s.RoomType == g.Key));
            return new RoomTypeRevenueRow(g.Key, g.Count(), k.SoldNights, (double)k.Occupancy, k.RoomRevenue, k.Adr,
                current.RoomRevenue == 0 ? 0 : (double)(k.RoomRevenue / current.RoomRevenue));
        }).ToList();

        var currentCharges = charges.Where(c => DateOnly.FromDateTime(c.PostedAt) >= from).ToList();
        var extra = currentCharges.Sum(c => c.Amount);
        var byCategory = currentCharges.GroupBy(c => c.Category)
            .Select(g => new ChargeCategoryRow(Texts.Of(g.Key), g.Count(), g.Sum(c => c.Amount), extra == 0 ? 0 : (double)(g.Sum(c => c.Amount) / extra)))
            .OrderByDescending(c => c.Amount)
            .ToList();

        var arrivals = reservations.Where(r => r.CheckIn >= from && r.CheckIn < end).ToList();
        var realized = arrivals.Where(r => r.Status != ReservationStatus.Cancelled).ToList();

        return new RevenueReport(
            q.From, q.To, roomCount, current, previous,
            ExtraRevenue: extra,
            PreviousExtraRevenue: charges.Where(c => DateOnly.FromDateTime(c.PostedAt) < from).Sum(c => c.Amount),
            Arrivals: realized.Count,
            Cancellations: arrivals.Count - realized.Count,
            AverageStay: realized.Count == 0 ? 0 : realized.Average(r => r.CheckOut.DayNumber - r.CheckIn.DayNumber),
            Days: days,
            RoomTypes: types,
            Charges: byCategory);
    }
}

// ---------- Misafir değeri: Pareto / ABC ----------

public sealed record GuestValueRow(int Rank, int GuestId, string GuestName, string TierText, long Stays, long Nights,
    decimal RoomRevenue, decimal ExtraRevenue, decimal Total, double Share, double Cumulative, string Class);

public sealed record GuestParetoReport(
    DateOnly From,
    DateOnly To,
    decimal Total,
    int GuestCount,
    int CountA,
    int CountB,
    int CountC,
    double CountShareA,
    double ValueShareA,
    double ValueShareB,
    double ValueShareC,
    IReadOnlyList<GuestValueRow> Guests);

/// <summary>Girişi [From, To] arasında olan ve gerçekleşen konaklamalardan misafir başına gelir (oda + harcama), ABC sınıfı.</summary>
public sealed record GetGuestPareto(DateOnly From, DateOnly To) : IQuery<GuestParetoReport>;

public sealed class GetGuestParetoValidator : AbstractValidator<GetGuestPareto>
{
    public GetGuestParetoValidator()
        => RuleFor(x => x.To).GreaterThanOrEqualTo(x => x.From).WithMessage("Bitiş tarihi başlangıçtan önce olamaz.");
}

public sealed class GetGuestParetoHandler(IUnitOfWorkFactory uowFactory) : IQueryHandler<GetGuestPareto, GuestParetoReport>
{
    public async Task<Result<GuestParetoReport>> HandleAsync(GetGuestPareto q, CancellationToken cancellationToken)
    {
        var end = q.To.AddDays(1);
        await using var uow = uowFactory.Create();
        var stays = await uow.Repository<Reservation>().ListAsync(
            new Specification<Reservation>().Where(r => r.CheckIn >= q.From && r.CheckIn < end
                && (r.Status == ReservationStatus.CheckedIn || r.Status == ReservationStatus.CheckedOut)),
            r => new { r.Id, r.GuestId, r.Guest.FullName, r.Guest.LoyaltyPoints, r.CheckIn, r.CheckOut, r.RoomTotal },
            cancellationToken);
        var ids = stays.Select(s => s.Id).ToList();
        var charges = await uow.Repository<FolioItem>().ListAsync(
            new Specification<FolioItem>().Where(f => ids.Contains(f.ReservationId)),
            f => new { f.ReservationId, f.Amount },
            cancellationToken);
        var chargeByReservation = charges.GroupBy(c => c.ReservationId).ToDictionary(g => g.Key, g => g.Sum(c => c.Amount));

        var guests = stays.GroupBy(s => s.GuestId).Select(g => new
        {
            GuestId = g.Key,
            g.First().FullName,
            g.First().LoyaltyPoints,
            Stays = g.Count(),
            Nights = g.Sum(s => s.CheckOut.DayNumber - s.CheckIn.DayNumber),
            Room = g.Sum(s => s.RoomTotal),
            Extra = g.Sum(s => chargeByReservation.GetValueOrDefault(s.Id)),
        }).ToList();

        var pareto = Pareto.Analyze(guests, g => g.Room + g.Extra);
        var rows = pareto.Items.Select(i => new GuestValueRow(
            i.Rank, i.Item.GuestId, i.Item.FullName, Texts.Of(Guest.TierOf(i.Item.LoyaltyPoints)), i.Item.Stays, i.Item.Nights,
            i.Item.Room, i.Item.Extra, i.Value, i.Share, i.Cumulative, i.Class.ToString())).ToList();

        return new GuestParetoReport(q.From, q.To, pareto.Total, rows.Count,
            pareto.CountOf(AbcClass.A), pareto.CountOf(AbcClass.B), pareto.CountOf(AbcClass.C),
            pareto.CountShareOf(AbcClass.A),
            pareto.ValueShareOf(AbcClass.A), pareto.ValueShareOf(AbcClass.B), pareto.ValueShareOf(AbcClass.C),
            rows);
    }
}

// ---------- Doluluk tahmini ----------

/// <summary>Gelecek bir gün: kesin rezervasyon (OTB), model tahmini ve aralığı, ikisinden büyüğü olan beklenen doluluk. Oranlar 0..1.</summary>
public sealed record ForecastDayRow(DateOnly Date, string DayName, double OnTheBooks, double Forecast, double Lower, double Upper, double Expected, long ExpectedRooms);

public sealed record OccupancyForecastReport(
    DateOnly Today,
    int RoomCount,
    string Method,
    double Mae,
    IReadOnlyList<DateOnly> HistoryDates,
    double[] HistoryActual,
    double[] HistoryFitted,
    IReadOnlyList<ForecastDayRow> Days,
    double HistoryAverage)
{
    public double ForecastAverage => Days.Count == 0 ? 0 : Days.Average(d => d.Expected);
    public double OnTheBooksAverage => Days.Count == 0 ? 0 : Days.Average(d => d.OnTheBooks);
}

/// <summary>
/// Son <see cref="HistoryDays"/> günün gerçekleşen doluluğundan önümüzdeki <see cref="Horizon"/> gün için tahmin.
/// Model haftalık deseni (7 gün) dikkate alır ve son dönem üzerinde yarıştırılarak seçilir. Kesin rezervasyonlar tahminin alt sınırıdır.
/// </summary>
public sealed record GetOccupancyForecast(int Horizon = 14, int HistoryDays = 56) : IQuery<OccupancyForecastReport>;

public sealed class GetOccupancyForecastValidator : AbstractValidator<GetOccupancyForecast>
{
    public GetOccupancyForecastValidator()
    {
        RuleFor(x => x.Horizon).InclusiveBetween(1, 60).WithName("Tahmin süresi");
        RuleFor(x => x.HistoryDays).InclusiveBetween(21, 365).WithName("Geçmiş gün sayısı");
    }
}

public sealed class GetOccupancyForecastHandler(IUnitOfWorkFactory uowFactory, TimeProvider time) : IQueryHandler<GetOccupancyForecast, OccupancyForecastReport>
{
    public async Task<Result<OccupancyForecastReport>> HandleAsync(GetOccupancyForecast q, CancellationToken cancellationToken)
    {
        var today = time.Today();
        var histFrom = today.AddDays(-q.HistoryDays);
        var end = today.AddDays(q.Horizon);

        await using var uow = uowFactory.Create();
        var roomCount = await uow.Repository<Room>().CountAsync(null, cancellationToken);
        var rows = await uow.Repository<Reservation>().ListAsync(
            new Specification<Reservation>().Where(r => r.Status != ReservationStatus.Cancelled && r.CheckIn < end && histFrom < r.CheckOut),
            r => new { r.CheckIn, r.CheckOut, r.RoomTotal },
            cancellationToken);
        var stays = rows.Select(r => new StaySlice(r.CheckIn, r.CheckOut, r.RoomTotal)).ToList();

        var history = HotelKpi.DailyOccupancy(roomCount, histFrom, q.HistoryDays, stays);
        var onBooks = HotelKpi.DailyOccupancy(roomCount, today, q.Horizon, stays);
        var model = Forecast.Auto(history, q.Horizon, seasonLength: 7).Clamp(0, 1);

        var days = Enumerable.Range(0, q.Horizon).Select(i =>
        {
            var d = today.AddDays(i);
            var expected = Math.Max(onBooks[i], model.Forecast[i]);
            return new ForecastDayRow(d, Days.Name(d), onBooks[i], model.Forecast[i], model.Lower[i], model.Upper[i], expected,
                (long)Math.Round(expected * roomCount, MidpointRounding.AwayFromZero));
        }).ToList();

        return new OccupancyForecastReport(
            today, roomCount, model.MethodName, model.Mae,
            [.. Enumerable.Range(0, q.HistoryDays).Select(i => histFrom.AddDays(i))],
            history, model.Fitted, days,
            history.Length == 0 ? 0 : history.Average());
    }
}

/// <summary>Gün adları (Pzt, Sal, …).</summary>
internal static class Days
{
    public static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public static string Name(DateOnly d) => Turkish.DateTimeFormat.GetAbbreviatedDayName(d.DayOfWeek);
}
