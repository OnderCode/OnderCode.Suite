using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using OnderCode.Kernel.Abstractions;
using OnderCode.Kernel.Domain;

namespace OnderCode.Data.Interceptors;

/// <summary>
/// Kayıttan hemen önce: denetim alanlarını doldurur, sürüm numarasını artırır,
/// yumuşak silinebilir varlıklarda silmeyi işaretlemeye çevirir.
/// </summary>
public sealed class AuditSaveChangesInterceptor(TimeProvider timeProvider, ICurrentUser currentUser) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Apply(DbContext? context)
    {
        if (context is null) return;
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var user = currentUser.UserName;

        foreach (var entry in context.ChangeTracker.Entries<Entity>().ToList())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    if (entry.Entity is IAuditable)
                    {
                        entry.Property(nameof(IAuditable.CreatedAt)).CurrentValue = now;
                        entry.Property(nameof(IAuditable.CreatedBy)).CurrentValue = user;
                    }
                    if (entry.Entity is IVersioned)
                        entry.Property(nameof(IVersioned.Version)).CurrentValue = 1L;
                    break;

                case EntityState.Modified:
                    Touch(entry, now, user);
                    break;

                case EntityState.Deleted when entry.Entity is ISoftDeletable:
                    entry.State = EntityState.Modified;
                    entry.Property(nameof(ISoftDeletable.IsDeleted)).CurrentValue = true;
                    entry.Property(nameof(ISoftDeletable.DeletedAt)).CurrentValue = now;
                    entry.Property(nameof(ISoftDeletable.DeletedBy)).CurrentValue = user;
                    Touch(entry, now, user);
                    break;
            }
        }
    }

    private static void Touch(EntityEntry<Entity> entry, DateTime now, string? user)
    {
        if (entry.Entity is IAuditable)
        {
            entry.Property(nameof(IAuditable.UpdatedAt)).CurrentValue = now;
            entry.Property(nameof(IAuditable.UpdatedBy)).CurrentValue = user;
        }
        if (entry.Entity is IVersioned)
        {
            // WHERE koşulunda eski sürüm kullanılır; arada biri kaydı değiştirdiyse güncelleme 0 satır etkiler
            var version = entry.Property(nameof(IVersioned.Version));
            version.CurrentValue = (long)version.OriginalValue! + 1;
        }
    }
}
