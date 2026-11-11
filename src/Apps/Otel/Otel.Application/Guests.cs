using OnderCode.Application.Messaging;
using OnderCode.Application.Queries;
using OnderCode.Application.Results;
using OnderCode.Kernel.Persistence;
using OnderCode.Kernel.Specifications;
using Otel.Domain;

namespace Otel.Application;

public sealed record GuestOption(int Id, string FullName, string TierText, int LoyaltyPoints, int StayCount);

/// <summary>Rezervasyon formundaki misafir seçimi için bütün misafirler (az sayıda; arama istemci tarafında).</summary>
public sealed record GetGuestOptions : IQuery<IReadOnlyList<GuestOption>>;

public sealed class GetGuestOptionsHandler(IUnitOfWorkFactory uowFactory) : IQueryHandler<GetGuestOptions, IReadOnlyList<GuestOption>>
{
    public async Task<Result<IReadOnlyList<GuestOption>>> HandleAsync(GetGuestOptions query, CancellationToken cancellationToken)
    {
        await using var uow = uowFactory.Create();
        var rows = await uow.Repository<Guest>().ListAsync(new Specification<Guest>().OrderBy(g => g.FullName),
            g => new { g.Id, g.FullName, g.LoyaltyPoints, g.StayCount }, cancellationToken);
        IReadOnlyList<GuestOption> result = [.. rows.Select(g => new GuestOption(g.Id, g.FullName, TierOf(g.LoyaltyPoints), g.LoyaltyPoints, g.StayCount))];
        return Result<IReadOnlyList<GuestOption>>.Success(result);
    }

    internal static string TierOf(int points) => Texts.Of(points >= Guest.GoldThreshold ? LoyaltyTier.Gold
        : points >= Guest.SilverThreshold ? LoyaltyTier.Silver : LoyaltyTier.Standard);
}

public sealed record GuestRow(int Id, string FullName, string? Nationality, string TierText, int LoyaltyPoints, int StayCount,
    decimal TotalSpent, DateOnly? LastStay, string? Email);

public sealed record ListGuests : PagedQuery<GuestRow>;

public sealed class ListGuestsValidator : PagedQueryValidator<ListGuests, GuestRow>
{
}

public sealed class ListGuestsHandler(IUnitOfWorkFactory uowFactory) : IQueryHandler<ListGuests, PagedResult<GuestRow>>
{
    public async Task<Result<PagedResult<GuestRow>>> HandleAsync(ListGuests q, CancellationToken cancellationToken)
    {
        await using var uow = uowFactory.Create();
        var spec = new Specification<Guest>().Search(q.Search, g => g.FullName, g => g.Email).OrderByDescending(g => g.LoyaltyPoints).OrderBy(g => g.FullName);
        var repo = uow.Repository<Guest>();
        var total = await repo.CountAsync(spec, cancellationToken);
        var guests = await repo.ListAsync(spec.Page((q.Page - 1) * q.PageSize, q.PageSize),
            g => new { g.Id, g.FullName, g.Nationality, g.LoyaltyPoints, g.StayCount, g.Email }, cancellationToken);

        var ids = guests.Select(g => g.Id).ToList();
        var stays = await uow.Repository<Reservation>().ListAsync(
            new Specification<Reservation>().Where(r => ids.Contains(r.GuestId) && r.Status == ReservationStatus.CheckedOut),
            r => new { r.GuestId, r.CheckOut, r.RoomTotal }, cancellationToken);
        var byGuest = stays.GroupBy(s => s.GuestId).ToDictionary(g => g.Key, g => (Last: g.Max(s => s.CheckOut), Spent: g.Sum(s => s.RoomTotal)));

        var items = guests.Select(g =>
        {
            var has = byGuest.TryGetValue(g.Id, out var info);
            return new GuestRow(g.Id, g.FullName, g.Nationality, GetGuestOptionsHandler.TierOf(g.LoyaltyPoints), g.LoyaltyPoints, g.StayCount,
                has ? info.Spent : 0m, has ? info.Last : null, g.Email);
        }).ToList();
        return new PagedResult<GuestRow>(items, total, q.Page, q.PageSize);
    }
}
