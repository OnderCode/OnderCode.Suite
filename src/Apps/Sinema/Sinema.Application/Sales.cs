using FluentValidation;
using OnderCode.Application.Messaging;
using OnderCode.Application.Results;
using OnderCode.Kernel.Exceptions;
using OnderCode.Kernel.Persistence;
using OnderCode.Kernel.Specifications;
using Sinema.Domain;

namespace Sinema.Application;

public sealed record SeatPick(string SeatCode, int TicketTypeId);

public sealed record ConcessionPick(int ItemId, int Quantity);

/// <summary>Gişe ya da online satış. Koltuklar aynı anda başkasına satılamaz (veritabanında benzersiz indeks de var).</summary>
public sealed record SellTickets(int? SessionId, IReadOnlyList<SeatPick> Seats, IReadOnlyList<ConcessionPick> Items, SalesChannel Channel, PaymentMethod Payment,
    string? CustomerName, int? StaffId, string? HolderId) : ICommand<string>;

public sealed class SellTicketsValidator : AbstractValidator<SellTickets>
{
    public SellTicketsValidator()
    {
        RuleFor(x => x).Must(x => x.Seats.Count > 0 || x.Items.Count > 0).WithMessage("Sepet boş.");
        RuleFor(x => x.Seats.Count).LessThanOrEqualTo(12).WithMessage("Tek seferde en fazla 12 bilet alınabilir.");
        RuleFor(x => x.CustomerName).NotEmpty().MaximumLength(60).When(x => x.Channel == SalesChannel.Online).WithName("Ad soyad");
        RuleFor(x => x.SessionId).NotNull().When(x => x.Seats.Count > 0).WithMessage("Bilet için seans seçilmeli.");
    }
}

public sealed class SellTicketsHandler(IUnitOfWorkFactory uowFactory, TimeProvider time, SeatHoldService holds) : ICommandHandler<SellTickets, string>
{
    public async Task<Result<string>> HandleAsync(SellTickets c, CancellationToken cancellationToken)
    {
        var now = time.Now();
        await using var uow = uowFactory.Create();
        var settings = await SettingsQueries.GetAsync(uow, cancellationToken);
        Session? session = null;
        if (c.SessionId is int sid)
        {
            session = await uow.Repository<Session>().FirstOrDefaultAsync(new Specification<Session>().Where(s => s.Id == sid).Include(s => s.Hall).Include(s => s.Film).Tracking(), cancellationToken);
            if (session is null) return Error.NotFound("Seans bulunamadı.");
            if (session.IsCancelled) return Error.Business("Bu seans iptal edildi.");
            if (now > session.End) return Error.Business("Bu seans bitti.");
            if (c.Channel == SalesChannel.Online && now > session.Start.AddMinutes(-settings.OnlineSalesCloseMinutes))
                return Error.Business("Bu seansın online satışı kapandı.");
        }

        var layout = session?.Hall!.ParsedLayout();
        var sold = session is null ? [] : await SessionQueries.SoldSeatsAsync(uow, session.Id, cancellationToken);
        var held = session is null ? [] : holds.Holds(session.Id);
        var rules = await SettingsQueries.RulesAsync(uow, cancellationToken);
        var types = (await uow.Repository<TicketType>().ListAsync(new Specification<TicketType>().Tracking(), cancellationToken)).ToDictionary(t => t.Id);

        var order = new Order(TicketCode.New(), session, c.Channel, c.Payment, now, c.CustomerName, c.StaffId);
        foreach (var pick in c.Seats)
        {
            var seat = layout!.Find(pick.SeatCode);
            if (seat is null) return Error.Business($"{pick.SeatCode} diye bir koltuk yok.");
            if (sold.Contains(seat.Code)) return Error.Business($"{seat.Code} koltuğu az önce satıldı. Lütfen başka bir koltuk seçin.");
            if (held.Any(h => h.SeatCode == seat.Code && h.HolderId != c.HolderId)) return Error.Business($"{seat.Code} koltuğu şu an başka birinin sepetinde.");
            if (!types.TryGetValue(pick.TicketTypeId, out var type) || !type.IsActive) return Error.Business("Geçersiz bilet türü.");
            order.AddTicket(seat.Code, seat.Type, type, PricingEngine.Quote(type, seat.Type, session!.Format, session.Start, rules));
        }
        if (c.Items.Count > 0)
        {
            var ids = c.Items.Select(i => i.ItemId).ToList();
            var items = (await uow.Repository<ConcessionItem>().ListAsync(new Specification<ConcessionItem>().Where(i => ids.Contains(i.Id)).Tracking(), cancellationToken)).ToDictionary(i => i.Id);
            foreach (var pick in c.Items.Where(i => i.Quantity > 0))
            {
                if (!items.TryGetValue(pick.ItemId, out var item) || !item.IsActive) return Error.Business("Büfe ürünü satışta değil.");
                order.AddConcession(item, pick.Quantity);
            }
        }

        uow.Repository<Order>().Add(order);
        try
        {
            await uow.SaveChangesAsync(cancellationToken);
        }
        catch (DuplicateRecordException)
        {
            return Error.Business("Seçtiğiniz koltuklardan biri aynı anda başka bir satışta satıldı. Koltuk planı yenilendi; lütfen tekrar seçin.");
        }
        if (c.HolderId is not null) holds.ReleaseAll(c.HolderId);
        if (session is not null) holds.NotifyChanged(session.Id);
        return order.Code;
    }
}

