using FluentValidation;
using OnderCode.Application.Messaging;
using OnderCode.Kernel.Persistence;

namespace OnderCode.Application.Queries;

/// <summary>Liste ekranlarının ortak sorgusu: sayfa, sayfa boyutu, Türkçe arama metni.</summary>
public abstract record PagedQuery<TItem> : IQuery<PagedResult<TItem>>
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 50;
    public string? Search { get; init; }
}

/// <summary>Sayfalama kuralları. Liste sorgusunun doğrulayıcısı bundan türetilir.</summary>
public abstract class PagedQueryValidator<TQuery, TItem> : AbstractValidator<TQuery> where TQuery : PagedQuery<TItem>
{
    protected PagedQueryValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1).WithName("Sayfa");
        RuleFor(q => q.PageSize).InclusiveBetween(1, 1000).WithName("Sayfa boyutu");
    }
}
