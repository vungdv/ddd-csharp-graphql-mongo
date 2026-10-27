using Microsoft.Extensions.DependencyInjection;
using Shop.Application;
using Shop.Application.Abstractions;
using Shop.Application.Catalog;
using Shop.Domain.Catalog;
using Shop.Domain.SharedKernel;

namespace Shop.Application.Tests.Catalog;

public sealed class TestClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
}

public sealed class InMemoryProductRepository : IProductRepository
{
    public Dictionary<ProductId, Product> Store { get; } = [];

    public int UpdateCalls { get; private set; }

    public Task<Product?> GetById(ProductId id, CancellationToken ct = default) =>
        Task.FromResult(Store.GetValueOrDefault(id));

    public Task<Product?> GetBySku(Sku sku, CancellationToken ct = default) =>
        Task.FromResult(Store.Values.FirstOrDefault(p => p.Sku == sku));

    public Task<IReadOnlyList<Product>> GetByIds(IEnumerable<ProductId> ids, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Product>>(ids.Where(Store.ContainsKey).Select(i => Store[i]).ToList());

    public Task<bool> ExistsBySku(Sku sku, CancellationToken ct = default) =>
        Task.FromResult(Store.Values.Any(p => p.Sku == sku));

    public Task Add(Product product, CancellationToken ct = default)
    {
        Store[product.Id] = product;
        return Task.CompletedTask;
    }

    public Task Update(Product product, CancellationToken ct = default)
    {
        UpdateCalls++;
        return Task.CompletedTask;
    }
}

/// <summary>Records the request and returns canned items so list-handler paging logic can be tested in isolation.</summary>
public sealed class FakeProductReadService : IProductReadService
{
    public List<ProductDto> Items { get; } = [];

    public ProductPageRequest? LastRequest { get; private set; }

    public Task<ProductDto?> GetById(Guid id, CancellationToken ct = default) =>
        Task.FromResult(Items.FirstOrDefault(p => p.Id == id));

    public Task<ProductDto?> GetBySku(string sku, CancellationToken ct = default) =>
        Task.FromResult(Items.FirstOrDefault(p => p.Sku == sku));

    public Task<IReadOnlyList<ProductDto>> GetByIds(IReadOnlyCollection<Guid> ids, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ProductDto>>(Items.Where(p => ids.Contains(p.Id)).ToList());

    public Task<ProductPageSlice> List(ProductPageRequest request, CancellationToken ct = default)
    {
        LastRequest = request;
        return Task.FromResult(new ProductPageSlice(Items.Take(request.Take).ToList(), Items.Count));
    }
}

public sealed class Harness
{
    public Harness(Func<InMemoryProductRepository, IProductRepository>? wrapRepo = null)
    {
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddSingleton<IClock>(Clock);
        services.AddSingleton(wrapRepo?.Invoke(Repo) ?? Repo);
        services.AddSingleton<IProductReadService>(Reads);
        Provider = services.BuildServiceProvider(validateScopes: true);
        Scope = Provider.CreateScope();
    }

    public TestClock Clock { get; } = new();

    public InMemoryProductRepository Repo { get; } = new();

    public FakeProductReadService Reads { get; } = new();

    private ServiceProvider Provider { get; }

    private IServiceScope Scope { get; }

    public IDispatcher Dispatcher => Scope.ServiceProvider.GetRequiredService<IDispatcher>();
}
