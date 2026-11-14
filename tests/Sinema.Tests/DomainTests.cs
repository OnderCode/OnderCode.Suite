using OnderCode.Kernel.Exceptions;
using Sinema.Application;
using Sinema.Domain;

namespace Sinema.Tests;

public sealed class HallLayoutTests
{
    [Fact]
    public void Rows_Get_Letters_And_Seats_Are_Numbered_Left_To_Right()
    {
        var layout = HallLayout.Parse("SS.SS/......./VVVV/E.CC");
        Assert.Equal(4 + 4 + 3, layout.Capacity);
        Assert.Equal("A1", layout.Seats[0].Code);
        Assert.Equal("A4", layout.Seats[3].Code);
        // Boş satır (ara koridor) harf almaz: üçüncü satır "B" olur.
        Assert.Equal(SeatType.Vip, layout.Find("B2")!.Type);
        Assert.Equal(SeatType.Accessible, layout.Find("c1")!.Type);
        Assert.Equal(2, layout.Count(SeatType.Couple));
        Assert.Null(layout.Find("D1"));
    }

    [Fact]
    public void Invalid_Layouts_Are_Rejected()
    {
        Assert.Throws<DomainException>(() => HallLayout.Parse(""));
        Assert.Throws<DomainException>(() => HallLayout.Parse("...../....."));
        Assert.Throws<DomainException>(() => HallLayout.Parse("SSX"));
        Assert.Throws<DomainException>(() => HallLayout.Parse(new string('S', 41)));
    }

    [Fact]
    public void Designer_Operations_Return_New_Layouts()
    {
        var grid = HallLayout.Grid(3, 6, aisleAt: 2);
        Assert.Equal(15, grid.Capacity);
        var vip = grid.FillRow(2, 'V');
        Assert.Equal(6, vip.Count(SeatType.Vip));   // koridor hücresi de koltuk olur
        Assert.Equal(16, vip.Capacity);
        var bigger = vip.Resize(4, 6);
        Assert.Equal(4, bigger.Rows);
        Assert.Equal(vip.ToString() + "/SSSSSS", bigger.ToString());
        var gap = bigger.With(0, 0, '.');
        Assert.Equal("A1", gap.Seats[0].Code);
        Assert.Equal(1, gap.Seats[0].Col);
        Assert.Equal(HallLayout.Parse(gap.ToString()).ToString(), gap.ToString());
    }

    [Fact]
    public void Templates_Are_Valid()
    {
        foreach (var (name, text) in HallLayout.Templates)
        {
            var l = HallLayout.Parse(text);
            Assert.True(l.Capacity >= 20, name);
        }
    }
}

public sealed class SessionRuleTests
{
    static readonly CinemaSettings Settings = new();

    static (Film Film, Hall Hall) Make(int minutes = 100)
        => (new Film("Deneme", "Dram", minutes, AgeRating.General, new DateOnly(2026, 11, 1)), new Hall("Salon 1", "SSSS/SSSS"));

    [Fact]
    public void Session_End_Includes_Ads_And_Ready_Includes_Cleaning()
    {
        var (film, hall) = Make(100);
        var s = new Session(film, hall, new DateTime(2026, 11, 14, 20, 0, 0), SessionFormat.TwoD, SessionLanguage.Subtitled, Settings);
        Assert.Equal(new DateTime(2026, 11, 14, 21, 55, 0), s.End);
        Assert.Equal(new DateTime(2026, 11, 14, 22, 10, 0), s.ReadyAt);
    }

    [Fact]
    public void Conflicts_Are_Found_And_Suggestion_Is_Free()
    {
        var (film, hall) = Make(100);
        var day = new DateOnly(2026, 11, 14);
        var a = new Session(film, hall, day.ToDateTime(new TimeOnly(10, 0)), SessionFormat.TwoD, SessionLanguage.Dubbed, Settings);
        var list = new[] { a };
        Assert.Same(a, SessionRules.FindConflict(list, day.ToDateTime(new TimeOnly(11, 30)), day.ToDateTime(new TimeOnly(13, 0))));
        Assert.Null(SessionRules.FindConflict(list, a.ReadyAt, a.ReadyAt.AddHours(2)));
        var next = SessionRules.SuggestStart(list, day, Settings);
        Assert.True(next >= a.ReadyAt);
        Assert.Equal(0, next.Minute % 5);
        Assert.Equal(day.ToDateTime(Settings.OpensAt), SessionRules.SuggestStart([], day, Settings));
        a.Cancel();
        Assert.Null(SessionRules.FindConflict(list, day.ToDateTime(new TimeOnly(11, 0)), day.ToDateTime(new TimeOnly(12, 0))));
    }

