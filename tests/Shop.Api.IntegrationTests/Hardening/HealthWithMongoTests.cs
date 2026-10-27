using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using Shop.Api.IntegrationTests.Catalog;
using Shop.Api.IntegrationTests.Mongo;

namespace Shop.Api.IntegrationTests.Hardening;

/// <summary>Acceptance criterion 10: /health is healthy against a real (replica set) Mongo once indexes are ready.</summary>
[Collection(MongoCollection.Name)]
public class HealthWithMongoTests(MongoFixture mongo)
{
    [DockerFact]
    public async Task Health_is_200_healthy_against_real_mongo_and_indexes_exist()
    {
        using var api = new ApiHost(mongo);

        var response = await api.Client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("Healthy");
        var db = api.Services.GetRequiredService<IMongoDatabase>();
        var indexes = await (await db.GetCollection<BsonDocument>("products").Indexes.ListAsync()).ToListAsync();
        indexes.Select(i => i["name"].AsString).Should().Contain("ux_products_sku");
    }
}
