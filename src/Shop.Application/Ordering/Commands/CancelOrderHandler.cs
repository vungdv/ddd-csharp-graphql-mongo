using Shop.Application.Abstractions;
using Shop.Application.Events;
using Shop.Application.Resilience;
using Shop.Domain.Ordering;
using Shop.Domain.SharedKernel;

namespace Shop.Application.Ordering.Commands;

/// <summary>
/// Cancels a placed order, then dispatches <see cref="OrderCancelled"/> after the save so the stock-restore handler runs.
/// A version mismatch (e.g. concurrent double cancel) is retried by reloading, so the loser sees ORDER_ALREADY_CANCELLED.
/// </summary>
public sealed class CancelOrderHandler(
    IOrderRepository orders,
    IDomainEventDispatcher events,
    IClock clock) : IRequestHandler<CancelOrderCommand, OrderDto>
{
    public async Task<Result<OrderDto>> Handle(CancelOrderCommand request, CancellationToken ct)
    {
        var order = await ResiliencePipelines.ConcurrencyConflict.ExecuteAsync(
            async token =>
            {
                var loaded = await orders.GetById(new OrderId(request.Id), token);
                if (loaded is null)
                {
                    return null;
                }

                loaded.Cancel(request.Reason, clock);
                await orders.Update(loaded, token);
                return loaded;
            },
            ct);

        if (order is null)
        {
            return Result<OrderDto>.Failure(
                new AppError(DomainErrorCodes.OrderNotFound, $"Order {request.Id} was not found.", "id"));
        }

        await events.Dispatch(order.DomainEvents, ct);
        order.ClearDomainEvents();
        return Result<OrderDto>.Success(OrderDto.From(order));
    }
}

/// <summary>Restores the stock of every line of a cancelled order (spec 4.4), atomically across products.</summary>
public sealed class OrderCancelledStockRestoreHandler(IStockReservation stock, IUnitOfWork unitOfWork)
    : IDomainEventHandler<OrderCancelled>
{
    public async Task Handle(OrderCancelled domainEvent, CancellationToken ct) =>
        await ResiliencePipelines.ConcurrencyConflict.ExecuteAsync(
            async retryToken => await unitOfWork.ExecuteAsync(
                async token =>
                {
                    await stock.Release(
                        domainEvent.Items.Select(i => new StockLine(i.ProductId.Value, i.Quantity)).ToList(),
                        token);
                    return true;
                },
                retryToken),
            ct);
}
