using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shop.Api.IntegrationTests.Catalog;
using Shop.Api.IntegrationTests.Mongo;

namespace Shop.Api.IntegrationTests.Ordering;

[Collection(MongoCollection.Name)]
public class OrderApiTests(MongoFixture mongo)
{
    private static string Str(JsonElement e, string name) => e.GetProperty(name).GetString()!;

    private static string IdOf(JsonElement createPayload) => Str(createPayload.GetProperty("product"), "id");

    private static JsonElement Error(JsonElement payload) => payload.GetProperty("errors")[0];

    private static async Task<string> Product(ApiHost api, string sku, decimal price, int stock) =>
        IdOf(await api.CreateProduct(sku, "Product " + sku, price, stock: stock));

    // ---- Acceptance criterion 5: place order ----

    [DockerFact]
    public async Task Place_order_returns_PLACED_order_with_total_and_decreases_stock()
    {
        using var api = new ApiHost(mongo);
        var shirt = await Product(api, "SHIRT-001", 19.99m, 10);
        var mug = await Product(api, "MUG-001", 5.50m, 4);

        var payload = await api.PlaceOrder("Ada@Example.com", (shirt, 2), (mug, 3));

        payload.GetProperty("errors").ValueKind.Should().Be(JsonValueKind.Null);
        var order = payload.GetProperty("order");
        Str(order, "status").Should().Be("PLACED");
        Str(order, "orderNumber").Should().MatchRegex(@"^ORD-\d{8}-[A-Z0-9]{6}$");
        Str(order, "orderNumber")[4..12].Should().Be(DateTimeOffset.Parse(Str(order, "placedAt")).UtcDateTime.ToString("yyyyMMdd"));
        Str(order.GetProperty("customer"), "email").Should().Be("ada@example.com", "emails are normalised to lower case");
        var lines = order.GetProperty("lines").EnumerateArray().ToList();
        lines.Should().HaveCount(2);
        lines.Sum(l => l.GetProperty("lineTotal").GetProperty("amount").GetDecimal()).Should().Be(56.48m);
        order.GetProperty("total").GetProperty("amount").GetDecimal().Should().Be(56.48m);
        lines[0].GetProperty("unitPrice").GetProperty("amount").GetDecimal().Should().Be(19.99m);
        Str(lines[0], "sku").Should().Be("SHIRT-001");
        Str(lines[0], "productName").Should().Be("Product SHIRT-001");
        order.GetProperty("cancelledAt").ValueKind.Should().Be(JsonValueKind.Null);
        (await api.StockInDb(shirt)).Should().Be(8);
        (await api.StockInDb(mug)).Should().Be(1);
    }

    [DockerFact]
    public async Task Invalid_input_reports_fields_that_match_the_input_names()
    {
        using var api = new ApiHost(mongo);
        var p = await Product(api, "A-001", 5m, 5);
        var res = await api.Post(
            "mutation($i: PlaceOrderInput!) { placeOrder(input: $i) { order { id } errors { code field } } }",
            new
            {
                i = new
                {
                    customer = new { name = "Ada", email = "nope" },
                    shippingAddress = new { line1 = "1 Main St", city = "", postalCode = "N1", country = "GB" },
                    lines = new[] { new { productId = p, quantity = 0 }, new { productId = "not-a-guid", quantity = 1 } },
                },
            });

        var payload = res.GetProperty("data").GetProperty("placeOrder");
        payload.GetProperty("order").ValueKind.Should().Be(JsonValueKind.Null);
        payload.GetProperty("errors").EnumerateArray().Select(e => Str(e, "field")).Should().Equal("lines[1].productId");

        var res2 = await api.Post(
            "mutation($i: PlaceOrderInput!) { placeOrder(input: $i) { errors { code field } } }",
            new
            {
                i = new
                {
                    customer = new { name = "Ada", email = "nope" },
                    shippingAddress = new { line1 = "1 Main St", city = "", postalCode = "N1", country = "GB" },
                    lines = new[] { new { productId = p, quantity = 0 } },
                },
            });
        var errors = res2.GetProperty("data").GetProperty("placeOrder").GetProperty("errors").EnumerateArray().ToList();
        errors.Select(e => Str(e, "field")).Should()
            .BeEquivalentTo("customer.email", "shippingAddress.city", "lines[0].quantity");
        errors.Should().OnlyContain(e => Str(e, "code") == "VALIDATION_FAILED");
    }

