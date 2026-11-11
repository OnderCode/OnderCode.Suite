using Microsoft.EntityFrameworkCore;
using OnderCode.Data.Repositories;
using OnderCode.Kernel.Domain;
using OnderCode.Kernel.Persistence;

namespace OnderCode.Data;

/// <summary>
/// Kısa ömürlü iş birimi. Kendi DbContext'ine sahiptir ve kapatılınca onu da kapatır.
/// Alan olayları kayıt (transaction varsa commit) başarıyla bittikten sonra yayınlanır.
/// </summary>
public sealed class UnitOfWork<TContext>(TContext context, IDomainEventDispatcher? dispatcher = null) : IUnitOfWork
    where TContext : DbContext
{
    private readonly Dictionary<Type, object> _repositories = [];
    private readonly List<IDomainEvent> _pendingEvents = [];
    private bool _inTransaction;

    /// <summary>Infrastructure katmanındaki özel sorgular için. Application katmanı bunu görmez.</summary>
    public TContext Context => context;

    public IRepository<T> Repository<T>() where T : Entity
    {
        if (!_repositories.TryGetValue(typeof(T), out var repository))
            _repositories[typeof(T)] = repository = new Repository<T>(context);
        return (IRepository<T>)repository;
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        CollectEvents();
        int affected;
        try
        {
            affected = await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (PersistenceErrors.TryTranslate(ex, out var translated))
        {
            _pendingEvents.Clear();
            throw translated!;
        }
        if (!_inTransaction) await DispatchPendingAsync(cancellationToken);
        return affected;
    }

    public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> work, CancellationToken cancellationToken = default)
        => ExecuteInTransactionAsync(async ct => { await work(ct); return true; }, cancellationToken);

    public async Task<TResult> ExecuteInTransactionAsync<TResult>(Func<CancellationToken, Task<TResult>> work, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        if (_inTransaction) return await work(cancellationToken); // iç içe çağrı dıştaki transaction'a katılır

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        _inTransaction = true;
        try
        {
            var result = await work(cancellationToken);
            await SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            _inTransaction = false;
            await DispatchPendingAsync(cancellationToken);
            return result;
        }
        catch
        {
            _inTransaction = false;
            _pendingEvents.Clear();
            await transaction.RollbackAsync(CancellationToken.None);
            context.ChangeTracker.Clear(); // yarım kalan değişiklikler bir sonraki kayda sızmasın
            throw;
        }
    }

    private void CollectEvents()
    {
        foreach (var entry in context.ChangeTracker.Entries<Entity>())
        {
            if (entry.Entity.DomainEvents.Count == 0) continue;
            _pendingEvents.AddRange(entry.Entity.DomainEvents);
            entry.Entity.ClearDomainEvents();
        }
    }

    private async Task DispatchPendingAsync(CancellationToken cancellationToken)
    {
        if (dispatcher is null || _pendingEvents.Count == 0)
        {
            _pendingEvents.Clear();
            return;
        }
        var events = _pendingEvents.ToArray();
        _pendingEvents.Clear();
        await dispatcher.DispatchAsync(events, cancellationToken);
    }

    public ValueTask DisposeAsync() => context.DisposeAsync();

    public void Dispose() => context.Dispose();
}

/// <summary>Her çağrıda yeni bir iş birimi (ve yeni DbContext) üretir.</summary>
public sealed class UnitOfWorkFactory<TContext>(IDbContextFactory<TContext> contextFactory, IDomainEventDispatcher? dispatcher = null)
    : IUnitOfWorkFactory where TContext : DbContext
{
    public IUnitOfWork Create() => new UnitOfWork<TContext>(contextFactory.CreateDbContext(), dispatcher);
}
