using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
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

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }
}
