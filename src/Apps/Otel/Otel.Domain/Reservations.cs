using OnderCode.Kernel.Domain;
using OnderCode.Kernel.Exceptions;

namespace Otel.Domain;

public enum ReservationStatus { Confirmed, CheckedIn, CheckedOut, Cancelled }

public enum ChargeCategory { Minibar, Restaurant, Laundry, Spa, Transfer, Other }

/// <summary>
/// Rezervasyon. Konaklama aralığı yarı açıktır: giriş günü dahil, çıkış günü hariç
/// (12–15 Kasım = 3 gece; 15 Kasım'da aynı odaya başka misafir girebilir).
/// </summary>
public sealed class Reservation : AuditableEntity
{
    private readonly List<FolioItem> _charges = [];

    private Reservation() { }

    public Reservation(string code, Guest guest, Room room, DateOnly checkIn, DateOnly checkOut, int adults, decimal roomTotal, DateOnly bookedOn, string? notes = null)
    {
        if (checkOut <= checkIn) throw new DomainException("Çıkış tarihi giriş tarihinden sonra olmalı.");
        if (adults < 1) throw new DomainException("En az bir yetişkin olmalı.");
        if (adults > room.RoomType.Capacity)
            throw new DomainException($"{room.Number} numaralı oda en fazla {room.RoomType.Capacity} kişiliktir.");

        Code = code;
        Guest = guest;
        Room = room;
        CheckIn = checkIn;
        CheckOut = checkOut;
        Adults = adults;
        RoomTotal = roomTotal;
        BookedOn = bookedOn;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        Status = ReservationStatus.Confirmed;
    }

    public string Code { get; private set; } = "";
    public int GuestId { get; private set; }
    public Guest Guest { get; private set; } = null!;
    public int RoomId { get; private set; }
    public Room Room { get; private set; } = null!;
    public DateOnly CheckIn { get; private set; }
    public DateOnly CheckOut { get; private set; }
    public DateOnly BookedOn { get; private set; }
    public int Adults { get; private set; }
    public ReservationStatus Status { get; private set; }
    /// <summary>Fiyat motorunun hesapladığı konaklama tutarı (indirim sonrası).</summary>
    public decimal RoomTotal { get; private set; }
    public string? Notes { get; private set; }
    public IReadOnlyCollection<FolioItem> Charges => _charges;

    public int Nights => CheckOut.DayNumber - CheckIn.DayNumber;
    public decimal ChargesTotal => _charges.Sum(c => c.Amount);
    public decimal Balance => RoomTotal + ChargesTotal;
    public bool IsActive => Status is ReservationStatus.Confirmed or ReservationStatus.CheckedIn;

    public bool Overlaps(DateOnly from, DateOnly to) => CheckIn < to && from < CheckOut;

    public void CheckInGuest(DateOnly today)
    {
        if (Status != ReservationStatus.Confirmed) throw new DomainException($"{Code}: yalnızca onaylı rezervasyona giriş yapılabilir.");
        if (today < CheckIn) throw new DomainException($"{Code}: giriş tarihi {CheckIn:dd.MM.yyyy}; erken giriş için rezervasyonu güncelleyin.");
        if (today >= CheckOut) throw new DomainException($"{Code}: konaklama tarihi geçmiş.");
        Room.MarkOccupied();
        Status = ReservationStatus.CheckedIn;
    }

    /// <summary>Çıkış: oda kirliye düşer, misafir puan kazanır. Temizlik görevi açılması çağıranın işidir.</summary>
    public void CheckOutGuest(int pointsPerNight)
    {
        if (Status != ReservationStatus.CheckedIn) throw new DomainException($"{Code}: çıkış için önce giriş yapılmış olmalı.");
        Status = ReservationStatus.CheckedOut;
        Room.MarkDirty();
        Guest.CompleteStay(Nights, pointsPerNight);
    }

    public void Cancel()
    {
        if (Status != ReservationStatus.Confirmed) throw new DomainException($"{Code}: yalnızca henüz giriş yapılmamış rezervasyon iptal edilebilir.");
        Status = ReservationStatus.Cancelled;
    }

    public FolioItem PostCharge(ChargeCategory category, string description, decimal amount, DateTime postedAt)
    {
        if (Status != ReservationStatus.CheckedIn) throw new DomainException($"{Code}: harcama yalnızca konaklayan misafirin odasına yazılır.");
        if (amount <= 0) throw new DomainException("Tutar sıfırdan büyük olmalı.");
        var item = new FolioItem(category, description, amount, postedAt);
        _charges.Add(item);
        return item;
    }

    /// <summary>Örnek veri ve geçmiş kayıt aktarımı için: harcamayı durum kontrolü olmadan ekler.</summary>
    public void ImportCharge(ChargeCategory category, string description, decimal amount, DateTime postedAt)
        => _charges.Add(new FolioItem(category, description, amount, postedAt));

    /// <summary>Örnek veri için geçmiş durumu doğrudan yazar.</summary>
    public void ImportStatus(ReservationStatus status) => Status = status;
}

/// <summary>Oda hesabına yazılan harcama (minibar, restoran...).</summary>
public sealed class FolioItem : Entity
{
    private FolioItem() { }

    internal FolioItem(ChargeCategory category, string description, decimal amount, DateTime postedAt)
    {
        Category = category;
        Description = description.Trim();
        Amount = amount;
        PostedAt = postedAt;
    }

    public int ReservationId { get; private set; }
    public ChargeCategory Category { get; private set; }
    public string Description { get; private set; } = "";
    public decimal Amount { get; private set; }
    public DateTime PostedAt { get; private set; }
}

/// <summary>Çakışma kuralı: aynı odada aktif iki rezervasyonun tarihleri kesişemez.</summary>
public static class OverbookingRule
{
    public static void EnsureFree(Room room, DateOnly checkIn, DateOnly checkOut, IEnumerable<Reservation> existing)
    {
        if (!room.IsSellable) throw new DomainException($"{room.Number} numaralı oda bakımda, satışa kapalı.");
        var clash = existing.FirstOrDefault(r => r.RoomId == room.Id && r.IsActive && r.Overlaps(checkIn, checkOut));
        if (clash is not null)
            throw new DomainException($"{room.Number} numaralı oda {clash.CheckIn:dd.MM} – {clash.CheckOut:dd.MM} arasında dolu ({clash.Code}).");
    }
}
