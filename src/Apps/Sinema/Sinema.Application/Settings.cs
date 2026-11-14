using FluentValidation;
using OnderCode.Application.Messaging;
using OnderCode.Application.Results;
using OnderCode.Kernel.Persistence;
using OnderCode.Kernel.Specifications;
using Sinema.Domain;

namespace Sinema.Application;

internal static class SettingsQueries
{
    public static async Task<CinemaSettings> GetAsync(IUnitOfWork uow, CancellationToken ct)
        => await uow.Repository<CinemaSettings>().FirstOrDefaultAsync(new Specification<CinemaSettings>(), ct) ?? new CinemaSettings();

    public static async Task<PricingRules> RulesAsync(IUnitOfWork uow, CancellationToken ct)
        => await uow.Repository<PricingRules>().FirstOrDefaultAsync(new Specification<PricingRules>(), ct) ?? new PricingRules();
}

public sealed record SettingsView(string Name, string Branch, int AdsMinutes, int CleaningMinutes, int HoldMinutes, int CheckInOpensMinutes,
    int OnlineSalesCloseMinutes, TimeOnly OpensAt, TimeOnly LastSessionAt);

public sealed record GetSettings : IQuery<SettingsView>;

public sealed class GetSettingsHandler(IUnitOfWorkFactory uowFactory) : IQueryHandler<GetSettings, SettingsView>
{
    public async Task<Result<SettingsView>> HandleAsync(GetSettings q, CancellationToken cancellationToken)
    {
        await using var uow = uowFactory.Create();
        var s = await SettingsQueries.GetAsync(uow, cancellationToken);
        return new SettingsView(s.Name, s.Branch, s.AdsMinutes, s.CleaningMinutes, s.HoldMinutes, s.CheckInOpensMinutes, s.OnlineSalesCloseMinutes, s.OpensAt, s.LastSessionAt);
    }
}

public sealed record SaveSettings(SettingsView Settings) : ICommand;

public sealed class SaveSettingsHandler(IUnitOfWorkFactory uowFactory, SeatHoldService holds) : ICommandHandler<SaveSettings>
{
    public async Task<Result> HandleAsync(SaveSettings c, CancellationToken cancellationToken)
    {
        await using var uow = uowFactory.Create();
        var s = await uow.Repository<CinemaSettings>().FirstOrDefaultAsync(new Specification<CinemaSettings>().Tracking(), cancellationToken);
        if (s is null) { s = new CinemaSettings(); uow.Repository<CinemaSettings>().Add(s); }
        var v = c.Settings;
        s.Update(v.Name, v.Branch, v.AdsMinutes, v.CleaningMinutes, v.HoldMinutes, v.CheckInOpensMinutes, v.OnlineSalesCloseMinutes, v.OpensAt, v.LastSessionAt);
        await uow.SaveChangesAsync(cancellationToken);
        holds.HoldMinutes = s.HoldMinutes;
        return Result.Success();
    }
}

// ---------- Fiyatlar ----------

public sealed record TicketTypeRow(int Id, string Name, decimal Price, string? Description, bool RequiresProof, bool IsActive, int SortOrder, int Sold30);

public sealed record PricingView(IReadOnlyList<TicketTypeRow> Types, decimal VipSurcharge, decimal CoupleSurcharge, decimal ThreeDSurcharge,
    DayOfWeek? DiscountDay, int DiscountPercent, TimeOnly MatineeBefore, decimal MatineeDiscount);

public sealed record GetPricing : IQuery<PricingView>;

public sealed class GetPricingHandler(IUnitOfWorkFactory uowFactory, TimeProvider time) : IQueryHandler<GetPricing, PricingView>
{
    public async Task<Result<PricingView>> HandleAsync(GetPricing q, CancellationToken cancellationToken)
    {
        await using var uow = uowFactory.Create();
        var types = await uow.Repository<TicketType>().ListAsync(new Specification<TicketType>().OrderBy(t => t.SortOrder), cancellationToken);
        var from = time.Today().AddDays(-30).Start();
        var sold = await uow.Repository<Ticket>().ListAsync(new Specification<Ticket>().Where(t => t.Status != TicketStatus.Refunded && t.Session!.Start >= from), t => t.TicketTypeId, cancellationToken);
        var counts = sold.GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
        var r = await SettingsQueries.RulesAsync(uow, cancellationToken);
        return new PricingView(types.Select(t => new TicketTypeRow(t.Id, t.Name, t.Price, t.Description, t.RequiresProof, t.IsActive, t.SortOrder, counts.GetValueOrDefault(t.Id))).ToList(),
            r.VipSurcharge, r.CoupleSurcharge, r.ThreeDSurcharge, r.DiscountDay, r.DiscountPercent, r.MatineeBefore, r.MatineeDiscount);
    }
}

