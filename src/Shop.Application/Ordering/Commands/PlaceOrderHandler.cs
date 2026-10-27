using Shop.Application.Abstractions;
using Shop.Application.Events;
using Shop.Application.Resilience;
using Shop.Domain.Catalog;
using Shop.Domain.Ordering;
using Shop.Domain.SharedKernel;

namespace Shop.Application.Ordering.Commands;

/// <summary>
/// Spec 5.3. Steps 2-7 run in one unit of work (transaction); the unit of work may re-run the delegate on a transient
/// transaction error, and a colliding order number restarts the whole transaction with a newly generated order (bounded
/// retry). Domain events are dispatched only after the transaction committed.
/// </summary>
public sealed class PlaceOrderHandler(
    IProductCatalog catalog,
    IStockReservation stock,
    IOrderRepository orders,
    IUnitOfWork unitOfWork,
    IDomainEventDispatcher events,
    IClock clock,
    IOrderNumberGenerator numbers) : IRequestHandler<PlaceOrderCommand, OrderDto>
{
    public async Task<Result<OrderDto>> Handle(PlaceOrderCommand request, CancellationToken ct)
    {
        var order = await ResiliencePipelines.DuplicateOrderNumber.ExecuteAsync(
            async token => await unitOfWork.ExecuteAsync(innerCt => PlaceInTransaction(request, innerCt), token),
            ct);

        await events.Dispatch(order.DomainEvents, ct);
        order.ClearDomainEvents();
        return Result<OrderDto>.Success(OrderDto.From(order));
    }

    private async Task<Order> PlaceInTransaction(PlaceOrderCommand request, CancellationToken ct)
    {
        // 2-3. Load all products; every one must exist and be active.
        var ids = request.Lines.Select(l => l.ProductId).Distinct().ToList();
        var snapshots = (await catalog.GetByIds(ids, ct)).ToDictionary(p => p.Id);
        for (var i = 0; i < request.Lines.Count; i++)
        {
            var productId = request.Lines[i].ProductId;
            if (!snapshots.TryGetValue(productId, out var snapshot))
            {
                throw new DomainException(
                    DomainErrorCodes.ProductNotFound,
                    $"Product {productId} was not found.",
                    $"lines[{i}].productId");
            }

            if (!snapshot.IsActive)
            {
                throw new DomainException(
                    DomainErrorCodes.ProductInactive,
                    $"Product {snapshot.Sku} is not active.",
                    $"lines[{i}].productId");
            }
        }

        // 4. Snapshot lines (name, SKU, unit price).
        var lines = request.Lines.Select(l =>
        {
            var p = snapshots[l.ProductId];
            return OrderLine.Create(new ProductId(p.Id), Sku.Create(p.Sku), p.Name, Money.Of(p.Price.Amount, p.Price.Currency), l.Quantity);
        }).ToList();

        // 5. Place (a fresh id and order number on every attempt).
        var order = Order.Place(
            CustomerInfo.Create(request.Customer.Name, request.Customer.Email),
            Address.Create(
                request.ShippingAddress.Line1,
                request.ShippingAddress.Line2,
                request.ShippingAddress.City,
                request.ShippingAddress.State,
                request.ShippingAddress.PostalCode,
                request.ShippingAddress.Country),
            lines,
            clock,
            numbers);

        // 6-7. Decrease stock (INSUFFICIENT_STOCK aborts everything), then persist the order; one transaction.
        await stock.Reserve(lines.Select(l => new StockLine(l.ProductId.Value, l.Quantity)).ToList(), ct);
        await orders.Add(order, ct);
        return order;
    }
}
