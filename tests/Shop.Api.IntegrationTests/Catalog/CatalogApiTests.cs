using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Shop.Api.IntegrationTests.Mongo;

namespace Shop.Api.IntegrationTests.Catalog;

[Collection(MongoCollection.Name)]
public class CatalogApiTests(MongoFixture mongo)
{
    private static string Str(JsonElement e, string name) => e.GetProperty(name).GetString()!;

    // ---- Acceptance criterion 1: create product ----

    [DockerFact]
    public async Task Create_valid_product_returns_active_product()
    {
        using var api = new ApiHost(mongo);

        var payload = await api.CreateProduct("TSHIRT-001", "T-Shirt", 19.99m, stock: 50);

        payload.GetProperty("errors").ValueKind.Should().Be(JsonValueKind.Null);
        var p = payload.GetProperty("product");
        p.GetProperty("isActive").GetBoolean().Should().BeTrue();
        p.GetProperty("price").GetProperty("amount").GetDecimal().Should().Be(19.99m);
        p.GetProperty("price").GetProperty("currency").GetString().Should().Be("USD");
        p.GetProperty("stockQuantity").GetInt32().Should().Be(50);
        Guid.TryParse(Str(p, "id"), out _).Should().BeTrue("ids are plain Guid strings");
    }

    [DockerFact]
    public async Task Create_duplicate_sku_returns_PRODUCT_SKU_ALREADY_EXISTS_with_null_product()
    {
        using var api = new ApiHost(mongo);
        await api.CreateProduct("DUP-001", "First", 5m);

        var payload = await api.CreateProduct("DUP-001", "Second", 6m);

        payload.GetProperty("product").ValueKind.Should().Be(JsonValueKind.Null);
        var error = payload.GetProperty("errors")[0];
        Str(error, "code").Should().Be("PRODUCT_SKU_ALREADY_EXISTS");
        Str(error, "field").Should().Be("sku");
    }

    [DockerFact]
    public async Task Create_with_non_positive_price_returns_VALIDATION_FAILED_on_priceAmount()
    {
        using var api = new ApiHost(mongo);

        foreach (var price in new[] { 0m, -1m })
        {
            var payload = await api.CreateProduct("BAD-" + (price == 0 ? "0" : "NEG"), "Bad", price);

            payload.GetProperty("product").ValueKind.Should().Be(JsonValueKind.Null);
            var error = payload.GetProperty("errors")[0];
            Str(error, "code").Should().Be("VALIDATION_FAILED");
            Str(error, "field").Should().Be("priceAmount");
        }
    }

    [DockerFact]
    public async Task Create_applies_input_defaults_for_currency_and_initial_stock()
    {
        using var api = new ApiHost(mongo);

        var res = await api.Post("""
            mutation { createProduct(input: { sku: "DEF-001", name: "Defaults", priceAmount: 1.5, category: "C" }) {
              product { stockQuantity price { currency } } errors { code } } }
            """);

        var p = res.GetProperty("data").GetProperty("createProduct").GetProperty("product");
        p.GetProperty("stockQuantity").GetInt32().Should().Be(0);
        p.GetProperty("price").GetProperty("currency").GetString().Should().Be("USD");
    }

    // ---- Acceptance criterion 2: browse ----

    private async Task<ApiHost> SeededHost()
    {
        var api = new ApiHost(mongo);
        await api.CreateProduct("APP-001", "Blue Shirt", 20m, "Apparel", description: "Cotton");
        await api.CreateProduct("APP-002", "Red Shirt", 20m, "Apparel");
        await api.CreateProduct("APP-003", "Green hoodie", 50m, "Apparel");
        await api.CreateProduct("HOM-001", "Coffee Mug", 8.5m, "Home", description: "Ceramic MUG for coffee");
        await api.CreateProduct("HOM-002", "Lamp", 35m, "Home");
        await api.CreateProduct("ELE-001", "Mouse", 25m, "Electronics");
        return api;
    }

    private const string ListQuery = """
        query($first: Int, $after: String, $filter: ProductFilterInput, $order: ProductSortInput) {
          products(first: $first, after: $after, filter: $filter, order: $order) {
            totalCount
            edges { cursor node { sku name price { amount } isActive } }
            pageInfo { hasNextPage hasPreviousPage startCursor endCursor }
          }
        }
        """;

    private static string[] Skus(JsonElement res) => res.GetProperty("data").GetProperty("products").GetProperty("edges")
        .EnumerateArray().Select(e => Str(e.GetProperty("node"), "sku")).ToArray();

