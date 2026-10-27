using MongoDB.Driver;
using Shop.Domain.Ordering;
using Shop.Domain.SharedKernel;
using Shop.Infrastructure.Mongo;

namespace Shop.Infrastructure.Ordering;

public sealed class OrderRepository : IOrderRepository
{
    public const string CollectionName = "orders";

    private readonly IMongoCollection<Order> _orders;
    private readonly IMongoSessionProvider? _sessions;

    /// <param name="sessions">When given and a transaction is running, all reads and writes join it.</param>
    public OrderRepository(IMongoDatabase database, IMongoSessionProvider? sessions = null)
    {
        MongoMappings.Register();
        _orders = database.GetCollection<Order>(CollectionName);
        _sessions = sessions;
    }

    private IClientSessionHandle? Session => _sessions?.Current;

    private IFindFluent<Order, Order> Find(System.Linq.Expressions.Expression<Func<Order, bool>> filter) =>
        Session is { } s ? _orders.Find(s, filter) : _orders.Find(filter);

    public async Task<Order?> GetById(OrderId id, CancellationToken ct = default) =>
        await Find(o => o.Id == id).FirstOrDefaultAsync(ct);

    public async Task<Order?> GetByOrderNumber(string orderNumber, CancellationToken ct = default) =>
        await Find(o => o.OrderNumber == orderNumber).FirstOrDefaultAsync(ct);

    public async Task Add(Order order, CancellationToken ct = default)
    {
        try
        {
            if (Session is { } s)
            {
                await _orders.InsertOneAsync(s, order, cancellationToken: ct);
            }
            else
            {
                await _orders.InsertOneAsync(order, cancellationToken: ct);
            }
        }
        catch (MongoWriteException ex) when (IsDuplicate(ex))
        {
            throw DuplicateError(order);
        }
    }

    public Task Update(Order order, CancellationToken ct = default) =>
        OptimisticConcurrency.Replace<Order, OrderId>(
            _orders,
            Session,
            order,
            "Order",
            ex => IsDuplicate(ex) ? DuplicateError(order) : null,
            ct);

    private static bool IsDuplicate(MongoWriteException ex) => ex.WriteError.Category == ServerErrorCategory.DuplicateKey;

    // No dedicated spec code exists for an order-number collision; it is a retryable conflict.
    private static DuplicateOrderException DuplicateError(Order order) =>
        new($"An order with number '{order.OrderNumber}' or id '{order.Id}' already exists.");
}
