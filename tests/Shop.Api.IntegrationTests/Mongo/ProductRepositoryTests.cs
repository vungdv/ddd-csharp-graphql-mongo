using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;
using Shop.Domain.Catalog;
using Shop.Domain.SharedKernel;
using Shop.Infrastructure.Catalog;
using Shop.Infrastructure.Mongo;

namespace Shop.Api.IntegrationTests.Mongo;

[Collection(MongoCollection.Name)]
public class ProductRepositoryTests(MongoFixture fixture)
{
    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 3, 1, 10, 0, 0, 123, TimeSpan.Zero);
    }

    private readonly FixedClock _clock = new();

    private Product NewProduct(string sku = "TSHIRT-001", int stock = 5) =>
        Product.Create(Sku.Create(sku), "T-Shirt", "Cotton tee", Money.Of(19.99m, "USD"), "Apparel", stock, _clock);

    private async Task<(IMongoDatabase Db, ProductRepository Repo)> Arrange()
    {
        var db = fixture.NewDatabase();
        await MongoIndexInitializer.EnsureIndexes(db);
        return (db, new ProductRepository(db));
    }

    [DockerFact]
    public async Task Round_trips_product()
    {
        var (_, repo) = await Arrange();
        var product = NewProduct();
        product.Deactivate(_clock);
        await repo.Add(product);

        var loaded = await repo.GetById(product.Id);

        loaded.Should().NotBeNull();
        loaded!.Id.Should().Be(product.Id);
        loaded.Sku.Should().Be(product.Sku);
        loaded.Name.Should().Be("T-Shirt");
        loaded.Description.Should().Be("Cotton tee");
        loaded.Price.Should().Be(Money.Of(19.99m, "USD"));
        loaded.Category.Should().Be("Apparel");
        loaded.StockQuantity.Should().Be(5);
        loaded.IsActive.Should().BeFalse();
        loaded.CreatedAt.Should().Be(product.CreatedAt);
        loaded.UpdatedAt.Should().Be(product.UpdatedAt);
        loaded.Version.Should().Be(0);
        loaded.DomainEvents.Should().BeEmpty();
    }

    [DockerFact]
    public async Task Stores_documents_in_the_specified_shape()
    {
        var (db, repo) = await Arrange();
        var product = NewProduct();
        await repo.Add(product);

        var doc = await db.GetCollection<BsonDocument>("products").Find(FilterDefinition<BsonDocument>.Empty).SingleAsync();

        doc["_id"].BsonType.Should().Be(BsonType.Binary);
        doc["_id"].AsBsonBinaryData.SubType.Should().Be(BsonBinarySubType.UuidStandard);
        doc["sku"].AsString.Should().Be("TSHIRT-001");
        doc["price"]["amount"].BsonType.Should().Be(BsonType.Decimal128);
        doc["price"]["amount"].AsDecimal.Should().Be(19.99m);
        doc["price"]["currency"].AsString.Should().Be("USD");
        doc["createdAt"].BsonType.Should().Be(BsonType.DateTime);
        doc["version"].AsInt32.Should().Be(0);
        doc.Contains("domainEvents").Should().BeFalse();
    }

    [DockerFact]
    public async Task Finds_by_sku_ids_and_existence()
    {
        var (_, repo) = await Arrange();
        var a = NewProduct("AAA-1");
        var b = NewProduct("BBB-1");
        await repo.Add(a);
        await repo.Add(b);

        (await repo.GetBySku(Sku.Create("AAA-1")))!.Id.Should().Be(a.Id);
        (await repo.GetBySku(Sku.Create("ZZZ-1"))).Should().BeNull();
        (await repo.ExistsBySku(Sku.Create("BBB-1"))).Should().BeTrue();
        (await repo.ExistsBySku(Sku.Create("ZZZ-1"))).Should().BeFalse();
        (await repo.GetById(ProductId.New())).Should().BeNull();

        var many = await repo.GetByIds([a.Id, b.Id, a.Id, ProductId.New()]);
        many.Select(p => p.Id).Should().BeEquivalentTo([a.Id, b.Id]);
        (await repo.GetByIds([])).Should().BeEmpty();
    }

    [DockerFact]
    public async Task Duplicate_sku_is_rejected_by_unique_index()
    {
        var (_, repo) = await Arrange();
        await repo.Add(NewProduct("DUP-1"));

        var act = () => repo.Add(NewProduct("DUP-1"));

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("PRODUCT_SKU_ALREADY_EXISTS");
    }

    [DockerFact]
    public async Task Update_persists_changes_and_increments_version()
    {
        var (_, repo) = await Arrange();
        var product = NewProduct();
        await repo.Add(product);

        var loaded = (await repo.GetById(product.Id))!;
        loaded.ChangePrice(Money.Of(25m), _clock);
        loaded.DecreaseStock(2, _clock);
        await repo.Update(loaded);

        loaded.Version.Should().Be(1);
        var reloaded = (await repo.GetById(product.Id))!;
        reloaded.Version.Should().Be(1);
        reloaded.DomainEvents.Should().BeEmpty("rehydrated aggregates carry no events");
        reloaded.Price.Should().Be(Money.Of(25m));
        reloaded.StockQuantity.Should().Be(3);

        reloaded.Rename("Tee", _clock);
        await repo.Update(reloaded);
        (await repo.GetById(product.Id))!.Version.Should().Be(2);
    }

    [DockerFact]
    public async Task Stale_update_raises_concurrency_conflict_and_keeps_stored_data()
    {
        var (_, repo) = await Arrange();
        var product = NewProduct();
        await repo.Add(product);

        var first = (await repo.GetById(product.Id))!;
        var second = (await repo.GetById(product.Id))!;
        first.DecreaseStock(1, _clock);
        await repo.Update(first);

        second.DecreaseStock(2, _clock);
        var act = () => repo.Update(second);

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("CONCURRENCY_CONFLICT");
        second.Version.Should().Be(0, "the in-memory version is restored on failure");
        var stored = (await repo.GetById(product.Id))!;
        stored.StockQuantity.Should().Be(4);
        stored.Version.Should().Be(1);
    }

    [DockerFact]
    public async Task Update_of_missing_product_is_a_conflict()
    {
        var (_, repo) = await Arrange();
        var act = () => repo.Update(NewProduct());
        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("CONCURRENCY_CONFLICT");
    }

    [DockerFact]
    public async Task Index_initializer_is_idempotent_and_creates_expected_indexes()
    {
        var (db, _) = await Arrange();
        await MongoIndexInitializer.EnsureIndexes(db);
        await MongoIndexInitializer.EnsureIndexes(db);

        var indexes = await (await db.GetCollection<BsonDocument>("products").Indexes.ListAsync()).ToListAsync();

        var byName = indexes.ToDictionary(i => i["name"].AsString);
        byName.Keys.Should().Contain(["ux_products_sku", "ix_products_category_isActive_en", "tx_products_name_description"]);
        byName["ux_products_sku"]["unique"].AsBoolean.Should().BeTrue();
        byName["ix_products_category_isActive_en"]["key"].AsBsonDocument.Names.Should().Equal("category", "isActive");
        byName["ix_products_category_isActive_en"]["collation"]["locale"].AsString.Should().Be("en");
        byName["ix_products_category_isActive_en"]["collation"]["strength"].AsInt32.Should().Be(2);
        byName.Keys.Should().NotContain("ix_products_category_isActive");
        byName["tx_products_name_description"]["weights"].AsBsonDocument.Names.Should().BeEquivalentTo("name", "description");

        // Sort-supporting indexes carry the same collation as ProductReadService's list queries (en, strength 2).
        foreach (var (name, first) in new[]
                 {
                     ("ix_products_name__id", "name"),
                     ("ix_products_price_amount__id", "price.amount"),
                     ("ix_products_createdAt__id", "createdAt"),
                 })
        {
            byName.Should().ContainKey(name);
            byName[name]["key"].AsBsonDocument.Names.Should().Equal(first, "_id");
            byName[name]["collation"]["locale"].AsString.Should().Be("en");
            byName[name]["collation"]["strength"].AsInt32.Should().Be(2);
        }
    }

    [DockerTheory]
    [InlineData("name", "ix_products_name__id")]
    [InlineData("price.amount", "ix_products_price_amount__id")]
    [InlineData("createdAt", "ix_products_createdAt__id")]
    public async Task Collated_sorted_list_queries_are_served_by_the_sort_indexes_without_in_memory_sort(string sortPath, string indexName)
    {
        var (db, _) = await Arrange();
        await MongoIndexInitializer.EnsureIndexes(db);

        var explain = await db.RunCommandAsync<BsonDocument>(new BsonDocument
        {
            {
                "explain", new BsonDocument
                {
                    { "find", "products" },
                    { "sort", new BsonDocument { { sortPath, 1 }, { "_id", 1 } } },
                    { "limit", 20 },
                    { "collation", new BsonDocument { { "locale", "en" }, { "strength", 2 } } },
                }
            },
            { "verbosity", "queryPlanner" },
        });

        var plan = explain["queryPlanner"]["winningPlan"].ToJson();
        plan.Should().Contain("IXSCAN").And.Contain(indexName).And.NotContain("\"SORT\"");
    }

    [DockerFact]
    public async Task Legacy_uncollated_category_index_is_dropped_and_replaced()
    {
        var (db, _) = await Arrange();
        var products = db.GetCollection<BsonDocument>("products");
        await products.Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(
            new BsonDocument { { "category", 1 }, { "isActive", 1 } },
            new CreateIndexOptions { Name = "ix_products_category_isActive" }));

        await MongoIndexInitializer.EnsureIndexes(db);

        var names = (await (await products.Indexes.ListAsync()).ToListAsync()).Select(i => i["name"].AsString).ToList();
        names.Should().Contain("ix_products_category_isActive_en").And.NotContain("ix_products_category_isActive");
    }

    [DockerFact]
    public async Task Collated_category_and_active_filter_is_served_by_the_category_index()
    {
        var (db, _) = await Arrange();
        await MongoIndexInitializer.EnsureIndexes(db);

        var explain = await db.RunCommandAsync<BsonDocument>(new BsonDocument
        {
            {
                "explain", new BsonDocument
                {
                    { "find", "products" },
                    { "filter", new BsonDocument { { "category", "Apparel" }, { "isActive", true } } },
                    { "collation", new BsonDocument { { "locale", "en" }, { "strength", 2 } } },
                }
            },
            { "verbosity", "queryPlanner" },
        });

        var plan = explain["queryPlanner"]["winningPlan"].ToJson();
        plan.Should().Contain("IXSCAN").And.Contain("ix_products_category_isActive_en");
    }
}
