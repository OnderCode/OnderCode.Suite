using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using OnderCode.Application.Messaging;
using OnderCode.Application.Results;
using OnderCode.Data.Initialization;
using Otopark.Application;
using Otopark.Domain;
using Otopark.Infrastructure;

namespace Otopark.Tests;

/// <summary>Örnek veriyle dolu gerçek veritabanı üzerinde gişe, yıkama ve rapor akışları.</summary>
public sealed class OtoparkFlowTests : IAsyncLifetime
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
        services.AddOtopark(o => o.DataDirectory = _dir);
        _services = services.BuildServiceProvider();
        await _services.GetRequiredService<DatabaseInitializer<OtoparkDbContext>>().InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task DemoData_ProducesALivelyLot()
    {
        var d = (await Sender.SendAsync(new GetDashboard())).Value;
        Assert.Equal(74, d.Capacity);
        Assert.Equal(1, d.OutOfService);
        Assert.InRange(d.Inside, 5, 61);
        Assert.True(d.TodayEntries >= d.Inside - d.SubscribersInside - 10);
        Assert.Equal(30, d.ParkingTrend.Length);
        Assert.True(d.ParkingTrend.Sum() > 100_000);
        Assert.True(d.WashTrend.Sum() > 50_000);

        var board = (await Sender.SendAsync(new GetLotBoard())).Value;
        Assert.Equal(74, board.Count);
        Assert.Equal(d.Inside, board.Count(t => t.Occupied));
        Assert.DoesNotContain(board, t => t.Occupied && t.OutOfService);

        var inside = (await Sender.SendAsync(new ListSessions { Filter = SessionFilter.Inside, PageSize = 1000 })).Value;
        Assert.Equal(d.Inside, inside.TotalCount);
        // Listedeki o anki tutar haritadakiyle aynı
        foreach (var row in inside.Items)
            Assert.Equal(board.Single(t => t.SessionId == row.Id).CurrentFee ?? 0m, row.ParkingFee);
        Assert.Contains(inside.Items, r => r.ParkingFee > 0);
    }

    /// <summary>Örnek veri o saatte otoparkı doldurmuşsa yıkaması olmayan bir aracı çıkarıp yer açar.</summary>
    async Task EnsureFreeSpotAsync(VehicleClass vehicle)
    {
        if ((await Sender.SendAsync(new GetFreeSpots(vehicle))).Value.Count > 0) return;
        var board = (await Sender.SendAsync(new GetLotBoard())).Value;
        var leaving = board.First(t => t.Vehicle == vehicle && t.Wash is null && t.SessionId is not null);
        Assert.True((await Sender.SendAsync(new RegisterExit(leaving.SessionId!.Value, PaymentMethod.Cash))).IsSuccess);
    }

    [Fact]
    public async Task Entry_Wash_Exit_Flow()
    {
        await EnsureFreeSpotAsync(VehicleClass.Car);
        var packages = (await Sender.SendAsync(new GetWashPackages())).Value;
        var outside = packages.First(p => p.Code == "DIS");

        var entry = await Sender.SendAsync(new RegisterEntry("34 qwx 001", VehicleClass.Car, WashPackageId: outside.Id));
        Assert.True(entry.IsSuccess, entry.ErrorMessage);
        Assert.Equal("34 QWX 001", entry.Value.Plate);
        Assert.NotNull(entry.Value.WashOrderId);

        var tile = (await Sender.SendAsync(new GetLotBoard())).Value.Single(t => t.SessionId == entry.Value.SessionId);
        Assert.Equal(entry.Value.SpotCode, tile.Code);
        Assert.Equal(WashStatus.Waiting, tile.Wash);

        // Yıkama bitmeden çıkış yok
        var blocked = await Sender.SendAsync(new RegisterExit(entry.Value.SessionId, PaymentMethod.Card));
        Assert.True(blocked.IsFailure);
        Assert.Contains("yıkama", blocked.Errors[0].Message, StringComparison.OrdinalIgnoreCase);

        var washId = entry.Value.WashOrderId!.Value;
        Assert.Equal(WashStatus.Washing, (await Sender.SendAsync(new AdvanceWash(washId, "Murat"))).Value);
        Assert.Equal(WashStatus.Ready, (await Sender.SendAsync(new AdvanceWash(washId))).Value);
        Assert.True((await Sender.SendAsync(new AdvanceWash(washId))).IsFailure); // otoparktaki araç çıkışta teslim edilir

        var quote = (await Sender.SendAsync(new QuoteExit(entry.Value.SessionId))).Value;
        Assert.Null(quote.BlockReason);
        Assert.Equal(250m, quote.WashTotal);

        var receipt = await Sender.SendAsync(new RegisterExit(entry.Value.SessionId, PaymentMethod.Card));
        Assert.True(receipt.IsSuccess, receipt.ErrorMessage);
        Assert.Equal(250m, receipt.Value.WashFee);
        Assert.Equal(0m, receipt.Value.Fee.Total); // saat sabit: süre 0, ücretsiz dilimde
        Assert.Equal(PaymentMethod.Card, receipt.Value.Payment);

        var board = (await Sender.SendAsync(new GetWashBoard())).Value;
        Assert.Equal(WashStatus.Delivered, board.Single(w => w.Id == washId).Status);
        Assert.True((await Sender.SendAsync(new RegisterExit(entry.Value.SessionId, PaymentMethod.Cash))).IsFailure);
    }

    [Fact]
    public async Task Same_Plate_Cannot_Enter_Twice_And_Invalid_Plate_Is_Rejected()
    {
        await EnsureFreeSpotAsync(VehicleClass.Car);
        Assert.True((await Sender.SendAsync(new RegisterEntry("34 QWX 002", VehicleClass.Car))).IsSuccess);
        var again = await Sender.SendAsync(new RegisterEntry("34qwx002", VehicleClass.Car));
        Assert.True(again.IsFailure);
        Assert.Equal(ErrorCodes.Domain, again.Errors[0].Code);
        Assert.Contains("zaten içeride", again.Errors[0].Message);

        var invalid = await Sender.SendAsync(new RegisterEntry("ABC 12", VehicleClass.Car));
        Assert.True(invalid.IsFailure);
        Assert.Contains(invalid.Errors, e => e.Field == nameof(RegisterEntry.Plate));
    }

    [Fact]
    public async Task Large_Vehicle_Gets_A_Large_Spot_And_Busy_Spot_Cannot_Close()
    {
        await EnsureFreeSpotAsync(VehicleClass.Large);
        var result = await Sender.SendAsync(new RegisterEntry("34 QWX 003", VehicleClass.Large));
        Assert.True(result.IsSuccess, result.ErrorMessage);
        var entry = result.Value;
        Assert.StartsWith("C-", entry.SpotCode);

        var tile = (await Sender.SendAsync(new GetLotBoard())).Value.Single(t => t.SessionId == entry.SessionId);
        var close = await Sender.SendAsync(new SetSpotService(tile.SpotId, true, "Deneme"));
        Assert.True(close.IsFailure);
    }

    [Fact]
    public async Task Subscriber_Is_Recognized_And_Pays_Nothing()
    {
        var subs = (await Sender.SendAsync(new ListSubscriptions(SubscriptionFilter.Active))).Value;
        Assert.NotEmpty(subs);
        Assert.Contains((await Sender.SendAsync(new ListSubscriptions(SubscriptionFilter.Expiring))).Value, s => s.DaysLeft < 7);

        SubscriptionRow? free = null;
        foreach (var s in subs.Where(s => s.VehicleText == "Otomobil"))
            if ((await Sender.SendAsync(new FindActiveSession(s.Plate))).Value is null) { free = s; break; }
        Assert.NotNull(free);

        await EnsureFreeSpotAsync(VehicleClass.Car);
        var entry = (await Sender.SendAsync(new RegisterEntry(free!.Plate, VehicleClass.Car))).Value;
        Assert.True(entry.IsSubscriber);
        Assert.Equal(free.OwnerName, entry.SubscriberName);

        var receipt = (await Sender.SendAsync(new RegisterExit(entry.SessionId, PaymentMethod.Cash))).Value;
        Assert.Equal(0m, receipt.Total);
        Assert.Equal(PaymentMethod.Subscription, receipt.Payment);

        var dup = await Sender.SendAsync(new CreateSubscription(free.Plate, "Başka", VehicleClass.Car, 1));
        Assert.True(dup.IsFailure);
    }

    [Fact]
    public async Task Walk_In_Wash_Gets_An_Estimate()
    {
        var packages = (await Sender.SendAsync(new GetWashPackages())).Value;
        var id = (await Sender.SendAsync(new CreateWashOrder("35 QWX 77", VehicleClass.Car, packages.First(p => p.Code == "ICD").Id))).Value;
        var card = (await Sender.SendAsync(new GetWashBoard())).Value.Single(w => w.Id == id);
        Assert.False(card.InLot);
        Assert.Equal(450m, card.Price);
        Assert.True(card.EstimatedReady > _time.Now());

        Assert.True((await Sender.SendAsync(new CancelWash(id))).IsSuccess);
        Assert.True((await Sender.SendAsync(new CreateWashOrder("35 QWX 77", VehicleClass.Car, packages[0].Id))).IsSuccess);
    }

    [Fact]
    public async Task Revenue_Report_Is_Consistent()
    {
        var r = (await Sender.SendAsync(new GetRevenueReport(Today.AddDays(-28), Today.AddDays(-1)))).Value;
        Assert.Equal(28, r.Days.Count);
        Assert.Equal(r.Parking, r.Days.Sum(d => d.ParkingRevenue));
        Assert.Equal(r.Wash, r.Days.Sum(d => d.WashRevenue));
        Assert.Equal(r.Sessions, r.Days.Sum(d => d.Exits));
        Assert.Equal(1.0, r.Packages.Sum(p => p.Share), 6);
        Assert.Equal(1.0, r.Vehicles.Sum(v => v.Share), 6);
        Assert.Equal("A", r.Packages[0].Class);
        Assert.True(r.PreviousTotal > 0);
        Assert.True(r.SubscriberVisits > 0);
        Assert.Equal(r.Parking, r.Vehicles.Sum(v => v.Revenue));
    }

    [Fact]
    public async Task Peak_Hours_Show_Weekday_Mornings_Busier_Than_Sunday_Nights()
    {
        var r = (await Sender.SendAsync(new GetPeakHours(Today.AddDays(-28), Today.AddDays(-1)))).Value;
        Assert.Equal(24, r.Hours.Count);
        var tuesday11 = r.Matrix[1, 11];
        var sunday23 = r.Matrix[6, 23];
        Assert.True(tuesday11 > sunday23 * 2, $"Salı 11:00 {tuesday11}, Pazar 23:00 {sunday23}");
        Assert.Equal(5, r.TopSlots.Count);
        Assert.DoesNotContain(r.TopSlots, s => s.DayName == "Paz");
    }

    [Fact]
    public async Task Revenue_Forecast_Covers_The_Horizon()
    {
        var r = (await Sender.SendAsync(new GetRevenueForecast(14, 56))).Value;
        Assert.Equal(14, r.Days.Count);
        Assert.Equal(56, r.HistoryRevenue.Length);
        Assert.All(r.Days, d => Assert.True(d.Forecast >= 0 && d.Lower <= d.Forecast && d.Forecast <= d.Upper));
        Assert.True(r.ForecastAverage > r.HistoryAverage * 0.5 && r.ForecastAverage < r.HistoryAverage * 1.5);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
