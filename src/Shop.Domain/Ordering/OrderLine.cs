using Shop.Domain.Catalog;
using Shop.Domain.SharedKernel;

namespace Shop.Domain.Ordering;

/// <summary>
/// Order line with a snapshot of product name, SKU and unit price. Identified within its order by the
/// referenced <see cref="ProductId"/> (an id only: Ordering never loads the Product aggregate).
/// </summary>
public sealed class OrderLine
{
    public const int MinQuantity = 1;
    public const int MaxQuantity = 1000;
    public const int MaxProductNameLength = 200;

    // Used by persistence rehydration only.
    private OrderLine()
    {
        Sku = null!;
        ProductName = null!;
        UnitPrice = null!;
    }

    /// <summary>Identifies the line within its order (no duplicates per order).</summary>
    public ProductId ProductId { get; private set; }

    public Sku Sku { get; private set; }

    public string ProductName { get; private set; }

    public Money UnitPrice { get; private set; }

    public int Quantity { get; private set; }

    public Money LineTotal => UnitPrice.Multiply(Quantity);

    public static OrderLine Create(ProductId productId, Sku sku, string productName, Money unitPrice, int quantity)
    {
        ArgumentNullException.ThrowIfNull(sku);
        ArgumentNullException.ThrowIfNull(unitPrice);
        if (productId.Value == Guid.Empty)
        {
            throw DomainException.Validation("productId", "Product id is required.");
        }

        if (string.IsNullOrWhiteSpace(productName) || productName.Length > MaxProductNameLength)
        {
            throw DomainException.Validation("productName", $"Product name is required and at most {MaxProductNameLength} characters.");
        }

        if (quantity is < MinQuantity or > MaxQuantity)
        {
            throw DomainException.Validation("quantity", $"Quantity must be between {MinQuantity} and {MaxQuantity}.");
        }

        return new OrderLine
        {
            ProductId = productId,
            Sku = sku,
            ProductName = productName,
            UnitPrice = unitPrice,
            Quantity = quantity,
        };
    }
}
