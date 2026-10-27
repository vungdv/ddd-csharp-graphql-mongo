using Shop.Domain.Catalog;
using Shop.Domain.SharedKernel;

namespace Shop.Domain.Ordering;

/// <summary>Product and quantity of an order line; enough for consumers such as stock restoration.</summary>
public sealed record OrderedItem(ProductId ProductId, int Quantity);

public sealed record OrderPlaced(
    OrderId OrderId,
    string OrderNumber,
    Money Total,
    IReadOnlyList<OrderedItem> Items,
    DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record OrderCancelled(
    OrderId OrderId,
    string OrderNumber,
    string? Reason,
    IReadOnlyList<OrderedItem> Items,
    DateTimeOffset OccurredAt) : IDomainEvent;
