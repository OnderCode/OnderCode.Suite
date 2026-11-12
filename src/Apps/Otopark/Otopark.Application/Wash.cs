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

public sealed record PackageOption(int Id, string Code, string Name, int Minutes, decimal CarPrice, decimal LargePrice, decimal MotorcyclePrice)
{
    public string Display => $"{Name} · {CarPrice:N0} ₺";

    public decimal? PriceFor(VehicleClass v) => v switch
    {
        VehicleClass.Large => LargePrice,
        VehicleClass.Motorcycle => MotorcyclePrice > 0 ? MotorcyclePrice : null,
        _ => CarPrice,
    };
}

public sealed record GetWashPackages : IQuery<IReadOnlyList<PackageOption>>;

public sealed class GetWashPackagesHandler(IUnitOfWorkFactory uowFactory) : IQueryHandler<GetWashPackages, IReadOnlyList<PackageOption>>
{
    public async Task<Result<IReadOnlyList<PackageOption>>> HandleAsync(GetWashPackages q, CancellationToken cancellationToken)
    {
        await using var uow = uowFactory.Create();
        var rows = await uow.Repository<WashPackage>().ListAsync(new Specification<WashPackage>().OrderBy(p => p.SortOrder),
            p => new PackageOption(p.Id, p.Code, p.Name, p.Minutes, p.CarPrice, p.LargePrice, p.MotorcyclePrice), cancellationToken);
        return Result<IReadOnlyList<PackageOption>>.Success(rows);
    }
}

/// <summary>Yıkama siparişi. Plaka otoparkta ise kalışa bağlanır ve ücret çıkışta alınır; değilse ayakta müşteri.</summary>
public sealed record CreateWashOrder(string Plate, VehicleClass Vehicle, int PackageId, string? Note = null) : ICommand<int>;

public sealed class CreateWashOrderValidator : AbstractValidator<CreateWashOrder>
{
    public CreateWashOrderValidator()
    {
        RuleFor(x => x.Plate).NotEmpty().Plate().WithName("Plaka");
        RuleFor(x => x.PackageId).GreaterThan(0).WithName("Paket").WithMessage("Paket seçin.");
        RuleFor(x => x.Note).MaximumLength(200);
    }
}

public sealed class CreateWashOrderHandler(IUnitOfWorkFactory uowFactory, TimeProvider time) : ICommandHandler<CreateWashOrder, int>
{
    public async Task<Result<int>> HandleAsync(CreateWashOrder c, CancellationToken cancellationToken)
    {
        var plate = TurkishPlate.Normalize(c.Plate)!;
        await using var uow = uowFactory.Create();
        var package = await uow.Repository<WashPackage>().FirstOrDefaultAsync(new Specification<WashPackage>().Where(p => p.Id == c.PackageId).Tracking(), cancellationToken)
                      ?? throw new NotFoundException("Yıkama paketi", c.PackageId);
        var session = await uow.Repository<ParkingSession>().FirstOrDefaultAsync(
            new Specification<ParkingSession>().Where(s => s.Plate == plate && s.Status == SessionStatus.Active).Tracking(), cancellationToken);
        var open = await uow.Repository<WashOrder>().AnyAsync(new Specification<WashOrder>().Where(w =>
            w.Plate == plate && (w.Status == WashStatus.Waiting || w.Status == WashStatus.Washing)), cancellationToken);
        if (open) throw new DomainException($"{plate} için açık bir yıkama siparişi zaten var.");

        // İçerideki aracın sınıfı giriş kaydından gelir
        var order = new WashOrder(plate, session?.Vehicle ?? c.Vehicle, package, time.Now(), session, c.Note);
        uow.Repository<WashOrder>().Add(order);
        await uow.SaveChangesAsync(cancellationToken);
        return order.Id;
    }
}

public sealed record AdvanceWash(int OrderId, string? Washer = null) : ICommand<WashStatus>;

public sealed class AdvanceWashHandler(IUnitOfWorkFactory uowFactory, TimeProvider time) : ICommandHandler<AdvanceWash, WashStatus>
{
    public async Task<Result<WashStatus>> HandleAsync(AdvanceWash c, CancellationToken cancellationToken)
    {
        await using var uow = uowFactory.Create();
        var order = await uow.Repository<WashOrder>().GetRequiredAsync(c.OrderId, cancellationToken);
        // Otoparktaki aracın yıkaması, araç çıkarken teslim edilir; burada en fazla "Hazır" olur
        if (order.Status == WashStatus.Ready && order.SessionId is not null)
        {
            var active = await uow.Repository<ParkingSession>().AnyAsync(
                new Specification<ParkingSession>().Where(s => s.Id == order.SessionId && s.Status == SessionStatus.Active), cancellationToken);
            if (active) throw new DomainException($"{order.Plate} otoparkta; yıkama ücreti çıkışta alınır ve araç çıkarken teslim edilir.");
        }
        var status = order.Advance(time.Now(), c.Washer);
        await uow.SaveChangesAsync(cancellationToken);
        return status;
    }
}

