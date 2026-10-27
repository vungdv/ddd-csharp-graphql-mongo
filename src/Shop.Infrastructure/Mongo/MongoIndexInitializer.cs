using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Shop.Domain.Catalog;
using Shop.Infrastructure.Catalog;

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

        await products.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<Product>(
                    keys.Ascending("sku"),
                    new CreateIndexOptions { Name = "ux_products_sku", Unique = true }),
                new CreateIndexModel<Product>(
                    keys.Ascending("category").Ascending("isActive"),
                    new CreateIndexOptions { Name = "ix_products_category_isActive" }),
                // Spec 6 index. Not used by ListProducts: $text matches whole words only, whereas catalog search is a
                // case-insensitive substring match (escaped regex) over name/description.
                new CreateIndexModel<Product>(
                    keys.Text("name").Text("description"),
                    new CreateIndexOptions { Name = "tx_products_name_description" }),
            ],
            ct);
    }
}
