using OnderCode.Kernel.Domain;
using OnderCode.Kernel.Exceptions;

namespace Otel.Domain;

public enum RoomStatus
{
    Available,   // Boş ve temiz
    Occupied,    // Misafir var
    Dirty,       // Çıkış yapıldı, temizlik bekliyor
    Maintenance, // Bakımda, satışa kapalı
}

public sealed class RoomType : Entity
{
    private RoomType() { }

    public RoomType(string code, string name, int capacity, decimal baseRate)
    {
        Code = code;
        Name = name;
        Capacity = capacity;
        BaseRate = baseRate;
    }

    public string Code { get; private set; } = "";
    public string Name { get; private set; } = "";
    public int Capacity { get; private set; }
    /// <summary>Sezon ve hafta sonu çarpanı uygulanmamış gecelik fiyat.</summary>
    public decimal BaseRate { get; private set; }
}

public sealed class Room : AuditableEntity
{
    private Room() { }

    public Room(string number, int floor, RoomType type)
    {
        Number = number;
        Floor = floor;
        RoomType = type;
        Status = RoomStatus.Available;
    }

    public string Number { get; private set; } = "";
    public int Floor { get; private set; }
    public int RoomTypeId { get; private set; }
    public RoomType RoomType { get; private set; } = null!;
    public RoomStatus Status { get; private set; }
    public string? MaintenanceNote { get; private set; }

    public bool IsSellable => Status != RoomStatus.Maintenance;

    public void MarkOccupied()
    {
        if (Status == RoomStatus.Maintenance) throw new DomainException($"{Number} numaralı oda bakımda; giriş yapılamaz.");
        if (Status == RoomStatus.Dirty) throw new DomainException($"{Number} numaralı oda henüz temizlenmedi; önce kat hizmetleri odayı hazırlamalı.");
        Status = RoomStatus.Occupied;
    }

    public void MarkDirty() => Status = RoomStatus.Dirty;

    public void MarkClean()
    {
        if (Status == RoomStatus.Occupied) throw new DomainException($"{Number} numaralı odada misafir var.");
        Status = RoomStatus.Available;
        MaintenanceNote = null;
    }

    public void StartMaintenance(string? note)
    {
        if (Status == RoomStatus.Occupied) throw new DomainException($"{Number} numaralı odada misafir varken bakıma alınamaz.");
        Status = RoomStatus.Maintenance;
        MaintenanceNote = string.IsNullOrWhiteSpace(note) ? "Bakım" : note.Trim();
    }
}
