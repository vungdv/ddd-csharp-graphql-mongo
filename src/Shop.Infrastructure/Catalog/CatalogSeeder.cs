using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shop.Domain.Catalog;
using Shop.Domain.SharedKernel;

namespace Shop.Infrastructure.Catalog;

/// <summary>Development-only, idempotent seed: inserts missing sample products (matched by SKU), never modifies existing ones.</summary>
public sealed class CatalogSeeder(IServiceScopeFactory scopes, ILogger<CatalogSeeder> logger) : BackgroundService
{
    private const int MaxAttempts = 10;

    public static readonly IReadOnlyList<(string Sku, string Name, string Description, decimal Price, string Category, int Stock)> Products =
    [
        ("TSHIRT-001", "Classic T-Shirt", "Soft cotton crew neck tee", 19.99m, "Apparel", 50),
        ("HOODIE-001", "Zip Hoodie", "Fleece-lined zip-up hoodie", 49.90m, "Apparel", 25),
        ("JEANS-001", "Slim Fit Jeans", "Stretch denim, mid rise", 59.00m, "Apparel", 30),
        ("MUG-001", "Ceramic Mug", "350ml dishwasher-safe mug", 9.50m, "Home", 100),
        ("LAMP-001", "Desk Lamp", "Adjustable LED desk lamp", 34.25m, "Home", 15),
        ("PILLOW-001", "Throw Pillow", "Linen cushion cover with insert", 24.00m, "Home", 40),
        ("MOUSE-001", "Wireless Mouse", "Ergonomic 2.4GHz wireless mouse", 29.99m, "Electronics", 60),
        ("KEYB-001", "Mechanical Keyboard", "Tenkeyless, brown switches", 89.99m, "Electronics", 20),
        ("CABLE-001", "USB-C Cable", "1m braided charging cable", 12.00m, "Electronics", 200),
        ("HEADPH-001", "Over-Ear Headphones", "Noise isolating wired headphones", 79.50m, "Electronics", 0),
    ];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        for (var attempt = 1; attempt <= MaxAttempts && !stoppingToken.IsCancellationRequested; attempt++)
        {
            try
            {
                var inserted = await SeedOnce(stoppingToken);
                logger.LogInformation("Catalog seed complete; inserted {Inserted} product(s).", inserted);
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Catalog seed attempt {Attempt}/{Max} failed.", attempt, MaxAttempts);
                await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                if (attempt == MaxAttempts)
                {
                    logger.LogError("Catalog seed gave up after {Max} attempts; sample data was not inserted.", MaxAttempts);
                }
            }
        }
    }

    public async Task<int> SeedOnce(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IProductRepository>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var inserted = 0;
        foreach (var (sku, name, description, price, category, stock) in Products)
        {
            var key = Sku.Create(sku);
            if (await repo.ExistsBySku(key, ct))
            {
                continue;
            }

            try
            {
                await repo.Add(Product.Create(key, name, description, Money.Of(price), category, stock, clock), ct);
                inserted++;
            }
            catch (DomainException ex) when (ex.Code == DomainErrorCodes.ProductSkuAlreadyExists)
            {
                // Lost a race with another instance seeding concurrently; fine.
            }
        }

        return inserted;
    }
}
