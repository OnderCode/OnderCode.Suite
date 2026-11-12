using Eczane.Domain;
using FluentValidation;
using OnderCode.Application.Messaging;
using OnderCode.Application.Results;
using OnderCode.Kernel.Exceptions;
using OnderCode.Kernel.Persistence;
using OnderCode.Kernel.Specifications;

namespace Eczane.Application;

// ---------- Personel ----------

public sealed record EmployeeRow(int Id, string FullName, StaffRole Role, DateOnly HireDate, decimal MonthlyGross, bool IsActive, int YearsOfService,
    int LeaveEntitlement, int LeaveUsed, int SickDays, bool OnLeaveToday, int DutiesThisYear, int DutyPoints, DateOnly? NextDuty,
    int Sales30, decimal Revenue30)
{
    public string RoleText => Texts.Of(Role);
    public int LeaveLeft => Math.Max(0, LeaveEntitlement - LeaveUsed);
    public string StatusText => !IsActive ? "Ayrıldı" : OnLeaveToday ? "İzinde" : "Çalışıyor";
    public bool IsPharmacist => Role is StaffRole.ResponsiblePharmacist or StaffRole.Pharmacist;
}

public sealed record ListEmployees(bool IncludeInactive = false) : IQuery<IReadOnlyList<EmployeeRow>>;

public sealed class ListEmployeesHandler(IUnitOfWorkFactory uowFactory, TimeProvider time) : IQueryHandler<ListEmployees, IReadOnlyList<EmployeeRow>>
{
    public async Task<Result<IReadOnlyList<EmployeeRow>>> HandleAsync(ListEmployees q, CancellationToken cancellationToken)
    {
        var today = time.Today();
        var yearStart = new DateOnly(today.Year, 1, 1);
        var yearEnd = new DateOnly(today.Year, 12, 31);
        await using var uow = uowFactory.Create();
        var spec = new Specification<Employee>().OrderBy(e => e.Role).OrderBy(e => e.FullName);
        if (!q.IncludeInactive) spec.Where(e => e.IsActive);
        var employees = await uow.Repository<Employee>().ListAsync(spec, cancellationToken);
        var leaves = await uow.Repository<LeaveRecord>().ListAsync(new Specification<LeaveRecord>().Where(l => l.End >= yearStart && l.Start <= yearEnd), cancellationToken);
        var duties = await uow.Repository<DutyShift>().ListAsync(new Specification<DutyShift>().Where(d => d.Date >= yearStart && d.Date <= yearEnd),
            d => new { d.Date, d.Kind, d.PharmacistId, d.AssistantId }, cancellationToken);
        var from30 = today.AddDays(-29).Start();
        var sales = await uow.Repository<Sale>().ListAsync(new Specification<Sale>().Where(s => s.SoldAt >= from30 && s.Status == SaleStatus.Completed && s.SoldById != null),
            s => new { s.SoldById, s.PatientShare, s.InstitutionShare }, cancellationToken);

        return employees.Select(e =>
        {
            var mine = leaves.Where(l => l.EmployeeId == e.Id).ToList();
            var myDuties = duties.Where(d => d.PharmacistId == e.Id || d.AssistantId == e.Id).ToList();
            var past = myDuties.Where(d => d.Date <= today).ToList();
            var mySales = sales.Where(s => s.SoldById == e.Id).ToList();
            return new EmployeeRow(e.Id, e.FullName, e.Role, e.HireDate, e.MonthlyGross, e.IsActive, e.YearsOfService(today), e.AnnualLeaveEntitlement(today),
                mine.Where(l => l.Type == LeaveType.Annual).Sum(l => l.Days), mine.Where(l => l.Type == LeaveType.Sick).Sum(l => l.Days),
                mine.Any(l => l.Covers(today)), past.Count, past.Sum(d => DutyPlanner.PointsOf(d.Kind)),
                myDuties.Where(d => d.Date > today).Select(d => (DateOnly?)d.Date).Min(), mySales.Count, mySales.Sum(s => s.PatientShare + s.InstitutionShare));
        }).ToList();
    }
}

