namespace Shop.Domain.Ordering;

public interface IOrderRepository
{
    Task<Order?> GetById(OrderId id, CancellationToken ct = default);

    Task<Order?> GetByOrderNumber(string orderNumber, CancellationToken ct = default);

    /// <exception cref="DuplicateOrderException">CONCURRENCY_CONFLICT on a duplicate order number or id (retryable with a new number).</exception>
    Task Add(Order order, CancellationToken ct = default);

    /// <exception cref="Shop.Domain.SharedKernel.DomainException">CONCURRENCY_CONFLICT when the stored version differs.</exception>
    Task Update(Order order, CancellationToken ct = default);
}
