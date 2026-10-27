using Shop.Application.Ordering;
using Shop.Domain.Catalog;
using Shop.Domain.SharedKernel;

namespace Shop.Application.Catalog.Integration;

/// <summary>Catalog-side adapter for Ordering's <see cref="IProductCatalog"/> port.</summary>
public sealed class ProductCatalogAdapter(IProductRepository products) : IProductCatalog
{
    public async Task<IReadOnlyList<ProductSnapshot>> GetByIds(IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
    {
        var found = await products.GetByIds(ids.Select(i => new ProductId(i)), ct);
        return found
            .Select(p => new ProductSnapshot(
                p.Id.Value,
                p.Sku.Value,
                p.Name,
                new SnapshotPrice(p.Price.Amount, p.Price.Currency),
                p.IsActive,
                p.StockQuantity))
            .ToList();
    }
}

/// <summary>
/// Catalog-side adapter for Ordering's <see cref="IStockReservation"/> port: the only place where order flows touch the
/// Product aggregate. Persists through the (transaction-aware) product repository; optimistic versioning applies.
/// </summary>
public sealed class StockReservationAdapter(IProductRepository products, IClock clock) : IStockReservation
{
    public Task Reserve(IReadOnlyCollection<StockLine> lines, CancellationToken ct = default) =>
        Apply(lines, (p, qty) => p.DecreaseStock(qty, clock), ct);

    public Task Release(IReadOnlyCollection<StockLine> lines, CancellationToken ct = default) =>
        Apply(lines, (p, qty) => p.AdjustStock(qty, clock), ct);

    private async Task Apply(IReadOnlyCollection<StockLine> lines, Action<Product, int> change, CancellationToken ct)
    {
        // Deliberately reloads even if the caller just read these products: inside a transaction the reload sees the same
        // snapshot, and it keeps this adapter independent of the caller (the aggregate is never shared across contexts).
        var loaded = (await products.GetByIds(lines.Select(l => new ProductId(l.ProductId)), ct)).ToDictionary(p => p.Id);
        foreach (var line in lines.OrderBy(l => l.ProductId))
        {
            if (!loaded.TryGetValue(new ProductId(line.ProductId), out var product))
            {
                throw new DomainException(
                    DomainErrorCodes.ProductNotFound,
                    $"Product {line.ProductId} was not found.");
            }

            change(product, line.Quantity);
            await products.Update(product, ct);
        }
    }
}