public sealed record SaveEmployee(int? Id, string FullName, StaffRole Role, DateOnly HireDate, decimal MonthlyGross) : ICommand<int>;

public sealed class SaveEmployeeValidator : AbstractValidator<SaveEmployee>
{
    public SaveEmployeeValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(100).WithName("Ad soyad");
        RuleFor(x => x.MonthlyGross).InclusiveBetween(0, 10_000_000).WithName("Aylık brüt");
    }
}

public sealed class SaveEmployeeHandler(IUnitOfWorkFactory uowFactory) : ICommandHandler<SaveEmployee, int>
{
    public async Task<Result<int>> HandleAsync(SaveEmployee c, CancellationToken cancellationToken)
    {
        await using var uow = uowFactory.Create();
        Employee e;
        if (c.Id is int id)
        {
            e = await uow.Repository<Employee>().GetRequiredAsync(id, cancellationToken);
            e.Rename(c.FullName);
            e.ChangeRole(c.Role);
            e.SetSalary(c.MonthlyGross);
        }
        else
        {
            e = new Employee(c.FullName, c.Role, c.HireDate, c.MonthlyGross);
            uow.Repository<Employee>().Add(e);
        }
        await uow.SaveChangesAsync(cancellationToken);
        return e.Id;
    }
}

public sealed record DismissEmployee(int EmployeeId, DateOnly On) : ICommand;

