using MongoDB.Driver;
using Shop.Domain.Catalog;
using Shop.Domain.SharedKernel;
using Shop.Infrastructure.Mongo;

namespace Shop.Infrastructure.Catalog;

public sealed class ProductRepository : IProductRepository
{
    public const string CollectionName = "products";

    private readonly IMongoCollection<Product> _products;
    private readonly IMongoSessionProvider? _sessions;

    /// <param name="sessions">When given and a transaction is running, all reads and writes join it.</param>
    public ProductRepository(IMongoDatabase database, IMongoSessionProvider? sessions = null)
    {
        MongoMappings.Register();
        _products = database.GetCollection<Product>(CollectionName);
        _sessions = sessions;
    }

    private IClientSessionHandle? Session => _sessions?.Current;

    private IFindFluent<Product, Product> Find(System.Linq.Expressions.Expression<Func<Product, bool>> filter) =>
        Session is { } s ? _products.Find(s, filter) : _products.Find(filter);

    public async Task<Product?> GetById(ProductId id, CancellationToken ct = default) =>
        await Find(p => p.Id == id).FirstOrDefaultAsync(ct);

    public async Task<Product?> GetBySku(Sku sku, CancellationToken ct = default) =>
        await Find(p => p.Sku == sku).FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<Product>> GetByIds(IEnumerable<ProductId> ids, CancellationToken ct = default)
    {
        var list = ids.Distinct().ToList();
        if (list.Count == 0)
        {
            return [];
        }

        var filter = Builders<Product>.Filter.In(p => p.Id, list);
        return await (Session is { } s ? _products.Find(s, filter) : _products.Find(filter)).ToListAsync(ct);
    }

    public Task<bool> ExistsBySku(Sku sku, CancellationToken ct = default) =>
        Find(p => p.Sku == sku).AnyAsync(ct);

    public async Task Add(Product product, CancellationToken ct = default)
    {
        try
        {
            if (Session is { } s)
            {
                await _products.InsertOneAsync(s, product, cancellationToken: ct);
            }
            else
            {
                await _products.InsertOneAsync(product, cancellationToken: ct);
            }
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
            Session,
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
