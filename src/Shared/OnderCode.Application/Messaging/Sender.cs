using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using OnderCode.Application.Results;

namespace OnderCode.Application.Messaging;

/// <summary>
/// İsteği işleyicisine, davranış hattından geçirerek ulaştırır. Her istek kendi DI kapsamında çalışır.
/// İstek türü başına bir kez yansıma (reflection) yapılır; sonrası önbellekten, doğrudan çağrıdır.
/// </summary>
internal sealed class Sender(IServiceScopeFactory scopeFactory) : ISender
{
    private static readonly ConcurrentDictionary<Type, object> Pipelines = new();

    public async Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        where TResponse : IResultType<TResponse>
    {
        ArgumentNullException.ThrowIfNull(request);
        var pipeline = (RequestPipeline<TResponse>)Pipelines.GetOrAdd(request.GetType(),
            static (requestType, responseType) => Activator.CreateInstance(typeof(RequestPipeline<,>).MakeGenericType(requestType, responseType))!,
            typeof(TResponse));

        await using var scope = scopeFactory.CreateAsyncScope();
        return await pipeline.HandleAsync(request, scope.ServiceProvider, cancellationToken);
    }
}

internal abstract class RequestPipeline<TResponse>
{
    public abstract Task<TResponse> HandleAsync(object request, IServiceProvider services, CancellationToken cancellationToken);
}

internal sealed class RequestPipeline<TRequest, TResponse> : RequestPipeline<TResponse>
    where TRequest : IRequest<TResponse>
    where TResponse : IResultType<TResponse>
{
    public override Task<TResponse> HandleAsync(object request, IServiceProvider services, CancellationToken cancellationToken)
    {
        var typed = (TRequest)request;
        var handler = services.GetService<IRequestHandler<TRequest, TResponse>>()
            ?? throw new InvalidOperationException($"'{typeof(TRequest).Name}' için kayıtlı bir işleyici yok. İşleyicinin bulunduğu derleme AddSuiteApplication'a verilmeli.");

        RequestHandlerDelegate<TResponse> next = () => handler.HandleAsync(typed, cancellationToken);
        foreach (var behavior in services.GetServices<IPipelineBehavior<TRequest, TResponse>>().Reverse())
        {
            var inner = next;
            next = () => behavior.HandleAsync(typed, inner, cancellationToken);
        }
        return next();
    }
}
