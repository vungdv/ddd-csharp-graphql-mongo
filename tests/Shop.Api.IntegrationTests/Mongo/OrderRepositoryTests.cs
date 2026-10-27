using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;
using Shop.Domain.Catalog;
using Shop.Domain.Ordering;
using Shop.Domain.SharedKernel;
using Shop.Infrastructure.Mongo;
using Shop.Infrastructure.Ordering;

namespace Shop.Api.IntegrationTests.Mongo;

[Collection(MongoCollection.Name)]
public class OrderRepositoryTests(MongoFixture fixture)
{
    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 3, 1, 10, 0, 0, 123, TimeSpan.Zero);
    }

    private sealed class FixedNumber(string number) : IOrderNumberGenerator
    {
        public string Next(DateTimeOffset now) => number;
    }

    private readonly FixedClock _clock = new();

    private Order NewOrder(string? number = null) =>
        Order.Place(
            CustomerInfo.Create("Ada Lovelace", "Ada@Example.com"),
            Address.Create("1 Main St", "Flat 2", "London", null, "N1 1AA", "GB"),
            [
                OrderLine.Create(ProductId.New(), Sku.Create("TSHIRT-001"), "T-Shirt", Money.Of(19.99m), 2),
                OrderLine.Create(ProductId.New(), Sku.Create("MUG-001"), "Mug", Money.Of(5.50m), 3),
            ],
            _clock,
            new FixedNumber(number ?? "ABCDEF"));

    private async Task<(IMongoDatabase Db, OrderRepository Repo)> Arrange()
    {
        var db = fixture.NewDatabase();
        await MongoIndexInitializer.EnsureIndexes(db);
        return (db, new OrderRepository(db));
    }

    [DockerFact]
    public async Task Round_trips_order_with_embedded_lines()
    {
        var (_, repo) = await Arrange();
        var order = NewOrder();
        await repo.Add(order);

        var loaded = (await repo.GetById(order.Id))!;

        loaded.Id.Should().Be(order.Id);
        loaded.OrderNumber.Should().Be(order.OrderNumber);
        loaded.Customer.Should().Be(order.Customer);
        loaded.Customer.Email.Should().Be("ada@example.com");
        loaded.ShippingAddress.Should().Be(order.ShippingAddress);
        loaded.Status.Should().Be(OrderStatus.Placed);
        loaded.Total.Should().Be(Money.Of(56.48m));
        loaded.PlacedAt.Should().Be(order.PlacedAt);
        loaded.CancelledAt.Should().BeNull();
        loaded.Version.Should().Be(0);
        loaded.Lines.Should().HaveCount(2);
        for (var i = 0; i < 2; i++)
        {
            loaded.Lines[i].ProductId.Should().Be(order.Lines[i].ProductId);
            loaded.Lines[i].Sku.Should().Be(order.Lines[i].Sku);
            loaded.Lines[i].ProductName.Should().Be(order.Lines[i].ProductName);
            loaded.Lines[i].UnitPrice.Should().Be(order.Lines[i].UnitPrice);
            loaded.Lines[i].Quantity.Should().Be(order.Lines[i].Quantity);
            loaded.Lines[i].LineTotal.Should().Be(order.Lines[i].LineTotal);
        }
    }

    [DockerFact]
    public async Task Stores_documented_bson_shape()
    {
        var (db, repo) = await Arrange();
        var order = NewOrder();
        await repo.Add(order);

        var doc = await db.GetCollection<BsonDocument>(OrderRepository.CollectionName).Find(new BsonDocument()).SingleAsync();

        doc["_id"].BsonType.Should().Be(BsonType.Binary);
        doc["_id"].AsBsonBinaryData.SubType.Should().Be(BsonBinarySubType.UuidStandard);
        doc["status"].Should().Be((BsonValue)"Placed");
        doc["orderNumber"].AsString.Should().Be(order.OrderNumber);
        doc["version"].AsInt32.Should().Be(0);
        doc["placedAt"].BsonType.Should().Be(BsonType.DateTime);
        doc["customer"].AsBsonDocument["email"].AsString.Should().Be("ada@example.com");
        doc["customer"].AsBsonDocument["name"].AsString.Should().Be("Ada Lovelace");
        doc["shippingAddress"].AsBsonDocument["postalCode"].AsString.Should().Be("N1 1AA");
        doc["total"].AsBsonDocument["amount"].BsonType.Should().Be(BsonType.Decimal128);
        doc["total"].AsBsonDocument["currency"].AsString.Should().Be("USD");
        var lines = doc["lines"].AsBsonArray;
        lines.Should().HaveCount(2);
        var line = lines[0].AsBsonDocument;
        line.Contains("_id").Should().BeFalse();
        line["productId"].AsBsonBinaryData.SubType.Should().Be(BsonBinarySubType.UuidStandard);
        line["unitPrice"].AsBsonDocument["amount"].BsonType.Should().Be(BsonType.Decimal128);
        line["lineTotal"].AsBsonDocument["amount"].ToDecimal().Should().Be(39.98m);
        line["quantity"].AsInt32.Should().Be(2);
        line["sku"].AsString.Should().Be("TSHIRT-001");
    }

    [DockerFact]
    public async Task Round_trips_null_optional_address_fields()
    {
        var (_, repo) = await Arrange();
        var order = Order.Place(
            CustomerInfo.Create("Ada", "ada@example.com"),
            Address.Create("1 Main St", null, "London", null, "N1 1AA", "GB"),
            [OrderLine.Create(ProductId.New(), Sku.Create("MUG-001"), "Mug", Money.Of(5m), 1)],
            _clock);
        await repo.Add(order);

        var loaded = (await repo.GetById(order.Id))!;

        loaded.ShippingAddress.Line2.Should().BeNull();
        loaded.ShippingAddress.State.Should().BeNull();
        loaded.ShippingAddress.Should().Be(order.ShippingAddress);
    }

    [DockerFact]
    public async Task Round_trips_cancelled_order_and_increments_version()
    {
        var (_, repo) = await Arrange();
        var order = NewOrder();
        await repo.Add(order);

        order.Cancel("changed mind", _clock);
        await repo.Update(order);

        order.Version.Should().Be(1);
        var loaded = (await repo.GetById(order.Id))!;
        loaded.Status.Should().Be(OrderStatus.Cancelled);
        loaded.CancelledAt.Should().Be(order.CancelledAt);
        loaded.CancellationReason.Should().Be("changed mind");
        loaded.Version.Should().Be(1);
    }

    [DockerFact]
    public async Task Gets_by_order_number_and_returns_null_when_missing()
    {
        var (_, repo) = await Arrange();
        var order = NewOrder("ORD-20260301-ZZZ999");
        await repo.Add(order);

        (await repo.GetByOrderNumber("ORD-20260301-ZZZ999"))!.Id.Should().Be(order.Id);
        (await repo.GetByOrderNumber("ORD-20260301-NOPE00")).Should().BeNull();
        (await repo.GetById(OrderId.New())).Should().BeNull();
    }

    [DockerFact]
    public async Task Duplicate_order_number_is_reported_as_conflict()
    {
        var (_, repo) = await Arrange();
        await repo.Add(NewOrder("ORD-20260301-AAAAAA"));

        var act = () => repo.Add(NewOrder("ORD-20260301-AAAAAA"));

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(DomainErrorCodes.ConcurrencyConflict);
    }

    [DockerFact]
    public async Task Concurrent_update_yields_conflict_and_leaves_version_unchanged()
    {
        var (_, repo) = await Arrange();
        var order = NewOrder();
        await repo.Add(order);

        var first = (await repo.GetById(order.Id))!;
        var second = (await repo.GetById(order.Id))!;
        first.Cancel("a", _clock);
        await repo.Update(first);

        second.Cancel("b", _clock);
        var act = () => repo.Update(second);

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(DomainErrorCodes.ConcurrencyConflict);
        second.Version.Should().Be(0);
        (await repo.GetById(order.Id))!.CancellationReason.Should().Be("a");
    }

    [DockerFact]
    public async Task Update_of_missing_order_is_a_conflict()
    {
        var (_, repo) = await Arrange();
        var act = () => repo.Update(NewOrder());
        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(DomainErrorCodes.ConcurrencyConflict);
    }

    [DockerFact]
    public async Task Creates_order_indexes_idempotently()
    {
        var (db, _) = await Arrange();
        await MongoIndexInitializer.EnsureIndexes(db);

        var indexes = await (await db.GetCollection<BsonDocument>(OrderRepository.CollectionName).Indexes.ListAsync()).ToListAsync();
        var byName = indexes.ToDictionary(i => i["name"].AsString);

        byName.Keys.Should().Contain(["ux_orders_orderNumber", "ix_orders_customerEmail_placedAt", "ix_orders_status_placedAt"]);
        byName["ux_orders_orderNumber"]["unique"].AsBoolean.Should().BeTrue();
        byName["ux_orders_orderNumber"]["key"].AsBsonDocument.Should().Equal(new BsonDocument("orderNumber", 1));
        byName["ix_orders_customerEmail_placedAt"]["key"].AsBsonDocument.Should().Equal(new BsonDocument { { "customer.email", 1 }, { "placedAt", -1 } });
        byName["ix_orders_status_placedAt"]["key"].AsBsonDocument.Should().Equal(new BsonDocument { { "status", 1 }, { "placedAt", -1 } });
    }
}
