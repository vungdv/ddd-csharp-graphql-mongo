using Shop.Application.Abstractions;

namespace Shop.Application.Catalog.Commands;

public sealed record CreateProductCommand(
    string Sku,
    string Name,
    string? Description,
    decimal PriceAmount,
    string Currency,
    string Category,
    int InitialStock) : IRequest<ProductDto>;

/// <summary>Null members are left unchanged.</summary>
public sealed record UpdateProductCommand(Guid Id, string? Name, string? Description, string? Category)
    : IRequest<ProductDto>;

/// <summary>A null currency keeps the product's current currency.</summary>
public sealed record ChangeProductPriceCommand(Guid Id, decimal Amount, string? Currency) : IRequest<ProductDto>;

public sealed record SetProductActiveCommand(Guid Id, bool IsActive) : IRequest<ProductDto>;

public sealed record AdjustProductStockCommand(Guid Id, int Delta) : IRequest<ProductDto>;