    // ---- Acceptance criterion 3: price change ----

    [DockerFact]
    public async Task Changing_price_does_not_alter_the_unit_price_of_placed_orders()
    {
        using var api = new ApiHost(mongo);
        var p = await Product(api, "PRICE-001", 10m, 10);
        var placed = (await api.PlaceOrder((p, 2))).GetProperty("order");

        await api.Post("mutation { changeProductPrice(input: { id: \"" + p + "\", amount: 99.00 }) { errors { code } } }");

        var res = await api.Post(
            "query($id: ID!) { order(id: $id) { total { amount } lines { unitPrice { amount } lineTotal { amount } } product(lineIndex: 0) { price { amount } } } }",
            new { id = Str(placed, "id") });
        var order = res.GetProperty("data").GetProperty("order");
        order.GetProperty("lines")[0].GetProperty("unitPrice").GetProperty("amount").GetDecimal().Should().Be(10m);
        order.GetProperty("total").GetProperty("amount").GetDecimal().Should().Be(20m);
        order.GetProperty("product").GetProperty("price").GetProperty("amount").GetDecimal().Should().Be(99m, "the resolver shows the live product");
    }

    // ---- Acceptance criterion 4: deactivate ----

    [DockerFact]
    public async Task Inactive_product_stays_queryable_but_cannot_be_ordered()
    {
        using var api = new ApiHost(mongo);
        var p = await Product(api, "OFF-001", 10m, 10);
        await api.Post("mutation { setProductActive(input: { id: \"" + p + "\", isActive: false }) { errors { code } } }");

        var queried = await api.Post("{ product(id: \"" + p + "\") { isActive } }");
        var payload = await api.PlaceOrder((p, 1));

        queried.GetProperty("data").GetProperty("product").GetProperty("isActive").GetBoolean().Should().BeFalse();
        payload.GetProperty("order").ValueKind.Should().Be(JsonValueKind.Null);
        Str(Error(payload), "code").Should().Be("PRODUCT_INACTIVE");
        Str(Error(payload), "field").Should().Be("lines[0].productId");
        (await api.StockInDb(p)).Should().Be(10);
    }

    // ---- Acceptance criterion 6: atomicity ----

    [DockerFact]
    public async Task A_failing_line_creates_no_order_and_changes_no_stock()
    {
        using var api = new ApiHost(mongo);
        var good = await Product(api, "GOOD-001", 10m, 5);
        var scarce = await Product(api, "SCARCE-001", 10m, 1);
        var off = await Product(api, "OFF-002", 10m, 5);
        await api.Post("mutation { setProductActive(input: { id: \"" + off + "\", isActive: false }) { errors { code } } }");

        var insufficient = await api.PlaceOrder((good, 3), (scarce, 2));
        var inactive = await api.PlaceOrder((good, 3), (off, 1));
        var missing = await api.PlaceOrder((good, 3), (Guid.NewGuid().ToString(), 1));

        Str(Error(insufficient), "code").Should().Be("INSUFFICIENT_STOCK");
        Str(Error(inactive), "code").Should().Be("PRODUCT_INACTIVE");
        Str(Error(missing), "code").Should().Be("PRODUCT_NOT_FOUND");
        foreach (var payload in new[] { insufficient, inactive, missing })
        {
            payload.GetProperty("order").ValueKind.Should().Be(JsonValueKind.Null);
        }

        (await api.OrderCountInDb()).Should().Be(0);
        (await api.StockInDb(good)).Should().Be(5, "the transaction rolled back the decrease of the valid first line");
        (await api.StockInDb(scarce)).Should().Be(1);
        (await api.StockInDb(off)).Should().Be(5);
    }

    // ---- Acceptance criterion 7: cancel ----