    [Fact]
    public void ThreeD_Needs_A_3D_Hall()
    {
        var (film, hall) = Make();
        Assert.Throws<DomainException>(() => new Session(film, hall, new DateTime(2026, 11, 14, 20, 0, 0), SessionFormat.ThreeD, SessionLanguage.Dubbed, Settings));
    }

    [Fact]
    public void Door_Opens_Before_Start_And_Closes_At_End()
    {
        var (film, hall) = Make(100);
        var s = new Session(film, hall, new DateTime(2026, 11, 14, 20, 0, 0), SessionFormat.TwoD, SessionLanguage.Dubbed, Settings);
        Assert.NotNull(CheckInRules.WhyNot(s, new DateTime(2026, 11, 14, 19, 0, 0), Settings));
        Assert.Null(CheckInRules.WhyNot(s, new DateTime(2026, 11, 14, 19, 20, 0), Settings));
        Assert.Null(CheckInRules.WhyNot(s, new DateTime(2026, 11, 14, 20, 30, 0), Settings));
        Assert.NotNull(CheckInRules.WhyNot(s, new DateTime(2026, 11, 14, 22, 0, 0), Settings));
    }
}

public sealed class PricingTests
{
    static readonly PricingRules Rules = new();
    static readonly TicketType Full = new("Tam", 260m);
    static readonly DateTime Saturday = new(2026, 11, 14, 20, 0, 0);
    static readonly DateTime Wednesday = new(2026, 11, 18, 20, 0, 0);

    [Fact]
    public void Plain_Evening_Ticket_Is_Base_Price()
        => Assert.Equal(260m, PricingEngine.Quote(Full, SeatType.Standard, SessionFormat.TwoD, Saturday, Rules).Total);

    [Fact]
    public void Surcharges_Add_Up()
    {
        Assert.Equal(260m + 90m, PricingEngine.Quote(Full, SeatType.Vip, SessionFormat.TwoD, Saturday, Rules).Total);
        Assert.Equal(260m + 60m + 40m, PricingEngine.Quote(Full, SeatType.Couple, SessionFormat.ThreeD, Saturday, Rules).Total);
    }

    [Fact]
    public void Bigger_Discount_Wins()
    {
        var wed = PricingEngine.Quote(Full, SeatType.Standard, SessionFormat.TwoD, Wednesday, Rules);
        Assert.Equal(104m, wed.Discount);
        Assert.Equal(156m, wed.Total);
        var matinee = PricingEngine.Quote(Full, SeatType.Standard, SessionFormat.TwoD, Saturday.Date.AddHours(11), Rules);
        Assert.Equal("Matine", matinee.DiscountReason);
        Assert.Equal(220m, matinee.Total);
        var wedMorning = PricingEngine.Quote(Full, SeatType.Standard, SessionFormat.TwoD, Wednesday.Date.AddHours(11), Rules);
        Assert.Equal(104m, wedMorning.Discount);
    }

    [Fact]
    public void Ticket_Codes_Are_Readable_And_Normalized()
    {
        var codes = Enumerable.Range(0, 500).Select(_ => TicketCode.New()).ToList();
        Assert.All(codes, c => Assert.Matches("^[A-Z2-9]{4}-[A-Z2-9]{4}$", c));
        Assert.All(codes, c => Assert.DoesNotContain('O', c));
        Assert.True(codes.Distinct().Count() > 495);
        Assert.Equal("AB23-CD45", TicketCode.Normalize(" ab23 cd45 "));
        Assert.Equal("AB23-CD45", TicketCode.Normalize("AB23CD45"));
    }
}

public sealed class SeatHoldTests
{
    sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public void Holds_Are_Exclusive_And_Expire()
    {
        var clock = new Clock();
        var svc = new SeatHoldService(clock) { HoldMinutes = 5 };
        var changes = 0;
        svc.Changed += _ => changes++;
        var sold = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "A1" };

        Assert.False(svc.TryHold(1, "A1", "ali", sold));
        Assert.True(svc.TryHold(1, "b4", "ali", sold));
        Assert.False(svc.TryHold(1, "B4", "veli", sold));
        Assert.True(svc.TryHold(2, "B4", "veli", sold));
        Assert.Single(svc.Holds(1));
        Assert.Equal("B4", svc.Holds(1)[0].SeatCode);

        clock.Now = clock.Now.AddMinutes(4);
        svc.Extend("ali");
        clock.Now = clock.Now.AddMinutes(4);
        Assert.Single(svc.Holds(1));
        Assert.Empty(svc.Holds(2));

        svc.Release(1, "B4", "veli");
        Assert.Single(svc.Holds(1));
        svc.ReleaseAll("ali");
        Assert.Empty(svc.Holds(1));
        Assert.True(changes >= 3);
    }
}
