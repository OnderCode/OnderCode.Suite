using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using OnderCode.Kernel.Domain;

namespace OnderCode.Application.Events;

/// <summary>Bir alan olayına tepki (ör. RezervasyonOnaylandi → odayı Dolu yap, karşılama e-postası kuyruğa al).</summary>
public interface IDomainEventHandler<in TEvent> where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken);
}

/// <summary>Veri katmanı kaydı tamamlayınca olayları ilgili işleyicilere dağıtır.</summary>
internal sealed class DomainEventDispatcher(IServiceScopeFactory scopeFactory) : IDomainEventDispatcher
{
    private static readonly ConcurrentDictionary<Type, EventInvoker> Invokers = new();

    public async Task DispatchAsync(IReadOnlyList<IDomainEvent> events, CancellationToken cancellationToken = default)
    {
        if (events.Count == 0) return;
        await using var scope = scopeFactory.CreateAsyncScope();
        foreach (var domainEvent in events)
        {
            var invoker = Invokers.GetOrAdd(domainEvent.GetType(),
                static type => (EventInvoker)Activator.CreateInstance(typeof(EventInvoker<>).MakeGenericType(type))!);
            await invoker.InvokeAsync(domainEvent, scope.ServiceProvider, cancellationToken);
        }
    }
}

internal abstract class EventInvoker
{
    public abstract Task InvokeAsync(IDomainEvent domainEvent, IServiceProvider services, CancellationToken cancellationToken);
}

internal sealed class EventInvoker<TEvent> : EventInvoker where TEvent : IDomainEvent
{
    public override async Task InvokeAsync(IDomainEvent domainEvent, IServiceProvider services, CancellationToken cancellationToken)
    {
        foreach (var handler in services.GetServices<IDomainEventHandler<TEvent>>())
            await handler.HandleAsync((TEvent)domainEvent, cancellationToken);
    }
}
