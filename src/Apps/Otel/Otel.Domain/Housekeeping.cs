using OnderCode.Kernel.Domain;
using OnderCode.Kernel.Exceptions;

namespace Otel.Domain;

public enum TaskKind { Cleaning, Maintenance }

public enum TaskState { Open, Done }

/// <summary>Kat hizmetleri görevi: çıkıştan sonra temizlik ya da bakım.</summary>
public sealed class HousekeepingTask : AuditableEntity
{
    private HousekeepingTask() { }

    public HousekeepingTask(Room room, TaskKind kind, string? note = null, string? assignedTo = null)
    {
        Room = room;
        Kind = kind;
        Note = note;
        AssignedTo = assignedTo;
        State = TaskState.Open;
    }

    public int RoomId { get; private set; }
    public Room Room { get; private set; } = null!;
    public TaskKind Kind { get; private set; }
    public TaskState State { get; private set; }
    public string? AssignedTo { get; private set; }
    public string? Note { get; private set; }
    public DateTime? CompletedAt { get; private set; }

    public void Assign(string person) => AssignedTo = person;

    /// <summary>Görev bitince oda yeniden satışa açılır.</summary>
    public void Complete(DateTime at)
    {
        if (State == TaskState.Done) throw new DomainException("Bu görev zaten tamamlanmış.");
        State = TaskState.Done;
        CompletedAt = at;
        Room.MarkClean();
    }
}
