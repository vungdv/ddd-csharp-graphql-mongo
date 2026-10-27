using Shop.Domain.SharedKernel;

namespace Shop.Domain.Catalog;

public sealed record ProductCreated(ProductId ProductId, Sku Sku, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record ProductPriceChanged(ProductId ProductId, Money OldPrice, Money NewPrice, DateTimeOffset OccurredAt)
    : IDomainEvent;
