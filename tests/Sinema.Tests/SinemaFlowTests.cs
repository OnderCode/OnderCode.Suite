using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using OnderCode.Application.Messaging;
using OnderCode.Data.Initialization;
using Sinema.Application;
using Sinema.Domain;
using Sinema.Infrastructure;

namespace Sinema.Tests;

/// <summary>Örnek veriyle dolu gerçek SQLite veritabanı üzerinde seans, satış, kapı ve rapor akışları.</summary>
public sealed class SinemaFlowTests : IAsyncLifetime
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ondercode-tests", Guid.NewGuid().ToString("N"));
    private readonly FixedTime _time = new(new DateTimeOffset(2026, 11, 14, 15, 0, 0, TimeSpan.Zero));
    private ServiceProvider _services = null!;

    private ISender Sender => _services.GetRequiredService<ISender>();
    private DateOnly Today => _time.Today();

    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(_time);
        services.AddSinema(Path.Combine(_dir, "uploads"), o => o.DataDirectory = _dir);
        _services = services.BuildServiceProvider();
        await _services.GetRequiredService<DatabaseInitializer<SinemaDbContext>>().InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private async Task<T> Ok<T>(IRequest<OnderCode.Application.Results.Result<T>> request)
    {
        var r = await Sender.SendAsync(request);
        Assert.True(r.IsSuccess, string.Join(" | ", r.Errors.Select(e => e.Message)));
        return r.Value;
    }

    private async Task<SessionRow> FutureSession(Func<SessionRow, bool>? filter = null)
    {
        var sessions = await Ok(new ListSessions(Today, Today.AddDays(3)));
        return sessions.Where(s => s.Start > _time.Now().AddHours(2) && (filter?.Invoke(s) ?? true)).OrderBy(s => s.Start).First();
    }

    [Fact]
    public async Task DemoData_Is_Lively()
    {
        var films = await Ok(new ListFilms());
        Assert.Equal(12, films.Count);
        Assert.Equal(2, films.Count(f => f.Status == FilmStatus.ComingSoon));
        Assert.All(films, f => Assert.StartsWith("posters/", f.PosterPath));

        var halls = await Ok(new ListHalls());
        Assert.Equal(6, halls.Count);
        Assert.Contains(halls, h => h.Vip > 0);

        var d = await Ok(new GetDashboard());
        Assert.True(d.SessionsToday > 10);
        Assert.True(d.TicketRevenue14.Sum() > 100_000);
        Assert.True(d.ConcessionRevenue14.Sum() > 10_000);
        Assert.Equal(14, d.Days.Count);
        Assert.NotEmpty(d.TopFilms);

        var report = await Ok(new GetReport(Today.AddDays(-20), Today));
        Assert.True(report.Tickets > 2000);
        Assert.InRange(report.Occupancy, 0.15, 0.9);
        Assert.True(report.Refunds > 0);
        Assert.True(report.OnlineTickets > 0 && report.BoxOfficeTickets > 0);

        var program = await Ok(new GetProgram(Today, 3));
        Assert.NotEmpty(program.NowShowing);
        Assert.Equal(2, program.ComingSoon.Count);
    }

    [Fact]
    public async Task Sell_CheckIn_Refund_Flow()
    {
        var session = await FutureSession(s => s.Free >= 10);
        var map = await Ok(new GetSeatMap(session.Id));
        var layout = HallLayout.Parse(map.Layout);
        var free = layout.Seats.Where(s => !map.SoldSeats.Contains(s.Code)).Take(2).ToList();
        Assert.Equal(2, free.Count);
        var types = (await Ok(new GetPricing())).Types;
        var items = await Ok(new ListConcessions(ActiveOnly: true));

        var code = await Ok(new SellTickets(session.Id, free.Select(s => new SeatPick(s.Code, types[0].Id)).ToList(),
            [new ConcessionPick(items[0].Id, 2)], SalesChannel.BoxOffice, PaymentMethod.Card, null, null, null));
        var order = await Ok(new GetOrder(code));
        Assert.Equal(2, order.Tickets.Count);
        Assert.Equal(order.TicketTotal + items[0].Price * 2, order.Total);

        // Aynı koltuk ikinci kez satılamaz.
        var again = await Sender.SendAsync(new SellTickets(session.Id, [new SeatPick(free[0].Code, types[0].Id)], [], SalesChannel.Online, PaymentMethod.Online, "Deneme Kişi", null, null));
        Assert.False(again.IsSuccess);

        // Kapı henüz açılmadı.
        var early = await Sender.SendAsync(new CheckInTicket(code, null));
        Assert.False(early.IsSuccess);

        // Seans saatine gel: giriş başarılı, ikinci okutmada uyarı.
        _time.Set(new DateTimeOffset(session.Start.AddMinutes(-10), TimeSpan.Zero));
        var ok = await Ok(new CheckInTicket(TicketCode.Normalize(code.ToLowerInvariant().Replace("-", " ")), null));
        Assert.Equal(2, ok.Admitted);
        var twice = await Sender.SendAsync(new CheckInTicket(code, null));
        Assert.True(!twice.IsSuccess || twice.Value.Admitted == 0);

        // Kullanılmış bilet iade edilemez.
        Assert.False((await Sender.SendAsync(new RefundOrder(code))).IsSuccess);
    }

    [Fact]
    public async Task Refund_Frees_Seats()
    {
        var session = await FutureSession(s => s.Free >= 10);
        var map = await Ok(new GetSeatMap(session.Id));
        var seat = HallLayout.Parse(map.Layout).Seats.First(s => !map.SoldSeats.Contains(s.Code));
        var types = (await Ok(new GetPricing())).Types;
        var code = await Ok(new SellTickets(session.Id, [new SeatPick(seat.Code, types[0].Id)], [], SalesChannel.Online, PaymentMethod.Online, "Ayşe Deneme", null, null));
        Assert.Contains(seat.Code, (await Ok(new GetSeatMap(session.Id))).SoldSeats);
        var refund = await Sender.SendAsync(new RefundOrder(code));
        Assert.True(refund.IsSuccess, string.Join(" | ", refund.Errors.Select(e => e.Message)));
        Assert.DoesNotContain(seat.Code, (await Ok(new GetSeatMap(session.Id))).SoldSeats);
        // İade edilen koltuk yeniden satılabilir (kısmi benzersiz indeks).
        await Ok(new SellTickets(session.Id, [new SeatPick(seat.Code, types[0].Id)], [], SalesChannel.BoxOffice, PaymentMethod.Cash, null, null, null));
    }

    [Fact]
    public async Task Overlapping_Session_Is_Rejected_And_Suggestion_Works()
    {
        var existing = await FutureSession();
        var films = await Ok(new ListFilms());
        var film = films.First(f => f.Status == FilmStatus.NowShowing);
        var clash = await Sender.SendAsync(new CreateSessions(film.Id, existing.HallId, existing.Day, TimeOnly.FromDateTime(existing.Start.AddMinutes(20)), SessionFormat.TwoD, SessionLanguage.Dubbed, 1));
        Assert.False(clash.IsSuccess);

        var day = Today.AddDays(10);
        var start = await Ok(new SuggestSessionStart(existing.HallId, day));
        var created = await Ok(new CreateSessions(film.Id, existing.HallId, day, start, SessionFormat.TwoD, SessionLanguage.Subtitled, 3));
        Assert.Equal(3, created);
        var next = await Ok(new SuggestSessionStart(existing.HallId, day));
        Assert.True(next > start);
    }

    [Fact]
    public async Task Cancel_Session_Refunds_Tickets()
    {
        var session = await FutureSession(s => s.Sold > 0);
        var refunded = await Ok(new CancelSession(session.Id));
        Assert.True(refunded > 0);
        var after = await Ok(new ListSessions(session.Day, session.Day, IncludeCancelled: true));
        Assert.True(after.Single(s => s.Id == session.Id).IsCancelled);
        Assert.Empty((await Ok(new GetSeatMap(session.Id))).SoldSeats);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
        public void Set(DateTimeOffset value) => _now = value;
    }
}
