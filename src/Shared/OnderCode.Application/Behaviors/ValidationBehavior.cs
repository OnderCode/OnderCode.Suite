using FluentValidation;
using OnderCode.Application.Messaging;
using OnderCode.Application.Results;

namespace OnderCode.Application.Behaviors;

/// <summary>İsteğin bütün doğrulayıcılarını çalıştırır; hata varsa işleyiciye hiç ulaşmadan alan bazlı hatalarla döner.</summary>
public sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
    where TResponse : IResultType<TResponse>
{
    private readonly IValidator<TRequest>[] _validators = [.. validators];

    public async Task<TResponse> HandleAsync(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (_validators.Length == 0) return await next();

        var errors = new List<Error>();
        foreach (var validator in _validators)
        {
            var result = await validator.ValidateAsync(request, cancellationToken);
            errors.AddRange(result.Errors.Select(f => Error.Validation(f.PropertyName, f.ErrorMessage)));
        }
        return errors.Count > 0 ? TResponse.Failure(errors) : await next();
    }
}
