using OnderCode.Application.Messaging;
using OnderCode.Application.Results;
using OnderCode.Kernel.Persistence;
using OnderCode.Kernel.Specifications;
using Sinema.Domain;

namespace Sinema.Application;

public sealed record FilmStat(int FilmId, string Title, string? Poster, string Accent, int Tickets, decimal Revenue, double Occupancy);

public sealed record DashboardView(
    int TicketsToday, int TicketsYesterday, decimal RevenueToday, decimal RevenueYesterday, decimal ConcessionToday, decimal ConcessionYesterday,
    double OccupancyToday, double OccupancyYesterday, double OnlineShareToday, int SessionsToday, int CheckedInToday,
    IReadOnlyList<SessionRow> NowPlaying, IReadOnlyList<SessionRow> Upcoming, IReadOnlyList<DateOnly> Days, IReadOnlyList<double> TicketRevenue14,
    IReadOnlyList<double> ConcessionRevenue14, IReadOnlyList<FilmStat> TopFilms, IReadOnlyList<OrderRow> RecentOrders);

public sealed record GetDashboard : IQuery<DashboardView>;

public sealed class GetDashboardHandler(IUnitOfWorkFactory uowFactory, TimeProvider time) : IQueryHandler<GetDashboard, DashboardView>
{
    public async Task<Result<DashboardView>> HandleAsync(GetDashboard q, CancellationToken cancellationToken)
    {
        var now = time.Now();
        var today = time.Today();
        await using var uow = uowFactory.Create();
        var from14 = today.AddDays(-13).Start();
        var orders = await uow.Repository<Order>().ListAsync(new Specification<Order>().Where(o => o.SoldAt >= from14 && o.Status == OrderStatus.Completed),
            o => new { o.SoldAt, o.TicketTotal, o.ConcessionTotal, o.Channel, Count = o.Tickets.Count }, cancellationToken);
        var sessions = await SessionQueries.LoadAsync(uow, today.AddDays(-7).Start(), today.AddDays(2).Start(), null, null, false, cancellationToken);
        var checkedIn = await uow.Repository<Ticket>().CountAsync(new Specification<Ticket>().Where(t => t.UsedAt >= today.Start()), cancellationToken);

        double Occ(DateOnly d, bool untilNow)
        {
            var list = sessions.Where(s => s.Day == d && (!untilNow || s.Start <= now.AddHours(3))).ToList();
            var cap = list.Sum(s => s.Capacity);
            return cap == 0 ? 0 : list.Sum(s => s.Sold) / (double)cap;
        }

        var days = Enumerable.Range(0, 14).Select(i => today.AddDays(-13 + i)).ToList();
        var byDay = orders.GroupBy(o => DateOnly.FromDateTime(o.SoldAt)).ToDictionary(g => g.Key, g => g.ToList());
        var t = byDay.GetValueOrDefault(today) ?? [];
        var y = byDay.GetValueOrDefault(today.AddDays(-1))?.Where(o => o.SoldAt.TimeOfDay <= now.TimeOfDay).ToList() ?? [];

        var week = sessions.Where(s => s.Day >= today.AddDays(-6) && s.Day <= today).ToList();
        var top = week.GroupBy(s => s.FilmId).Select(g => new FilmStat(g.Key, g.First().FilmTitle, g.First().Poster, g.First().Accent, g.Sum(s => s.Sold), g.Sum(s => s.Revenue),
            g.Sum(s => s.Capacity) == 0 ? 0 : g.Sum(s => s.Sold) / (double)g.Sum(s => s.Capacity))).OrderByDescending(f => f.Revenue).Take(5).ToList();

        var recent = await new ListOrdersHandler(uowFactory).HandleAsync(new ListOrders(today, today), cancellationToken);

        return new DashboardView(
            sessions.Where(s => s.Day == today).Sum(s => s.Sold), sessions.Where(s => s.Day == today.AddDays(-1)).Sum(s => s.Sold),
            t.Sum(o => o.TicketTotal + o.ConcessionTotal), y.Sum(o => o.TicketTotal + o.ConcessionTotal), t.Sum(o => o.ConcessionTotal), y.Sum(o => o.ConcessionTotal),
            Occ(today, false), Occ(today.AddDays(-1), false),
            t.Sum(o => o.Count) == 0 ? 0 : t.Where(o => o.Channel == SalesChannel.Online).Sum(o => o.Count) / (double)t.Sum(o => o.Count),
            sessions.Count(s => s.Day == today), checkedIn,
            sessions.Where(s => s.Start <= now && s.End > now).OrderBy(s => s.End).ToList(),
            sessions.Where(s => s.Start > now).OrderBy(s => s.Start).Take(8).ToList(),
            days,
            days.Select(d => (double)(byDay.GetValueOrDefault(d)?.Sum(o => o.TicketTotal) ?? 0)).ToList(),
            days.Select(d => (double)(byDay.GetValueOrDefault(d)?.Sum(o => o.ConcessionTotal) ?? 0)).ToList(),
            top,
            recent.IsSuccess ? recent.Value.Take(8).ToList() : []);
    }
}
