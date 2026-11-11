using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using OnderCode.Application.Messaging;
using OnderCode.Application.Results;
using OnderCode.Data.Initialization;
using Otel.Application;
using Otel.Domain;
using Otel.Infrastructure;

namespace Otel.Tests;

/// <summary>Örnek veriyle dolu gerçek veritabanı üzerinde ön büro akışları.</summary>
public sealed class OtelFlowTests : IAsyncLifetime
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ondercode-tests", Guid.NewGuid().ToString("N"));
    private readonly FixedTime _time = new(new DateTimeOffset(2026, 11, 11, 9, 0, 0, TimeSpan.Zero));
    private ServiceProvider _services = null!;

    private ISender Sender => _services.GetRequiredService<ISender>();
    private DateOnly Today => _time.Today();

    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(_time);
        services.AddOtel(o => o.DataDirectory = _dir);
        _services = services.BuildServiceProvider();
        await _services.GetRequiredService<DatabaseInitializer<OtelDbContext>>().InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task DemoData_ProducesALivelyDashboard()
    {
        var d = (await Sender.SendAsync(new GetDashboard())).Value;

        Assert.Equal(26, d.RoomCount);
        Assert.InRange(d.Last30.Occupancy, 0.4m, 0.95m);
        Assert.True(d.Last30.Adr > 2000m);
        Assert.Equal(d.Last30.RevPar, decimal.Round(d.Last30.RoomRevenue / d.Last30.AvailableNights, 2));
        Assert.True(d.InHouseGuests > 5);
        Assert.Equal(30, d.OccupancyTrend.Length);
        Assert.Equal(4, d.RoomTypeLabels.Length);

        var board = (await Sender.SendAsync(new GetRoomBoard())).Value;
        Assert.Contains(board, t => t.Status == RoomStatus.Maintenance);
        Assert.Equal(d.InHouseGuests, board.Count(t => t.Status == RoomStatus.Occupied));
    }

    [Fact]
    public async Task FullStay_ReserveCheckInChargeCheckOutClean()
    {
        var clean = (await Sender.SendAsync(new GetRoomBoard())).Value.Where(t => t.Status == RoomStatus.Available).Select(t => t.RoomId).ToHashSet();
        var room = (await Sender.SendAsync(new FindAvailableRooms(Today, Today.AddDays(2), 2))).Value.First(r => clean.Contains(r.RoomId));
        var created = await Sender.SendAsync(new CreateReservation(null, "Test Misafir", null, room.RoomId, Today, Today.AddDays(2), 2));
        Assert.True(created.IsSuccess, created.ErrorMessage);
        var id = created.Value;

        Assert.True((await Sender.SendAsync(new CheckIn(id))).IsSuccess);
        Assert.True((await Sender.SendAsync(new PostCharge(id, ChargeCategory.Restaurant, "Akşam yemeği", 1250m))).IsSuccess);

        var checkout = await Sender.SendAsync(new CheckOut(id));
        Assert.True(checkout.IsSuccess, checkout.ErrorMessage);
        Assert.Equal(room.Total + 1250m, checkout.Value.Balance);
        Assert.Equal(20, checkout.Value.LoyaltyPoints);

        var tile = (await Sender.SendAsync(new GetRoomBoard())).Value.Single(t => t.RoomId == room.RoomId);
        Assert.Equal(RoomStatus.Dirty, tile.Status);
        Assert.Contains((await Sender.SendAsync(new GetHousekeepingTasks())).Value, t => t.RoomNumber == room.Number && t.StateText == "Açık");

        Assert.True((await Sender.SendAsync(new MarkRoomReady(room.RoomId))).IsSuccess);
        tile = (await Sender.SendAsync(new GetRoomBoard())).Value.Single(t => t.RoomId == room.RoomId);
        Assert.Equal(RoomStatus.Available, tile.Status);
    }

    [Fact]
    public async Task Overbooking_IsRejected_WithFriendlyMessage()
    {
        var from = Today.AddDays(60);
        var room = (await Sender.SendAsync(new FindAvailableRooms(from, from.AddDays(3), 1))).Value.First();
        Assert.True((await Sender.SendAsync(new CreateReservation(null, "Birinci", null, room.RoomId, from, from.AddDays(3), 1))).IsSuccess);

        var clash = await Sender.SendAsync(new CreateReservation(null, "İkinci", null, room.RoomId, from.AddDays(2), from.AddDays(4), 1));
        Assert.True(clash.IsFailure);
        Assert.Equal(ErrorCodes.Domain, clash.Errors[0].Code);
        Assert.Contains("dolu", clash.Errors[0].Message);

        // Aynı gün çıkış/giriş serbest ve oda artık müsait listesinde yok
        Assert.True((await Sender.SendAsync(new CreateReservation(null, "Üçüncü", null, room.RoomId, from.AddDays(3), from.AddDays(5), 1))).IsSuccess);
        var available = (await Sender.SendAsync(new FindAvailableRooms(from.AddDays(1), from.AddDays(2), 1))).Value;
        Assert.DoesNotContain(available, r => r.RoomId == room.RoomId);
    }

    [Fact]
    public async Task Validation_RejectsPastDates_AndInvalidIdentity()
    {
        var result = await Sender.SendAsync(new CreateReservation(null, "Ali", "12345678901", 1, Today.AddDays(-1), Today.AddDays(-2), 1));
        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, e => e.Field == nameof(CreateReservation.CheckIn));
        Assert.Contains(result.Errors, e => e.Field == nameof(CreateReservation.CheckOut));
        Assert.Contains(result.Errors, e => e.Field == nameof(CreateReservation.NewGuestIdentityNo));
    }

    [Fact]
    public async Task EarlyBooking_IsAppliedToTheStoredPrice()
    {
        var from = Today.AddDays(40);
        var room = (await Sender.SendAsync(new FindAvailableRooms(from, from.AddDays(2), 1))).Value.First();
        Assert.True(room.Discount > 0);
        var id = (await Sender.SendAsync(new CreateReservation(null, "Erken Kuş", null, room.RoomId, from, from.AddDays(2), 1))).Value;
        var detail = (await Sender.SendAsync(new GetReservationDetail(id))).Value;
        Assert.Equal(room.Total, detail.RoomTotal);
    }

    [Fact]
    public async Task Timeline_And_Lists_Load()
    {
        var timeline = (await Sender.SendAsync(new GetTimeline(Today.AddDays(-3), 21))).Value;
        Assert.Equal(26, timeline.Rooms.Count);
        Assert.NotEmpty(timeline.Bars);

        var reservations = (await Sender.SendAsync(new ListReservations { PageSize = 1000, Status = ReservationStatus.CheckedIn })).Value;
        Assert.All(reservations.Items, r => Assert.Equal("Konaklıyor", r.StatusText));

        var guests = (await Sender.SendAsync(new ListGuests { PageSize = 50 })).Value;
        Assert.Equal(140, guests.TotalCount);
        Assert.True(guests.Items[0].LoyaltyPoints >= guests.Items[^1].LoyaltyPoints);
    }

    [Fact]
    public async Task Revenue_Report_Is_Consistent_With_Kpis()
    {
        var result = await Sender.SendAsync(new GetRevenueReport(Today.AddDays(-30), Today.AddDays(-1)));
        Assert.True(result.IsSuccess, result.ErrorMessage);
        var r = result.Value;

        Assert.Equal(30, r.Days.Count);
        Assert.Equal(26 * 30, r.Current.AvailableNights);
        Assert.Equal(r.Current.SoldNights, r.Days.Sum(d => d.SoldRooms));
        // günlük ve oda tipi tutarları ayrı ayrı kuruşa yuvarlanır; toplam fark birkaç kuruşu geçmez
        Assert.InRange(Math.Abs(r.Current.RoomRevenue - r.Days.Sum(d => d.RoomRevenue)), 0m, 1m);
        Assert.InRange(Math.Abs(r.Current.RoomRevenue - r.RoomTypes.Sum(t => t.Revenue)), 0m, 1m);
        Assert.Equal(1.0, r.RoomTypes.Sum(t => t.Share), 3);
        Assert.True(r.ExtraRevenue > 0);
        Assert.Equal(r.ExtraRevenue, r.Charges.Sum(c => c.Amount));
        Assert.True(r.Previous.SoldNights > 0);

        var invalid = await Sender.SendAsync(new GetRevenueReport(Today, Today.AddDays(-1)));
        Assert.True(invalid.IsFailure);
    }

    [Fact]
    public async Task Guest_Pareto_Classifies_Guests()
    {
        var r = (await Sender.SendAsync(new GetGuestPareto(Today.AddDays(-75), Today))).Value;

        Assert.True(r.GuestCount > 20);
        Assert.Equal(r.GuestCount, r.CountA + r.CountB + r.CountC);
        Assert.Equal(r.Total, r.Guests.Sum(g => g.Total));
        Assert.Equal(1.0, r.Guests[^1].Cumulative, 6);
        Assert.InRange(r.ValueShareA, 0.79, 0.95);
        Assert.True(r.CountShareA < 0.8);
        Assert.True(r.Guests.Zip(r.Guests.Skip(1)).All(p => p.First.Total >= p.Second.Total));
    }

    [Fact]
    public async Task Occupancy_Forecast_Respects_Bounds_And_Bookings()
    {
        var r = (await Sender.SendAsync(new GetOccupancyForecast(Horizon: 14, HistoryDays: 56))).Value;

        Assert.Equal(14, r.Days.Count);
        Assert.Equal(56, r.HistoryActual.Length);
        Assert.Equal(Today, r.Days[0].Date);
        Assert.All(r.Days, d =>
        {
            Assert.InRange(d.Forecast, 0, 1);
            Assert.True(d.Expected >= d.OnTheBooks);
            Assert.True(d.Lower <= d.Forecast && d.Forecast <= d.Upper);
        });
        Assert.InRange(r.HistoryAverage, 0.4, 0.98);
        Assert.False(string.IsNullOrEmpty(r.Method));
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
