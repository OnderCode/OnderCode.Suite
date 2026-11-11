using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using OnderCode.Kernel.Domain;

namespace OnderCode.Data.Conventions;

/// <summary>Varlıkların uyguladığı arayüzlere göre ortak model ayarları.</summary>
internal static class SuiteModelConventions
{
    public static void Apply(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes().ToList())
        {
            var clr = entityType.ClrType;
            if (entityType.IsOwned() || !typeof(Entity).IsAssignableFrom(clr)) continue;

            var builder = modelBuilder.Entity(clr);
            var isRoot = entityType.BaseType is null;

            if (isRoot)
            {
                builder.Ignore(nameof(Entity.DomainEvents));

                if (typeof(ISoftDeletable).IsAssignableFrom(clr))
                {
                    builder.HasQueryFilter(NotDeleted(clr));
                    builder.HasIndex(nameof(ISoftDeletable.IsDeleted));
                }

                if (typeof(IVersioned).IsAssignableFrom(clr))
                    builder.Property(nameof(IVersioned.Version)).IsConcurrencyToken();
            }

            if (isRoot && typeof(IAuditable).IsAssignableFrom(clr))
            {
                builder.Property(nameof(IAuditable.CreatedAt)).HasConversion(UtcDateTimeConverter.Instance);
                builder.Property(nameof(IAuditable.UpdatedAt)).HasConversion(UtcDateTimeConverter.Instance);
                builder.Property(nameof(IAuditable.CreatedBy)).HasMaxLength(100);
                builder.Property(nameof(IAuditable.UpdatedBy)).HasMaxLength(100);
            }

            if (isRoot && typeof(ISoftDeletable).IsAssignableFrom(clr))
            {
                builder.Property(nameof(ISoftDeletable.DeletedAt)).HasConversion(UtcDateTimeConverter.Instance);
                builder.Property(nameof(ISoftDeletable.DeletedBy)).HasMaxLength(100);
            }
        }
    }

    // e => !e.IsDeleted
    private static LambdaExpression NotDeleted(Type type)
    {
        var e = Expression.Parameter(type, "e");
        return Expression.Lambda(Expression.Not(Expression.Property(e, nameof(ISoftDeletable.IsDeleted))), e);
    }
}
