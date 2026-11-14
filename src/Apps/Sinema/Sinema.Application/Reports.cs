using OnderCode.Application.Messaging;
using OnderCode.Application.Results;
using OnderCode.Kernel.Persistence;
using OnderCode.Kernel.Specifications;
using Sinema.Domain;

namespace Sinema.Application;

public sealed record DailyRow(DateOnly Day, int Sessions, int Tickets, decimal TicketRevenue, decimal ConcessionRevenue, double Occupancy)
{
    public decimal Total => TicketRevenue + ConcessionRevenue;
    public string DayText => $"{Day:dd.MM} {Texts.DayShort(Day)}";
}

public sealed record FilmReportRow(int FilmId, string Title, int Sessions, int Tickets, decimal Revenue, double Occupancy, decimal AveragePrice, double Share);

public sealed record HallReportRow(int HallId, string Hall, int Sessions, int Seats, int Tickets, decimal Revenue, double Occupancy);

public sealed record TypeReportRow(string Name, int Tickets, decimal Revenue, double Share);

public sealed record ConcessionReportRow(string Name, string Category, int Quantity, decimal Revenue);

public sealed record ReportView(DateOnly From, DateOnly To, IReadOnlyList<DailyRow> Daily, IReadOnlyList<FilmReportRow> Films, IReadOnlyList<HallReportRow> Halls,
    IReadOnlyList<TypeReportRow> TicketTypes, IReadOnlyList<ConcessionReportRow> Concessions, IReadOnlyList<string> SlotNames,
    double[,] Heatmap, int OnlineTickets, int BoxOfficeTickets, decimal CashRevenue, decimal CardRevenue, decimal OnlineRevenue, decimal SpendPerHead, int Refunds)
{
    public int Tickets => Daily.Sum(d => d.Tickets);
    public decimal TicketRevenue => Daily.Sum(d => d.TicketRevenue);
    public decimal ConcessionRevenue => Daily.Sum(d => d.ConcessionRevenue);
    public double Occupancy => Halls.Sum(h => h.Seats) == 0 ? 0 : Halls.Sum(h => h.Tickets) / (double)Halls.Sum(h => h.Seats);
}

public sealed record GetReport(DateOnly From, DateOnly To) : IQuery<ReportView>;

public sealed class GetReportHandler(IUnitOfWorkFactory uowFactory) : IQueryHandler<GetReport, ReportView>
{
    /// <summary>Gün içi dilimler (seans başlangıç saatine göre).</summary>
    public static readonly (string Name, int From, int To)[] Slots = [("10–13", 0, 13), ("13–16", 13, 16), ("16–19", 16, 19), ("19–22", 19, 22), ("22+", 22, 30)];