    [DockerFact]
    public async Task Cancel_restores_stock_stores_reason_and_time_and_second_cancel_is_rejected()
    {
        using var api = new ApiHost(mongo);
        var a = await Product(api, "CAN-A", 10m, 10);
        var b = await Product(api, "CAN-B", 3m, 6);
        var placed = (await api.PlaceOrder((a, 4), (b, 5))).GetProperty("order");
        (await api.StockInDb(a)).Should().Be(6);
        (await api.StockInDb(b)).Should().Be(1);

        var cancelled = await api.CancelOrder(Str(placed, "id"), "changed my mind");
        var again = await api.CancelOrder(Str(placed, "id"));

        var order = cancelled.GetProperty("order");
        Str(order, "status").Should().Be("CANCELLED");
        Str(order, "cancellationReason").Should().Be("changed my mind");
        order.GetProperty("cancelledAt").ValueKind.Should().Be(JsonValueKind.String);
        (await api.StockInDb(a)).Should().Be(10);
        (await api.StockInDb(b)).Should().Be(6);
        again.GetProperty("order").ValueKind.Should().Be(JsonValueKind.Null);
        Str(Error(again), "code").Should().Be("ORDER_ALREADY_CANCELLED");
        (await api.StockInDb(a)).Should().Be(10, "the second cancel must not restore stock again");
    }

    [DockerFact]
    public async Task Cancelling_an_unknown_order_returns_ORDER_NOT_FOUND()
    {
        using var api = new ApiHost(mongo);

        var payload = await api.CancelOrder(Guid.NewGuid().ToString());

        Str(Error(payload), "code").Should().Be("ORDER_NOT_FOUND");
    }

    // ---- Acceptance criterion 8: concurrency ----

    [DockerFact]
    public async Task Competing_orders_for_the_last_unit_yield_exactly_one_success()
    {
        using var api = new ApiHost(mongo);
        const int competitors = 8;

        for (var round = 0; round < 3; round++)
        {
            var p = await Product(api, "LAST-" + round, 10m, 1);
            using var gate = new ManualResetEventSlim(false);
            var tasks = Enumerable.Range(0, competitors).Select(i => Task.Run(async () =>
            {
                gate.Wait();
                return await api.PlaceOrder($"buyer{i}@example.com", (p, 1));
            })).ToList();
            await Task.Delay(100); // let every task reach the gate
            gate.Set();
            var results = await Task.WhenAll(tasks);

            var successes = results.Where(r => r.GetProperty("order").ValueKind == JsonValueKind.Object).ToList();
            var failures = results.Except(successes).ToList();
            successes.Should().HaveCount(1, "exactly one order may win the last unit (round {0})", round);
            failures.Should().HaveCount(competitors - 1);
            failures.Select(f => Str(Error(f), "code")).Should()
                .OnlyContain(c => c == "INSUFFICIENT_STOCK" || c == "CONCURRENCY_CONFLICT");
            (await api.StockInDb(p)).Should().Be(0);
        }

        (await api.OrderCountInDb()).Should().Be(3, "one order per round, no orphans from aborted transactions");
    }

    [DockerFact]
    public async Task Concurrent_orders_with_enough_stock_all_succeed_because_transient_conflicts_are_retried()
    {
        using var api = new ApiHost(mongo);
        const int buyers = 10;
        var p = await Product(api, "PLENTY-1", 10m, 50);
        using var gate = new ManualResetEventSlim(false);
        var tasks = Enumerable.Range(0, buyers).Select(i => Task.Run(async () =>
        {
            gate.Wait();
            return await api.PlaceOrder($"buyer{i}@example.com", (p, 2));
        })).ToList();
        await Task.Delay(100);
        gate.Set();

        var results = await Task.WhenAll(tasks);

        results.Should().OnlyContain(r => r.GetProperty("order").ValueKind == JsonValueKind.Object);
        results.Select(r => Str(r.GetProperty("order"), "orderNumber")).Distinct().Should().HaveCount(buyers);
        (await api.StockInDb(p)).Should().Be(50 - (2 * buyers));
        (await api.OrderCountInDb()).Should().Be(buyers);
    }

    // ---- End to end ----

