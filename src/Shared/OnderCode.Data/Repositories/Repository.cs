using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using OnderCode.Kernel.Domain;
using OnderCode.Kernel.Exceptions;
using OnderCode.Kernel.Persistence;
using OnderCode.Kernel.Specifications;

namespace OnderCode.Data.Repositories;

/// <summary>
/// Genel depo. Özel sorgular için türetilebilir; çoğu durumda Specification yeterlidir.
/// Rapor gibi ağır sorgular için Infrastructure katmanında doğrudan DbContext kullanmak serbesttir.
/// </summary>
public class Repository<T>(DbContext context) : IRepository<T> where T : Entity
{
    protected DbContext Context { get; } = context;
    protected DbSet<T> Set => Context.Set<T>();

    public virtual Task<T?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
        => Set.FindAsync([id], cancellationToken).AsTask();

    public virtual async Task<T> GetRequiredAsync(int id, CancellationToken cancellationToken = default)
        => await GetByIdAsync(id, cancellationToken) ?? throw new NotFoundException(typeof(T).Name, id);

    public virtual Task<T?> FirstOrDefaultAsync(ISpecification<T> specification, CancellationToken cancellationToken = default)
        => SpecificationEvaluator.Apply(Set, specification).FirstOrDefaultAsync(cancellationToken);

    public virtual async Task<IReadOnlyList<T>> ListAsync(ISpecification<T>? specification = null, CancellationToken cancellationToken = default)
        => await SpecificationEvaluator.Apply(Set, specification).ToListAsync(cancellationToken);

    public virtual async Task<IReadOnlyList<TResult>> ListAsync<TResult>(ISpecification<T> specification, Expression<Func<T, TResult>> selector, CancellationToken cancellationToken = default)
        => await SpecificationEvaluator.Apply(Set, specification).Select(selector).ToListAsync(cancellationToken);

    public virtual async Task<PagedResult<T>> PagedAsync(ISpecification<T> specification, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pageSize, 1000);

        var total = await SpecificationEvaluator.ApplyCriteria(Set, specification).CountAsync(cancellationToken);
        var items = await SpecificationEvaluator.Apply(Set, specification, ensureOrder: true)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return new PagedResult<T>(items, total, page, pageSize);
    }

    public virtual Task<int> CountAsync(ISpecification<T>? specification = null, CancellationToken cancellationToken = default)
        => SpecificationEvaluator.ApplyCriteria(Set, specification).CountAsync(cancellationToken);

    public virtual Task<bool> AnyAsync(ISpecification<T>? specification = null, CancellationToken cancellationToken = default)
        => SpecificationEvaluator.ApplyCriteria(Set, specification).AnyAsync(cancellationToken);

    public virtual void Add(T entity) => Set.Add(entity);

    public virtual void AddRange(IEnumerable<T> entities) => Set.AddRange(entities);

    public virtual void Update(T entity)
    {
        // Zaten izleniyorsa EF değişiklikleri kendisi bulur; Update bütün kolonları yazdırır
        if (Context.Entry(entity).State == EntityState.Detached) Set.Update(entity);
    }

    public virtual void Remove(T entity) => Set.Remove(entity);
}
