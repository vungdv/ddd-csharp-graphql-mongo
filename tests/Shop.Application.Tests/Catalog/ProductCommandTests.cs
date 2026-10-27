using FluentAssertions;
using Shop.Application.Catalog.Commands;
using Shop.Domain.Catalog;
using Shop.Domain.SharedKernel;

namespace Shop.Application.Tests.Catalog;

public class ProductCommandTests
{
    private readonly Harness _h = new();

    private static CreateProductCommand Create(string sku = "TSHIRT-001", decimal price = 19.99m, string currency = "USD") =>
        new(sku, "T-Shirt", "Tee", price, currency, "Apparel", 5);

    [Fact]
    public async Task Create_returns_active_product_dto_and_persists()
    {
        var result = await _h.Dispatcher.Send(Create());

        result.IsSuccess.Should().BeTrue();
        result.Value!.IsActive.Should().BeTrue();
        result.Value.Price.Should().BeEquivalentTo(new { Amount = 19.99m, Currency = "USD" });
        result.Value.StockQuantity.Should().Be(5);
        _h.Repo.Store.Should().ContainKey(new ProductId(result.Value.Id));
    }

    [Fact]
    public async Task Create_duplicate_sku_yields_PRODUCT_SKU_ALREADY_EXISTS()
    {
        await _h.Dispatcher.Send(Create());

        var result = await _h.Dispatcher.Send(Create());

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Code.Should().Be(DomainErrorCodes.ProductSkuAlreadyExists);
        _h.Repo.Store.Should().HaveCount(1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task Create_non_positive_price_yields_VALIDATION_FAILED_on_priceAmount(decimal price)
    {
        var result = await _h.Dispatcher.Send(Create(price: price));

        result.Errors.Should().Contain(e => e.Code == DomainErrorCodes.ValidationFailed && e.Field == "priceAmount");
        _h.Repo.Store.Should().BeEmpty();
    }

    [Fact]
    public async Task Create_reports_every_invalid_field()
    {
        var result = await _h.Dispatcher.Send(new CreateProductCommand("x", "", null, 1.234m, "US", "", -1));

        result.Errors.Select(e => e.Field).Should()
            .BeEquivalentTo("sku", "name", "priceAmount", "currency", "category", "initialStock");
        result.Errors.Should().OnlyContain(e => e.Code == DomainErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task Change_price_updates_and_keeps_currency_when_omitted()
    {
        var created = (await _h.Dispatcher.Send(Create(currency: "EUR"))).Value!;

        var result = await _h.Dispatcher.Send(new ChangeProductPriceCommand(created.Id, 25m, null));

        result.Value!.Price.Should().BeEquivalentTo(new { Amount = 25m, Currency = "EUR" });
        _h.Repo.UpdateCalls.Should().Be(1);
    }

    [Fact]
    public async Task Change_price_to_zero_fails_validation_without_loading()
    {
        var created = (await _h.Dispatcher.Send(Create())).Value!;

        var result = await _h.Dispatcher.Send(new ChangeProductPriceCommand(created.Id, 0m, null));

        result.Errors.Should().ContainSingle().Which.Field.Should().Be("amount");
        _h.Repo.UpdateCalls.Should().Be(0);
    }

    [Fact]
    public async Task Missing_product_yields_PRODUCT_NOT_FOUND()
    {
        var result = await _h.Dispatcher.Send(new SetProductActiveCommand(Guid.NewGuid(), false));

        result.Errors.Should().ContainSingle().Which.Code.Should().Be(DomainErrorCodes.ProductNotFound);
    }

    [Fact]
    public async Task Deactivate_then_reactivate()
    {
        var created = (await _h.Dispatcher.Send(Create())).Value!;

        var off = await _h.Dispatcher.Send(new SetProductActiveCommand(created.Id, false));
        var on = await _h.Dispatcher.Send(new SetProductActiveCommand(created.Id, true));

        off.Value!.IsActive.Should().BeFalse();
        on.Value!.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Update_changes_only_supplied_members()
    {
        var created = (await _h.Dispatcher.Send(Create())).Value!;

        var result = await _h.Dispatcher.Send(new UpdateProductCommand(created.Id, "New name", null, null));

        result.Value!.Name.Should().Be("New name");
        result.Value.Category.Should().Be("Apparel");
        result.Value.Description.Should().Be("Tee");
    }

    [Fact]
    public async Task Adjust_stock_below_zero_yields_INSUFFICIENT_STOCK_and_does_not_save()
    {
        var created = (await _h.Dispatcher.Send(Create())).Value!;

        var result = await _h.Dispatcher.Send(new AdjustProductStockCommand(created.Id, -6));

        result.Errors.Should().ContainSingle().Which.Code.Should().Be(DomainErrorCodes.InsufficientStock);
        _h.Repo.UpdateCalls.Should().Be(0);
    }

    [Fact]
    public async Task Adjust_stock_applies_signed_delta()
    {
        var created = (await _h.Dispatcher.Send(Create())).Value!;

        var result = await _h.Dispatcher.Send(new AdjustProductStockCommand(created.Id, -2));

        result.Value!.StockQuantity.Should().Be(3);
    }

    [Fact]
    public async Task Concurrency_conflicts_from_the_repository_map_to_a_coded_error()
    {
        var h = new Harness(inner => new ConflictingRepository(inner));
        var created = (await h.Dispatcher.Send(Create())).Value!;

        var result = await h.Dispatcher.Send(new SetProductActiveCommand(created.Id, false));

        result.Errors.Should().ContainSingle().Which.Code.Should().Be(DomainErrorCodes.ConcurrencyConflict);
    }

    private sealed class ConflictingRepository(InMemoryProductRepository inner) : IProductRepository
    {
        public Task<Product?> GetById(ProductId id, CancellationToken ct = default) => inner.GetById(id, ct);

        public Task<Product?> GetBySku(Sku sku, CancellationToken ct = default) => inner.GetBySku(sku, ct);

        public Task<IReadOnlyList<Product>> GetByIds(IEnumerable<ProductId> ids, CancellationToken ct = default) =>
            inner.GetByIds(ids, ct);

        public Task<bool> ExistsBySku(Sku sku, CancellationToken ct = default) => inner.ExistsBySku(sku, ct);

        public Task Add(Product product, CancellationToken ct = default) => inner.Add(product, ct);

        public Task Update(Product product, CancellationToken ct = default) =>
            throw new DomainException(DomainErrorCodes.ConcurrencyConflict, "conflict");
    }
}
