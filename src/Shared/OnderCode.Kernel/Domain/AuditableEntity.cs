namespace OnderCode.Kernel.Domain;

/// <summary>Denetim alanları ve sürüm numarası olan varlık. Değerleri altyapı yönetir.</summary>
public abstract class AuditableEntity : Entity, IAuditable, IVersioned
{
    public DateTime CreatedAt { get; protected set; }
    public string? CreatedBy { get; protected set; }
    public DateTime? UpdatedAt { get; protected set; }
    public string? UpdatedBy { get; protected set; }
    public long Version { get; protected set; }
}

/// <summary>Denetim + yumuşak silme. ERP'deki kayıtların çoğu için doğru taban budur.</summary>
public abstract class FullAuditableEntity : AuditableEntity, ISoftDeletable
{
    public bool IsDeleted { get; protected set; }
    public DateTime? DeletedAt { get; protected set; }
    public string? DeletedBy { get; protected set; }
}
