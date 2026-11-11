using System.Linq.Expressions;
using OnderCode.Kernel.Domain;
using OnderCode.Kernel.Text;

namespace OnderCode.Kernel.Specifications;

/// <summary>Bir sorgunun tarifi: filtre, ilişki yükleme, sıralama, sayfalama.</summary>
public interface ISpecification<T> where T : Entity
{
    IReadOnlyList<Expression<Func<T, bool>>> Criteria { get; }
    IReadOnlyList<LambdaExpression> Includes { get; }
    IReadOnlyList<string> IncludePaths { get; }
    IReadOnlyList<OrderClause> Orders { get; }
    int? Skip { get; }
    int? Take { get; }
    /// <summary>Varsayılan false: liste sorguları izlenmez (hızlı, bellek dostu).</summary>
    bool AsTracking { get; }
    bool SplitQuery { get; }
    /// <summary>Yumuşak silinmiş kayıtları da getir.</summary>
    bool IncludeDeleted { get; }
}

public sealed record OrderClause(LambdaExpression KeySelector, bool Descending);

/// <summary>
/// Akıcı (fluent) sorgu tarifi. Hem yerinde kullanılır hem de adlandırılmış tariflere taban olur:
/// <code>
/// var spec = new Specification&lt;Oda&gt;().Where(o =&gt; o.Durum == OdaDurumu.Bos).OrderBy(o =&gt; o.No);
/// public sealed class BosOdalar : Specification&lt;Oda&gt; { public BosOdalar() =&gt; Where(o =&gt; o.Durum == OdaDurumu.Bos); }
/// </code>
/// </summary>
public class Specification<T> : ISpecification<T> where T : Entity
{
    private readonly List<Expression<Func<T, bool>>> _criteria = [];
    private readonly List<LambdaExpression> _includes = [];
    private readonly List<string> _includePaths = [];
    private readonly List<OrderClause> _orders = [];

    public IReadOnlyList<Expression<Func<T, bool>>> Criteria => _criteria;
    public IReadOnlyList<LambdaExpression> Includes => _includes;
    public IReadOnlyList<string> IncludePaths => _includePaths;
    public IReadOnlyList<OrderClause> Orders => _orders;
    public int? Skip { get; private set; }
    public int? Take { get; private set; }
    public bool AsTracking { get; private set; }
    public bool SplitQuery { get; private set; }
    public bool IncludeDeleted { get; private set; }

    public Specification<T> Where(Expression<Func<T, bool>> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        _criteria.Add(predicate);
        return this;
    }

    public Specification<T> Include<TProperty>(Expression<Func<T, TProperty>> navigation)
    {
        ArgumentNullException.ThrowIfNull(navigation);
        _includes.Add(navigation);
        return this;
    }

    /// <summary>Çok seviyeli yükleme: "Satirlar.Urun".</summary>
    public Specification<T> Include(string navigationPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(navigationPath);
        _includePaths.Add(navigationPath);
        return this;
    }

    /// <summary>Sıralama ekler. Birden çok çağrı sırayla uygulanır (OrderBy, ThenBy, ...).</summary>
    public Specification<T> OrderBy<TKey>(Expression<Func<T, TKey>> key)
    {
        _orders.Add(new OrderClause(key, Descending: false));
        return this;
    }

    public Specification<T> OrderByDescending<TKey>(Expression<Func<T, TKey>> key)
    {
        _orders.Add(new OrderClause(key, Descending: true));
        return this;
    }

    public Specification<T> Page(int skip, int take)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(skip);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(take);
        Skip = skip;
        Take = take;
        return this;
    }

    /// <summary>Kayıtlar değiştirilecekse izlemeyi aç.</summary>
    public Specification<T> Tracking()
    {
        AsTracking = true;
        return this;
    }

    public Specification<T> WithDeleted()
    {
        IncludeDeleted = true;
        return this;
    }

    public Specification<T> Split()
    {
        SplitQuery = true;
        return this;
    }

    /// <summary>
    /// Türkçe arama: büyük/küçük harf ve Türkçe karakter duyarsız ("sanliurfa" → "Şanlıurfa").
    /// Her kelime alanlardan en az birinde geçmelidir. Arama veritabanında çalışır.
    /// </summary>
    public Specification<T> Search(string? term, params Expression<Func<T, string?>>[] fields)
    {
        if (string.IsNullOrWhiteSpace(term) || fields.Length == 0) return this;

        var fold = typeof(TurkishText).GetMethod(nameof(TurkishText.Fold), [typeof(string)])!;
        var contains = typeof(string).GetMethod(nameof(string.Contains), [typeof(string)])!;
        var x = Expression.Parameter(typeof(T), "x");

        foreach (var word in term.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            // Sabit yerine bir nesnenin özelliği: EF bunu SQL parametresine çevirir, sorgu planı önbellekte kalır
            var value = Expression.Property(Expression.Constant(new SearchValue(TurkishText.Fold(word))), nameof(SearchValue.Value));
            Expression? anyField = null;
            foreach (var field in fields)
            {
                var body = new ParameterReplacer(field.Parameters[0], x).Visit(field.Body);
                Expression match = Expression.Call(Expression.Call(fold, body), contains, value);
                anyField = anyField is null ? match : Expression.OrElse(anyField, match);
            }
            _criteria.Add(Expression.Lambda<Func<T, bool>>(anyField!, x));
        }
        return this;
    }

    private sealed class SearchValue(string value)
    {
        public string Value { get; } = value;
    }

    private sealed class ParameterReplacer(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : base.VisitParameter(node);
    }
}
