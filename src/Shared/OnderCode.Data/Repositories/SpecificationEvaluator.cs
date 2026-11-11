using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using OnderCode.Kernel.Domain;
using OnderCode.Kernel.Specifications;

namespace OnderCode.Data.Repositories;

/// <summary>Specification'ı EF sorgusuna çevirir.</summary>
public static class SpecificationEvaluator
{
    private static readonly MethodInfo IncludeMethod = typeof(EntityFrameworkQueryableExtensions)
        .GetMethods()
        .Single(m => m.Name == nameof(EntityFrameworkQueryableExtensions.Include)
                     && m.GetGenericArguments().Length == 2
                     && m.GetParameters()[1].ParameterType.IsGenericType);

    /// <summary>Yalnızca filtreler (sayma ve var-mı sorguları için).</summary>
    public static IQueryable<T> ApplyCriteria<T>(IQueryable<T> query, ISpecification<T>? spec) where T : Entity
    {
        if (spec is null) return query;
        if (spec.IncludeDeleted) query = query.IgnoreQueryFilters();
        foreach (var criteria in spec.Criteria) query = query.Where(criteria);
        return query;
    }

    public static IQueryable<T> Apply<T>(IQueryable<T> query, ISpecification<T>? spec, bool ensureOrder = false) where T : Entity
    {
        if (spec is null)
        {
            query = query.AsNoTracking();
            return ensureOrder ? query.OrderBy(e => e.Id) : query;
        }

        query = ApplyCriteria(query, spec);

        foreach (var include in spec.Includes)
            query = (IQueryable<T>)IncludeMethod.MakeGenericMethod(typeof(T), include.ReturnType).Invoke(null, [query, include])!;
        foreach (var path in spec.IncludePaths)
            query = query.Include(path);

        if (spec.Orders.Count > 0)
        {
            for (var i = 0; i < spec.Orders.Count; i++)
                query = ApplyOrder(query, spec.Orders[i], first: i == 0);
        }
        else if (ensureOrder)
        {
            query = query.OrderBy(e => e.Id);
        }

        if (spec.Skip is int skip) query = query.Skip(skip);
        if (spec.Take is int take) query = query.Take(take);
        if (!spec.AsTracking) query = query.AsNoTracking();
        if (spec.SplitQuery) query = query.AsSplitQuery();
        return query;
    }

    // Anahtarın gerçek türüyle Queryable.OrderBy/ThenBy çağrısı (object'e kutulama yok, SQL'e temiz çevrilir)
    private static IQueryable<T> ApplyOrder<T>(IQueryable<T> query, OrderClause order, bool first)
    {
        var method = (first, order.Descending) switch
        {
            (true, false) => nameof(Queryable.OrderBy),
            (true, true) => nameof(Queryable.OrderByDescending),
            (false, false) => nameof(Queryable.ThenBy),
            _ => nameof(Queryable.ThenByDescending),
        };
        var call = Expression.Call(typeof(Queryable), method, [typeof(T), order.KeySelector.ReturnType],
            query.Expression, Expression.Quote(order.KeySelector));
        return query.Provider.CreateQuery<T>(call);
    }
}
