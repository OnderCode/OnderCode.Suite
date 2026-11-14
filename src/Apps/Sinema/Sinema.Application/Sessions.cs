using FluentValidation;
using OnderCode.Application.Messaging;
using OnderCode.Application.Results;
using OnderCode.Kernel.Persistence;
using OnderCode.Kernel.Specifications;
using Sinema.Domain;

namespace Sinema.Application;

public sealed record SessionRow(int Id, int FilmId, string FilmTitle, string? Poster, string Accent, AgeRating Rating, int Duration, int HallId, string HallName,
    DateTime Start, DateTime End, DateTime ReadyAt, SessionFormat Format, SessionLanguage Language, int Capacity, int Sold, bool IsCancelled, decimal Revenue)
{
    public double Occupancy => Capacity == 0 ? 0 : Sold / (double)Capacity;
    public int Free => Math.Max(0, Capacity - Sold);
    public string FormatText => Texts.Of(Format);
    public string LanguageText => Texts.Short(Language);
    public string PosterUrl => Poster ?? "posters/bos.svg";
    public DateOnly Day => DateOnly.FromDateTime(Start);
}

public sealed record ListSessions(DateOnly From, DateOnly To, int? HallId = null, int? FilmId = null, bool IncludeCancelled = false) : IQuery<IReadOnlyList<SessionRow>>;

public sealed class ListSessionsHandler(IUnitOfWorkFactory uowFactory) : IQueryHandler<ListSessions, IReadOnlyList<SessionRow>>
{
    public async Task<Result<IReadOnlyList<SessionRow>>> HandleAsync(ListSessions q, CancellationToken cancellationToken)
    {
        await using var uow = uowFactory.Create();
        return (await SessionQueries.LoadAsync(uow, q.From.Start(), q.To.AddDays(1).Start(), q.HallId, q.FilmId, q.IncludeCancelled, cancellationToken)).ToList();
    }
}

internal static class SessionQueries
{
    public static async Task<IReadOnlyList<SessionRow>> LoadAsync(IUnitOfWork uow, DateTime from, DateTime to, int? hallId, int? filmId, bool includeCancelled, CancellationToken ct)
    {
        var spec = new Specification<Session>().Where(s => s.Start >= from && s.Start < to).OrderBy(s => s.Start);
        if (hallId is int h) spec.Where(s => s.HallId == h);
        if (filmId is int f) spec.Where(s => s.FilmId == f);
        if (!includeCancelled) spec.Where(s => !s.IsCancelled);
        var sessions = await uow.Repository<Session>().ListAsync(spec, s => new
        {
            s.Id, s.FilmId, s.Film!.Title, s.Film.PosterPath, s.Film.AccentColor, s.Film.Rating, s.Film.DurationMinutes, s.HallId, HallName = s.Hall!.Name,
            s.Hall.Capacity, s.Start, s.End, s.ReadyAt, s.Format, s.Language, s.IsCancelled,
        }, ct);
        var ids = sessions.Select(s => s.Id).ToList();
        var sold = ids.Count == 0 ? [] : await uow.Repository<Ticket>().ListAsync(
            new Specification<Ticket>().Where(t => ids.Contains(t.SessionId) && t.Status != TicketStatus.Refunded), t => new { t.SessionId, t.Price }, ct);
        var bySession = sold.GroupBy(t => t.SessionId).ToDictionary(g => g.Key, g => (Count: g.Count(), Sum: g.Sum(x => x.Price)));
        return sessions.Select(s =>
        {
            var x = bySession.GetValueOrDefault(s.Id);
            return new SessionRow(s.Id, s.FilmId, s.Title, s.PosterPath, s.AccentColor, s.Rating, s.DurationMinutes, s.HallId, s.HallName, s.Start, s.End, s.ReadyAt,
                s.Format, s.Language, s.Capacity, x.Count, s.IsCancelled, x.Sum);
        }).ToList();
    }

