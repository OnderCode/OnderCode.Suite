using OnderCode.Application.Messaging;
using OnderCode.Application.Results;
using OnderCode.Kernel.Persistence;
using OnderCode.Kernel.Specifications;
using Otel.Domain;

namespace Otel.Application;

// ---------- Oda haritası ----------

public sealed record RoomTile(
    int RoomId, string Number, int Floor, string RoomType, int Capacity, decimal BaseRate, RoomStatus Status,
    string? GuestName, DateOnly? CheckOut, int? ReservationId, string? MaintenanceNote,
    string? ArrivalGuest, int? ArrivalReservationId)
{
    public string StatusText => Texts.Of(Status);
}

public sealed record GetRoomBoard : IQuery<IReadOnlyList<RoomTile>>;

public sealed class GetRoomBoardHandler(IUnitOfWorkFactory uowFactory, TimeProvider time) : IQueryHandler<GetRoomBoard, IReadOnlyList<RoomTile>>
{
    public async Task<Result<IReadOnlyList<RoomTile>>> HandleAsync(GetRoomBoard query, CancellationToken cancellationToken)
    {
        var today = time.Today();
        await using var uow = uowFactory.Create();

        var rooms = await uow.Repository<Room>().ListAsync(
            new Specification<Room>().OrderBy(r => r.Floor).OrderBy(r => r.Number),
            r => new { r.Id, r.Number, r.Floor, Type = r.RoomType.Name, r.RoomType.Capacity, r.RoomType.BaseRate, r.Status, r.MaintenanceNote },
            cancellationToken);

        var inHouse = await uow.Repository<Reservation>().ListAsync(
            new Specification<Reservation>().Where(r => r.Status == ReservationStatus.CheckedIn),
            r => new { r.Id, r.RoomId, r.Guest.FullName, r.CheckOut },
            cancellationToken);

        var arrivals = await uow.Repository<Reservation>().ListAsync(
            new Specification<Reservation>().Where(r => r.Status == ReservationStatus.Confirmed && r.CheckIn == today),
            r => new { r.Id, r.RoomId, r.Guest.FullName },
            cancellationToken);

        var stay = inHouse.ToDictionary(x => x.RoomId);
        var arrival = arrivals.GroupBy(x => x.RoomId).ToDictionary(g => g.Key, g => g.First());

        IReadOnlyList<RoomTile> tiles = [.. rooms.Select(r =>
        {
            stay.TryGetValue(r.Id, out var s);
            arrival.TryGetValue(r.Id, out var a);
            return new RoomTile(r.Id, r.Number, r.Floor, r.Type, r.Capacity, r.BaseRate, r.Status,
                s?.FullName, s?.CheckOut, s?.Id, r.MaintenanceNote, a?.FullName, a?.Id);
        })];
        return Result<IReadOnlyList<RoomTile>>.Success(tiles);
    }
}

// ---------- Bakım ----------

public sealed record StartMaintenance(int RoomId, string? Note) : ICommand;

public sealed class StartMaintenanceHandler(IUnitOfWorkFactory uowFactory) : ICommandHandler<StartMaintenance>
{
    public async Task<Result> HandleAsync(StartMaintenance command, CancellationToken cancellationToken)
    {
        await using var uow = uowFactory.Create();
        var room = await uow.Repository<Room>().GetRequiredAsync(command.RoomId, cancellationToken);
        room.StartMaintenance(command.Note);
        uow.Repository<HousekeepingTask>().Add(new HousekeepingTask(room, TaskKind.Maintenance, room.MaintenanceNote));
        await uow.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

// ---------- Kat hizmetleri ----------

public sealed record TaskRow(int Id, string RoomNumber, int Floor, string KindText, string StateText, string? AssignedTo, string? Note, DateTime CreatedAt, DateTime? CompletedAt);

public sealed record GetHousekeepingTasks(bool IncludeDone = false) : IQuery<IReadOnlyList<TaskRow>>;

public sealed class GetHousekeepingTasksHandler(IUnitOfWorkFactory uowFactory) : IQueryHandler<GetHousekeepingTasks, IReadOnlyList<TaskRow>>
{
    public async Task<Result<IReadOnlyList<TaskRow>>> HandleAsync(GetHousekeepingTasks query, CancellationToken cancellationToken)
    {
        await using var uow = uowFactory.Create();
        var spec = new Specification<HousekeepingTask>().OrderBy(t => t.State).OrderByDescending(t => t.CreatedAt);
        if (!query.IncludeDone) spec.Where(t => t.State == TaskState.Open);
        var rows = await uow.Repository<HousekeepingTask>().ListAsync(spec,
            t => new { t.Id, t.Room.Number, t.Room.Floor, t.Kind, t.State, t.AssignedTo, t.Note, t.CreatedAt, t.CompletedAt },
            cancellationToken);
        IReadOnlyList<TaskRow> result = [.. rows.Select(t => new TaskRow(t.Id, t.Number, t.Floor, Texts.Of(t.Kind), Texts.Of(t.State),
            t.AssignedTo, t.Note, t.CreatedAt.ToLocalTime(), t.CompletedAt?.ToLocalTime()))];
        return Result<IReadOnlyList<TaskRow>>.Success(result);
    }
}

public sealed record CompleteTask(int TaskId) : ICommand;

public sealed class CompleteTaskHandler(IUnitOfWorkFactory uowFactory, TimeProvider time) : ICommandHandler<CompleteTask>
{
    public async Task<Result> HandleAsync(CompleteTask command, CancellationToken cancellationToken)
    {
        await using var uow = uowFactory.Create();
        var task = await uow.Repository<HousekeepingTask>().FirstOrDefaultAsync(
            new Specification<HousekeepingTask>().Where(t => t.Id == command.TaskId).Include(t => t.Room).Tracking(), cancellationToken)
            ?? throw new OnderCode.Kernel.Exceptions.NotFoundException("Görev", command.TaskId);
        task.Complete(time.GetUtcNow().UtcDateTime);
        await uow.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

public sealed record AssignTask(int TaskId, string Person) : ICommand;

public sealed class AssignTaskHandler(IUnitOfWorkFactory uowFactory) : ICommandHandler<AssignTask>
{
    public async Task<Result> HandleAsync(AssignTask command, CancellationToken cancellationToken)
    {
        await using var uow = uowFactory.Create();
        var task = await uow.Repository<HousekeepingTask>().GetRequiredAsync(command.TaskId, cancellationToken);
        task.Assign(command.Person.Trim());
        await uow.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

// ---------- Temizlik / bakım bitti ----------

/// <summary>Odanın açık görevlerini kapatır ve odayı yeniden satışa açar (kirli ya da bakımdaki oda için).</summary>
public sealed record MarkRoomReady(int RoomId) : ICommand;

public sealed class MarkRoomReadyHandler(IUnitOfWorkFactory uowFactory, TimeProvider time) : ICommandHandler<MarkRoomReady>
{
    public async Task<Result> HandleAsync(MarkRoomReady command, CancellationToken cancellationToken)
    {
        await using var uow = uowFactory.Create();
        var room = await uow.Repository<Room>().GetRequiredAsync(command.RoomId, cancellationToken);
        var tasks = await uow.Repository<HousekeepingTask>().ListAsync(
            new Specification<HousekeepingTask>().Where(t => t.RoomId == command.RoomId && t.State == TaskState.Open).Tracking(), cancellationToken);
        var now = time.GetUtcNow().UtcDateTime;
        foreach (var task in tasks) task.Complete(now);
        if (tasks.Count == 0) room.MarkClean();
        await uow.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
