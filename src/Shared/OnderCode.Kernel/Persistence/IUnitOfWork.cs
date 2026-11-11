using OnderCode.Kernel.Domain;

namespace OnderCode.Kernel.Persistence;

/// <summary>
/// Bir iş adımı (use case) için tek bağlam. Kısa ömürlüdür: her işlemde yeni açılır, iş bitince kapatılır.
/// WinForms'ta form boyunca açık kalan DbContext eski veri ve bellek sorunlarının ana kaynağıdır; bu yapı bunu engeller.
/// <code>
/// await using var uow = uowFactory.Create();
/// uow.Repository&lt;Rezervasyon&gt;().Add(rezervasyon);
/// await uow.SaveChangesAsync(ct);
/// </code>
/// </summary>
public interface IUnitOfWork : IAsyncDisposable, IDisposable
{
    IRepository<T> Repository<T>() where T : Entity;

    /// <summary>Değişiklikleri yazar. Veritabanı hatalarını anlaşılır alan istisnalarına çevirir.</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>İşi tek transaction içinde yapar; hata olursa hiçbir şey yazılmaz.</summary>
    Task ExecuteInTransactionAsync(Func<CancellationToken, Task> work, CancellationToken cancellationToken = default);

    Task<TResult> ExecuteInTransactionAsync<TResult>(Func<CancellationToken, Task<TResult>> work, CancellationToken cancellationToken = default);
}

public interface IUnitOfWorkFactory
{
    IUnitOfWork Create();
}
