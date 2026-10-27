using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Shop.Application;
using Shop.Application.Abstractions;
using Shop.Application.Tests.Catalog;
using Shop.Domain.Catalog;
using Shop.Domain.Ordering;
using Shop.Domain.SharedKernel;

namespace Shop.Application.Tests.Ordering;

/// <summary>
/// In-memory store with transaction semantics: writes made inside a unit of work are staged and become visible only on
/// commit, so a rolled-back operation provably leaves no trace. Products are returned as copies (like a real database).
/// </summary>
public sealed class FakeStore
{
    private static readonly MethodInfo Clone = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!;

    public Dictionary<ProductId, Product> Products { get; } = [];

    public Dictionary<OrderId, Order> Orders { get; } = [];

    public Dictionary<ProductId, Product>? PendingProducts { get; private set; }

    public Dictionary<OrderId, Order>? PendingOrders { get; private set; }

    public bool InTransaction => PendingProducts is not null;

    public int Transactions { get; private set; }

    public void Begin()
    {
        Transactions++;
        PendingProducts = [];
        PendingOrders = [];
    }

    public void Commit()
    {
        foreach (var (k, v) in PendingProducts!)
        {
            Products[k] = v;
        }

        foreach (var (k, v) in PendingOrders!)
        {
            Orders[k] = v;
        }

        Rollback();
    }

    public void Rollback()
    {
        PendingProducts = null;
        PendingOrders = null;
    }

    public Product? ReadProduct(ProductId id) =>
        (PendingProducts is not null && PendingProducts.TryGetValue(id, out var p) ? p : Products.GetValueOrDefault(id)) is { } found
            ? (Product)Clone.Invoke(found, null)!
            : null;

    public void WriteProduct(Product product)
    {
        var copy = (Product)Clone.Invoke(product, null)!;
        (PendingProducts ?? Products)[product.Id] = copy;
    }

    public IEnumerable<Order> AllOrders() => Orders.Values.Concat(PendingOrders?.Values ?? Enumerable.Empty<Order>());
}

public sealed class FakeProductRepository(FakeStore store) : IProductRepository
{
    public Task<Product?> GetById(ProductId id, CancellationToken ct = default) => Task.FromResult(store.ReadProduct(id));

    public Task<Product?> GetBySku(Sku sku, CancellationToken ct = default) =>
        Task.FromResult(store.Products.Values.FirstOrDefault(p => p.Sku == sku));

    public Task<IReadOnlyList<Product>> GetByIds(IEnumerable<ProductId> ids, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Product>>(ids.Select(store.ReadProduct).OfType<Product>().ToList());

    public Task<bool> ExistsBySku(Sku sku, CancellationToken ct = default) =>
        Task.FromResult(store.Products.Values.Any(p => p.Sku == sku));

    public Task Add(Product product, CancellationToken ct = default)
    {
        store.WriteProduct(product);
        return Task.CompletedTask;
    }

    public Task Update(Product product, CancellationToken ct = default)
    {
        store.WriteProduct(product);
        return Task.CompletedTask;
    }
}

public sealed class FakeOrderRepository(FakeStore store) : IOrderRepository
{
    public Task<Order?> GetById(OrderId id, CancellationToken ct = default) =>
        Task.FromResult(store.AllOrders().FirstOrDefault(o => o.Id == id));

    public Task<Order?> GetByOrderNumber(string orderNumber, CancellationToken ct = default) =>
        Task.FromResult(store.AllOrders().FirstOrDefault(o => o.OrderNumber == orderNumber));

    public Task Add(Order order, CancellationToken ct = default)
    {
        if (store.AllOrders().Any(o => o.Id == order.Id || o.OrderNumber == order.OrderNumber))
        {
            throw new DuplicateOrderException($"Duplicate order {order.OrderNumber}.");
        }

        (store.PendingOrders ?? store.Orders)[order.Id] = order;
        return Task.CompletedTask;
    }

    public Task Update(Order order, CancellationToken ct = default)
    {
        (store.PendingOrders ?? store.Orders)[order.Id] = order;
        return Task.CompletedTask;
    }
}

public sealed class FakeUnitOfWork(FakeStore store) : IUnitOfWork
{
    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct = default)
    {
        if (store.InTransaction)
        {
            return await work(ct);
        }

        store.Begin();
        try
        {
            var result = await work(ct);
            store.Commit();
            return result;
        }
        catch
        {
            store.Rollback();
            throw;
        }
    }
}

/// <summary>Hands out the queued numbers first, then random ones.</summary>
public sealed class ScriptedNumberGenerator : IOrderNumberGenerator
{
    public Queue<string> Queued { get; } = new();

    public string? Always { get; set; }

    public int Calls { get; private set; }

    public string Next(DateTimeOffset now)
    {
        Calls++;
        return Always ?? (Queued.Count > 0 ? Queued.Dequeue() : RandomOrderNumberGenerator.Instance.Next(now));
    }
}

public sealed class OrderHarness
{
    public OrderHarness(Action<IServiceCollection>? configure = null)
    {
        Products = new FakeProductRepository(Store);
        var services = new ServiceCollection();
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(Microsoft.Extensions.Logging.Abstractions.NullLogger<>));
        services.AddApplication();
        services.AddSingleton<IClock>(Clock);
        services.AddSingleton(Store);
        services.AddSingleton<IProductRepository>(Products);
        services.AddSingleton<IOrderRepository>(new FakeOrderRepository(Store));
        services.AddSingleton<IUnitOfWork>(new FakeUnitOfWork(Store));
        services.AddSingleton<IOrderNumberGenerator>(Numbers);
        configure?.Invoke(services);
        Provider = services.BuildServiceProvider(validateScopes: true);
        Scope = Provider.CreateScope();
    }

    public TestClock Clock { get; } = new();

    public FakeStore Store { get; } = new();

    public FakeProductRepository Products { get; }

    public ScriptedNumberGenerator Numbers { get; } = new();

    private ServiceProvider Provider { get; }

    private IServiceScope Scope { get; }

    public IDispatcher Dispatcher => Scope.ServiceProvider.GetRequiredService<IDispatcher>();

    public Product AddProduct(string sku, decimal price = 10m, int stock = 5, bool active = true)
    {
        var product = Product.Create(Sku.Create(sku), "Name " + sku, null, Money.Of(price), "Cat", stock, Clock);
        if (!active)
        {
            product.Deactivate(Clock);
        }

        Store.Products[product.Id] = product;
        return product;
    }

    public int Stock(Product p) => Store.Products[p.Id].StockQuantity;
}
