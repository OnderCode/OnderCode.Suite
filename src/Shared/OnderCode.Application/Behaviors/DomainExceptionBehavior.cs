using OnderCode.Application.Messaging;
using OnderCode.Application.Results;
using OnderCode.Kernel.Exceptions;

namespace OnderCode.Application.Behaviors;

/// <summary>
/// İşleyicilerden ya da veri katmanından fırlayan alan istisnalarını (bulunamadı, eşzamanlılık, mükerrer kayıt...)
/// başarısız Result'a çevirir. Ekranlar try/catch yazmaz, yalnızca sonucu kontrol eder.
/// </summary>
public sealed class DomainExceptionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
    where TResponse : IResultType<TResponse>
{
    public async Task<TResponse> HandleAsync(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        try
        {
            return await next();
        }
        catch (DomainException ex)
        {
            return TResponse.Failure([ToError(ex)]);
        }
    }

    private static Error ToError(DomainException ex) => ex switch
    {
        NotFoundException => new Error(ErrorCodes.NotFound, ex.Message),
        ConcurrencyConflictException => new Error(ErrorCodes.Concurrency, ex.Message),
        DuplicateRecordException => new Error(ErrorCodes.Duplicate, ex.Message),
        ReferenceInUseException => new Error(ErrorCodes.InUse, ex.Message),
        _ => new Error(ErrorCodes.Domain, ex.Message),
    };
}