public sealed record CancelWash(int OrderId) : ICommand;

public sealed class CancelWashHandler(IUnitOfWorkFactory uowFactory) : ICommandHandler<CancelWash>
{
    public async Task<Result> HandleAsync(CancelWash c, CancellationToken cancellationToken)
    {
        await using var uow = uowFactory.Create();
        var order = await uow.Repository<WashOrder>().GetRequiredAsync(c.OrderId, cancellationToken);
        order.Cancel();
        await uow.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

// ---------- Yıkama panosu ----------

public sealed record WashCard(
    int Id, string Plate, string PackageName, VehicleClass Vehicle, decimal Price, WashStatus Status, string? Washer, string? Note,
    DateTime OrderedAt, DateTime? StartedAt, DateTime? ReadyAt, DateTime? DeliveredAt, DateTime EstimatedReady, string? SpotCode)
{
    public bool InLot => SpotCode is not null;
    public string VehicleText => Texts.Of(Vehicle);
    public string StatusText => Texts.Of(Status);
}

/// <summary>Bekleyen, yıkanan, hazır ve bugün teslim edilen siparişler. Tahmini bitiş, sıradaki işlerin süresi ve bölme sayısıyla hesaplanır.</summary>
public sealed record GetWashBoard(int Bays = 3) : IQuery<IReadOnlyList<WashCard>>;

public sealed class GetWashBoardHandler(IUnitOfWorkFactory uowFactory, TimeProvider time) : IQueryHandler<GetWashBoard, IReadOnlyList<WashCard>>
{
    public async Task<Result<IReadOnlyList<WashCard>>> HandleAsync(GetWashBoard q, CancellationToken cancellationToken)
    {
        var now = time.Now();
        var dayStart = now.Date;
        await using var uow = uowFactory.Create();
        var rows = await uow.Repository<WashOrder>().ListAsync(
            new Specification<WashOrder>().Where(w => w.Status == WashStatus.Waiting || w.Status == WashStatus.Washing || w.Status == WashStatus.Ready
                                                       || (w.Status == WashStatus.Delivered && w.DeliveredAt >= dayStart))
                .OrderBy(w => w.OrderedAt),
            w => new
            {
                w.Id, w.Plate, PackageName = w.Package.Name, w.Package.Minutes, w.Vehicle, w.Price, w.Status, w.Washer, w.Note,
                w.OrderedAt, w.StartedAt, w.ReadyAt, w.DeliveredAt, w.SessionId,
            },
            cancellationToken);
        var sessionIds = rows.Where(r => r.SessionId != null).Select(r => r.SessionId!.Value).ToList();
        var spots = (await uow.Repository<ParkingSession>().ListAsync(
                new Specification<ParkingSession>().Where(s => sessionIds.Contains(s.Id) && s.Status == SessionStatus.Active),
                s => new { s.Id, s.Spot.Code }, cancellationToken))
            .ToDictionary(s => s.Id, s => s.Code);

        // Bölmeler: yıkanan araçlar bitince boşalır, sıradakiler sırayla girer
        var bays = rows.Where(r => r.Status == WashStatus.Washing)
            .Select(r => Max(now, (r.StartedAt ?? now).AddMinutes(r.Minutes))).OrderBy(t => t).ToList();
        while (bays.Count < Math.Max(1, q.Bays)) bays.Add(now);
        var estimates = new Dictionary<int, DateTime>();
        foreach (var r in rows)
        {
            if (r.Status == WashStatus.Washing) estimates[r.Id] = Max(now, (r.StartedAt ?? now).AddMinutes(r.Minutes));
            else if (r.Status == WashStatus.Waiting)
            {
                bays.Sort();
                var start = bays[0];
                var end = start.AddMinutes(r.Minutes);
                bays[0] = end;
                estimates[r.Id] = end;
            }
            else estimates[r.Id] = r.ReadyAt ?? now;
        }

        IReadOnlyList<WashCard> cards = [.. rows.Select(r => new WashCard(r.Id, r.Plate, r.PackageName, r.Vehicle, r.Price, r.Status, r.Washer, r.Note,
            r.OrderedAt, r.StartedAt, r.ReadyAt, r.DeliveredAt, estimates[r.Id],
            r.SessionId is int sid && spots.TryGetValue(sid, out var code) ? code : null))];
        return Result<IReadOnlyList<WashCard>>.Success(cards);
    }

    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;
}
