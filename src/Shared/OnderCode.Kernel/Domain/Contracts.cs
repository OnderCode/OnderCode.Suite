namespace OnderCode.Kernel.Domain;

/// <summary>Kim, ne zaman oluşturdu/değiştirdi. Değerleri altyapı doldurur (UTC).</summary>
public interface IAuditable
{
    DateTime CreatedAt { get; }
    string? CreatedBy { get; }
    DateTime? UpdatedAt { get; }
    string? UpdatedBy { get; }
}

/// <summary>Silinince satır kalır, yalnızca işaretlenir. Sorgular silinenleri kendiliğinden gizler.</summary>
public interface ISoftDeletable
{
    bool IsDeleted { get; }
    DateTime? DeletedAt { get; }
    string? DeletedBy { get; }
}

/// <summary>İyimser eşzamanlılık: her kayıtta artan sürüm numarası. İki kişi aynı kaydı düzenlerse ikincisi uyarı alır.</summary>
public interface IVersioned
{
    long Version { get; }
}

/// <summary>Alan olayı işaretçisi (ör. RezervasyonOnaylandi).</summary>
public interface IDomainEvent
{
}

/// <summary>Alan olaylarını kayıt başarıyla tamamlandıktan sonra yayınlar. Application katmanı uygular.</summary>
public interface IDomainEventDispatcher
{
    Task DispatchAsync(IReadOnlyList<IDomainEvent> events, CancellationToken cancellationToken = default);
}