public sealed record TicketLine(int Id, string SeatCode, SeatType SeatType, string TicketType, bool RequiresProof, decimal Price, TicketStatus Status, DateTime? UsedAt)
{
    public string StatusText => Texts.Of(Status);
    public string SeatTypeText => Texts.Of(SeatType);
}

public sealed record ConcessionLineView(string Name, int Quantity, decimal UnitPrice)
{
    public decimal Total => UnitPrice * Quantity;
}

public sealed record OrderView(int Id, string Code, OrderStatus Status, SalesChannel Channel, PaymentMethod Payment, DateTime SoldAt, string? CustomerName, string? SoldBy,
    int? SessionId, string? FilmTitle, string? Poster, string? Accent, AgeRating? Rating, string? HallName, DateTime? Start, DateTime? End,
    SessionFormat? Format, SessionLanguage? Language, IReadOnlyList<TicketLine> Tickets, IReadOnlyList<ConcessionLineView> Concessions,
    decimal TicketTotal, decimal ConcessionTotal, decimal Total)
{
    public string Seats => string.Join(", ", Tickets.Select(t => t.SeatCode));
    public bool AllUsed => Tickets.Count > 0 && Tickets.All(t => t.Status == TicketStatus.Used);
}

public sealed record GetOrder(string Code) : IQuery<OrderView>;

public sealed class GetOrderHandler(IUnitOfWorkFactory uowFactory) : IQueryHandler<GetOrder, OrderView>
{
    public async Task<Result<OrderView>> HandleAsync(GetOrder q, CancellationToken cancellationToken)
    {
        var code = TicketCode.Normalize(q.Code);
        await using var uow = uowFactory.Create();
        var view = await OrderQueries.LoadAsync(uow, code, cancellationToken);
        return view is null ? Error.NotFound("Bu kodla bir bilet bulunamadı. Kodu kontrol edin (ör. K7P2-QX9D).") : view;
    }
}

internal static class OrderQueries
{
    public static async Task<OrderView?> LoadAsync(IUnitOfWork uow, string code, CancellationToken ct)
    {
        var o = await uow.Repository<Order>().FirstOrDefaultAsync(new Specification<Order>().Where(x => x.Code == code)
            .Include(x => x.Tickets).Include(x => x.Concessions).Include(x => x.SoldBy).Include("Session.Film").Include("Session.Hall"), ct);
        if (o is null) return null;
        var s = o.Session;
        return new OrderView(o.Id, o.Code, o.Status, o.Channel, o.Payment, o.SoldAt, o.CustomerName, o.SoldBy?.FullName, o.SessionId,
            s?.Film?.Title, s?.Film?.PosterPath, s?.Film?.AccentColor, s?.Film?.Rating, s?.Hall?.Name, s?.Start, s?.End, s?.Format, s?.Language,
            o.Tickets.OrderBy(t => t.SeatCode.Length).ThenBy(t => t.SeatCode).Select(t => new TicketLine(t.Id, t.SeatCode, t.SeatType, t.TicketTypeName, t.RequiresProof, t.Price, t.Status, t.UsedAt)).ToList(),
            o.Concessions.Select(c => new ConcessionLineView(c.Name, c.Quantity, c.UnitPrice)).ToList(), o.TicketTotal, o.ConcessionTotal, o.Total);
    }
}

public sealed record CheckInResult(OrderView Order, int Admitted, IReadOnlyList<string> Warnings);

/// <summary>Kapı kontrolü: koddaki bütün geçerli biletleri (ya da tek koltuğu) "kullanıldı" yapar.</summary>
public sealed record CheckInTicket(string Code, int? UsherId, string? SeatCode = null) : ICommand<CheckInResult>;

