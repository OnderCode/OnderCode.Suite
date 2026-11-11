using OnderCode.Kernel.Exceptions;
using Otel.Domain;

namespace Otel.Tests;

public sealed class PricingEngineTests
{
    private static readonly DateOnly Mon = new(2026, 11, 16); // pazartesi

    [Fact]
    public void WeekdayNights_UseBaseRate()
    {
        var q = PricingEngine.Quote(1000m, Mon, Mon.AddDays(3), Mon, [], PricingPolicy.Default);
        Assert.Equal(3, q.NightCount);
        Assert.Equal(3000m, q.Total);
        Assert.All(q.Nights, n => Assert.False(n.IsWeekend));
    }

    [Fact]
    public void FridayAndSaturdayNights_AreWeekend()
    {
        var friday = Mon.AddDays(4);
        var q = PricingEngine.Quote(1000m, friday, friday.AddDays(3), friday, [], PricingPolicy.Default); // cuma, cumartesi, pazar
        Assert.Equal(new[] { 1200m, 1200m, 1000m }, q.Nights.Select(n => n.Price));
        Assert.Equal(3400m, q.Total);
    }

    [Fact]
    public void Season_MultipliesAndHighestWinsOnOverlap()
    {
        var seasons = new[]
        {
            new Season("Kış", Mon, Mon.AddDays(10), 1.10m),
            new Season("Yılbaşı", Mon.AddDays(1), Mon.AddDays(1), 1.50m),
        };
        var q = PricingEngine.Quote(1000m, Mon, Mon.AddDays(2), Mon, seasons, PricingPolicy.Default);
        Assert.Equal(new[] { 1100m, 1500m }, q.Nights.Select(n => n.Price));
        Assert.Equal("Yılbaşı", q.Nights[1].SeasonName);
    }

    [Fact]
    public void EarlyBooking_GivesDiscount_AtThreshold()
    {
        var late = PricingEngine.Quote(1000m, Mon, Mon.AddDays(2), Mon.AddDays(-29), [], PricingPolicy.Default);
        var early = PricingEngine.Quote(1000m, Mon, Mon.AddDays(2), Mon.AddDays(-30), [], PricingPolicy.Default);
        Assert.Equal(0m, late.EarlyBookingDiscount);
        Assert.Equal(200m, early.EarlyBookingDiscount);
        Assert.Equal(1800m, early.Total);
    }
}

public sealed class ReservationRulesTests
{
    private static readonly DateOnly D = new(2026, 11, 16);
    private static readonly RoomType Std = new("STD", "Standart", 2, 1000m);

    private static Room NewRoom() => new("101", 1, Std);

    private static Reservation NewReservation(Room room, DateOnly checkIn, DateOnly checkOut, int adults = 2)
        => new("R26-00001", new Guest("Deneme Misafir"), room, checkIn, checkOut, adults, 1000m, checkIn.AddDays(-5));

    [Fact]
    public void SameDayTurnover_IsNotAConflict()
    {
        var room = NewRoom();
        var existing = NewReservation(room, D, D.AddDays(3));
        OverbookingRule.EnsureFree(room, D.AddDays(3), D.AddDays(5), [existing]);
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 6)]
    [InlineData(-3, 9)]
    public void OverlappingDates_AreRejected(int fromOffset, int toOffset)
    {
        var room = NewRoom();
        var existing = NewReservation(room, D, D.AddDays(3));
        var ex = Assert.Throws<DomainException>(() => OverbookingRule.EnsureFree(room, D.AddDays(fromOffset), D.AddDays(toOffset), [existing]));
        Assert.Contains("dolu", ex.Message);
    }

    [Fact]
    public void CancelledReservation_DoesNotBlock()
    {
        var room = NewRoom();
        var existing = NewReservation(room, D, D.AddDays(3));
        existing.Cancel();
        OverbookingRule.EnsureFree(room, D, D.AddDays(3), [existing]);
    }

    [Fact]
    public void Capacity_IsEnforced()
        => Assert.Throws<DomainException>(() => NewReservation(NewRoom(), D, D.AddDays(1), adults: 3));

    [Fact]
    public void Lifecycle_CheckInThenCheckOut_UpdatesRoomAndLoyalty()
    {
        var room = NewRoom();
        var r = NewReservation(room, D, D.AddDays(4));

        Assert.Throws<DomainException>(() => r.CheckInGuest(D.AddDays(-1)));
        r.CheckInGuest(D);
        Assert.Equal(RoomStatus.Occupied, room.Status);

        r.PostCharge(ChargeCategory.Minibar, "Minibar", 250m, DateTime.UtcNow);
        Assert.Equal(1250m, r.Balance);

        r.CheckOutGuest(pointsPerNight: 10);
        Assert.Equal(RoomStatus.Dirty, room.Status);
        Assert.Equal(40, r.Guest.LoyaltyPoints);
        Assert.Equal(1, r.Guest.StayCount);
        Assert.Throws<DomainException>(() => r.PostCharge(ChargeCategory.Minibar, "Geç", 10m, DateTime.UtcNow));
    }

    [Fact]
    public void Maintenance_BlocksSaleAndCheckIn()
    {
        var room = NewRoom();
        room.StartMaintenance("Klima");
        Assert.Throws<DomainException>(() => OverbookingRule.EnsureFree(room, D, D.AddDays(1), []));
        room.MarkClean();
        Assert.Equal(RoomStatus.Available, room.Status);
    }
}

public sealed class HotelKpiTests
{
    private static readonly DateOnly D = new(2026, 11, 1);

    [Fact]
    public void Occupancy_Adr_RevPar()
    {
        // 2 oda, 10 gün = 20 oda-gece. 5 gecelik 5000 ₺ + aralığın yarısına düşen 4 gecelik 4000 ₺
        var stays = new[]
        {
            new StaySlice(D, D.AddDays(5), 5000m),
            new StaySlice(D.AddDays(8), D.AddDays(12), 4000m),
        };
        var k = HotelKpi.Calculate(roomCount: 2, D, D.AddDays(10), stays);
        Assert.Equal(7, k.SoldNights);
        Assert.Equal(20, k.AvailableNights);
        Assert.Equal(0.35m, k.Occupancy);
        Assert.Equal(7000m, k.RoomRevenue);
        Assert.Equal(1000m, k.Adr);
        Assert.Equal(350m, k.RevPar);
        Assert.Equal(k.Occupancy * k.Adr, k.RevPar);
    }

    [Fact]
    public void DailyOccupancy_CountsCheckInDayButNotCheckOutDay()
    {
        var stays = new[] { new StaySlice(D, D.AddDays(2), 2000m) };
        var daily = HotelKpi.DailyOccupancy(1, D, 3, stays);
        Assert.Equal(new[] { 1.0, 1.0, 0.0 }, daily);
    }
}
