namespace Shop.Application.Ordering;

/// <summary>Product data as Ordering sees it: a read-only snapshot, never the Product aggregate.</summary>
/// <summary>Price as Ordering consumes it, kept local so the port does not depend on Catalog DTOs.</summary>
public sealed record SnapshotPrice(decimal Amount, string Currency);

public sealed record ProductSnapshot(Guid Id, string Sku, string Name, SnapshotPrice Price, bool IsActive, int StockQuantity);

/// <summary>
/// Ordering's port onto the Catalog context (spec 3.3). The adapter lives in the Catalog side
/// (<c>Shop.Application.Catalog.Integration</c>) so Ordering code never touches Product or IProductRepository.
/// </summary>
public interface IProductCatalog
{
    /// <summary>Returns the snapshots of the products that exist; unknown ids are simply absent.</summary>
    Task<IReadOnlyList<ProductSnapshot>> GetByIds(IReadOnlyCollection<Guid> ids, CancellationToken ct = default);
}

public sealed record StockLine(Guid ProductId, int Quantity);

/// <summary>
/// Ordering's port for changing stock, implemented by the Catalog side where the Product aggregate is owned.
/// Joins the ambient unit of work when one is running.
/// </summary>
public interface IStockReservation
{
    /// <summary>Decreases stock per line. PRODUCT_NOT_FOUND / INSUFFICIENT_STOCK abort without partial persistence of later lines.</summary>
    Task Reserve(IReadOnlyCollection<StockLine> lines, CancellationToken ct = default);

    /// <summary>Gives the quantities back (order cancelled).</summary>
    Task Release(IReadOnlyCollection<StockLine> lines, CancellationToken ct = default);
}