    public static async Task<HashSet<string>> SoldSeatsAsync(IUnitOfWork uow, int sessionId, CancellationToken ct)
    {
        var seats = await uow.Repository<Ticket>().ListAsync(new Specification<Ticket>().Where(t => t.SessionId == sessionId && t.Status != TicketStatus.Refunded), t => t.SeatCode, ct);
        return seats.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}

/// <summary>Koltuk seçimi ekranı için seans, plan ve satılmış koltuklar.</summary>
public sealed record SeatMapView(SessionRow Session, string Layout, string? HallFeatures, IReadOnlySet<string> SoldSeats, IReadOnlySet<string> UsedSeats,
    string? Synopsis, string Genres, bool OnlineOpen, string? ClosedReason);

public sealed record GetSeatMap(int SessionId) : IQuery<SeatMapView>;

public sealed class GetSeatMapHandler(IUnitOfWorkFactory uowFactory, TimeProvider time) : IQueryHandler<GetSeatMap, SeatMapView>
{
    public async Task<Result<SeatMapView>> HandleAsync(GetSeatMap q, CancellationToken cancellationToken)
    {
        await using var uow = uowFactory.Create();
        var s = await uow.Repository<Session>().FirstOrDefaultAsync(new Specification<Session>().Where(x => x.Id == q.SessionId).Include(x => x.Film).Include(x => x.Hall), cancellationToken);
        if (s is null) return Error.NotFound("Seans bulunamadı.");
        var settings = await SettingsQueries.GetAsync(uow, cancellationToken);
        var rows = await SessionQueries.LoadAsync(uow, s.Start, s.Start.AddMinutes(1), s.HallId, null, true, cancellationToken);
        var row = rows.First(r => r.Id == s.Id);
        var tickets = await uow.Repository<Ticket>().ListAsync(new Specification<Ticket>().Where(t => t.SessionId == s.Id && t.Status != TicketStatus.Refunded),
            t => new { t.SeatCode, t.Status }, cancellationToken);
        var now = time.Now();
        string? closed = s.IsCancelled ? "Bu seans iptal edildi."
            : now > s.Start.AddMinutes(-settings.OnlineSalesCloseMinutes) ? "Bu seansın online satışı kapandı; biletinizi gişeden alabilirsiniz."
            : null;
        return new SeatMapView(row, s.Hall!.Layout, s.Hall.Features,
            tickets.Select(t => t.SeatCode).ToHashSet(StringComparer.OrdinalIgnoreCase),
            tickets.Where(t => t.Status == TicketStatus.Used).Select(t => t.SeatCode).ToHashSet(StringComparer.OrdinalIgnoreCase),
            s.Film!.Synopsis, s.Film.Genres, closed is null, closed);
    }
}

public sealed record CreateSessions(int FilmId, int HallId, DateOnly Date, TimeOnly Time, SessionFormat Format, SessionLanguage Language, int RepeatDays) : ICommand<int>;

public sealed class CreateSessionsValidator : AbstractValidator<CreateSessions>
{
    public CreateSessionsValidator()
    {
        RuleFor(x => x.FilmId).GreaterThan(0).WithName("Film");
        RuleFor(x => x.HallId).GreaterThan(0).WithName("Salon");
        RuleFor(x => x.RepeatDays).InclusiveBetween(1, 28).WithName("Gün sayısı");
    }
}

public sealed class CreateSessionsHandler(IUnitOfWorkFactory uowFactory, TimeProvider time) : ICommandHandler<CreateSessions, int>
{
    public async Task<Result<int>> HandleAsync(CreateSessions c, CancellationToken cancellationToken)
    {
        await using var uow = uowFactory.Create();
        var film = await uow.Repository<Film>().GetRequiredAsync(c.FilmId, cancellationToken);
        var hall = await uow.Repository<Hall>().GetRequiredAsync(c.HallId, cancellationToken);
        var settings = await SettingsQueries.GetAsync(uow, cancellationToken);
        var now = time.Now();
        var first = c.Date.ToDateTime(c.Time);
        var last = c.Date.AddDays(c.RepeatDays).ToDateTime(c.Time).AddHours(6);
        var existing = await uow.Repository<Session>().ListAsync(
            new Specification<Session>().Where(s => s.HallId == hall.Id && !s.IsCancelled && s.Start < last && s.ReadyAt > first.AddHours(-6)).Include(s => s.Film), cancellationToken);
        var created = new List<Session>();
        var errors = new List<Error>();
        for (var d = 0; d < c.RepeatDays; d++)
        {
            var start = c.Date.AddDays(d).ToDateTime(c.Time);
            if (start < now) { errors.Add(Error.Business($"{start:dd.MM HH:mm}: geçmiş bir saate seans açılamaz.")); continue; }
            var session = new Session(film, hall, start, c.Format, c.Language, settings);
            var conflict = SessionRules.FindConflict(existing.Concat(created), session.Start, session.ReadyAt);
            if (conflict is not null)
            {
                errors.Add(Error.Business($"{start:dd.MM ddd HH:mm}: {hall.Name} {conflict.Start:HH:mm}–{conflict.ReadyAt:HH:mm} arası dolu ({conflict.Film?.Title ?? "başka seans"}, temizlik dahil)."));
                continue;
            }
            created.Add(session);
        }
        if (errors.Count > 0) return Result<int>.Failure(errors);
        uow.Repository<Session>().AddRange(created);
        await uow.SaveChangesAsync(cancellationToken);
        return created.Count;
    }
}

public sealed record CancelSession(int SessionId) : ICommand<int>;

public sealed class CancelSessionHandler(IUnitOfWorkFactory uowFactory, TimeProvider time, SeatHoldService holds) : ICommandHandler<CancelSession, int>
{
    public async Task<Result<int>> HandleAsync(CancelSession c, CancellationToken cancellationToken)
    {
        await using var uow = uowFactory.Create();
        var session = await uow.Repository<Session>().GetRequiredAsync(c.SessionId, cancellationToken);
        if (session.End < time.Now()) return Error.Business("Bitmiş bir seans iptal edilemez.");
        var orders = await uow.Repository<Order>().ListAsync(new Specification<Order>().Where(o => o.SessionId == session.Id && o.Status == OrderStatus.Completed)
            .Include(o => o.Tickets).Tracking(), cancellationToken);
        if (orders.Any(o => o.Tickets.Any(t => t.Status == TicketStatus.Used))) return Error.Business("Bu seansa kapıdan giriş yapılmış; iptal edilemez.");
        foreach (var o in orders) o.Refund(time.Now());
        session.Cancel();
        await uow.SaveChangesAsync(cancellationToken);
        holds.NotifyChanged(session.Id);
        return orders.Count;
    }
}

public sealed record SuggestSessionStart(int HallId, DateOnly Date) : IQuery<TimeOnly>;

public sealed class SuggestSessionStartHandler(IUnitOfWorkFactory uowFactory) : IQueryHandler<SuggestSessionStart, TimeOnly>
{
    public async Task<Result<TimeOnly>> HandleAsync(SuggestSessionStart q, CancellationToken cancellationToken)
    {
        await using var uow = uowFactory.Create();
        var settings = await SettingsQueries.GetAsync(uow, cancellationToken);
        var from = q.Date.Start();
        var to = q.Date.AddDays(1).Start();
        var sessions = await uow.Repository<Session>().ListAsync(new Specification<Session>().Where(s => s.HallId == q.HallId && s.Start >= from && s.Start < to && !s.IsCancelled), cancellationToken);
        return TimeOnly.FromDateTime(SessionRules.SuggestStart(sessions, q.Date, settings));
    }
}
