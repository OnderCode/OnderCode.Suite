using OnderCode.Application.Messaging;
using OnderCode.Application.Results;
using OnderCode.Kernel.Persistence;
using OnderCode.Kernel.Specifications;
using Otel.Domain;

namespace Otel.Application;

public sealed record MovementRow(int ReservationId, string Movement, string GuestName, string RoomNumber, DateOnly CheckIn, DateOnly CheckOut, string StatusText);

public sealed record DashboardView(
    DateOnly Today,
    KpiSet Last30,
    KpiSet Previous30,
    decimal OccupancyToday,
    int InHouseGuests,
    int ArrivalsToday,
    int DeparturesToday,
    int DirtyRooms,
    int MaintenanceRooms,
    int RoomCount,
    string[] TrendLabels,
    double[] OccupancyTrend,
    double[] RevenueTrend,
    string[] RoomTypeLabels,
    double[] RevenueByRoomType,
    double[] OccupancyByRoomType,
    IReadOnlyList<MovementRow> Movements);

public sealed record GetDashboard : IQuery<DashboardView>;

/// <summary>Gösterge paneli: son 30 gün ile önceki 30 günün OCC / ADR / RevPAR karşılaştırması, günlük trend, bugünkü hareketler.</summary>
public sealed class GetDashboardHandler(IUnitOfWorkFactory uowFactory, TimeProvider time) : IQueryHandler<GetDashboard, DashboardView>
{
    private const int Window = 30;

    public async Task<Result<DashboardView>> HandleAsync(GetDashboard query, CancellationToken cancellationToken)
    {
        var today = time.Today();
        var from = today.AddDays(-Window * 2);
        var end = today.AddDays(1);

        await using var uow = uowFactory.Create();
        var rooms = await uow.Repository<Room>().ListAsync(new Specification<Room>(), r => new { r.Status, Type = r.RoomType.Name }, cancellationToken);
        var rows = await uow.Repository<Reservation>().ListAsync(
            new Specification<Reservation>().Where(r => r.Status != ReservationStatus.Cancelled && r.CheckIn < end && from < r.CheckOut),
            r => new { r.Id, r.CheckIn, r.CheckOut, r.RoomTotal, r.Status, r.Guest.FullName, r.Room.Number, Type = r.Room.RoomType.Name },
            cancellationToken);

        var stays = rows.Select(r => new StaySlice(r.CheckIn, r.CheckOut, r.RoomTotal, r.Type)).ToList();
        var roomCount = rooms.Count;

        var last30 = HotelKpi.Calculate(roomCount, today.AddDays(-Window), today, stays);
        var prev30 = HotelKpi.Calculate(roomCount, today.AddDays(-Window * 2), today.AddDays(-Window), stays);

        var trendFrom = today.AddDays(-(Window - 1));
        var occTrend = HotelKpi.DailyOccupancy(roomCount, trendFrom, Window, stays).Select(v => Math.Round(v * 100, 1)).ToArray();
        var revTrend = Enumerable.Range(0, Window).Select(i =>
        {
            var d = trendFrom.AddDays(i);
            return (double)HotelKpi.Calculate(roomCount, d, d.AddDays(1), stays).RoomRevenue;
        }).ToArray();
        var labels = Enumerable.Range(0, Window).Select(i => trendFrom.AddDays(i).ToString("dd.MM")).ToArray();

        var types = rooms.GroupBy(r => r.Type).Select(g => (Name: g.Key, Count: g.Count())).OrderBy(t => t.Name).ToList();
        var revenueByType = types.Select(t => (double)HotelKpi.Calculate(t.Count, today.AddDays(-Window), today, stays.Where(s => s.RoomType == t.Name)).RoomRevenue).ToArray();
        var occByType = types.Select(t => (double)HotelKpi.Calculate(t.Count, today.AddDays(-Window), today, stays.Where(s => s.RoomType == t.Name)).Occupancy * 100).ToArray();

        var occupiedToday = stays.Count(s => s.CheckIn <= today && today < s.CheckOut);
        var movements = rows
            .Where(r => (r.CheckIn == today && r.Status is ReservationStatus.Confirmed or ReservationStatus.CheckedIn)
                     || (r.CheckOut == today && r.Status is ReservationStatus.CheckedIn or ReservationStatus.CheckedOut))
            .Select(r => new MovementRow(r.Id, r.CheckIn == today ? "Giriş" : "Çıkış", r.FullName, r.Number, r.CheckIn, r.CheckOut, Texts.Of(r.Status)))
            .OrderBy(m => m.Movement).ThenBy(m => m.RoomNumber)
            .ToList();

        return new DashboardView(
            today, last30, prev30,
            roomCount == 0 ? 0 : decimal.Round((decimal)occupiedToday / roomCount, 4),
            InHouseGuests: rows.Count(r => r.Status == ReservationStatus.CheckedIn),
            ArrivalsToday: rows.Count(r => r.CheckIn == today && r.Status is ReservationStatus.Confirmed or ReservationStatus.CheckedIn),
            DeparturesToday: rows.Count(r => r.CheckOut == today && r.Status is ReservationStatus.CheckedIn or ReservationStatus.CheckedOut),
            DirtyRooms: rooms.Count(r => r.Status == RoomStatus.Dirty),
            MaintenanceRooms: rooms.Count(r => r.Status == RoomStatus.Maintenance),
            RoomCount: roomCount,
            TrendLabels: labels,
            OccupancyTrend: occTrend,
            RevenueTrend: revTrend,
            RoomTypeLabels: [.. types.Select(t => t.Name)],
            RevenueByRoomType: revenueByType,
            OccupancyByRoomType: occByType,
            Movements: movements);
    }
}
