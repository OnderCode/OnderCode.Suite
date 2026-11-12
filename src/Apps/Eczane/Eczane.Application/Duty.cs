using Eczane.Domain;
using OnderCode.Application.Messaging;
using OnderCode.Application.Results;
using OnderCode.Kernel.Exceptions;
using OnderCode.Kernel.Persistence;
using OnderCode.Kernel.Specifications;

namespace Eczane.Application;

public sealed record DutyCell(DateOnly Date, int? ShiftId, DutyKind? Kind, int? PharmacistId, string? Pharmacist, int? AssistantId, string? Assistant,
    IReadOnlyList<string> OnLeave, string? Note)
{
    public string KindText => Kind is { } k ? Texts.Of(k) : "";
    public bool IsDuty => ShiftId is not null;
    public bool Unstaffed => IsDuty && PharmacistId is null;
}

public sealed record DutyStat(int EmployeeId, string Name, StaffRole Role, int MonthCount, int YearCount, int YearPoints, int Sundays)
{
    public string RoleText => Texts.Of(Role);
}

public sealed record DutyMonth(int Year, int Month, IReadOnlyList<DutyCell> Days, IReadOnlyList<DutyStat> Stats)
{
    public string Title => Texts.MonthName(Year, Month);
    public int Duties => Days.Count(d => d.IsDuty);
    public int Unstaffed => Days.Count(d => d.Unstaffed);
}

/// <summary>Ayın nöbet çizelgesi: nöbet günleri, görevliler, o gün izinde olanlar ve yıl içi adil dağılım tablosu.</summary>
public sealed record GetDutyMonth(int Year, int Month) : IQuery<DutyMonth>;

public sealed class GetDutyMonthHandler(IUnitOfWorkFactory uowFactory) : IQueryHandler<GetDutyMonth, DutyMonth>
{
    public async Task<Result<DutyMonth>> HandleAsync(GetDutyMonth q, CancellationToken cancellationToken)
    {
        var first = new DateOnly(q.Year, q.Month, 1);
        var last = first.AddMonths(1).AddDays(-1);
        var yearStart = new DateOnly(q.Year, 1, 1);
        await using var uow = uowFactory.Create();
        var employees = await uow.Repository<Employee>().ListAsync(new Specification<Employee>().Where(e => e.IsActive || e.LeftOn >= yearStart), cancellationToken);
        var names = employees.ToDictionary(e => e.Id, e => e.FullName);
        var shifts = await uow.Repository<DutyShift>().ListAsync(new Specification<DutyShift>().Where(d => d.Date >= yearStart && d.Date <= last.AddMonths(2)), cancellationToken);
        var leaves = await uow.Repository<LeaveRecord>().ListAsync(new Specification<LeaveRecord>().Where(l => l.End >= first && l.Start <= last), cancellationToken);

        var days = new List<DutyCell>();
        for (var d = first; d <= last; d = d.AddDays(1))
        {
            var s = shifts.FirstOrDefault(x => x.Date == d);
            var off = leaves.Where(l => l.Covers(d)).Select(l => names.GetValueOrDefault(l.EmployeeId) ?? "").Where(n => n.Length > 0).ToList();
            days.Add(new DutyCell(d, s?.Id, s?.Kind, s?.PharmacistId, s?.PharmacistId is int p ? names.GetValueOrDefault(p) : null,
                s?.AssistantId, s?.AssistantId is int a ? names.GetValueOrDefault(a) : null, off, s?.Note));
        }
        var year = shifts.Where(s => s.Date.Year == q.Year).ToList();
        var stats = employees.Where(e => e.IsActive).Select(e =>
        {
            var mine = year.Where(s => s.PharmacistId == e.Id || s.AssistantId == e.Id).ToList();
            return new DutyStat(e.Id, e.FullName, e.Role, mine.Count(s => s.Date >= first && s.Date <= last), mine.Count, mine.Sum(s => s.Points),
                mine.Count(s => s.Kind != DutyKind.Night));
        }).OrderByDescending(s => s.Role is StaffRole.ResponsiblePharmacist or StaffRole.Pharmacist).ThenBy(s => s.Name).ToList();
        return new DutyMonth(q.Year, q.Month, days, stats);
    }
}

/// <summary>
/// Ayın nöbetlerini personele adil dağıtır (yıl başından beri biriken puanlar dikkate alınır). Geçmiş günlere dokunulmaz;
/// <paramref name="OnlyEmpty"/> açıksa yalnızca görevlisi olmayan nöbetler doldurulur.
/// </summary>
public sealed record PlanDutyMonth(int Year, int Month, bool OnlyEmpty = true) : ICommand<int>;

