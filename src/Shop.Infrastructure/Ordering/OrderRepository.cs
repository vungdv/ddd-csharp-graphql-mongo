using MongoDB.Driver;
using Shop.Domain.Ordering;
using Shop.Domain.SharedKernel;
using Shop.Infrastructure.Mongo;

namespace Shop.Infrastructure.Ordering;

public sealed class OrderRepository : IOrderRepository
{
    public const string CollectionName = "orders";

    private readonly IMongoCollection<Order> _orders;

    public OrderRepository(IMongoDatabase database)
    {
        MongoMappings.Register();
        _orders = database.GetCollection<Order>(CollectionName);
    }

    public async Task<Order?> GetById(OrderId id, CancellationToken ct = default) =>
        await _orders.Find(o => o.Id == id).FirstOrDefaultAsync(ct);

    public async Task<Order?> GetByOrderNumber(string orderNumber, CancellationToken ct = default) =>
        await _orders.Find(o => o.OrderNumber == orderNumber).FirstOrDefaultAsync(ct);

    public async Task Add(Order order, CancellationToken ct = default)
    {
        try
        {
            await _orders.InsertOneAsync(order, cancellationToken: ct);
        }
        catch (MongoWriteException ex) when (IsDuplicate(ex))
        {
            throw DuplicateError(order);
        }
    }

    public Task Update(Order order, CancellationToken ct = default) =>
        OptimisticConcurrency.Replace<Order, OrderId>(
            _orders,
            order,
            "Order",
            ex => IsDuplicate(ex) ? DuplicateError(order) : null,
            ct);

    private static bool IsDuplicate(MongoWriteException ex) => ex.WriteError.Category == ServerErrorCategory.DuplicateKey;

    // No dedicated spec code exists for an order-number collision; it is a retryable conflict.
    private static DomainException DuplicateError(Order order) =>
        new(
            DomainErrorCodes.ConcurrencyConflict,
            $"An order with number '{order.OrderNumber}' or id '{order.Id}' already exists.");
}
