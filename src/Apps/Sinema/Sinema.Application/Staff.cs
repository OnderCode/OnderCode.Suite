using FluentValidation;
using OnderCode.Application.Messaging;
using OnderCode.Application.Results;
using OnderCode.Kernel.Persistence;
using OnderCode.Kernel.Specifications;
using Sinema.Domain;

namespace Sinema.Application;

public sealed record EmployeeRow(int Id, string FullName, StaffRole Role, DateOnly HireDate, bool IsActive, string? Shift, int Sales30, decimal Revenue30, int CheckIns30)
{
    public string RoleText => Texts.Of(Role);
}

public sealed record ListEmployees(bool IncludeInactive = true) : IQuery<IReadOnlyList<EmployeeRow>>;

public sealed class ListEmployeesHandler(IUnitOfWorkFactory uowFactory, TimeProvider time) : IQueryHandler<ListEmployees, IReadOnlyList<EmployeeRow>>
{
    public async Task<Result<IReadOnlyList<EmployeeRow>>> HandleAsync(ListEmployees q, CancellationToken cancellationToken)
    {
        var from = time.Today().AddDays(-30).Start();
        await using var uow = uowFactory.Create();
        var spec = new Specification<Employee>().OrderBy(e => e.Role).OrderBy(e => e.FullName);
        if (!q.IncludeInactive) spec.Where(e => e.IsActive);
        var staff = await uow.Repository<Employee>().ListAsync(spec, cancellationToken);
        var sales = await uow.Repository<Order>().ListAsync(new Specification<Order>().Where(o => o.SoldAt >= from && o.Status == OrderStatus.Completed && o.SoldById != null),
            o => new { o.SoldById, o.Total }, cancellationToken);
        var checks = await uow.Repository<Ticket>().ListAsync(new Specification<Ticket>().Where(t => t.UsedAt >= from && t.CheckedById != null), t => t.CheckedById, cancellationToken);
        return staff.Select(e => new EmployeeRow(e.Id, e.FullName, e.Role, e.HireDate, e.IsActive, e.Shift,
            sales.Count(s => s.SoldById == e.Id), sales.Where(s => s.SoldById == e.Id).Sum(s => s.Total), checks.Count(c => c == e.Id))).ToList();
    }
}

public sealed record SaveEmployee(int? Id, string FullName, StaffRole Role, DateOnly HireDate, string? Shift, bool IsActive) : ICommand<int>;

public sealed class SaveEmployeeValidator : AbstractValidator<SaveEmployee>
{
    public SaveEmployeeValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(80).WithName("Ad soyad");
        RuleFor(x => x.Shift).MaximumLength(60).WithName("Vardiya");
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
            if (e.Role == StaffRole.Manager && (c.Role != StaffRole.Manager || !c.IsActive))
            {
                var otherManagers = await uow.Repository<Employee>().CountAsync(new Specification<Employee>().Where(x => x.Role == StaffRole.Manager && x.IsActive && x.Id != id), cancellationToken);
                if (otherManagers == 0) return Error.Business("Sistemde en az bir aktif yönetici kalmalı.");
            }
            e.ChangeRole(c.Role);
        }
        else
        {
            e = new Employee(c.FullName, c.Role, c.HireDate);
            uow.Repository<Employee>().Add(e);
        }
        e.SetShift(c.Shift);
        e.SetActive(c.IsActive);
        await uow.SaveChangesAsync(cancellationToken);
        return e.Id;
    }
}

// ---------- Büfe ----------

public sealed record ConcessionRow(int Id, string Name, ConcessionCategory Category, decimal Price, bool IsActive, int SortOrder, int Sold30, decimal Revenue30)
{
    public string CategoryText => Texts.Of(Category);
}

public sealed record ListConcessions(bool ActiveOnly = false) : IQuery<IReadOnlyList<ConcessionRow>>;

public sealed class ListConcessionsHandler(IUnitOfWorkFactory uowFactory, TimeProvider time) : IQueryHandler<ListConcessions, IReadOnlyList<ConcessionRow>>
{
    public async Task<Result<IReadOnlyList<ConcessionRow>>> HandleAsync(ListConcessions q, CancellationToken cancellationToken)
    {
        var from = time.Today().AddDays(-30).Start();
        await using var uow = uowFactory.Create();
        var spec = new Specification<ConcessionItem>().OrderBy(i => i.Category).OrderBy(i => i.SortOrder);
        if (q.ActiveOnly) spec.Where(i => i.IsActive);
        var items = await uow.Repository<ConcessionItem>().ListAsync(spec, cancellationToken);
        var lines = await uow.Repository<ConcessionLine>().ListAsync(new Specification<ConcessionLine>().Where(l => l.Order!.SoldAt >= from && l.Order.Status == OrderStatus.Completed),
            l => new { l.ItemId, l.Quantity, l.UnitPrice }, cancellationToken);
        return items.Select(i =>
        {
            var mine = lines.Where(l => l.ItemId == i.Id).ToList();
            return new ConcessionRow(i.Id, i.Name, i.Category, i.Price, i.IsActive, i.SortOrder, mine.Sum(l => l.Quantity), mine.Sum(l => l.Quantity * l.UnitPrice));
        }).ToList();
    }
}

public sealed record SaveConcession(int? Id, string Name, ConcessionCategory Category, decimal Price, bool IsActive) : ICommand<int>;

public sealed class SaveConcessionValidator : AbstractValidator<SaveConcession>
{
    public SaveConcessionValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(50).WithName("Ürün adı");
        RuleFor(x => x.Price).GreaterThan(0).LessThanOrEqualTo(10_000).WithName("Fiyat");
    }
}

public sealed class SaveConcessionHandler(IUnitOfWorkFactory uowFactory) : ICommandHandler<SaveConcession, int>
{
    public async Task<Result<int>> HandleAsync(SaveConcession c, CancellationToken cancellationToken)
    {
        await using var uow = uowFactory.Create();
        ConcessionItem i;
        if (c.Id is int id)
        {
            i = await uow.Repository<ConcessionItem>().GetRequiredAsync(id, cancellationToken);
            i.Update(c.Name, c.Category, c.Price);
        }
        else
        {
            var count = await uow.Repository<ConcessionItem>().CountAsync(cancellationToken: cancellationToken);
            i = new ConcessionItem(c.Name, c.Category, c.Price, count + 1);
            uow.Repository<ConcessionItem>().Add(i);
        }
        i.SetActive(c.IsActive);
        await uow.SaveChangesAsync(cancellationToken);
        return i.Id;
    }
}
