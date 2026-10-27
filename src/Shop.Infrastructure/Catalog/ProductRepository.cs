using MongoDB.Driver;
using Shop.Domain.Catalog;
using Shop.Domain.SharedKernel;
using Shop.Infrastructure.Mongo;

namespace Shop.Infrastructure.Catalog;

public sealed class ProductRepository : IProductRepository
{
    public const string CollectionName = "products";

    private readonly IMongoCollection<Product> _products;

    public ProductRepository(IMongoDatabase database)
    {
        MongoMappings.Register();
        _products = database.GetCollection<Product>(CollectionName);
    }

    public async Task<Product?> GetById(ProductId id, CancellationToken ct = default) =>
        await _products.Find(p => p.Id == id).FirstOrDefaultAsync(ct);

    public async Task<Product?> GetBySku(Sku sku, CancellationToken ct = default) =>
        await _products.Find(p => p.Sku == sku).FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<Product>> GetByIds(IEnumerable<ProductId> ids, CancellationToken ct = default)
    {
        var list = ids.Distinct().ToList();
        if (list.Count == 0)
        {
            return [];
        }

        return await _products.Find(Builders<Product>.Filter.In(p => p.Id, list)).ToListAsync(ct);
    }

    public Task<bool> ExistsBySku(Sku sku, CancellationToken ct = default) =>
        _products.Find(p => p.Sku == sku).AnyAsync(ct);

    public async Task Add(Product product, CancellationToken ct = default)
    {
        try
        {
            await _products.InsertOneAsync(product, cancellationToken: ct);
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            throw new DomainException(
                DomainErrorCodes.ProductSkuAlreadyExists,
                $"A product with SKU '{product.Sku}' already exists.",
                "sku");
        }
    }

    public Task Update(Product product, CancellationToken ct = default) =>
        OptimisticConcurrency.Replace<Product, ProductId>(
            _products,
            product,
            "Product",
            ex => ex.WriteError.Category == ServerErrorCategory.DuplicateKey
                ? new DomainException(
                    DomainErrorCodes.ProductSkuAlreadyExists,
                    $"A product with SKU '{product.Sku}' already exists.",
                    "sku")
                : null,
            ct);
}