    private static JsonElement Conn(JsonElement res) => res.GetProperty("data").GetProperty("products");

    [DockerFact]
    public async Task Products_filter_by_category_with_total_count_of_filtered_set()
    {
        using var api = await SeededHost();

        var res = await api.Post(ListQuery, new { first = 2, filter = new { category = "Apparel" }, order = new { field = "NAME", direction = "ASC" } });

        Conn(res).GetProperty("totalCount").GetInt32().Should().Be(3);
        Skus(res).Should().Equal("APP-001", "APP-003");
        Conn(res).GetProperty("pageInfo").GetProperty("hasNextPage").GetBoolean().Should().BeTrue();
    }

    [DockerFact]
    public async Task Products_filter_search_is_case_insensitive_over_name_and_description()
    {
        using var api = await SeededHost();

        var res = await api.Post(ListQuery, new { filter = new { search = "mug" }, order = new { field = "NAME", direction = "ASC" } });

        Skus(res).Should().Equal("HOM-001");

        var byDescription = await api.Post(ListQuery, new { filter = new { search = "COTTON" } });
        Skus(byDescription).Should().Equal("APP-001");
    }

    [DockerFact]
    public async Task Products_filter_by_price_range_and_active_flag()
    {
        using var api = await SeededHost();
        var hoodie = (await api.Post("""{ productBySku(sku: "APP-003") { id } }""")).GetProperty("data").GetProperty("productBySku");
        await api.Post("mutation($i: SetProductActiveInput!) { setProductActive(input: $i) { errors { code } } }",
            new { i = new { id = Str(hoodie, "id"), isActive = false } });

        var range = await api.Post(ListQuery, new { filter = new { minPrice = 20, maxPrice = 35 }, order = new { field = "PRICE", direction = "ASC" } });
        var active = await api.Post(ListQuery, new { filter = new { isActive = true }, first = 100 });
        var inactive = await api.Post(ListQuery, new { filter = new { isActive = false } });

        Skus(range).Should().BeEquivalentTo(["APP-001", "APP-002", "ELE-001", "HOM-002"]);
        Conn(range).GetProperty("totalCount").GetInt32().Should().Be(4);
        Conn(active).GetProperty("totalCount").GetInt32().Should().Be(5);
        Skus(inactive).Should().Equal("APP-003");
    }