public sealed record SavePricingRules(decimal VipSurcharge, decimal CoupleSurcharge, decimal ThreeDSurcharge, DayOfWeek? DiscountDay, int DiscountPercent,
    TimeOnly MatineeBefore, decimal MatineeDiscount) : ICommand;

public sealed class SavePricingRulesHandler(IUnitOfWorkFactory uowFactory) : ICommandHandler<SavePricingRules>
{
    public async Task<Result> HandleAsync(SavePricingRules c, CancellationToken cancellationToken)
    {
        await using var uow = uowFactory.Create();
        var r = await uow.Repository<PricingRules>().FirstOrDefaultAsync(new Specification<PricingRules>().Tracking(), cancellationToken);
        if (r is null) { r = new PricingRules(); uow.Repository<PricingRules>().Add(r); }
        r.Update(c.VipSurcharge, c.CoupleSurcharge, c.ThreeDSurcharge, c.DiscountDay, c.DiscountPercent, c.MatineeBefore, c.MatineeDiscount);
        await uow.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

public sealed record SaveTicketType(int? Id, string Name, decimal Price, string? Description, bool RequiresProof, bool IsActive) : ICommand<int>;

public sealed class SaveTicketTypeValidator : AbstractValidator<SaveTicketType>
{
    public SaveTicketTypeValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(30).WithName("Bilet türü");
        RuleFor(x => x.Price).InclusiveBetween(0, 10_000).WithName("Fiyat");
    }
}

public sealed class SaveTicketTypeHandler(IUnitOfWorkFactory uowFactory) : ICommandHandler<SaveTicketType, int>
{
    public async Task<Result<int>> HandleAsync(SaveTicketType c, CancellationToken cancellationToken)
    {
        await using var uow = uowFactory.Create();
        TicketType t;
        if (c.Id is int id)
        {
            t = await uow.Repository<TicketType>().GetRequiredAsync(id, cancellationToken);
            t.Update(c.Name, c.Price, c.Description, c.RequiresProof);
        }
        else
        {
            var count = await uow.Repository<TicketType>().CountAsync(cancellationToken: cancellationToken);
            t = new TicketType(c.Name, c.Price, c.Description, c.RequiresProof, count + 1);
            uow.Repository<TicketType>().Add(t);
        }
        if (!c.IsActive)
        {
            var activeOthers = await uow.Repository<TicketType>().CountAsync(new Specification<TicketType>().Where(x => x.IsActive && x.Id != t.Id), cancellationToken);
            if (activeOthers == 0) return Error.Business("En az bir bilet türü satışta kalmalı.");
        }
        t.SetActive(c.IsActive);
        await uow.SaveChangesAsync(cancellationToken);
        return t.Id;
    }
}

/// <summary>Bir seans ve koltuk tipi için bütün bilet türlerinin fiyatı (seçim ekranında gösterilir).</summary>
public sealed record QuoteRow(int TicketTypeId, string Name, string? Description, bool RequiresProof, SeatType Seat, PriceQuote Quote);

public sealed record GetQuotes(int SessionId) : IQuery<IReadOnlyList<QuoteRow>>;

public sealed class GetQuotesHandler(IUnitOfWorkFactory uowFactory) : IQueryHandler<GetQuotes, IReadOnlyList<QuoteRow>>
{
    public async Task<Result<IReadOnlyList<QuoteRow>>> HandleAsync(GetQuotes q, CancellationToken cancellationToken)
    {
        await using var uow = uowFactory.Create();
        var s = await uow.Repository<Session>().GetRequiredAsync(q.SessionId, cancellationToken);
        var rules = await SettingsQueries.RulesAsync(uow, cancellationToken);
        var types = await uow.Repository<TicketType>().ListAsync(new Specification<TicketType>().Where(t => t.IsActive).OrderBy(t => t.SortOrder), cancellationToken);
        return types.SelectMany(t => Enum.GetValues<SeatType>().Select(seat => new QuoteRow(t.Id, t.Name, t.Description, t.RequiresProof, seat,
            PricingEngine.Quote(t, seat, s.Format, s.Start, rules)))).ToList();
    }
}
