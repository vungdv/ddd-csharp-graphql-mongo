using Shop.Domain.Catalog;

namespace Shop.Application.Catalog;

public sealed record MoneyDto(decimal Amount, string Currency);

public sealed record ProductDto(
    Guid Id,
    string Sku,
    string Name,
    string? Description,
    MoneyDto Price,
    string Category,
    int StockQuantity,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static ProductDto From(Product p) => new(
        p.Id.Value,
        p.Sku.Value,
        p.Name,
        p.Description,
        new MoneyDto(p.Price.Amount, p.Price.Currency),
        p.Category,
        p.StockQuantity,
        p.IsActive,
        p.CreatedAt,
        p.UpdatedAt);
}