    public async Task<Result<ReportView>> HandleAsync(GetReport q, CancellationToken cancellationToken)
    {
        var from = q.From.Start();
        var to = q.To.AddDays(1).Start();
        await using var uow = uowFactory.Create();
        var sessions = await SessionQueries.LoadAsync(uow, from, to, null, null, false, cancellationToken);
        var tickets = await uow.Repository<Ticket>().ListAsync(new Specification<Ticket>().Where(t => t.Status != TicketStatus.Refunded && t.Session!.Start >= from && t.Session.Start < to),
            t => new { t.TicketTypeName, t.Price, t.Order!.Channel }, cancellationToken);
        var orders = await uow.Repository<Order>().ListAsync(new Specification<Order>().Where(o => o.SoldAt >= from && o.SoldAt < to),
            o => new { o.SoldAt, o.ConcessionTotal, o.TicketTotal, o.Payment, o.Status }, cancellationToken);
        var lines = await uow.Repository<ConcessionLine>().ListAsync(new Specification<ConcessionLine>().Where(l => l.Order!.SoldAt >= from && l.Order.SoldAt < to && l.Order.Status == OrderStatus.Completed),
            l => new { l.Name, l.Category, l.Quantity, l.UnitPrice }, cancellationToken);
        var done = orders.Where(o => o.Status == OrderStatus.Completed).ToList();

        var daily = new List<DailyRow>();
        for (var d = q.From; d <= q.To; d = d.AddDays(1))
        {
            var ss = sessions.Where(s => s.Day == d).ToList();
            var cap = ss.Sum(s => s.Capacity);
            daily.Add(new DailyRow(d, ss.Count, ss.Sum(s => s.Sold), ss.Sum(s => s.Revenue),
                done.Where(o => DateOnly.FromDateTime(o.SoldAt) == d).Sum(o => o.ConcessionTotal), cap == 0 ? 0 : ss.Sum(s => s.Sold) / (double)cap));
        }

        var totalRevenue = sessions.Sum(s => s.Revenue);
        var films = sessions.GroupBy(s => s.FilmId).Select(g =>
        {
            var cap = g.Sum(s => s.Capacity);
            var sold = g.Sum(s => s.Sold);
            var rev = g.Sum(s => s.Revenue);
            return new FilmReportRow(g.Key, g.First().FilmTitle, g.Count(), sold, rev, cap == 0 ? 0 : sold / (double)cap, sold == 0 ? 0 : Math.Round(rev / sold, 2),
                totalRevenue == 0 ? 0 : (double)(rev / totalRevenue));
        }).OrderByDescending(f => f.Revenue).ToList();

        var halls = sessions.GroupBy(s => s.HallId).Select(g => new HallReportRow(g.Key, g.First().HallName, g.Count(), g.Sum(s => s.Capacity), g.Sum(s => s.Sold),
            g.Sum(s => s.Revenue), g.Sum(s => s.Capacity) == 0 ? 0 : g.Sum(s => s.Sold) / (double)g.Sum(s => s.Capacity))).OrderBy(h => h.Hall).ToList();

        var types = tickets.GroupBy(t => t.TicketTypeName).Select(g => new TypeReportRow(g.Key, g.Count(), g.Sum(t => t.Price), tickets.Count == 0 ? 0 : g.Count() / (double)tickets.Count))
            .OrderByDescending(t => t.Tickets).ToList();

        var heat = new double[7, Slots.Length];
        for (var dow = 0; dow < 7; dow++)
        {
            for (var k = 0; k < Slots.Length; k++)
            {
                var slot = Slots[k];
                var ss = sessions.Where(s => ((int)s.Start.DayOfWeek + 6) % 7 == dow && s.Start.Hour >= slot.From && s.Start.Hour < slot.To).ToList();
                var cap = ss.Sum(s => s.Capacity);
                heat[dow, k] = cap == 0 ? -1 : ss.Sum(s => s.Sold) / (double)cap;
            }
        }

        var concessions = lines.GroupBy(l => l.Name).Select(g => new ConcessionReportRow(g.Key, Texts.Of(g.First().Category), g.Sum(l => l.Quantity), g.Sum(l => l.Quantity * l.UnitPrice)))
            .OrderByDescending(c => c.Revenue).ToList();
        var attendance = sessions.Sum(s => s.Sold);

        return new ReportView(q.From, q.To, daily, films, halls, types, concessions, Slots.Select(s => s.Name).ToList(), heat,
            tickets.Count(t => t.Channel == SalesChannel.Online), tickets.Count(t => t.Channel == SalesChannel.BoxOffice),
            done.Where(o => o.Payment == PaymentMethod.Cash).Sum(o => o.TicketTotal + o.ConcessionTotal),
            done.Where(o => o.Payment == PaymentMethod.Card).Sum(o => o.TicketTotal + o.ConcessionTotal),
            done.Where(o => o.Payment == PaymentMethod.Online).Sum(o => o.TicketTotal + o.ConcessionTotal),
            attendance == 0 ? 0 : Math.Round(done.Sum(o => o.ConcessionTotal) / attendance, 2),
            orders.Count(o => o.Status == OrderStatus.Refunded));
    }
}
