namespace Shop.Domain.Catalog;

public interface IProductRepository
{
    Task<Product?> GetById(ProductId id, CancellationToken ct = default);

    Task<Product?> GetBySku(Sku sku, CancellationToken ct = default);

    Task<IReadOnlyList<Product>> GetByIds(IEnumerable<ProductId> ids, CancellationToken ct = default);

    Task<bool> ExistsBySku(Sku sku, CancellationToken ct = default);

    /// <exception cref="Shop.Domain.SharedKernel.DomainException">PRODUCT_SKU_ALREADY_EXISTS on duplicate SKU.</exception>
    Task Add(Product product, CancellationToken ct = default);

    /// <exception cref="Shop.Domain.SharedKernel.DomainException">CONCURRENCY_CONFLICT when the stored version differs.</exception>
    Task Update(Product product, CancellationToken ct = default);
}
