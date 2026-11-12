using FluentValidation;
using OnderCode.Application.Messaging;
using OnderCode.Application.Results;
using OnderCode.Application.Validation;
using OnderCode.Kernel.Exceptions;
using OnderCode.Kernel.Persistence;
using OnderCode.Kernel.Specifications;
using OnderCode.Kernel.Validation;
using Otopark.Domain;

namespace Otopark.Application;

public enum SubscriptionFilter { All, Active, Expiring, Expired }

public sealed record SubscriptionRow(int Id, string Plate, string OwnerName, string VehicleText, DateOnly StartDate, DateOnly EndDate,
    int DaysLeft, string StatusText, decimal MonthlyFee, decimal TotalPaid, int VisitsLast30);

public sealed record ListSubscriptions(SubscriptionFilter Filter = SubscriptionFilter.All, string? Search = null) : IQuery<IReadOnlyList<SubscriptionRow>>;

public sealed class ListSubscriptionsHandler(IUnitOfWorkFactory uowFactory, TimeProvider time) : IQueryHandler<ListSubscriptions, IReadOnlyList<SubscriptionRow>>
{
    /// <summary>Bu kadar gün içinde bitecek abonelik "yakında bitiyor" sayılır.</summary>
    public const int ExpiringDays = 7;

    public async Task<Result<IReadOnlyList<SubscriptionRow>>> HandleAsync(ListSubscriptions q, CancellationToken cancellationToken)
    {
        var today = time.Today();
        var since = today.AddDays(-30).Start();
        await using var uow = uowFactory.Create();
        var spec = new Specification<Subscription>().OrderBy(s => s.EndDate);
        if (!string.IsNullOrWhiteSpace(q.Search)) spec.Search(q.Search, s => s.Plate, s => s.OwnerName);
        var subs = await uow.Repository<Subscription>().ListAsync(spec, cancellationToken);
        var visits = (await uow.Repository<ParkingSession>().ListAsync(
                new Specification<ParkingSession>().Where(s => s.SubscriptionId != null && s.EntryAt >= since), s => s.SubscriptionId!.Value, cancellationToken))
            .GroupBy(id => id).ToDictionary(g => g.Key, g => g.Count());

        IReadOnlyList<SubscriptionRow> rows = [.. subs.Select(s =>
            {
                var left = s.EndDate.DayNumber - today.DayNumber;
                var status = today < s.StartDate ? "Başlamadı" : left < 0 ? "Bitti" : left < ExpiringDays ? "Yakında bitiyor" : "Aktif";
                return new SubscriptionRow(s.Id, s.Plate, s.OwnerName, Texts.Of(s.Vehicle), s.StartDate, s.EndDate, Math.Max(0, left), status,
                    s.MonthlyFee, s.TotalPaid, visits.GetValueOrDefault(s.Id));
            })
            .Where(r => q.Filter switch
            {
                SubscriptionFilter.Active => r.StatusText is "Aktif" or "Yakında bitiyor",
                SubscriptionFilter.Expiring => r.StatusText == "Yakında bitiyor",
                SubscriptionFilter.Expired => r.StatusText == "Bitti",
                _ => true,
            })];
        return Result<IReadOnlyList<SubscriptionRow>>.Success(rows);
    }
}

public sealed record CreateSubscription(string Plate, string OwnerName, VehicleClass Vehicle, int Months) : ICommand<int>;

public sealed class CreateSubscriptionValidator : AbstractValidator<CreateSubscription>
{
    public CreateSubscriptionValidator()
    {
        RuleFor(x => x.Plate).NotEmpty().Plate().WithName("Plaka");
        RuleFor(x => x.OwnerName).NotEmpty().MaximumLength(120).WithName("Abone adı");
        RuleFor(x => x.Months).InclusiveBetween(1, 12).WithName("Süre (ay)");
    }
}

public sealed class CreateSubscriptionHandler(IUnitOfWorkFactory uowFactory, TimeProvider time) : ICommandHandler<CreateSubscription, int>
{
    public async Task<Result<int>> HandleAsync(CreateSubscription c, CancellationToken cancellationToken)
    {
        var plate = TurkishPlate.Normalize(c.Plate)!;
        var today = time.Today();
        await using var uow = uowFactory.Create();
        var exists = await uow.Repository<Subscription>().AnyAsync(
            new Specification<Subscription>().Where(s => s.Plate == plate && s.EndDate >= today), cancellationToken);
        if (exists) throw new DomainException($"{plate} için süresi dolmamış bir abonelik var; uzatmayı kullanın.");
        var sub = new Subscription(plate, c.OwnerName, c.Vehicle, today, c.Months, Subscription.MonthlyFeeFor(c.Vehicle));
        uow.Repository<Subscription>().Add(sub);
        await uow.SaveChangesAsync(cancellationToken);
        return sub.Id;
    }
}

public sealed record ExtendSubscription(int SubscriptionId, int Months) : ICommand;

public sealed class ExtendSubscriptionValidator : AbstractValidator<ExtendSubscription>
{
    public ExtendSubscriptionValidator() => RuleFor(x => x.Months).InclusiveBetween(1, 12).WithName("Süre (ay)");
}

public sealed class ExtendSubscriptionHandler(IUnitOfWorkFactory uowFactory) : ICommandHandler<ExtendSubscription>
{
    public async Task<Result> HandleAsync(ExtendSubscription c, CancellationToken cancellationToken)
    {
        await using var uow = uowFactory.Create();
        var sub = await uow.Repository<Subscription>().GetRequiredAsync(c.SubscriptionId, cancellationToken);
        sub.Extend(c.Months);
        await uow.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
