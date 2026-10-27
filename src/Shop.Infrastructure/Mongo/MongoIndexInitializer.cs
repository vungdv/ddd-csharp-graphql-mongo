using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Shop.Domain.Catalog;
using Shop.Domain.Ordering;
using Shop.Infrastructure.Catalog;
using Shop.Infrastructure.Ordering;

namespace Shop.Infrastructure.Mongo;

/// <summary>Readiness flag set once startup index creation has succeeded.</summary>
public sealed class MongoIndexReadiness
{
    private volatile bool _ready;

    public bool IsReady => _ready;

    public void MarkReady() => _ready = true;
}

/// <summary>Reports Unhealthy until the required indexes exist.</summary>
public sealed class MongoIndexesHealthCheck(MongoIndexReadiness readiness) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(readiness.IsReady
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("MongoDB indexes have not been created yet."));
}

/// <summary>
/// Idempotently creates collection indexes (CreateMany is a no-op for identical specs). Startup tries once
/// synchronously; on failure the host still starts and a background loop retries with backoff while /health
/// reports Unhealthy via <see cref="MongoIndexReadiness"/>.
/// </summary>
public sealed class MongoIndexInitializer : BackgroundService
{
    public const int ErrorAfterAttempts = 3;

    private readonly Func<CancellationToken, Task> _ensure;
    private readonly MongoIndexReadiness _readiness;
    private readonly ILogger? _logger;
    private readonly TimeSpan _initialDelay;

    public MongoIndexInitializer(
        IMongoDatabase database,
        MongoIndexReadiness readiness,
        ILogger<MongoIndexInitializer>? logger = null)
        : this(ct => EnsureIndexes(database, ct), readiness, logger, TimeSpan.FromSeconds(1))
    {
    }

    public MongoIndexInitializer(
        Func<CancellationToken, Task> ensure,
        MongoIndexReadiness readiness,
        ILogger? logger,
        TimeSpan initialDelay)
    {
        _ensure = ensure;
        _readiness = readiness;
        _logger = logger;
        _initialDelay = initialDelay;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _ensure(cancellationToken);
            _readiness.MarkReady();
            _logger?.LogInformation("MongoDB indexes ensured.");
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger?.LogWarning(ex, "Ensuring MongoDB indexes failed at startup; retrying in the background.");
            await base.StartAsync(cancellationToken);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var delay = _initialDelay;
        var attempts = 1; // the synchronous startup attempt
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(delay, stoppingToken);
                attempts++;
                await _ensure(stoppingToken);
                _readiness.MarkReady();
                _logger?.LogInformation("MongoDB indexes ensured after {Attempts} attempts.", attempts);
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger?.Log(
                    attempts >= ErrorAfterAttempts ? LogLevel.Error : LogLevel.Warning,
                    ex,
                    "Ensuring MongoDB indexes failed (attempt {Attempts}); retrying in {Delay}.",
                    attempts,
                    delay);
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 30));
            }
        }
    }

    public static async Task EnsureIndexes(IMongoDatabase database, CancellationToken ct = default)
    {
        MongoMappings.Register();
        var products = database.GetCollection<Product>(ProductRepository.CollectionName);
        var keys = Builders<Product>.IndexKeys;

        // Replaced by the collated ix_products_category_isActive_en (same name, different options would conflict).
        await DropIndexIfExists(products, "ix_products_category_isActive", ct);

        await products.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<Product>(
                    keys.Ascending("sku"),
                    new CreateIndexOptions { Name = "ux_products_sku", Unique = true }),
                new CreateIndexModel<Product>(
                    keys.Ascending("category").Ascending("isActive"),
                    new CreateIndexOptions { Name = "ix_products_category_isActive_en", Collation = ProductReadService.NaturalOrder }),
                // Spec 6 index. Not used by ListProducts: $text matches whole words only, whereas catalog search is a
                // case-insensitive substring match (escaped regex) over name/description.
                new CreateIndexModel<Product>(
                    keys.Text("name").Text("description"),
                    new CreateIndexOptions { Name = "tx_products_name_description" }),
                // Sort-supporting indexes for ListProducts. Queries run with ProductReadService.NaturalOrder, and MongoDB only
                // uses an index for a collated query/sort when the index has the same collation, so these carry it too.
                new CreateIndexModel<Product>(
                    keys.Ascending("name").Ascending("_id"),
                    new CreateIndexOptions { Name = "ix_products_name__id", Collation = ProductReadService.NaturalOrder }),
                new CreateIndexModel<Product>(
                    keys.Ascending("price.amount").Ascending("_id"),
                    new CreateIndexOptions { Name = "ix_products_price_amount__id", Collation = ProductReadService.NaturalOrder }),
                new CreateIndexModel<Product>(
                    keys.Ascending("createdAt").Ascending("_id"),
                    new CreateIndexOptions { Name = "ix_products_createdAt__id", Collation = ProductReadService.NaturalOrder }),
            ],
            ct);

        var orders = database.GetCollection<Order>(OrderRepository.CollectionName);
        var orderKeys = Builders<Order>.IndexKeys;

        await orders.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<Order>(
                    orderKeys.Ascending("orderNumber"),
                    new CreateIndexOptions { Name = "ux_orders_orderNumber", Unique = true }),
                new CreateIndexModel<Order>(
                    orderKeys.Ascending("customer.email").Descending("placedAt"),
                    new CreateIndexOptions { Name = "ix_orders_customerEmail_placedAt" }),
                new CreateIndexModel<Order>(
                    orderKeys.Ascending("status").Descending("placedAt"),
                    new CreateIndexOptions { Name = "ix_orders_status_placedAt" }),
                // Default ListOrders sort: placedAt desc, _id desc (keyset cursor).
                new CreateIndexModel<Order>(
                    orderKeys.Descending("placedAt").Descending("_id"),
                    new CreateIndexOptions { Name = "ix_orders_placedAt__id" }),
            ],
            ct);
    }

    private static async Task DropIndexIfExists<T>(IMongoCollection<T> collection, string name, CancellationToken ct)
    {
        try
        {
            await collection.Indexes.DropOneAsync(name, ct);
        }
        catch (MongoCommandException ex) when (ex.CodeName is "IndexNotFound" or "NamespaceNotFound")
        {
            // Nothing to drop.
        }
    }
}
