using System.Runtime.CompilerServices;

namespace OnderCode.Kernel.Domain;

/// <summary>
/// Bütün varlıkların tabanı. Kimliği veritabanı üretir; 0 ise varlık henüz kaydedilmemiştir.
/// Eşitlik kimliğe göredir; alan olayları (domain events) kayıttan sonra yayınlanmak üzere burada birikir.
/// </summary>
public abstract class Entity
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public int Id { get; protected set; }

    /// <summary>Henüz veritabanına yazılmamış mı?</summary>
    public bool IsTransient => Id == 0;

    /// <summary>Kaydedildikten sonra yayınlanacak alan olayları.</summary>
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents;

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public void ClearDomainEvents() => _domainEvents.Clear();

    public override bool Equals(object? obj)
    {
        if (obj is not Entity other || other.GetType() != GetType()) return false;
        if (ReferenceEquals(this, other)) return true;
        return !IsTransient && Id == other.Id;
    }

    // Kaydedilmemiş varlığın kimliği değişeceği için nesne kimliği kullanılır
    public override int GetHashCode() => IsTransient ? RuntimeHelpers.GetHashCode(this) : HashCode.Combine(GetType(), Id);

    public static bool operator ==(Entity? left, Entity? right) => Equals(left, right);
    public static bool operator !=(Entity? left, Entity? right) => !Equals(left, right);
}
