using FluentValidation;
using OnderCode.Application.Messaging;
using OnderCode.Application.Results;
using OnderCode.Kernel.Exceptions;
using OnderCode.Kernel.Persistence;
using OnderCode.Kernel.Specifications;
using Otopark.Domain;

namespace Otopark.Application;

// ---------- Otopark haritası ----------

public sealed record SpotTile(
    int SpotId, string Code, string Zone, SpotKind Kind, bool OutOfService, string? ServiceNote,
    int? SessionId, string? Plate, VehicleClass? Vehicle, DateTime? EntryAt, decimal? CurrentFee,
    bool IsSubscriber, string? SubscriberName, WashStatus? Wash)
{
    public bool Occupied => SessionId is not null;
    public string KindText => Texts.Of(Kind);
}

public sealed record GetLotBoard : IQuery<IReadOnlyList<SpotTile>>;

public sealed class GetLotBoardHandler(IUnitOfWorkFactory uowFactory, TimeProvider time) : IQueryHandler<GetLotBoard, IReadOnlyList<SpotTile>>
{
    public async Task<Result<IReadOnlyList<SpotTile>>> HandleAsync(GetLotBoard query, CancellationToken cancellationToken)
    {
        var now = time.Now();
        await using var uow = uowFactory.Create();
        var spots = await uow.Repository<ParkingSpot>().ListAsync(new Specification<ParkingSpot>().OrderBy(s => s.Zone).OrderBy(s => s.Code), cancellationToken);
        var sessions = await uow.Repository<ParkingSession>().ListAsync(
            new Specification<ParkingSession>().Where(s => s.Status == SessionStatus.Active).Include(s => s.Subscription!), cancellationToken);
        var sessionIds = sessions.Select(s => s.Id).ToList();
        var washes = await uow.Repository<WashOrder>().ListAsync(
            new Specification<WashOrder>().Where(w => w.SessionId != null && sessionIds.Contains(w.SessionId.Value) && w.Status != WashStatus.Cancelled),
            w => new { w.SessionId, w.Status },
            cancellationToken);
        var washBySession = washes.GroupBy(w => w.SessionId!.Value).ToDictionary(g => g.Key, g => g.Min(w => w.Status));
        var bySpot = sessions.GroupBy(s => s.SpotId).ToDictionary(g => g.Key, g => g.First());

        IReadOnlyList<SpotTile> tiles = [.. spots.Select(spot =>
        {
            if (!bySpot.TryGetValue(spot.Id, out var s))
                return new SpotTile(spot.Id, spot.Code, spot.Zone, spot.Kind, spot.OutOfService, spot.ServiceNote, null, null, null, null, null, false, null, null);
            var washed = washBySession.TryGetValue(s.Id, out var ws);
            var fee = s.Quote(now, washed).Total;
            return new SpotTile(spot.Id, spot.Code, spot.Zone, spot.Kind, spot.OutOfService, spot.ServiceNote,
                s.Id, s.Plate, s.Vehicle, s.EntryAt, fee, s.SubscriptionId is not null, s.Subscription?.OwnerName, washed ? ws : null);
        })];
        return Result<IReadOnlyList<SpotTile>>.Success(tiles);
    }
}

/// <summary>Park yerini kullanım dışı bırakır ya da yeniden açar.</summary>
public sealed record SetSpotService(int SpotId, bool OutOfService, string? Note) : ICommand;

public sealed class SetSpotServiceHandler(IUnitOfWorkFactory uowFactory) : ICommandHandler<SetSpotService>
{
    public async Task<Result> HandleAsync(SetSpotService c, CancellationToken cancellationToken)
    {
        await using var uow = uowFactory.Create();
        var spot = await uow.Repository<ParkingSpot>().GetRequiredAsync(c.SpotId, cancellationToken);
        if (c.OutOfService)
        {
            var busy = await uow.Repository<ParkingSession>().AnyAsync(
                new Specification<ParkingSession>().Where(s => s.SpotId == c.SpotId && s.Status == SessionStatus.Active), cancellationToken);
            if (busy) throw new DomainException($"{spot.Code} dolu; önce aracı başka yere alın.");
            spot.TakeOutOfService(c.Note);
        }
        else spot.PutInService();
        await uow.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

/// <summary>Araç sınıfına uygun boş yerler (giriş formundaki liste).</summary>
public sealed record SpotOption(int SpotId, string Code, string Zone, string KindText, int Preference)
{
    public string Display => $"{Code} · {KindText}";
}

public sealed record GetFreeSpots(VehicleClass Vehicle) : IQuery<IReadOnlyList<SpotOption>>;

public sealed class GetFreeSpotsHandler(IUnitOfWorkFactory uowFactory) : IQueryHandler<GetFreeSpots, IReadOnlyList<SpotOption>>
{
    public async Task<Result<IReadOnlyList<SpotOption>>> HandleAsync(GetFreeSpots q, CancellationToken cancellationToken)
    {
        await using var uow = uowFactory.Create();
        var free = await SpotFinder.FreeSpotsAsync(uow, q.Vehicle, cancellationToken);
        IReadOnlyList<SpotOption> result = [.. free.Select(s => new SpotOption(s.Id, s.Code, s.Zone, Texts.Of(s.Kind), s.Preference(q.Vehicle)))];
        return Result<IReadOnlyList<SpotOption>>.Success(result);
    }
}

internal static class SpotFinder
{
    /// <summary>Boş, kullanımda ve araç sınıfına uygun yerler; tercih sırasına göre.</summary>
    public static async Task<List<ParkingSpot>> FreeSpotsAsync(IUnitOfWork uow, VehicleClass vehicle, CancellationToken ct, bool tracking = false)
    {
        var spec = new Specification<ParkingSpot>().Where(s => !s.OutOfService);
        if (tracking) spec.Tracking();
        var spots = await uow.Repository<ParkingSpot>().ListAsync(spec, ct);
        var busy = (await uow.Repository<ParkingSession>().ListAsync(
            new Specification<ParkingSession>().Where(s => s.Status == SessionStatus.Active), s => s.SpotId, ct)).ToHashSet();
        return [.. spots.Where(s => !busy.Contains(s.Id) && s.Accepts(vehicle))
            .OrderBy(s => s.Preference(vehicle)).ThenBy(s => s.Code, StringComparer.Ordinal)];
    }
}