    [DockerFact]
    public async Task End_to_end_create_product_place_order_query_and_cancel()
    {
        using var api = new ApiHost(mongo);
        var created = await api.CreateProduct("TSHIRT-001", "T-Shirt", 19.99m, stock: 50);
        var productId = IdOf(created);

        var placed = (await api.PlaceOrder((productId, 2))).GetProperty("order");
        var id = Str(placed, "id");
        var number = Str(placed, "orderNumber");

        var byId = await api.Post("query($id: ID!) { order(id: $id) { " + ApiHost.OrderFields + " product(lineIndex: 0) { sku stockQuantity } } }", new { id });
        var byNumber = await api.Post("query($n: String!) { orderByNumber(orderNumber: $n) { id status } }", new { n = number.ToLowerInvariant() });
        var unknown = await api.Post("{ order(id: \"" + Guid.NewGuid() + "\") { id } orderByNumber(orderNumber: \"ORD-20000101-ZZZZZZ\") { id } }");

        var order = byId.GetProperty("data").GetProperty("order");
        Str(order, "orderNumber").Should().Be(number);
        order.GetProperty("total").GetProperty("amount").GetDecimal().Should().Be(39.98m);
        Str(order.GetProperty("shippingAddress"), "city").Should().Be("London");
        order.GetProperty("shippingAddress").GetProperty("line2").ValueKind.Should().Be(JsonValueKind.Null);
        Str(order.GetProperty("product"), "sku").Should().Be("TSHIRT-001");
        order.GetProperty("product").GetProperty("stockQuantity").GetInt32().Should().Be(48);
        Str(byNumber.GetProperty("data").GetProperty("orderByNumber"), "id").Should().Be(id);
        unknown.GetProperty("data").GetProperty("order").ValueKind.Should().Be(JsonValueKind.Null);
        unknown.GetProperty("data").GetProperty("orderByNumber").ValueKind.Should().Be(JsonValueKind.Null);

        var cancelled = (await api.CancelOrder(id, "no longer needed")).GetProperty("order");
        Str(cancelled, "status").Should().Be("CANCELLED");
        var after = await api.Post("{ product(id: \"" + productId + "\") { stockQuantity } }");
        after.GetProperty("data").GetProperty("product").GetProperty("stockQuantity").GetInt32().Should().Be(50);
    }

    [DockerFact]
    public async Task Product_resolver_rejects_an_out_of_range_line_index()
    {
        using var api = new ApiHost(mongo);
        var p = await Product(api, "IDX-001", 1m, 5);
        var id = Str((await api.PlaceOrder((p, 1))).GetProperty("order"), "id");

        var res = await api.Post("{ order(id: \"" + id + "\") { product(lineIndex: 3) { id } } }");

        res.GetProperty("errors")[0].GetProperty("extensions").GetProperty("code").GetString().Should().Be("VALIDATION_FAILED");
    }

    // ---- Queries ----

    [DockerFact]
    public async Task Orders_support_filters_cursor_pagination_and_total_count()
    {
        using var api = new ApiHost(mongo);
        var p = await Product(api, "LIST-001", 1m, 100);
        var ids = new List<string>();
        for (var i = 0; i < 5; i++)
        {
            var email = i % 2 == 0 ? "Even@Example.com" : "odd@example.com";
            ids.Add(Str((await api.PlaceOrder(email, (p, 1))).GetProperty("order"), "id"));
            await Task.Delay(5);
        }

        await api.CancelOrder(ids[0]);

        const string list = "query($first: Int, $after: String, $f: OrderFilterInput) { orders(first: $first, after: $after, filter: $f) { totalCount pageInfo { hasNextPage endCursor startCursor } edges { cursor node { id placedAt status } } } }";
        var page1 = (await api.Post(list, new { first = 2 })).GetProperty("data").GetProperty("orders");
        var cursor = Str(page1.GetProperty("pageInfo"), "endCursor");
        var page2 = (await api.Post(list, new { first = 2, after = cursor })).GetProperty("data").GetProperty("orders");
        var page3 = (await api.Post(list, new { first = 2, after = Str(page2.GetProperty("pageInfo"), "endCursor") })).GetProperty("data").GetProperty("orders");

        page1.GetProperty("totalCount").GetInt32().Should().Be(5);
        page1.GetProperty("pageInfo").GetProperty("hasNextPage").GetBoolean().Should().BeTrue();
        page3.GetProperty("pageInfo").GetProperty("hasNextPage").GetBoolean().Should().BeFalse();
        var paged = new[] { page1, page2, page3 }
            .SelectMany(pg => pg.GetProperty("edges").EnumerateArray().Select(e => e.GetProperty("node"))).ToList();
        paged.Select(n => Str(n, "id")).Should().BeEquivalentTo(ids).And.OnlyHaveUniqueItems();
        paged.Select(n => DateTimeOffset.Parse(Str(n, "placedAt"))).Should().BeInDescendingOrder("default sort is placedAt desc");
        paged.Select(n => Str(n, "id")).Should().Equal(ids.AsEnumerable().Reverse(), "orders were placed in increasing time order");

        async Task<JsonElement> Filter(object f) =>
            (await api.Post(list, new { first = 100, f })).GetProperty("data").GetProperty("orders");

        (await Filter(new { status = "CANCELLED" })).GetProperty("totalCount").GetInt32().Should().Be(1);
        (await Filter(new { status = "PLACED" })).GetProperty("totalCount").GetInt32().Should().Be(4);
        (await Filter(new { customerEmail = "EVEN@example.com" })).GetProperty("totalCount").GetInt32().Should().Be(3);
        (await Filter(new { customerEmail = "odd@example.com", status = "PLACED" })).GetProperty("totalCount").GetInt32().Should().Be(2);

        var thirdPlacedAt = paged.First(n => Str(n, "id") == ids[2]).GetProperty("placedAt").GetString();
        (await Filter(new { placedFrom = thirdPlacedAt })).GetProperty("totalCount").GetInt32().Should().Be(3);
        (await Filter(new { placedTo = thirdPlacedAt })).GetProperty("totalCount").GetInt32().Should().Be(3);
        (await Filter(new { placedFrom = "2000-01-01T00:00:00Z", placedTo = "2000-01-02T00:00:00Z" })).GetProperty("totalCount").GetInt32().Should().Be(0);
    }

