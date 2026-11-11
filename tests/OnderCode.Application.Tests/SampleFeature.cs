using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OnderCode.Application.Events;
using OnderCode.Application.Messaging;
using OnderCode.Application.Queries;
using OnderCode.Application.Results;
using OnderCode.Application.Validation;
using OnderCode.Data;
using OnderCode.Kernel.Domain;
using OnderCode.Kernel.Persistence;
using OnderCode.Kernel.Specifications;

namespace OnderCode.Application.Tests;

// Uçtan uca örnek: müşteri kaydı. Gerçek uygulamalardaki bir özelliğin (feature) birebir küçük kopyası.

public sealed class Customer : FullAuditableEntity
{
    private Customer() { }

    public Customer(string name, string tckn)
    {
        Name = name;
        Tckn = tckn;
        Raise(new CustomerRegistered(this));
    }

    public string Name { get; private set; } = "";
    public string Tckn { get; private set; } = "";

    public void Rename(string name) => Name = name;
}

public sealed record CustomerRegistered(Customer Customer) : IDomainEvent;

public sealed class SampleDbContext(DbContextOptions<SampleDbContext> options) : SuiteDbContext(options)
{
    public DbSet<Customer> Customers { get; set; } = null!;
}

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.Property(c => c.Name).HasMaxLength(100);
        builder.Property(c => c.Tckn).HasMaxLength(11);
        builder.HasIndex(c => c.Tckn).IsUnique();
    }
}

public sealed record CustomerDto(int Id, string Name);

// --- Kayıt (değer döndüren komut) ---

public sealed record RegisterCustomer(string Name, string Tckn) : ICommand<int>;

public sealed class RegisterCustomerValidator : AbstractValidator<RegisterCustomer>
{
    public RegisterCustomerValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100).WithName("Ad");
        RuleFor(x => x.Tckn).NotEmpty().Tckn().WithName("T.C. kimlik no");
    }
}

public sealed class RegisterCustomerHandler(IUnitOfWorkFactory uowFactory) : ICommandHandler<RegisterCustomer, int>
{
    public async Task<Result<int>> HandleAsync(RegisterCustomer command, CancellationToken cancellationToken)
    {
        await using var uow = uowFactory.Create();
        var customer = new Customer(command.Name, command.Tckn);
        uow.Repository<Customer>().Add(customer);
        await uow.SaveChangesAsync(cancellationToken);
        return customer.Id;
    }
}

// --- Ad değiştirme (değer döndürmeyen komut) ---

public sealed record RenameCustomer(int Id, string Name) : ICommand;

public sealed class RenameCustomerValidator : AbstractValidator<RenameCustomer>
{
    public RenameCustomerValidator() => RuleFor(x => x.Name).NotEmpty().WithName("Ad");
}

public sealed class RenameCustomerHandler(IUnitOfWorkFactory uowFactory) : ICommandHandler<RenameCustomer>
{
    public async Task<Result> HandleAsync(RenameCustomer command, CancellationToken cancellationToken)
    {
        await using var uow = uowFactory.Create();
        var customer = await uow.Repository<Customer>().GetRequiredAsync(command.Id, cancellationToken);
        customer.Rename(command.Name);
        await uow.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

// --- Sorgular ---

public sealed record GetCustomer(int Id) : IQuery<CustomerDto>;

public sealed class GetCustomerHandler(IUnitOfWorkFactory uowFactory) : IQueryHandler<GetCustomer, CustomerDto>
{
    public async Task<Result<CustomerDto>> HandleAsync(GetCustomer query, CancellationToken cancellationToken)
    {
        await using var uow = uowFactory.Create();
        var customer = await uow.Repository<Customer>().GetRequiredAsync(query.Id, cancellationToken);
        return new CustomerDto(customer.Id, customer.Name);
    }
}

public sealed record ListCustomers : PagedQuery<CustomerDto>;

public sealed class ListCustomersValidator : PagedQueryValidator<ListCustomers, CustomerDto>
{
}

public sealed class ListCustomersHandler(IUnitOfWorkFactory uowFactory) : IQueryHandler<ListCustomers, PagedResult<CustomerDto>>
{
    public async Task<Result<PagedResult<CustomerDto>>> HandleAsync(ListCustomers query, CancellationToken cancellationToken)
    {
        await using var uow = uowFactory.Create();
        var spec = new Specification<Customer>().Search(query.Search, c => c.Name).OrderBy(c => c.Name);
        var page = await uow.Repository<Customer>().PagedAsync(spec, query.Page, query.PageSize, cancellationToken);
        return new PagedResult<CustomerDto>([.. page.Items.Select(c => new CustomerDto(c.Id, c.Name))], page.TotalCount, page.Page, page.PageSize);
    }
}

// --- Olay işleyici ---

public sealed class WelcomeLog
{
    public List<string> Entries { get; } = [];
}

public sealed class SendWelcomeMessage(WelcomeLog log) : IDomainEventHandler<CustomerRegistered>
{
    public Task HandleAsync(CustomerRegistered domainEvent, CancellationToken cancellationToken)
    {
        log.Entries.Add($"{domainEvent.Customer.Id}:{domainEvent.Customer.Name}");
        return Task.CompletedTask;
    }
}

// İşleyicisi olmayan komut
public sealed record OrphanCommand : ICommand;
