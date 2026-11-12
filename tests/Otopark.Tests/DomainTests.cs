using OnderCode.Kernel.Exceptions;
using OnderCode.Kernel.Validation;
using Otopark.Domain;

namespace Otopark.Tests;

public sealed class TariffTests
{
    private static readonly DateTime T0 = new(2026, 11, 16, 9, 0, 0);
    private static readonly Tariff Car = Tariff.For(VehicleClass.Car); // 60 / 40 / 350, 15 dk ücretsiz

    private static decimal Fee(double minutes, bool washed = false, bool subscriber = false)
        => TariffEngine.Calculate(Car, T0, T0.AddMinutes(minutes), washed, subscriber).Total;

    [Theory]
    [InlineData(10, 0)]       // ücretsiz süre
    [InlineData(15, 0)]
    [InlineData(16, 60)]      // ilk saat
    [InlineData(60, 60)]
    [InlineData(61, 100)]     // başlayan ikinci saat
    [InlineData(180, 140)]
    [InlineData(600, 350)]    // günlük üst sınır
    [InlineData(1560, 430)]   // 26 saat: 1 gün + 2 ek saat
    public void Hourly_Tariff_With_Grace_And_DailyCap(double minutes, decimal expected) => Assert.Equal(expected, Fee(minutes));

    [Fact]
    public void Wash_Makes_First_Two_Hours_Free()
    {
        var fee = TariffEngine.Calculate(Car, T0, T0.AddMinutes(150), washed: true);
        Assert.Equal(140m, fee.Gross);          // 3 saat
        Assert.Equal(80m, fee.WashDiscount);    // kalan 30 dk = ilk saat 60
        Assert.Equal(60m, fee.Total);
        Assert.Equal(0m, Fee(110, washed: true));
    }

    [Fact]
    public void Subscriber_Pays_Nothing_But_Gross_Is_Visible()
    {
        var fee = TariffEngine.Calculate(Car, T0, T0.AddHours(5), subscriber: true);
        Assert.Equal(220m, fee.Gross);
        Assert.Equal(0m, fee.Total);
    }

    [Fact]
    public void Exit_Before_Entry_Is_Rejected()
        => Assert.Throws<ArgumentException>(() => TariffEngine.Calculate(Car, T0, T0.AddMinutes(-1)));
}

public sealed class PlateTests
{
    [Theory]
    [InlineData("34abc123", "34 ABC 123")]
    [InlineData("06-ab-1234", "06 AB 1234")]
    [InlineData("34 i 1234", "34 I 1234")]
    [InlineData(" 81 zzz 99 ", "81 ZZZ 99")]
    public void Normalizes_Common_Spellings(string input, string expected) => Assert.Equal(expected, TurkishPlate.Normalize(input));

    [Theory]
    [InlineData("00 AB 123")]   // il kodu yok
    [InlineData("82 AB 123")]
    [InlineData("34 A 123")]    // tek harfe 4 rakam
    [InlineData("34 ABCD 12")]
    [InlineData("34 ABC 1234")] // üç harfe en çok 3 rakam
    [InlineData("ABC 123")]
    [InlineData("")]
    public void Rejects_Invalid_Plates(string input) => Assert.False(TurkishPlate.IsValid(input));
}

public sealed class SpotAndWashTests
{
    private static readonly DateTime Now = new(2026, 11, 16, 10, 0, 0);

    [Fact]
    public void Spots_Accept_Only_Matching_Vehicles()
    {
        Assert.True(new ParkingSpot("A-03", "A", SpotKind.Standard).Accepts(VehicleClass.Car));
        Assert.False(new ParkingSpot("A-03", "A", SpotKind.Standard).Accepts(VehicleClass.Large));
        Assert.True(new ParkingSpot("C-01", "C", SpotKind.Large).Accepts(VehicleClass.Car));
        Assert.False(new ParkingSpot("M-01", "M", SpotKind.Motorcycle).Accepts(VehicleClass.Car));
        Assert.True(new ParkingSpot("A-01", "A", SpotKind.Disabled).Preference(VehicleClass.Car) > new ParkingSpot("A-05", "A", SpotKind.Standard).Preference(VehicleClass.Car));
    }