    [DockerFact]
    public async Task Orders_first_is_capped_at_100_and_an_invalid_cursor_is_a_coded_error()
    {
        using var api = new ApiHost(mongo);
        using (var scope = api.Services.CreateScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<Shop.Domain.Ordering.IOrderRepository>();
            var clock = scope.ServiceProvider.GetRequiredService<Shop.Domain.SharedKernel.IClock>();
            for (var i = 0; i < 101; i++)
            {
                await repo.Add(Shop.Domain.Ordering.Order.Place(
                    Shop.Domain.Ordering.CustomerInfo.Create("Ada", "ada@example.com"),
                    Shop.Domain.Ordering.Address.Create("1 Main St", null, "London", null, "N1", "GB"),
                    [Shop.Domain.Ordering.OrderLine.Create(Shop.Domain.Catalog.ProductId.New(), Shop.Domain.Catalog.Sku.Create("CAP-001"), "Cap", Shop.Domain.SharedKernel.Money.Of(1m), 1)],
                    clock));
            }
        }

        var capped = (await api.Post("{ orders(first: 1000) { totalCount edges { cursor } pageInfo { hasNextPage } } }")).GetProperty("data").GetProperty("orders");
        capped.GetProperty("totalCount").GetInt32().Should().Be(101);
        capped.GetProperty("edges").GetArrayLength().Should().Be(100);
        capped.GetProperty("pageInfo").GetProperty("hasNextPage").GetBoolean().Should().BeTrue();

        var bad = await api.Post("{ orders(after: \"garbage\") { totalCount } }");
        bad.GetProperty("errors")[0].GetProperty("extensions").GetProperty("code").GetString().Should().Be("VALIDATION_FAILED");
    }

    // ---- Duplicate order number retry against real Mongo ----

    private sealed class ScriptedNumbers(params string[] numbers) : Shop.Domain.Ordering.IOrderNumberGenerator
    {
        private readonly Queue<string> _queue = new(numbers);

        public string Next(DateTimeOffset now) =>
            _queue.Count > 0 ? _queue.Dequeue() : Shop.Domain.Ordering.RandomOrderNumberGenerator.Instance.Next(now);
    }

