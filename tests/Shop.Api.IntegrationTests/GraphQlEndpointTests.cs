using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Shop.Api.IntegrationTests;

public class GraphQlEndpointTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Graphql_endpoint_answers_placeholder_query()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/graphql", new { query = "{ ping }" });

        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("data").GetProperty("ping").GetString().Should().Be("pong");
    }
}
