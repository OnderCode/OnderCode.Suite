using OnderCode.Kernel.Domain;
using OnderCode.Kernel.Exceptions;

namespace Eczane.Domain;

/// <summary>
/// Nöbet türü. Eczacı odası eczaneye nöbet günlerini verir; gece nöbeti hafta içi akşamdan sabaha,
/// pazar ve bayram nöbeti tam gündür.
/// </summary>
public enum DutyKind { Night, Sunday, Holiday }

/// <summary>Eczanenin bir nöbet günü ve o gün nöbete kalan eczacı ile yardımcı personel.</summary>
public sealed class DutyShift : AuditableEntity
{
    private DutyShift() { }

    public DutyShift(DateOnly date, DutyKind kind, string? note = null)
    {
        Date = date;
        Kind = kind;
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
    }

    public DateOnly Date { get; private set; }
    public DutyKind Kind { get; private set; }
    public string? Note { get; private set; }

    public int? PharmacistId { get; private set; }
    public Employee? Pharmacist { get; private set; }

    public int? AssistantId { get; private set; }
    public Employee? Assistant { get; private set; }

    public bool IsStaffed => PharmacistId is not null || Pharmacist is not null;

    /// <summary>Adil dağıtım puanı: gece 1, pazar 2, bayram 3.</summary>
    public int Points => DutyPlanner.PointsOf(Kind);

    public void Assign(Employee pharmacist, Employee? assistant)
    {
        if (!pharmacist.IsPharmacist) throw new DomainException($"{pharmacist.FullName} eczacı değil; nöbette bir eczacı bulunmalı.");
        if (!pharmacist.IsActive) throw new DomainException($"{pharmacist.FullName} artık çalışmıyor.");
        if (assistant is not null && assistant.Id == pharmacist.Id && assistant.Id != 0) throw new DomainException("Aynı kişi iki görev alamaz.");
        if (assistant is { IsActive: false }) throw new DomainException($"{assistant.FullName} artık çalışmıyor.");
        Pharmacist = pharmacist;
        PharmacistId = pharmacist.Id;
        Assistant = assistant;
        AssistantId = assistant?.Id;
    }

    public void Clear()
    {
        Pharmacist = null;
        PharmacistId = null;
        Assistant = null;
        AssistantId = null;
    }
}

/// <summary>
/// Nöbetleri personele adil dağıtır. Her nöbet için izinde olmayan, son 2 gün içinde nöbet tutmamış ve
/// o ana kadarki puanı en düşük kişi seçilir (eşitlikte daha az nöbet tutan, sonra adı). Eczacı ve yardımcı ayrı havuzdur.
/// Geçmiş puanlar (bu ayın öncesi) başlangıç değeri olarak verilebilir; böylece denge aylar boyunca korunur.
/// </summary>
public static class DutyPlanner
{
    public const int RestDays = 2;

    public static int PointsOf(DutyKind kind) => kind switch
    {
        DutyKind.Sunday => 2,
        DutyKind.Holiday => 3,
        _ => 1,
    };

    public sealed record Person(int Id, string Name, bool IsPharmacist);

    public sealed record Assignment(DateOnly Date, DutyKind Kind, int? PharmacistId, int? AssistantId, string? Problem);

    public static IReadOnlyList<Assignment> Plan(
        IEnumerable<(DateOnly Date, DutyKind Kind)> shifts,
        IReadOnlyList<Person> people,
        Func<int, DateOnly, bool> isOnLeave,
        IReadOnlyDictionary<int, int>? startingPoints = null,
        IReadOnlyDictionary<int, DateOnly>? lastDuty = null)
    {
        var points = people.ToDictionary(p => p.Id, p => startingPoints?.GetValueOrDefault(p.Id) ?? 0);
        var counts = people.ToDictionary(p => p.Id, _ => 0);
        var last = people.ToDictionary(p => p.Id, p => lastDuty is not null && lastDuty.TryGetValue(p.Id, out var d) ? d : (DateOnly?)null);
        var result = new List<Assignment>();

        foreach (var (date, kind) in shifts.OrderBy(s => s.Date))
        {
            Person? Pick(bool pharmacist) => people
                .Where(p => p.IsPharmacist == pharmacist && !isOnLeave(p.Id, date))
                .Where(p => last[p.Id] is not { } l || date.DayNumber - l.DayNumber > RestDays)
                .OrderBy(p => points[p.Id]).ThenBy(p => counts[p.Id]).ThenBy(p => p.Name, StringComparer.Ordinal)
                .FirstOrDefault();

            var ph = Pick(true);
            var asst = Pick(false);
            var add = PointsOf(kind);
            foreach (var p in new[] { ph, asst })
            {
                if (p is null) continue;
                points[p.Id] += add;
                counts[p.Id]++;
                last[p.Id] = date;
            }
            result.Add(new Assignment(date, kind, ph?.Id, asst?.Id, ph is null ? "Uygun eczacı yok (izin ya da dinlenme)" : null));
        }
        return result;
    }
}
