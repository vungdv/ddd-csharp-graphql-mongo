using FluentAssertions;
using HotChocolate.Execution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shop.Api.IntegrationTests.Mongo;
using Shop.Infrastructure.Catalog;

namespace Shop.Api.IntegrationTests.Catalog;

[Collection(MongoCollection.Name)]
public class SchemaAndSeedTests(MongoFixture mongo)
{
    [DockerFact]
    public async Task Schema_exposes_the_sdl_from_spec_7_1()
    {
        using var api = new ApiHost(mongo);
        var executor = await api.Services.GetRequiredService<IRequestExecutorResolver>().GetRequestExecutorAsync();

        var sdl = executor.Schema.ToString();

        string[] expected =
        [
            "type Money {",
            "amount: Decimal!",
            "type Product {",
            "id: ID!",
            "description: String",
            "price: Money!",
            "createdAt: DateTime!",
            "type PageInfo {",
            "type ProductEdge {",
            "type ProductConnection {",
            "totalCount: Int!",
            "enum ProductSortField {",
            "field: ProductSortField!",
            "CREATED_AT",
            "enum SortDirection {",
            "direction: SortDirection! = ASC",
            "input ProductFilterInput {",
            "minPrice: Decimal",
            "product(id: ID!): Product",
            "productBySku(sku: String!): Product",
            "products(first: Int = 20 after: String filter: ProductFilterInput order: ProductSortInput): ProductConnection!",
            "currency: String! = \"USD\"",
            "initialStock: Int! = 0",
            "input UpdateProductInput {",
            "input ChangeProductPriceInput {",
            "input SetProductActiveInput {",
            "input AdjustProductStockInput {",
            "type CreateProductPayload {",
            "errors: [UserError!]",
            "type UserError {",
            "createProduct(input: CreateProductInput!): CreateProductPayload!",
            "updateProduct(input: UpdateProductInput!): UpdateProductPayload!",
            "changeProductPrice(input: ChangeProductPriceInput!): ChangeProductPricePayload!",
            "setProductActive(input: SetProductActiveInput!): SetProductActivePayload!",
            "adjustProductStock(input: AdjustProductStockInput!): AdjustProductStockPayload!",
            "enum OrderStatus {",
            "PLACED",
            "CANCELLED",
            "type Address {",
            "line1: String!",
            "line2: String",
            "state: String",
            "postalCode: String!",
            "country: String!",
            "type Customer {",
            "name: String!",
            "email: String!",
            "type OrderLine {",
            "productId: ID!",
            "sku: String!",
            "productName: String!",
            "unitPrice: Money!",
            "quantity: Int!",
            "lineTotal: Money!",
            "type Order {",
            "orderNumber: String!",
            "customer: Customer!",
            "shippingAddress: Address!",
            "lines: [OrderLine!]!",
            "status: OrderStatus!",
            "total: Money!",
            "placedAt: DateTime!",
            "cancelledAt: DateTime",
            "cancellationReason: String",
            "product(lineIndex: Int!): Product",
            "type OrderEdge {",
            "cursor: String!",
            "node: Order!",
            "type OrderConnection {",
            "edges: [OrderEdge!]!",
            "pageInfo: PageInfo!",
            "input OrderFilterInput {",
            "status: OrderStatus",
            "customerEmail: String",
            "placedFrom: DateTime",
            "placedTo: DateTime",
            "order(id: ID!): Order",
            "orderByNumber(orderNumber: String!): Order",
            "orders(first: Int = 20 after: String filter: OrderFilterInput): OrderConnection!",
            "input AddressInput {",
            "input CustomerInput {",
            "input OrderLineInput {",
            "input PlaceOrderInput {",
            "customer: CustomerInput!",
            "shippingAddress: AddressInput!",
            "lines: [OrderLineInput!]!",
            "input CancelOrderInput {",
            "reason: String",
            "type PlaceOrderPayload {",
            "order: Order",
            "type CancelOrderPayload {",
            "placeOrder(input: PlaceOrderInput!): PlaceOrderPayload!",
            "cancelOrder(input: CancelOrderInput!): CancelOrderPayload!",
        ];
        var normalized = System.Text.RegularExpressions.Regex.Replace(sdl, @"\s+", " ");
        foreach (var fragment in expected)
        {
            normalized.Should().Contain(System.Text.RegularExpressions.Regex.Replace(fragment, @"\s+", " "), fragment);
        }
    }

    [DockerFact]
    public async Task Seeder_inserts_ten_products_in_three_categories_and_is_idempotent()
    {
        using var api = new ApiHost(mongo);
        var seeder = new CatalogSeeder(api.Services.GetRequiredService<IServiceScopeFactory>(), NullLogger<CatalogSeeder>.Instance);

        var first = await seeder.SeedOnce(CancellationToken.None);
        var second = await seeder.SeedOnce(CancellationToken.None);

        first.Should().Be(10);
        second.Should().Be(0);
        CatalogSeeder.Products.Select(p => p.Category).Distinct().Should().HaveCount(3);
        var res = await api.Post("{ products(first: 100) { totalCount } }");
        res.GetProperty("data").GetProperty("products").GetProperty("totalCount").GetInt32().Should().Be(10);
    }
}
