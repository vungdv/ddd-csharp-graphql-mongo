using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Shop.Api.GraphQL.Common;
using Shop.Api.IntegrationTests.Catalog;
using Shop.Api.IntegrationTests.Mongo;

namespace Shop.Api.IntegrationTests.Hardening;

/// <summary>Spec 7.3: execution depth limit, operation cost limit, page-size cap and Development-only playground.</summary>
[Collection(MongoCollection.Name)]
public class GraphQlLimitsTests(MongoFixture mongo)
{
    private const string FiveDeep = "{ orders(first: 1) { edges { node { lines { sku } } } } }";

    [DockerFact]
    public async Task Default_execution_depth_limit_is_10()
    {
        using var api = new ApiHost(mongo);

        api.Services.GetRequiredService<GraphQlLimitsOptions>().MaxExecutionDepth.Should().Be(10);
        (await api.Post(FiveDeep)).TryGetProperty("errors", out _).Should().BeFalse();
    }

    [DockerFact]
    public async Task Real_query_deeper_than_the_configured_depth_limit_is_rejected_and_not_executed()
    {
        // The schema has no recursive type, so the limit is lowered through configuration to exercise the rule with a real query.
        using var api = new ApiHost(mongo, settings: new Dictionary<string, string> { ["GraphQL:MaxExecutionDepth"] = "4" });

        var res = await api.Post(FiveDeep);
        var shallow = await api.Post("{ orders(first: 1) { totalCount } }");

        res.TryGetProperty("data", out _).Should().BeFalse(res.GetRawText());
        res.GetProperty("errors")[0].GetProperty("message").GetString().Should().Contain("depth");
        shallow.TryGetProperty("errors", out _).Should().BeFalse(shallow.GetRawText());
    }

    [DockerFact]
    public async Task Query_whose_cost_exceeds_the_limit_is_rejected_before_execution()
    {
        using var api = new ApiHost(mongo);
        var fields = "edges { node { id lines { sku unitPrice { amount } } product(lineIndex: 0) { name } } }";
        var query = "{ a: orders(first: 100) { " + fields + " } b: orders(first: 100) { " + fields + " } c: orders(first: 100) { " + fields + " } }";

        var res = await api.Post(query);

        res.TryGetProperty("data", out var data).Should().BeFalse(res.GetRawText());
        data.ValueKind.Should().Be(JsonValueKind.Undefined);
        var error = res.GetProperty("errors")[0];
        error.GetProperty("message").GetString().Should().Contain("cost");
        error.GetProperty("extensions").GetProperty("maxFieldCost").GetDouble().Should().Be(GraphQlLimits.MaxFieldCost);
    }

    [DockerFact]
    public async Task Maximum_page_of_products_and_orders_with_nested_selections_is_within_the_cost_limit()
    {
        using var api = new ApiHost(mongo);

        var res = await api.Post(
            """
            {
              products(first: 100) { totalCount edges { cursor node { id sku name price { amount currency } stockQuantity } } pageInfo { hasNextPage endCursor } }
              orders(first: 100) { totalCount edges { node { orderNumber status total { amount } lines { sku quantity lineTotal { amount } } } } }
            }
            """);

        res.TryGetProperty("errors", out _).Should().BeFalse(res.GetRawText());
        res.GetProperty("data").GetProperty("products").GetProperty("totalCount").GetInt32().Should().Be(0);
    }

    private const string StandardIntrospection = """
        query IntrospectionQuery {
          __schema {
            queryType { name } mutationType { name } subscriptionType { name }
            types { ...FullType }
            directives { name description locations args { ...InputValue } }
          }
        }
        fragment FullType on __Type {
          kind name description
          fields(includeDeprecated: true) { name description args { ...InputValue } type { ...TypeRef } isDeprecated deprecationReason }
          inputFields { ...InputValue }
          interfaces { ...TypeRef }
          enumValues(includeDeprecated: true) { name description isDeprecated deprecationReason }
          possibleTypes { ...TypeRef }
        }
        fragment InputValue on __InputValue { name description type { ...TypeRef } defaultValue }
        fragment TypeRef on __Type {
          kind name
          ofType { kind name ofType { kind name ofType { kind name ofType { kind name ofType { kind name ofType { kind name ofType { kind name } } } } } } }
        }
        """;

    [DockerFact]
    public async Task Full_standard_introspection_query_works_in_Development_despite_the_depth_limit()
    {
        using var api = new ApiHost(mongo, environment: "Development");

        var res = await api.Post(StandardIntrospection);

        res.TryGetProperty("errors", out _).Should().BeFalse(res.GetRawText());
        res.GetProperty("data").GetProperty("__schema").GetProperty("queryType").GetProperty("name").GetString().Should().Be("Query");
    }

    [DockerFact]
    public async Task Introspection_is_denied_outside_Development_and_allowed_in_Development()
    {
        using var development = new ApiHost(mongo, environment: "Development");
        using var testing = new ApiHost(mongo);
        const string q = "{ __schema { queryType { name } } }";

        var allowed = await development.Post(q);
        var denied = await testing.Post(q);

        allowed.TryGetProperty("errors", out _).Should().BeFalse(allowed.GetRawText());
        denied.TryGetProperty("data", out var data).Should().BeFalse();
        data.ValueKind.Should().Be(JsonValueKind.Undefined);
        denied.GetProperty("errors")[0].GetProperty("message").GetString().Should().Contain("Introspection");
    }

    [DockerFact]
    public async Task Playground_is_served_in_Development_only()
    {
        using var development = new ApiHost(mongo, environment: "Development");
        using var testing = new ApiHost(mongo);

        var dev = await Browse(development);
        var other = await Browse(testing);

        dev.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
        other.Content.Headers.ContentType?.MediaType.Should().NotBe("text/html");
    }

    private static Task<HttpResponseMessage> Browse(ApiHost api)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/graphql");
        request.Headers.Accept.ParseAdd("text/html");
        return api.Client.SendAsync(request);
    }
}
