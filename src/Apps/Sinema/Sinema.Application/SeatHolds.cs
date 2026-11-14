using System.Collections.Concurrent;

namespace Sinema.Application;

/// <summary>Bir koltuğun geçici olarak kimin elinde olduğu.</summary>
public sealed record SeatHold(int SessionId, string SeatCode, string HolderId, DateTime ExpiresAt);

/// <summary>
/// Koltuk tutma (bellekte, tek sunucu). Online müşteri ya da gişe görevlisi bir koltuğa dokunduğunda koltuk
/// <see cref="HoldMinutes"/> dakika boyunca ona ayrılır; diğer ekranlar <see cref="Changed"/> olayıyla anında güncellenir.
/// Süre dolan tutmalar kendiliğinden düşer. Satış tamamlanınca tutmalar bırakılır, koltuk "satıldı" olur.
/// </summary>
public sealed class SeatHoldService(TimeProvider time)
{
    readonly ConcurrentDictionary<(int Session, string Seat), SeatHold> _holds = new();
    readonly Lock _gate = new();

    public int HoldMinutes { get; set; } = 5;

    /// <summary>Bir seansın koltuk durumu değişti (tutma, bırakma, satış, iade).</summary>
    public event Action<int>? Changed;

    DateTime Now => time.GetUtcNow().UtcDateTime;

    /// <summary>Koltuğu tutar. Başkası tutuyorsa ya da satılmışsa false.</summary>
    public bool TryHold(int sessionId, string seatCode, string holderId, ISet<string> soldSeats)
    {
        var key = (sessionId, seatCode.ToUpperInvariant());
        bool ok;
        lock (_gate)
        {
            Purge();
            if (soldSeats.Contains(key.Item2)) return false;
            if (_holds.TryGetValue(key, out var existing) && existing.HolderId != holderId) return false;
            _holds[key] = new SeatHold(sessionId, key.Item2, holderId, Now.AddMinutes(HoldMinutes));
            ok = true;
        }
        Changed?.Invoke(sessionId);
        return ok;
    }

    public void Release(int sessionId, string seatCode, string holderId)
    {
        var key = (sessionId, seatCode.ToUpperInvariant());
        var removed = false;
        lock (_gate)
        {
            if (_holds.TryGetValue(key, out var h) && h.HolderId == holderId) removed = _holds.TryRemove(key, out _);
        }
        if (removed) Changed?.Invoke(sessionId);
    }

    /// <summary>Kişinin bütün tutmalarını bırakır (sayfadan çıkınca, satış bitince).</summary>
    public void ReleaseAll(string holderId)
    {
        var sessions = new HashSet<int>();
        lock (_gate)
        {
            foreach (var kv in _holds.Where(k => k.Value.HolderId == holderId).ToList())
                if (_holds.TryRemove(kv.Key, out _)) sessions.Add(kv.Key.Session);
        }
        foreach (var s in sessions) Changed?.Invoke(s);
    }

    /// <summary>Kişinin tutmalarının süresini yeniler (ödeme adımına geçerken).</summary>
    public DateTime? Extend(string holderId)
    {
        DateTime? until = null;
        lock (_gate)
        {
            foreach (var kv in _holds.Where(k => k.Value.HolderId == holderId).ToList())
            {
                var renewed = kv.Value with { ExpiresAt = Now.AddMinutes(HoldMinutes) };
                _holds[kv.Key] = renewed;
                until = renewed.ExpiresAt;
            }
        }
        return until;
    }

    /// <summary>Seanstaki geçerli tutmalar.</summary>
    public IReadOnlyList<SeatHold> Holds(int sessionId)
    {
        lock (_gate)
        {
            Purge();
            return _holds.Values.Where(h => h.SessionId == sessionId).ToList();
        }
    }

    public IReadOnlyList<SeatHold> HoldsOf(string holderId)
    {
        lock (_gate)
        {
            Purge();
            return _holds.Values.Where(h => h.HolderId == holderId).ToList();
        }
    }

    /// <summary>Satış/iade sonrası ekranlara haber verir.</summary>
    public void NotifyChanged(int sessionId) => Changed?.Invoke(sessionId);

    /// <summary>Süresi dolanları temizler; etkilenen seanslara haber verir.</summary>
    public void Sweep()
    {
        List<int> affected;
        lock (_gate) affected = Purge();
        foreach (var s in affected.Distinct()) Changed?.Invoke(s);
    }

    List<int> Purge()
    {
        var now = Now;
        var affected = new List<int>();
        foreach (var kv in _holds.Where(k => k.Value.ExpiresAt <= now).ToList())
            if (_holds.TryRemove(kv.Key, out _)) affected.Add(kv.Key.Session);
        return affected;
    }
}
