using OnderCode.Application.Messaging;
using OnderCode.Application.Results;
using OnderCode.Kernel.Persistence;
using OnderCode.Kernel.Specifications;
using Otopark.Domain;

namespace Otopark.Application;

public sealed record MovementRow(int SessionId, string Movement, DateTime At, string Plate, string SpotCode, string VehicleText, decimal? Amount, string? Note);

public sealed record DashboardView(
    DateTime Now,
    int Capacity,
    int Inside,
    int FreeCar,
    int FreeLarge,
    int FreeMotorcycle,
    int OutOfService,
    int SubscribersInside,
    decimal TodayParking,
    decimal TodayWash,
    decimal YesterdayToNowTotal,
    int TodayEntries,
    int TodayExits,
    double AverageStayMinutes,
    int WashWaiting,
    int WashWashing,
    int WashReady,
    string[] TrendLabels,
    double[] ParkingTrend,
    double[] WashTrend,
    string[] HourLabels,
    double[] EntriesByHourToday,
    double[] EntriesByHourTypical,
    IReadOnlyList<MovementRow> Movements)
{
    public decimal TodayTotal => TodayParking + TodayWash;
    public double Occupancy => Capacity - OutOfService <= 0 ? 0 : (double)Inside / (Capacity - OutOfService);
}

/// <summary>Anlık doluluk, bugünkü ciro (dün aynı saatle karşılaştırmalı), 30 günlük gelir, saatlik giriş ve son hareketler.</summary>
public sealed record GetDashboard : IQuery<DashboardView>;

public sealed class GetDashboardHandler(IUnitOfWorkFactory uowFactory, TimeProvider time) : IQueryHandler<GetDashboard, DashboardView>
{
    public async Task<Result<DashboardView>> HandleAsync(GetDashboard q, CancellationToken cancellationToken)
    {
        var now = time.Now();
        var today = now.Date;
        var from30 = today.AddDays(-29);
        await using var uow = uowFactory.Create();

        var spots = await uow.Repository<ParkingSpot>().ListAsync(cancellationToken: cancellationToken);
        var active = await uow.Repository<ParkingSession>().ListAsync(
            new Specification<ParkingSession>().Where(s => s.Status == SessionStatus.Active),
            s => new { s.SpotId, s.SubscriptionId }, cancellationToken);
        var sessions = await uow.Repository<ParkingSession>().ListAsync(
            new Specification<ParkingSession>().Where(s => s.EntryAt >= from30.AddDays(-1) || s.ExitAt >= from30.AddDays(-1) || s.Status == SessionStatus.Active),
            s => new { s.Id, s.Plate, s.Vehicle, Spot = s.Spot.Code, s.EntryAt, s.ExitAt, s.ParkingFee, s.WashFee, s.Status },
            cancellationToken);
        var washes = await uow.Repository<WashOrder>().ListAsync(
            new Specification<WashOrder>().Where(w => w.DeliveredAt >= from30.AddDays(-1) || w.Status == WashStatus.Waiting || w.Status == WashStatus.Washing || w.Status == WashStatus.Ready),
            w => new { w.Status, w.Price, w.DeliveredAt }, cancellationToken);

        var busy = active.Select(a => a.SpotId).ToHashSet();
        int Free(VehicleClass v) => spots.Count(s => !s.OutOfService && !busy.Contains(s.Id) && s.Accepts(v) && (v != VehicleClass.Car || s.Kind != SpotKind.Large));

        decimal Parking(DateTime from, DateTime to) => sessions.Where(s => s.ExitAt >= from && s.ExitAt < to).Sum(s => s.ParkingFee);
        decimal Wash(DateTime from, DateTime to) => washes.Where(w => w.Status == WashStatus.Delivered && w.DeliveredAt >= from && w.DeliveredAt < to).Sum(w => w.Price);

        var closedToday = sessions.Where(s => s.ExitAt >= today).ToList();
        // Ortalama kalış son 7 günün çıkışlarından: sabah saatlerinde bugünün çıkışları çoğunlukla gece kalanlardır
        var week = sessions.Where(s => s.ExitAt >= today.AddDays(-7) && s.ExitAt <= now).ToList();
        var days = Enumerable.Range(0, 30).Select(i => from30.AddDays(i)).ToArray();

        // Saat saat giriş: bugün ve son 4 haftanın aynı günü ortalaması
        var hours = Enumerable.Range(6, 18).ToArray(); // 06:00 - 23:00
        var entriesToday = hours.Select(h => (double)sessions.Count(s => s.EntryAt >= today && s.EntryAt.Hour == h)).ToArray();
        var sameDays = Enumerable.Range(1, 4).Select(w => today.AddDays(-7 * w)).ToHashSet();
        var typical = hours.Select(h => Math.Round(sessions.Count(s => sameDays.Contains(s.EntryAt.Date) && s.EntryAt.Hour == h) / 4.0, 1)).ToArray();

        var movements = sessions
            .SelectMany(s => s.ExitAt is { } exit && exit >= today
                ? new[] { (s, "Giriş", s.EntryAt), (s, "Çıkış", exit) }
                : new[] { (s, "Giriş", s.EntryAt) })
            .Where(m => m.Item3 >= today && m.Item3 <= now)
            .OrderByDescending(m => m.Item3)
            .Take(40)
            .Select(m => new MovementRow(m.s.Id, m.Item2, m.Item3, m.s.Plate, m.s.Spot, Texts.Of(m.s.Vehicle),
                m.Item2 == "Çıkış" ? m.s.ParkingFee + m.s.WashFee : null,
                m.Item2 == "Çıkış" ? Texts.Duration(m.Item3 - m.s.EntryAt) : null))
            .ToList();

        return new DashboardView(
            now,
            spots.Count,
            active.Count,
            Free(VehicleClass.Car),
            Free(VehicleClass.Large),
            Free(VehicleClass.Motorcycle),
            spots.Count(s => s.OutOfService),
            active.Count(a => a.SubscriptionId != null),
            Parking(today, now.AddSeconds(1)),
            Wash(today, now.AddSeconds(1)),
            Parking(today.AddDays(-1), now.AddDays(-1)) + Wash(today.AddDays(-1), now.AddDays(-1)),
            sessions.Count(s => s.EntryAt >= today),
            closedToday.Count,
            week.Count == 0 ? 0 : week.Average(s => (s.ExitAt!.Value - s.EntryAt).TotalMinutes),
            washes.Count(w => w.Status == WashStatus.Waiting),
            washes.Count(w => w.Status == WashStatus.Washing),
            washes.Count(w => w.Status == WashStatus.Ready),
            [.. days.Select(d => d.ToString("dd.MM"))],
            [.. days.Select(d => (double)Parking(d, d.AddDays(1)))],
            [.. days.Select(d => (double)Wash(d, d.AddDays(1)))],
            [.. hours.Select(h => h.ToString("00"))],
            entriesToday,
            typical,
            movements);
    }
}