public sealed class PlanDutyMonthHandler(IUnitOfWorkFactory uowFactory, TimeProvider time) : ICommandHandler<PlanDutyMonth, int>
{
    public async Task<Result<int>> HandleAsync(PlanDutyMonth c, CancellationToken cancellationToken)
    {
        var today = time.Today();
        var first = new DateOnly(c.Year, c.Month, 1);
        var last = first.AddMonths(1).AddDays(-1);
        var from = first > today ? first : today;
        var yearStart = new DateOnly(c.Year, 1, 1);
        await using var uow = uowFactory.Create();
        var employees = await uow.Repository<Employee>().ListAsync(new Specification<Employee>().Where(e => e.IsActive).Tracking(), cancellationToken);
        var shifts = await uow.Repository<DutyShift>().ListAsync(new Specification<DutyShift>().Where(d => d.Date >= yearStart && d.Date <= last).Tracking(), cancellationToken);
        var leaves = await uow.Repository<LeaveRecord>().ListAsync(new Specification<LeaveRecord>().Where(l => l.End >= from && l.Start <= last), cancellationToken);

        var target = shifts.Where(s => s.Date >= from && s.Date <= last && (!c.OnlyEmpty || !s.IsStaffed)).ToList();
        if (target.Count == 0) return 0;
        var before = shifts.Except(target).Where(s => s.Date < from || s.IsStaffed).ToList();
        var points = employees.ToDictionary(e => e.Id, e => before.Where(s => s.PharmacistId == e.Id || s.AssistantId == e.Id).Sum(s => s.Points));
        var lastDuty = employees.Select(e => (e.Id, Last: before.Where(s => (s.PharmacistId == e.Id || s.AssistantId == e.Id) && s.Date < from)
                .Select(s => (DateOnly?)s.Date).Max()))
            .Where(x => x.Last is not null).ToDictionary(x => x.Id, x => x.Last!.Value);
        var people = employees.Where(e => e.Role != StaffRole.Intern)
            .Select(e => new DutyPlanner.Person(e.Id, e.FullName, e.IsPharmacist)).ToList();
        // Doldurulmuş nöbetlerin günleri dinlenme kuralına girsin
        var fixedDays = shifts.Where(s => s.IsStaffed && s.Date >= from && !target.Contains(s)).ToList();
        bool Busy(int id, DateOnly d) => leaves.Any(l => l.EmployeeId == id && l.Covers(d))
            || fixedDays.Any(s => (s.PharmacistId == id || s.AssistantId == id) && Math.Abs(s.Date.DayNumber - d.DayNumber) <= DutyPlanner.RestDays);

        var plan = DutyPlanner.Plan(target.Select(s => (s.Date, s.Kind)), people, Busy, points, lastDuty);
        var byId = employees.ToDictionary(e => e.Id);
        var assigned = 0;
        foreach (var a in plan)
        {
            var shift = target.First(s => s.Date == a.Date);
            if (a.PharmacistId is not int ph) { shift.Clear(); continue; }
            shift.Assign(byId[ph], a.AssistantId is int asst ? byId[asst] : null);
            assigned++;
        }
        await uow.SaveChangesAsync(cancellationToken);
        return assigned;
    }
}

public sealed record AssignDuty(int ShiftId, int PharmacistId, int? AssistantId) : ICommand;

public sealed class AssignDutyHandler(IUnitOfWorkFactory uowFactory) : ICommandHandler<AssignDuty>
{
    public async Task<Result> HandleAsync(AssignDuty c, CancellationToken cancellationToken)
    {
        await using var uow = uowFactory.Create();
        var shift = await uow.Repository<DutyShift>().GetRequiredAsync(c.ShiftId, cancellationToken);
        var ph = await uow.Repository<Employee>().GetRequiredAsync(c.PharmacistId, cancellationToken);
        var asst = c.AssistantId is int a ? await uow.Repository<Employee>().GetRequiredAsync(a, cancellationToken) : null;
        var onLeave = await uow.Repository<LeaveRecord>().ListAsync(new Specification<LeaveRecord>()
            .Where(l => l.Start <= shift.Date && l.End >= shift.Date && (l.EmployeeId == ph.Id || l.EmployeeId == (c.AssistantId ?? 0))).Include(l => l.Employee), cancellationToken);
        if (onLeave.Count > 0) throw new DomainException($"{onLeave[0].Employee.FullName} o gün izinde.");
        shift.Assign(ph, asst);
        await uow.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

/// <summary>Eczacı odasının verdiği nöbet gününü ekler.</summary>
public sealed record AddDutyDay(DateOnly Date, DutyKind Kind, string? Note = null) : ICommand<int>;

public sealed class AddDutyDayHandler(IUnitOfWorkFactory uowFactory) : ICommandHandler<AddDutyDay, int>
{
    public async Task<Result<int>> HandleAsync(AddDutyDay c, CancellationToken cancellationToken)
    {
        await using var uow = uowFactory.Create();
        if (await uow.Repository<DutyShift>().AnyAsync(new Specification<DutyShift>().Where(d => d.Date == c.Date), cancellationToken))
            throw new DomainException($"{c.Date:dd.MM.yyyy} zaten nöbet günü.");
        var kind = c.Kind == DutyKind.Night && c.Date.DayOfWeek == DayOfWeek.Sunday ? DutyKind.Sunday : c.Kind;
        var shift = new DutyShift(c.Date, kind, c.Note);
        uow.Repository<DutyShift>().Add(shift);
        await uow.SaveChangesAsync(cancellationToken);
        return shift.Id;
    }
}

public sealed record RemoveDutyDay(int ShiftId) : ICommand;

public sealed class RemoveDutyDayHandler(IUnitOfWorkFactory uowFactory, TimeProvider time) : ICommandHandler<RemoveDutyDay>
{
    public async Task<Result> HandleAsync(RemoveDutyDay c, CancellationToken cancellationToken)
    {
        await using var uow = uowFactory.Create();
        var shift = await uow.Repository<DutyShift>().GetRequiredAsync(c.ShiftId, cancellationToken);
        if (shift.Date < time.Today()) throw new DomainException("Geçmiş nöbet silinemez.");
        uow.Repository<DutyShift>().Remove(shift);
        await uow.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
