using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shop.Domain.SharedKernel;

namespace Shop.Application.Events;

public interface IDomainEventHandler<in TEvent>
    where TEvent : IDomainEvent
{
    Task Handle(TEvent domainEvent, CancellationToken ct);
}

/// <summary>In-process dispatcher; call it only after the aggregate changes have been saved successfully.</summary>
public interface IDomainEventDispatcher
{
    Task Dispatch(IEnumerable<IDomainEvent> events, CancellationToken ct = default);
}

/// <summary>
/// Runs every registered <see cref="IDomainEventHandler{TEvent}"/> for each event. The state change is already committed
/// when this runs (no outbox in v1), so a failing handler is logged and does not fail the caller or stop other handlers.
/// </summary>
public sealed class DomainEventDispatcher(IServiceProvider services, ILogger<DomainEventDispatcher> logger) : IDomainEventDispatcher
{
    public async Task Dispatch(IEnumerable<IDomainEvent> events, CancellationToken ct = default)
    {
        foreach (var domainEvent in events.ToList())
        {
            var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(domainEvent.GetType());
            var handle = handlerType.GetMethod(nameof(IDomainEventHandler<IDomainEvent>.Handle))!;
            foreach (var handler in services.GetServices(handlerType))
            {
                try
                {
                    await (Task)handle.Invoke(handler, [domainEvent, ct])!;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(
                        ex,
                        "Domain event handler {Handler} failed for {Event}; the originating change is already committed.",
                        handler!.GetType().Name,
                        domainEvent.GetType().Name);
                }
            }
        }
    }
}
