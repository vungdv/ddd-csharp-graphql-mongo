using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Shop.Api.IntegrationTests;

public class HealthEndpointTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Health_returns_503_when_mongo_is_unreachable()
    {
        using var unreachable = factory.WithWebHostBuilder(b => b.UseSetting(
            "Mongo:ConnectionString",
            "mongodb://127.0.0.1:1/?directConnection=true&serverSelectionTimeoutMS=500&connectTimeoutMS=500"));
        using var client = unreachable.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }
}