    [Fact]
    public void OutOfService_Spot_Cannot_Take_A_Vehicle()
    {
        var spot = new ParkingSpot("B-01", "B", SpotKind.Standard);
        spot.TakeOutOfService("Boya");
        var ex = Assert.Throws<DomainException>(() => new ParkingSession("34 AB 123", VehicleClass.Car, spot, Now));
        Assert.Contains("kullanım dışı", ex.Message);
    }

    [Fact]
    public void Wash_Order_Follows_Its_Workflow()
    {
        var package = new WashPackage("DIS", "Dış yıkama", 20, 250m, 350m, 150m, 1);
        var order = new WashOrder("34 AB 123", VehicleClass.Large, package, Now);
        Assert.Equal(350m, order.Price);

        Assert.Equal(WashStatus.Washing, order.Advance(Now.AddMinutes(5), "Murat"));
        Assert.Throws<DomainException>(() => order.Cancel());          // başlamış iş iptal edilmez
        Assert.Throws<DomainException>(() => order.DeliverWithExit(Now)); // bitmeden araç çıkamaz
        Assert.Equal(WashStatus.Ready, order.Advance(Now.AddMinutes(25)));
        Assert.Equal(WashStatus.Delivered, order.Advance(Now.AddMinutes(30)));
        Assert.Throws<DomainException>(() => order.Advance(Now.AddMinutes(31)));
        Assert.Equal("Murat", order.Washer);
    }

    [Fact]
    public void Package_Not_For_Motorcycles_Is_Rejected()
    {
        var detail = new WashPackage("DET", "Detaylı temizlik", 120, 1200m, 1600m, 0m, 4);
        Assert.Throws<DomainException>(() => new WashOrder("34 AB 123", VehicleClass.Motorcycle, detail, Now));
    }

    [Fact]
    public void Session_Closes_Once_With_Fixed_Fee()
    {
        var spot = new ParkingSpot("A-05", "A", SpotKind.Standard);
        var s = new ParkingSession("34abc123", VehicleClass.Car, spot, Now);
        Assert.Equal("34 ABC 123", s.Plate);
        var fee = s.Close(Now.AddMinutes(100), PaymentMethod.Card, washFee: 0m, washed: false);
        Assert.Equal(100m, fee.Total);
        Assert.Equal(100m, s.Total);
        Assert.Throws<DomainException>(() => s.Close(Now.AddHours(5), PaymentMethod.Cash, 0m, false));
    }

    [Fact]
    public void Subscription_Covers_Its_Months()
    {
        var sub = new Subscription("34 AB 123", "Test Abone", VehicleClass.Car, new DateOnly(2026, 11, 1), 2, 3000m);
        Assert.Equal(new DateOnly(2026, 12, 31), sub.EndDate);
        Assert.True(sub.IsActiveOn(new DateOnly(2026, 12, 31)));
        Assert.False(sub.IsActiveOn(new DateOnly(2027, 1, 1)));
        Assert.Equal(6000m, sub.TotalPaid);
        sub.Extend(1);
        Assert.Equal(new DateOnly(2027, 1, 31), sub.EndDate);
    }

    [Fact]
    public void Peak_Concurrency_Counts_Overlaps()
    {
        var day = new DateTime(2026, 11, 16);
        var stays = new[]
        {
            new StaySpan(day.AddHours(8), day.AddHours(12), VehicleClass.Car),
            new StaySpan(day.AddHours(9), day.AddHours(10), VehicleClass.Car),
            new StaySpan(day.AddHours(11), null, VehicleClass.Car),
            new StaySpan(day.AddHours(13), day.AddHours(14), VehicleClass.Car),
        };
        Assert.Equal(2, LotKpi.PeakConcurrent(day, day.AddDays(1), stays, day.AddHours(15)));
        var hourly = LotKpi.HourlyOccupancy(day.AddHours(8), 4, stays, day.AddHours(15));
        Assert.Equal(new[] { 1, 2, 1, 2 }, hourly);
    }
}
