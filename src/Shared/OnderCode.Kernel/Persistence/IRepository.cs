using System.Linq.Expressions;
using OnderCode.Kernel.Domain;
using OnderCode.Kernel.Specifications;

namespace OnderCode.Kernel.Persistence;

/// <summary>Okuma işlemleri. Liste sorguları varsayılan olarak izlenmez.</summary>
public interface IReadRepository<T> where T : Entity
{
    /// <summary>Kimliğe göre getirir (izlenir, düzenlemeye hazır). Yoksa null.</summary>
    Task<T?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Kimliğe göre getirir; yoksa <see cref="Exceptions.NotFoundException"/>.</summary>
    Task<T> GetRequiredAsync(int id, CancellationToken cancellationToken = default);

    Task<T?> FirstOrDefaultAsync(ISpecification<T> specification, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<T>> ListAsync(ISpecification<T>? specification = null, CancellationToken cancellationToken = default);

    /// <summary>Yalnızca gereken kolonları çeker (ekran listeleri için ideal).</summary>
    Task<IReadOnlyList<TResult>> ListAsync<TResult>(ISpecification<T> specification, Expression<Func<T, TResult>> selector, CancellationToken cancellationToken = default);

    /// <summary>Sayfa ve toplam kayıt sayısı. Sıralama verilmemişse Id'ye göre sıralar.</summary>
    Task<PagedResult<T>> PagedAsync(ISpecification<T> specification, int page, int pageSize, CancellationToken cancellationToken = default);

    Task<int> CountAsync(ISpecification<T>? specification = null, CancellationToken cancellationToken = default);

    Task<bool> AnyAsync(ISpecification<T>? specification = null, CancellationToken cancellationToken = default);
}

/// <summary>Okuma + yazma. Değişiklikler <see cref="IUnitOfWork.SaveChangesAsync"/> ile tek seferde yazılır.</summary>
public interface IRepository<T> : IReadRepository<T> where T : Entity
{
    void Add(T entity);
    void AddRange(IEnumerable<T> entities);
    /// <summary>İzlenmeyen (başka ekrandan gelen) bir kaydı güncellenecek olarak işaretler.</summary>
    void Update(T entity);
    /// <summary>Yumuşak silinebilir varlıklarda satır kalır, yalnızca işaretlenir.</summary>
    void Remove(T entity);
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPrevious => Page > 1;
    public bool HasNext => Page < TotalPages;
}