public sealed class DismissEmployeeHandler(IUnitOfWorkFactory uowFactory) : ICommandHandler<DismissEmployee>
{
    public async Task<Result> HandleAsync(DismissEmployee c, CancellationToken cancellationToken)
    {
        await using var uow = uowFactory.Create();
        var e = await uow.Repository<Employee>().GetRequiredAsync(c.EmployeeId, cancellationToken);
        e.Leave(c.On);
        // Ayrılan kişinin gelecekteki nöbetleri boşa düşer; yeniden dağıtılmalı
        var future = await uow.Repository<DutyShift>().ListAsync(new Specification<DutyShift>()
            .Where(d => d.Date >= c.On && (d.PharmacistId == e.Id || d.AssistantId == e.Id)).Tracking(), cancellationToken);
        foreach (var d in future) d.Clear();
        await uow.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

// ---------- İzinler ----------

public sealed record LeaveRow(int Id, int EmployeeId, string Employee, LeaveType Type, DateOnly Start, DateOnly End, int Days, string? Note)
{
    public string TypeText => Texts.Of(Type);
}

public sealed record ListLeaves(int Year) : IQuery<IReadOnlyList<LeaveRow>>;

public sealed class ListLeavesHandler(IUnitOfWorkFactory uowFactory) : IQueryHandler<ListLeaves, IReadOnlyList<LeaveRow>>
{
    public async Task<Result<IReadOnlyList<LeaveRow>>> HandleAsync(ListLeaves q, CancellationToken cancellationToken)
    {
        var from = new DateOnly(q.Year, 1, 1);
        var to = new DateOnly(q.Year, 12, 31);
        await using var uow = uowFactory.Create();
        var list = await uow.Repository<LeaveRecord>().ListAsync(new Specification<LeaveRecord>().Where(l => l.End >= from && l.Start <= to)
            .Include(l => l.Employee).OrderByDescending(l => l.Start), cancellationToken);
        return list.Select(l => new LeaveRow(l.Id, l.EmployeeId, l.Employee.FullName, l.Type, l.Start, l.End, l.Days, l.Note)).ToList();
    }
}

/// <summary>İzin girişi. Çakışan izin olamaz; yıllık izin kalan hakkı aşamaz. O günlere denk gelen nöbetler boşaltılır.</summary>
public sealed record AddLeave(int EmployeeId, LeaveType Type, DateOnly Start, DateOnly End, string? Note) : ICommand<int>;

public sealed class AddLeaveValidator : AbstractValidator<AddLeave>
{
    public AddLeaveValidator()
    {
        RuleFor(x => x.End).GreaterThanOrEqualTo(x => x.Start).WithMessage("Bitiş başlangıçtan önce olamaz.");
        RuleFor(x => x.Note).MaximumLength(150).WithName("Not");
    }
}

public sealed class AddLeaveHandler(IUnitOfWorkFactory uowFactory) : ICommandHandler<AddLeave, int>
{
    public async Task<Result<int>> HandleAsync(AddLeave c, CancellationToken cancellationToken)
    {
        await using var uow = uowFactory.Create();
        var e = await uow.Repository<Employee>().GetRequiredAsync(c.EmployeeId, cancellationToken);
        var existing = await uow.Repository<LeaveRecord>().ListAsync(new Specification<LeaveRecord>().Where(l => l.EmployeeId == e.Id && l.End >= c.Start.AddYears(-1)), cancellationToken);
        if (existing.FirstOrDefault(l => l.Overlaps(c.Start, c.End)) is { } clash)
            throw new DomainException($"{e.FullName} için {clash.Start:dd.MM}–{clash.End:dd.MM} arasında başka bir izin var.");
        var leave = new LeaveRecord(e, c.Type, c.Start, c.End, c.Note);
        if (c.Type == LeaveType.Annual)
        {
            var used = existing.Where(l => l.Type == LeaveType.Annual && l.Start.Year == c.Start.Year).Sum(l => l.Days);
            var right = e.AnnualLeaveEntitlement(c.Start);
            if (used + leave.Days > right) throw new DomainException($"{e.FullName}: yıllık izin hakkı {right} gün, kullanılan {used}, istenen {leave.Days}.");
        }
        uow.Repository<LeaveRecord>().Add(leave);
        var duties = await uow.Repository<DutyShift>().ListAsync(new Specification<DutyShift>()
            .Where(d => d.Date >= c.Start && d.Date <= c.End && (d.PharmacistId == e.Id || d.AssistantId == e.Id)).Tracking(), cancellationToken);
        foreach (var d in duties) d.Clear();
        await uow.SaveChangesAsync(cancellationToken);
        return leave.Id;
    }
}

public sealed record DeleteLeave(int LeaveId) : ICommand;

public sealed class DeleteLeaveHandler(IUnitOfWorkFactory uowFactory) : ICommandHandler<DeleteLeave>
{
    public async Task<Result> HandleAsync(DeleteLeave c, CancellationToken cancellationToken)
    {
        await using var uow = uowFactory.Create();
        var l = await uow.Repository<LeaveRecord>().GetRequiredAsync(c.LeaveId, cancellationToken);
        uow.Repository<LeaveRecord>().Remove(l);
        await uow.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

public sealed record EmployeeOption(int Id, string FullName, StaffRole Role)
{
    public string RoleText => Texts.Of(Role);
    public bool IsPharmacist => Role is StaffRole.ResponsiblePharmacist or StaffRole.Pharmacist;
}

public sealed record GetEmployeeOptions : IQuery<IReadOnlyList<EmployeeOption>>;

public sealed class GetEmployeeOptionsHandler(IUnitOfWorkFactory uowFactory) : IQueryHandler<GetEmployeeOptions, IReadOnlyList<EmployeeOption>>
{
    public async Task<Result<IReadOnlyList<EmployeeOption>>> HandleAsync(GetEmployeeOptions q, CancellationToken cancellationToken)
    {
        await using var uow = uowFactory.Create();
        var list = await uow.Repository<Employee>().ListAsync(new Specification<Employee>().Where(e => e.IsActive).OrderBy(e => e.Role).OrderBy(e => e.FullName),
            e => new EmployeeOption(e.Id, e.FullName, e.Role), cancellationToken);
        return list.ToList();
    }
}
