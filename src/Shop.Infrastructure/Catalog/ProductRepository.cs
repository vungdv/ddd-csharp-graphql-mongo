using MongoDB.Bson.Serialization;
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

    public async Task Update(Product product, CancellationToken ct = default)
    {
        var expected = product.Version;
        var setVersion = BsonClassMap.LookupClassMap(typeof(Product)).AllMemberMaps
            .Single(m => m.MemberName == nameof(Product.Version)).Setter;

        // The replacement document carries the incremented version.
        setVersion(product, expected + 1);
        try
        {
            var result = await _products.ReplaceOneAsync(
                p => p.Id == product.Id && p.Version == expected,
                product,
                cancellationToken: ct);

            if (result.MatchedCount == 0)
            {
                setVersion(product, expected);
                throw new DomainException(
                    DomainErrorCodes.ConcurrencyConflict,
                    $"Product {product.Id} was modified concurrently or no longer exists.");
            }
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            setVersion(product, expected);
            throw new DomainException(
                DomainErrorCodes.ProductSkuAlreadyExists,
                $"A product with SKU '{product.Sku}' already exists.",
                "sku");
        }
        catch (Exception) when (product.Version != expected)
        {
            setVersion(product, expected);
            throw;
        }
    }
}