    [DockerFact]
    public async Task Colliding_order_number_restarts_the_transaction_and_places_one_order_with_a_new_number()
    {
        var numbers = new ScriptedNumbers("ORD-20260101-AAAAAA", "ORD-20260101-AAAAAA", "ORD-20260101-BBBBBB");
        using var api = new ApiHost(mongo, services =>
        {
            services.RemoveAll<Shop.Domain.Ordering.IOrderNumberGenerator>();
            services.AddSingleton<Shop.Domain.Ordering.IOrderNumberGenerator>(numbers);
        });
        var p = await Product(api, "DUP-001", 10m, 10);
        var first = (await api.PlaceOrder((p, 1))).GetProperty("order");
        Str(first, "orderNumber").Should().Be("ORD-20260101-AAAAAA");

        var second = (await api.PlaceOrder((p, 2))).GetProperty("order");

        Str(second, "orderNumber").Should().Be("ORD-20260101-BBBBBB", "the first attempt collided and the transaction was restarted");
        (await api.OrderCountInDb()).Should().Be(2, "no order from the aborted attempt");
        (await api.StockInDb(p)).Should().Be(7, "the aborted attempt's stock decrease was rolled back");
    }

    // ---- Cursor ties and offsets against real Mongo ----

    private sealed class FixedClock(DateTimeOffset now) : Shop.Domain.SharedKernel.IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private static async Task<List<string>> SeedOrders(ApiHost api, DateTimeOffset placedAt, int count)
    {
        using var scope = api.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<Shop.Domain.Ordering.IOrderRepository>();
        var ids = new List<string>();
        for (var i = 0; i < count; i++)
        {
            var order = Shop.Domain.Ordering.Order.Place(
                Shop.Domain.Ordering.CustomerInfo.Create("Ada", "ada@example.com"),
                Shop.Domain.Ordering.Address.Create("1 Main St", null, "London", null, "N1", "GB"),
                [Shop.Domain.Ordering.OrderLine.Create(Shop.Domain.Catalog.ProductId.New(), Shop.Domain.Catalog.Sku.Create("TIE-001"), "Tie", Shop.Domain.SharedKernel.Money.Of(1m), 1)],
                new FixedClock(placedAt));
            await repo.Add(order);
            ids.Add(order.Id.ToString());
        }

        return ids;
    }

    [DockerFact]
    public async Task Cursor_paging_is_stable_when_many_orders_share_the_same_placedAt()
    {
        using var api = new ApiHost(mongo);
        var ids = await SeedOrders(api, new DateTimeOffset(2026, 3, 1, 10, 0, 0, TimeSpan.Zero), 7);
        // Mongo orders UUIDs (standard binary) bytewise; the tiebreak is _id descending.
        var expected = ids.OrderByDescending(i => Guid.Parse(i).ToByteArray(bigEndian: true), Comparer<byte[]>.Create(
            (a, b) => a.AsSpan().SequenceCompareTo(b))).ToList();

        const string list = "query($after: String) { orders(first: 2, after: $after) { totalCount pageInfo { hasNextPage endCursor } edges { node { id } } } }";
        var seen = new List<string>();
        string? after = null;
        for (var guard = 0; guard < 10; guard++)
        {
            var page = (await api.Post(list, new { after })).GetProperty("data").GetProperty("orders");
            page.GetProperty("totalCount").GetInt32().Should().Be(7);
            seen.AddRange(page.GetProperty("edges").EnumerateArray().Select(e => Str(e.GetProperty("node"), "id")));
            if (!page.GetProperty("pageInfo").GetProperty("hasNextPage").GetBoolean())
            {
                break;
            }

            after = Str(page.GetProperty("pageInfo"), "endCursor");
        }

        seen.Should().OnlyHaveUniqueItems("no duplicates across pages");
        seen.Should().BeEquivalentTo(ids, "no gaps");
        seen.Should().Equal(expected, "ties are ordered by id descending, identically on every run");
    }

    [DockerFact]
    public async Task PlacedFrom_with_a_non_utc_offset_is_compared_as_the_same_instant()
    {
        using var api = new ApiHost(mongo);
        await SeedOrders(api, new DateTimeOffset(2026, 3, 1, 10, 0, 0, TimeSpan.Zero), 1);
        const string q = "query($f: DateTime!) { orders(filter: { placedFrom: $f }) { totalCount } }";

        var exact = await api.Post(q, new { f = "2026-03-01T17:00:00+07:00" });
        var later = await api.Post(q, new { f = "2026-03-01T17:00:01+07:00" });

        exact.GetProperty("data").GetProperty("orders").GetProperty("totalCount").GetInt32().Should().Be(1, "17:00+07:00 is 10:00Z, inclusive");
        later.GetProperty("data").GetProperty("orders").GetProperty("totalCount").GetInt32().Should().Be(0);
    }
}
