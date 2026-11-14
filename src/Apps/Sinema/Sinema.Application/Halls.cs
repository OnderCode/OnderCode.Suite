using FluentValidation;
using OnderCode.Application.Messaging;
using OnderCode.Application.Results;
using OnderCode.Kernel.Persistence;
using OnderCode.Kernel.Specifications;
using Sinema.Domain;

namespace Sinema.Application;

public sealed record HallRow(int Id, string Name, string Layout, int Capacity, string? Features, bool Supports3D, bool IsActive,
    int Standard, int Vip, int Accessible, int Couple, int SessionsToday, int UpcomingSessions, double Occupancy30)
{
    public HallLayout Parsed => HallLayout.Parse(Layout);
}

public sealed record ListHalls(bool ActiveOnly = false) : IQuery<IReadOnlyList<HallRow>>;

public sealed class ListHallsHandler(IUnitOfWorkFactory uowFactory, TimeProvider time) : IQueryHandler<ListHalls, IReadOnlyList<HallRow>>
{
    public async Task<Result<IReadOnlyList<HallRow>>> HandleAsync(ListHalls q, CancellationToken cancellationToken)
    {
        var now = time.Now();
        var today = time.Today();
        var from30 = today.AddDays(-30).Start();
        await using var uow = uowFactory.Create();
        var spec = new Specification<Hall>().OrderBy(h => h.SortOrder).OrderBy(h => h.Name);
        if (q.ActiveOnly) spec.Where(h => h.IsActive);
        var halls = await uow.Repository<Hall>().ListAsync(spec, cancellationToken);
        var sessions = await uow.Repository<Session>().ListAsync(new Specification<Session>().Where(s => !s.IsCancelled && s.Start >= from30),
            s => new { s.Id, s.HallId, s.Start }, cancellationToken);
        var pastIds = sessions.Where(s => s.Start < now).Select(s => s.Id).ToList();
        var sold = await uow.Repository<Ticket>().ListAsync(new Specification<Ticket>().Where(t => t.Status != TicketStatus.Refunded && t.Session!.Start >= from30 && t.Session.Start < now),
            t => t.SessionId, cancellationToken);
        var soldBySession = sold.GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());

        return halls.Select(h =>
        {
            var layout = h.ParsedLayout();
            var mine = sessions.Where(s => s.HallId == h.Id).ToList();
            var past = mine.Where(s => s.Start < now).ToList();
            var occ = past.Count == 0 || h.Capacity == 0 ? 0 : past.Sum(s => soldBySession.GetValueOrDefault(s.Id)) / (double)(past.Count * h.Capacity);
            return new HallRow(h.Id, h.Name, h.Layout, h.Capacity, h.Features, h.Supports3D, h.IsActive,
                layout.Count(SeatType.Standard), layout.Count(SeatType.Vip), layout.Count(SeatType.Accessible), layout.Count(SeatType.Couple),
                mine.Count(s => DateOnly.FromDateTime(s.Start) == today), mine.Count(s => s.Start >= now), occ);
        }).ToList();
    }
}

public sealed record SaveHall(int? Id, string Name, string Layout, string? Features, bool Supports3D, bool IsActive) : ICommand<int>;

public sealed class SaveHallValidator : AbstractValidator<SaveHall>
{
    public SaveHallValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(40).WithName("Salon adı");
        RuleFor(x => x.Features).MaximumLength(120).WithName("Özellikler");
        RuleFor(x => x.Layout).NotEmpty().WithName("Koltuk planı");
    }
}

public sealed class SaveHallHandler(IUnitOfWorkFactory uowFactory, TimeProvider time) : ICommandHandler<SaveHall, int>
{
    public async Task<Result<int>> HandleAsync(SaveHall c, CancellationToken cancellationToken)
    {
        var now = time.Now();
        await using var uow = uowFactory.Create();
        Hall h;
        if (c.Id is int id)
        {
            h = await uow.Repository<Hall>().GetRequiredAsync(id, cancellationToken);
            var newLayout = HallLayout.Parse(c.Layout).ToString();
            if (newLayout != h.Layout)
            {
                // İleri tarihli satılmış koltuklar yeni planda yoksa plan değiştirilemez
                var futureSeats = await uow.Repository<Ticket>().ListAsync(
                    new Specification<Ticket>().Where(t => t.Status == TicketStatus.Valid && t.Session!.HallId == id && t.Session.Start > now),
                    t => t.SeatCode, cancellationToken);
                var parsed = HallLayout.Parse(newLayout);
                var missing = futureSeats.Distinct().Where(code => parsed.Find(code) is null).ToList();
                if (missing.Count > 0)
                    return Error.Business($"İleri tarihli seanslarda satılmış {missing.Count} koltuk yeni planda yok (ör. {string.Join(", ", missing.Take(5))}). Önce o satışları iade edin ya da planı koruyun.");
                h.SetLayout(newLayout);
            }
            h.Rename(c.Name);
        }
        else
        {
            h = new Hall(c.Name, c.Layout);
            var count = await uow.Repository<Hall>().CountAsync(cancellationToken: cancellationToken);
            h.SetSortOrder(count + 1);
            uow.Repository<Hall>().Add(h);
        }
        if (!c.Supports3D && h.Supports3D)
        {
            var has3D = await uow.Repository<Session>().AnyAsync(new Specification<Session>().Where(s => s.HallId == h.Id && s.Format == SessionFormat.ThreeD && s.Start > now && !s.IsCancelled), cancellationToken);
            if (has3D) return Error.Business("Bu salonda ileri tarihli 3D seanslar var; 3D desteği kapatılamaz.");
        }
        h.SetFeatures(c.Features, c.Supports3D);
        h.SetActive(c.IsActive);
        await uow.SaveChangesAsync(cancellationToken);
        return h.Id;
    }
}