public sealed class CheckInTicketHandler(IUnitOfWorkFactory uowFactory, TimeProvider time, SeatHoldService holds) : ICommandHandler<CheckInTicket, CheckInResult>
{
    public async Task<Result<CheckInResult>> HandleAsync(CheckInTicket c, CancellationToken cancellationToken)
    {
        var code = TicketCode.Normalize(c.Code);
        var now = time.Now();
        await using var uow = uowFactory.Create();
        var order = await uow.Repository<Order>().FirstOrDefaultAsync(new Specification<Order>().Where(o => o.Code == code).Include(o => o.Tickets).Include(o => o.Session).Tracking(), cancellationToken);
        if (order is null) return Error.NotFound("Bilet bulunamadı. Kodu kontrol edin.");
        if (order.Session is null || order.Tickets.Count == 0) return Error.Business("Bu kod bir büfe satışına ait; bilet içermiyor.");
        if (order.Status == OrderStatus.Refunded) return Error.Business("Bu satış iade edilmiş; bilet geçersiz.");
        var settings = await SettingsQueries.GetAsync(uow, cancellationToken);
        if (CheckInRules.WhyNot(order.Session, now, settings) is { } why) return Error.Business(why);

        var targets = order.Tickets.Where(t => c.SeatCode is null || string.Equals(t.SeatCode, c.SeatCode, StringComparison.OrdinalIgnoreCase)).ToList();
        if (targets.Count == 0) return Error.Business("Bu satışta o koltuk yok.");
        var fresh = targets.Where(t => t.Status == TicketStatus.Valid).ToList();
        if (fresh.Count == 0)
        {
            var first = targets.Where(t => t.UsedAt != null).Min(t => t.UsedAt);
            return Error.Business($"Bu bilet(ler) {first:HH:mm}'de zaten okutulmuş. Aynı bilet ikinci kez kullanılamaz.");
        }
        foreach (var t in fresh) t.CheckIn(now, c.UsherId);
        await uow.SaveChangesAsync(cancellationToken);
        holds.NotifyChanged(order.Session.Id);

        var view = (await OrderQueries.LoadAsync(uow, code, cancellationToken))!;
        var warnings = fresh.Where(t => t.RequiresProof).GroupBy(t => t.TicketTypeName).Select(g => $"{g.Count()} × {g.Key}: belge kontrol edin").ToList();
        return new CheckInResult(view, fresh.Count, warnings);
    }
}

public sealed record RefundOrder(string Code) : ICommand;

public sealed class RefundOrderHandler(IUnitOfWorkFactory uowFactory, TimeProvider time, SeatHoldService holds) : ICommandHandler<RefundOrder>
{
    public async Task<Result> HandleAsync(RefundOrder c, CancellationToken cancellationToken)
    {
        var code = TicketCode.Normalize(c.Code);
        var now = time.Now();
        await using var uow = uowFactory.Create();
        var order = await uow.Repository<Order>().FirstOrDefaultAsync(new Specification<Order>().Where(o => o.Code == code).Include(o => o.Tickets).Include(o => o.Session).Tracking(), cancellationToken);
        if (order is null) return Error.NotFound("Satış bulunamadı.");
        if (order.Session is { } s && now >= s.Start) return Error.Business("Seans başladıktan sonra iade yapılamaz.");
        if (order.Session is null && DateOnly.FromDateTime(order.SoldAt) != DateOnly.FromDateTime(now)) return Error.Business("Büfe satışları yalnızca aynı gün iade edilebilir.");
        order.Refund(now);
        await uow.SaveChangesAsync(cancellationToken);
        if (order.SessionId is int sid) holds.NotifyChanged(sid);
        return Result.Success();
    }
}

public sealed record OrderRow(int Id, string Code, DateTime SoldAt, SalesChannel Channel, PaymentMethod Payment, string? FilmTitle, string? HallName, DateTime? Start,
    string Seats, int TicketCount, decimal TicketTotal, decimal ConcessionTotal, decimal Total, OrderStatus Status, string? SoldBy, string? Customer)
{
    public string ChannelText => Texts.Of(Channel);
    public string PaymentText => Texts.Of(Payment);
    public string StatusText => Texts.Of(Status);
    public string What => FilmTitle is null ? "Büfe" : $"{FilmTitle} · {HallName} {Start:dd.MM HH:mm}";
}

public sealed record ListOrders(DateOnly From, DateOnly To, SalesChannel? Channel = null) : IQuery<IReadOnlyList<OrderRow>>;

public sealed class ListOrdersHandler(IUnitOfWorkFactory uowFactory) : IQueryHandler<ListOrders, IReadOnlyList<OrderRow>>
{
    public async Task<Result<IReadOnlyList<OrderRow>>> HandleAsync(ListOrders q, CancellationToken cancellationToken)
    {
        var from = q.From.Start();
        var to = q.To.AddDays(1).Start();
        await using var uow = uowFactory.Create();
        var spec = new Specification<Order>().Where(o => o.SoldAt >= from && o.SoldAt < to).OrderByDescending(o => o.SoldAt);
        if (q.Channel is { } ch) spec.Where(o => o.Channel == ch);
        var orders = await uow.Repository<Order>().ListAsync(spec, o => new
        {
            o.Id, o.Code, o.SoldAt, o.Channel, o.Payment, Film = o.Session!.Film!.Title, Hall = o.Session.Hall!.Name, Start = (DateTime?)o.Session.Start,
            Seats = o.Tickets.Select(t => t.SeatCode).ToList(), o.TicketTotal, o.ConcessionTotal, o.Total, o.Status, SoldBy = o.SoldBy!.FullName, o.CustomerName,
        }, cancellationToken);
        return orders.Select(o => new OrderRow(o.Id, o.Code, o.SoldAt, o.Channel, o.Payment, o.Film, o.Hall, o.Start, string.Join(", ", o.Seats), o.Seats.Count,
            o.TicketTotal, o.ConcessionTotal, o.Total, o.Status, o.SoldBy, o.CustomerName)).ToList();
    }
}
