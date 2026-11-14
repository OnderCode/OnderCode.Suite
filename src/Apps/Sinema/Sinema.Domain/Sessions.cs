using OnderCode.Kernel.Domain;
using OnderCode.Kernel.Exceptions;

namespace Sinema.Domain;

public enum SessionFormat { TwoD, ThreeD }

public enum SessionLanguage { Dubbed, Subtitled }

/// <summary>
/// Sinemanın işletme ayarları (tek satır). Seans bitişi = başlangıç + reklam + film süresi; ardından salon temizliği.
/// </summary>
public sealed class CinemaSettings : Entity
{
    public CinemaSettings() { }

    public string Name { get; private set; } = "OnderCode Sinema";
    public string Branch { get; private set; } = "Merkez";
    /// <summary>Filmden önce gösterilen reklam ve fragman süresi.</summary>
    public int AdsMinutes { get; private set; } = 15;
    /// <summary>Seanslar arasında salon temizliği için ayrılan süre.</summary>
    public int CleaningMinutes { get; private set; } = 15;
    /// <summary>Online satışta seçilen koltuğun başkasına kapalı tutulduğu süre.</summary>
    public int HoldMinutes { get; private set; } = 5;
    /// <summary>Kapıda bilet, seans başlangıcından bu kadar dakika önce okutulabilir.</summary>
    public int CheckInOpensMinutes { get; private set; } = 45;
    /// <summary>Online satış seans başlangıcından bu kadar dakika önce kapanır.</summary>
    public int OnlineSalesCloseMinutes { get; private set; } = 15;
    public TimeOnly OpensAt { get; private set; } = new(10, 0);
    /// <summary>Gece yarısından sonra kapanabilir (ör. 01:00).</summary>
    public TimeOnly LastSessionAt { get; private set; } = new(23, 30);

    public void Update(string name, string branch, int ads, int cleaning, int hold, int checkIn, int onlineClose, TimeOnly opens, TimeOnly lastSession)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Sinema adı boş olamaz.");
        if (ads is < 0 or > 40) throw new DomainException("Reklam süresi 0-40 dakika olmalı.");
        if (cleaning is < 5 or > 60) throw new DomainException("Temizlik süresi 5-60 dakika olmalı.");
        if (hold is < 1 or > 20) throw new DomainException("Koltuk tutma süresi 1-20 dakika olmalı.");
        Name = name.Trim();
        Branch = (branch ?? "").Trim();
        AdsMinutes = ads;
        CleaningMinutes = cleaning;
        HoldMinutes = hold;
        CheckInOpensMinutes = Math.Clamp(checkIn, 5, 180);
        OnlineSalesCloseMinutes = Math.Clamp(onlineClose, 0, 120);
        OpensAt = opens;
        LastSessionAt = lastSession;
    }
}

/// <summary>Bir filmin bir salondaki gösterimi.</summary>
public sealed class Session : FullAuditableEntity
{
    private Session() { }

    public Session(Film film, Hall hall, DateTime start, SessionFormat format, SessionLanguage language, CinemaSettings settings)
    {
        if (!film.CanSchedule) throw new DomainException($"“{film.Title}” arşivde; seans açılamaz.");
        if (!hall.IsActive) throw new DomainException($"{hall.Name} kullanıma kapalı.");
        if (format == SessionFormat.ThreeD && !hall.Supports3D) throw new DomainException($"{hall.Name} 3D gösterim desteklemiyor.");
        Film = film;
        FilmId = film.Id;
        Hall = hall;
        HallId = hall.Id;
        Format = format;
        Language = language;
        Reschedule(start, film.DurationMinutes, settings);
    }

    public int FilmId { get; private set; }
    public Film? Film { get; private set; }
    public int HallId { get; private set; }
    public Hall? Hall { get; private set; }
    public DateTime Start { get; private set; }
    /// <summary>Film bitişi (reklam + film).</summary>
    public DateTime End { get; private set; }
    /// <summary>Salonun bir sonraki seansa hazır olduğu an (bitiş + temizlik).</summary>
    public DateTime ReadyAt { get; private set; }
    public SessionFormat Format { get; private set; }
    public SessionLanguage Language { get; private set; }
    public bool IsCancelled { get; private set; }

    public void Reschedule(DateTime start, int filmMinutes, CinemaSettings settings)
    {
        Start = new DateTime(start.Year, start.Month, start.Day, start.Hour, start.Minute, 0, DateTimeKind.Unspecified);
        End = Start.AddMinutes(settings.AdsMinutes + filmMinutes);
        ReadyAt = End.AddMinutes(settings.CleaningMinutes);
    }

    /// <summary>Aynı salonda zaman çakışması var mı? Temizlik süresi de dolu sayılır.</summary>
    public bool OverlapsWith(DateTime start, DateTime readyAt) => !IsCancelled && Start < readyAt && start < ReadyAt;

    public void Cancel()
    {
        if (IsCancelled) throw new DomainException("Seans zaten iptal edilmiş.");
        IsCancelled = true;
    }
}

/// <summary>Seans planlama kuralları (saf mantık, testlerle doğrulanır).</summary>
public static class SessionRules
{
    /// <summary>Yeni seansın aynı salondaki seanslarla çakışıp çakışmadığını söyler; çakışan seansı döndürür.</summary>
    public static Session? FindConflict(IEnumerable<Session> sameHall, DateTime start, DateTime readyAt, int? ignoreId = null)
        => sameHall.Where(s => s.Id != ignoreId).OrderBy(s => s.Start).FirstOrDefault(s => s.OverlapsWith(start, readyAt));

    /// <summary>Bir salonda o gün için ilk boş başlangıç saatini önerir (5 dakikaya yuvarlanır).</summary>
    public static DateTime SuggestStart(IEnumerable<Session> sameHallSameDay, DateOnly day, CinemaSettings settings)
    {
        var last = sameHallSameDay.Where(s => !s.IsCancelled).Select(s => s.ReadyAt).DefaultIfEmpty(day.ToDateTime(settings.OpensAt)).Max();
        var minute = (int)Math.Ceiling(last.Minute / 5.0) * 5;
        return new DateTime(last.Year, last.Month, last.Day, last.Hour, 0, 0).AddMinutes(minute);
    }
}