    [DockerFact]
    public async Task Products_sort_by_price_desc_then_name_and_created_at()
    {
        using var api = await SeededHost();

        var byPrice = await api.Post(ListQuery, new { first = 100, order = new { field = "PRICE", direction = "DESC" } });
        var byCreated = await api.Post(ListQuery, new { first = 100, order = new { field = "CREATED_AT", direction = "ASC" } });
        var byNameDefaultDir = await api.Post(ListQuery, new { first = 100, order = new { field = "NAME" } });

        var prices = Conn(byPrice).GetProperty("edges").EnumerateArray()
            .Select(e => e.GetProperty("node").GetProperty("price").GetProperty("amount").GetDecimal()).ToArray();
        prices.Should().BeInDescendingOrder();
        Skus(byCreated).Should().Equal("APP-001", "APP-002", "APP-003", "HOM-001", "HOM-002", "ELE-001");
        var names = Conn(byNameDefaultDir).GetProperty("edges").EnumerateArray()
            .Select(e => Str(e.GetProperty("node"), "name")).ToArray();
        names.Should().Equal(names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
    }

    [DockerFact]
    public async Task Name_sort_is_case_insensitive_and_pagination_follows_it()
    {
        using var api = new ApiHost(mongo);
        await api.CreateProduct("N-001", "cherry", 1m);
        await api.CreateProduct("N-002", "Apple", 1m);
        await api.CreateProduct("N-003", "banana", 1m);
        await api.CreateProduct("N-004", "Banana", 1m);
        var order = new { field = "NAME", direction = "ASC" };

        var all = await api.Post(ListQuery, new { first = 10, order });
        var paged = new List<string>();
        string? after = null;
        for (var i = 0; i < 4; i++)
        {
            var res = await api.Post(ListQuery, new { first = 1, after, order });
            paged.AddRange(Skus(res));
            after = Str(Conn(res).GetProperty("pageInfo"), "endCursor");
        }

        // banana/Banana tie case-insensitively and fall back to id order (creation order is not guaranteed), so compare sets per slot.
        var skus = Skus(all);
        skus[0].Should().Be("N-002");
        skus[3].Should().Be("N-001");
        skus[1..3].Should().BeEquivalentTo(["N-003", "N-004"]);
        paged.Should().Equal(skus);
    }

    [DockerFact]
    public async Task Search_treats_regex_metacharacters_literally_and_does_not_error()
    {
        using var api = new ApiHost(mongo);
        await api.CreateProduct("RX-001", "Plain Name", 1m);
        await api.CreateProduct("RX-002", "Cup (large) v1.0", 1m);

        foreach (var (term, expected) in new[] { (".", new[] { "RX-002" }), ("(", new[] { "RX-002" }), (".*", new string[0]), ("(large)", new[] { "RX-002" }), ("[", new string[0]), ("\\", new string[0]) })
        {
            var res = await api.Post(ListQuery, new { filter = new { search = term } });

            res.TryGetProperty("errors", out _).Should().BeFalse($"search '{term}'");
            Skus(res).Should().BeEquivalentTo(expected, $"search '{term}'");
        }
    }

    [DockerFact]
    public async Task Search_longer_than_100_characters_is_rejected()
    {
        using var api = new ApiHost(mongo);

        var res = await api.Post(ListQuery, new { filter = new { search = new string('a', 101) } });

        res.GetProperty("errors")[0].GetProperty("extensions").GetProperty("code").GetString().Should().Be("VALIDATION_FAILED");
    }

    [DockerFact]
    public async Task Bad_variable_is_reported_as_a_graphql_error_not_masked_as_internal()
    {
        using var api = new ApiHost(mongo);

        var res = await api.Post(ListQuery, new { first = "not-an-int" });

        var error = res.GetProperty("errors")[0];
        error.GetProperty("message").GetString().Should().NotBe("An unexpected error occurred.");
        res.GetRawText().Should().NotContain("INTERNAL_ERROR").And.NotContain("traceId");
    }

    [DockerFact]
    public async Task Cursor_pagination_is_stable_with_ties_and_visits_every_item_once()
    {
        using var api = await SeededHost(); // two products share price 20
        foreach (var (field, dir) in new[] { ("PRICE", "ASC"), ("PRICE", "DESC"), ("NAME", "DESC"), ("CREATED_AT", "ASC") })
        {
            var seen = new List<string>();
            string? after = null;
            int pages = 0;
            do
            {
                var res = await api.Post(ListQuery, new { first = 2, after, order = new { field, direction = dir } });
                seen.AddRange(Skus(res));
                var info = Conn(res).GetProperty("pageInfo");
                after = info.GetProperty("hasNextPage").GetBoolean() ? Str(info, "endCursor") : null;
                pages++;
            }
            while (after is not null && pages < 10);

            pages.Should().Be(3, $"{field} {dir}");
            seen.Should().HaveCount(6).And.OnlyHaveUniqueItems($"{field} {dir}");
        }
    }

    [DockerFact]
    public async Task Cursor_pagination_order_matches_the_unpaged_order()
    {
        using var api = await SeededHost();
        var all = Skus(await api.Post(ListQuery, new { first = 100, order = new { field = "PRICE", direction = "DESC" } }));

        var paged = new List<string>();
        string? after = null;
        for (var i = 0; i < 6; i++)
        {
            var res = await api.Post(ListQuery, new { first = 1, after, order = new { field = "PRICE", direction = "DESC" } });
            paged.AddRange(Skus(res));
            after = Str(Conn(res).GetProperty("pageInfo"), "endCursor");
        }

        paged.Should().Equal(all);
    }

    [DockerFact]
    public async Task Products_first_above_100_is_capped_to_100()
    {
        using var api = new ApiHost(mongo);
        await Task.WhenAll(Enumerable.Range(0, 105).Select(i => api.CreateProduct($"BULK-{i:000}", $"Item {i}", 1m + i)));

        var res = await api.Post(ListQuery, new { first = 500 });

        Conn(res).GetProperty("edges").GetArrayLength().Should().Be(100);
        Conn(res).GetProperty("totalCount").GetInt32().Should().Be(105);
        Conn(res).GetProperty("pageInfo").GetProperty("hasNextPage").GetBoolean().Should().BeTrue();
    }

    [DockerFact]
    public async Task Products_default_page_size_is_20()
    {
        using var api = new ApiHost(mongo);
        await Task.WhenAll(Enumerable.Range(0, 25).Select(i => api.CreateProduct($"DEF-{i:000}", $"Item {i}", 1m + i)));

        var res = await api.Post("{ products { totalCount edges { cursor } } }");

        Conn(res).GetProperty("edges").GetArrayLength().Should().Be(20);
        Conn(res).GetProperty("totalCount").GetInt32().Should().Be(25);
    }

    [DockerFact]
    public async Task Invalid_cursor_or_cursor_for_other_sort_is_a_coded_top_level_error()
    {
        using var api = await SeededHost();
        var first = await api.Post(ListQuery, new { first = 1, order = new { field = "NAME", direction = "ASC" } });
        var cursor = Str(Conn(first).GetProperty("pageInfo"), "endCursor");

        var garbage = await api.Post(ListQuery, new { after = "garbage!!" });
        var otherSort = await api.Post(ListQuery, new { after = cursor, order = new { field = "PRICE", direction = "ASC" } });

        foreach (var res in new[] { garbage, otherSort })
        {
            res.GetProperty("errors")[0].GetProperty("extensions").GetProperty("code").GetString().Should().Be("VALIDATION_FAILED");
        }
    }

    // ---- Acceptance criterion 3/4 (catalog side) ----

    [DockerFact]
    public async Task Change_price_updates_price_and_validates_amount()
    {
        using var api = new ApiHost(mongo);
        var id = Str((await api.CreateProduct("PRICE-001", "Priced", 10m)).GetProperty("product"), "id");
        const string mutation = """
            mutation($i: ChangeProductPriceInput!) { changeProductPrice(input: $i) {
              product { price { amount currency } } errors { code field } } }
            """;

        var ok = await api.Post(mutation, new { i = new { id, amount = 12.34m } });
        var bad = await api.Post(mutation, new { i = new { id, amount = 0 } });
        var read = await api.Post("query($id: ID!) { product(id: $id) { price { amount } } }", new { id });

        ok.GetProperty("data").GetProperty("changeProductPrice").GetProperty("product").GetProperty("price")
            .GetProperty("amount").GetDecimal().Should().Be(12.34m);
        var err = bad.GetProperty("data").GetProperty("changeProductPrice");
        err.GetProperty("product").ValueKind.Should().Be(JsonValueKind.Null);
        Str(err.GetProperty("errors")[0], "field").Should().Be("amount");
        read.GetProperty("data").GetProperty("product").GetProperty("price").GetProperty("amount").GetDecimal().Should().Be(12.34m);
    }

    [DockerFact]
    public async Task Deactivated_product_stays_queryable_and_can_be_reactivated()
    {
        using var api = new ApiHost(mongo);
        var id = Str((await api.CreateProduct("ACT-001", "Toggle", 10m)).GetProperty("product"), "id");
        const string toggle = "mutation($i: SetProductActiveInput!) { setProductActive(input: $i) { product { isActive } errors { code } } }";

        var off = await api.Post(toggle, new { i = new { id, isActive = false } });
        var byId = await api.Post("query($id: ID!) { product(id: $id) { isActive } }", new { id });
        var bySku = await api.Post("""{ productBySku(sku: "ACT-001") { isActive } }""");
        var on = await api.Post(toggle, new { i = new { id, isActive = true } });

        off.GetProperty("data").GetProperty("setProductActive").GetProperty("product").GetProperty("isActive").GetBoolean().Should().BeFalse();
        byId.GetProperty("data").GetProperty("product").GetProperty("isActive").GetBoolean().Should().BeFalse();
        bySku.GetProperty("data").GetProperty("productBySku").GetProperty("isActive").GetBoolean().Should().BeFalse();
        on.GetProperty("data").GetProperty("setProductActive").GetProperty("product").GetProperty("isActive").GetBoolean().Should().BeTrue();
    }

    [DockerFact]
    public async Task Update_and_adjust_stock_mutations_work_and_report_errors()
    {
        using var api = new ApiHost(mongo);
        var id = Str((await api.CreateProduct("UPD-001", "Old", 10m, stock: 3)).GetProperty("product"), "id");

        var upd = await api.Post("mutation($i: UpdateProductInput!) { updateProduct(input: $i) { product { name category } errors { code } } }",
            new { i = new { id, name = "New", category = "Other" } });
        var stockOk = await api.Post("mutation($i: AdjustProductStockInput!) { adjustProductStock(input: $i) { product { stockQuantity } errors { code } } }",
            new { i = new { id, delta = 7 } });
        var stockBad = await api.Post("mutation($i: AdjustProductStockInput!) { adjustProductStock(input: $i) { product { stockQuantity } errors { code } } }",
            new { i = new { id, delta = -100 } });

        var u = upd.GetProperty("data").GetProperty("updateProduct").GetProperty("product");
        Str(u, "name").Should().Be("New");
        Str(u, "category").Should().Be("Other");
        stockOk.GetProperty("data").GetProperty("adjustProductStock").GetProperty("product").GetProperty("stockQuantity").GetInt32().Should().Be(10);
        Str(stockBad.GetProperty("data").GetProperty("adjustProductStock").GetProperty("errors")[0], "code").Should().Be("INSUFFICIENT_STOCK");
    }

    [DockerFact]
    public async Task Mutations_on_unknown_or_malformed_ids_return_coded_errors()
    {
        using var api = new ApiHost(mongo);
        const string m = "mutation($i: SetProductActiveInput!) { setProductActive(input: $i) { product { id } errors { code field } } }";

        var missing = await api.Post(m, new { i = new { id = Guid.NewGuid().ToString(), isActive = false } });
        var malformed = await api.Post(m, new { i = new { id = "nope", isActive = false } });

        Str(missing.GetProperty("data").GetProperty("setProductActive").GetProperty("errors")[0], "code").Should().Be("PRODUCT_NOT_FOUND");
        var e = malformed.GetProperty("data").GetProperty("setProductActive").GetProperty("errors")[0];
        Str(e, "code").Should().Be("VALIDATION_FAILED");
        Str(e, "field").Should().Be("id");
    }

    // ---- Missing entities, batching ----

    [DockerFact]
    public async Task Missing_entities_resolve_to_null_without_errors()
    {
        using var api = new ApiHost(mongo);

        var res = await api.Post("""
            { a: product(id: "5b7f3b8e-1c1c-4c0a-9d51-3f3c1f0a0001") { id } b: product(id: "not-a-guid") { id } c: productBySku(sku: "NOPE-1") { id } }
            """);

        res.TryGetProperty("errors", out _).Should().BeFalse();
        var data = res.GetProperty("data");
        data.GetProperty("a").ValueKind.Should().Be(JsonValueKind.Null);
        data.GetProperty("b").ValueKind.Should().Be(JsonValueKind.Null);
        data.GetProperty("c").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [DockerFact]
    public async Task Aliased_product_lookups_in_one_request_resolve_through_the_dataloader()
    {
        using var api = new ApiHost(mongo);
        var a = Str((await api.CreateProduct("DL-001", "A", 1m)).GetProperty("product"), "id");
        var b = Str((await api.CreateProduct("DL-002", "B", 2m)).GetProperty("product"), "id");

        var res = await api.Post("query($a: ID!, $b: ID!) { x: product(id: $a) { sku } y: product(id: $b) { sku } z: product(id: $a) { sku } }", new { a, b });

        var data = res.GetProperty("data");
        Str(data.GetProperty("x"), "sku").Should().Be("DL-001");
        Str(data.GetProperty("y"), "sku").Should().Be("DL-002");
        Str(data.GetProperty("z"), "sku").Should().Be("DL-001");
    }

    // ---- Error shape ----

    [DockerFact]
    public async Task Unexpected_exceptions_return_generic_error_with_trace_id_and_no_details()
    {
        using var api = new ApiHost(mongo, services =>
        {
            var existing = services.Single(d => d.ServiceType == typeof(Shop.Application.Catalog.IProductReadService));
            services.Remove(existing);
            services.AddSingleton<Shop.Application.Catalog.IProductReadService, ExplodingReadService>();
        });

        var res = await api.Post("""{ productBySku(sku: "X-1") { id } }""");

        var error = res.GetProperty("errors")[0];
        Str(error, "message").Should().Be("An unexpected error occurred.");
        error.GetProperty("extensions").GetProperty("traceId").GetString().Should().NotBeNullOrWhiteSpace();
        error.GetProperty("extensions").GetProperty("code").GetString().Should().Be("INTERNAL_ERROR");
        res.GetRawText().Should().NotContain("secret connection detail").And.NotContain("ExplodingReadService");
    }

    private sealed class ExplodingReadService : Shop.Application.Catalog.IProductReadService
    {
        public Task<Shop.Application.Catalog.ProductDto?> GetById(Guid id, CancellationToken ct = default) => Boom();

        public Task<Shop.Application.Catalog.ProductDto?> GetBySku(string sku, CancellationToken ct = default) => Boom();

        public Task<IReadOnlyList<Shop.Application.Catalog.ProductDto>> GetByIds(IReadOnlyCollection<Guid> ids, CancellationToken ct = default) =>
            throw new InvalidOperationException("secret connection detail");

        public Task<Shop.Application.Catalog.ProductPageSlice> List(Shop.Application.Catalog.ProductPageRequest request, CancellationToken ct = default) =>
            throw new InvalidOperationException("secret connection detail");

        private static Task<Shop.Application.Catalog.ProductDto?> Boom() => throw new InvalidOperationException("secret connection detail");
    }
}
