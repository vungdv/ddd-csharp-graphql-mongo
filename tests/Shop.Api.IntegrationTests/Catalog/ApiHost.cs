using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using Shop.Api.IntegrationTests.Mongo;

namespace Shop.Api.IntegrationTests.Catalog;

/// <summary>In-process API wired to a fresh database in the shared Testcontainers Mongo; "Testing" env => no seeding.</summary>
public sealed class ApiHost : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public ApiHost(MongoFixture mongo, Action<IServiceCollection>? configureServices = null)
    {
        var database = "shop_" + Guid.NewGuid().ToString("N");
        var connection = mongo.ConnectionString;
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            b.UseSetting("Mongo:ConnectionString", connection);
            b.UseSetting("Mongo:Database", database);
            if (configureServices is not null)
            {
                b.ConfigureServices(configureServices);
            }
        });
        _client = _factory.CreateClient();
    }

    public IServiceProvider Services => _factory.Services;

    public async Task<JsonElement> Post(string query, object? variables = null)
    {
        var response = await _client.PostAsJsonAsync("/graphql", new { query, variables });
        var json = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    /// <summary>Creates a product through the API and returns its data node.</summary>
    public async Task<JsonElement> CreateProduct(
        string sku,
        string name,
        decimal price,
        string category = "Apparel",
        int stock = 10,
        string? description = null)
    {
        var res = await Post(
            """
            mutation($i: CreateProductInput!) {
              createProduct(input: $i) { product { id sku name price { amount currency } isActive stockQuantity category } errors { code message field } }
            }
            """,
            new { i = new { sku, name, description, priceAmount = price, category, initialStock = stock } });
        if (!res.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object) { throw new InvalidOperationException(res.GetRawText()); }
        return data.GetProperty("createProduct");
    }

    public const string OrderFields = """
        id orderNumber status total { amount currency } placedAt cancelledAt cancellationReason
        customer { name email }
        shippingAddress { line1 line2 city state postalCode country }
        lines { productId sku productName unitPrice { amount currency } quantity lineTotal { amount currency } }
        """;

    public static object Customer(string email = "ada@example.com") => new { name = "Ada Lovelace", email };

    public static object Address => new { line1 = "1 Main St", city = "London", postalCode = "N1 1AA", country = "GB" };

    /// <summary>Runs placeOrder and returns the <c>placeOrder</c> payload node.</summary>
    public async Task<JsonElement> PlaceOrder(string email, params (string ProductId, int Quantity)[] lines)
    {
        var res = await Post(
            "mutation($i: PlaceOrderInput!) { placeOrder(input: $i) { order { " + OrderFields + " } errors { code message field } } }",
            new
            {
                i = new
                {
                    customer = Customer(email),
                    shippingAddress = Address,
                    lines = lines.Select(l => new { productId = l.ProductId, quantity = l.Quantity }).ToArray(),
                },
            });
        if (!res.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object) { throw new InvalidOperationException(res.GetRawText()); }
        return data.GetProperty("placeOrder");
    }

    public Task<JsonElement> PlaceOrder(params (string ProductId, int Quantity)[] lines) => PlaceOrder("ada@example.com", lines);

    public async Task<JsonElement> CancelOrder(string id, string? reason = null)
    {
        var res = await Post(
            "mutation($i: CancelOrderInput!) { cancelOrder(input: $i) { order { " + OrderFields + " } errors { code message field } } }",
            new { i = new { id, reason } });
        if (!res.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object) { throw new InvalidOperationException(res.GetRawText()); }
        return data.GetProperty("cancelOrder");
    }

    /// <summary>Current stock straight from the database (bypasses the API).</summary>
    public async Task<int> StockInDb(string productId)
    {
        var db = Services.GetRequiredService<MongoDB.Driver.IMongoDatabase>();
        var doc = await db.GetCollection<MongoDB.Bson.BsonDocument>("products")
            .Find(MongoDB.Driver.Builders<MongoDB.Bson.BsonDocument>.Filter.Eq("_id", new MongoDB.Bson.BsonBinaryData(Guid.Parse(productId), MongoDB.Bson.GuidRepresentation.Standard)))
            .FirstAsync();
        return doc["stockQuantity"].AsInt32;
    }

    public async Task<long> OrderCountInDb()
    {
        var db = Services.GetRequiredService<MongoDB.Driver.IMongoDatabase>();
        return await db.GetCollection<MongoDB.Bson.BsonDocument>("orders").CountDocumentsAsync(MongoDB.Bson.BsonDocument.Parse("{}"));
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }
}
