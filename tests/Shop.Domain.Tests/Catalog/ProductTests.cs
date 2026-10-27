using FluentAssertions;
using Shop.Domain.Catalog;
using Shop.Domain.SharedKernel;

namespace Shop.Domain.Tests.Catalog;

public class ProductTests
{
    private readonly TestClock _clock = new();

    private Product NewProduct(int stock = 10, decimal price = 19.99m) =>
        Product.Create(Sku.Create("TSHIRT-001"), "T-Shirt", "Cotton", Money.Of(price), "Apparel", stock, _clock);

    private static string CodeOf(Action act) => act.Should().Throw<DomainException>().Which.Code;

    [Fact]
    public void Create_sets_state_and_raises_event()
    {
        var p = NewProduct();

        p.Id.Value.Should().NotBe(Guid.Empty);
        p.Sku.Value.Should().Be("TSHIRT-001");
        p.Name.Should().Be("T-Shirt");
        p.Description.Should().Be("Cotton");
        p.Price.Should().Be(Money.Of(19.99m));
        p.Category.Should().Be("Apparel");
        p.StockQuantity.Should().Be(10);
        p.IsActive.Should().BeTrue();
        p.CreatedAt.Should().Be(_clock.UtcNow);
        p.UpdatedAt.Should().Be(_clock.UtcNow);
        p.Version.Should().Be(0);
        p.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<ProductCreated>()
            .Which.ProductId.Should().Be(p.Id);
    }

    [Fact]
    public void Create_rejects_zero_price() =>
        CodeOf(() => NewProduct(price: 0m)).Should().Be("INVALID_MONEY");

    [Fact]
    public void Create_rejects_negative_stock() =>
        CodeOf(() => NewProduct(stock: -1)).Should().Be("VALIDATION_FAILED");

    [Theory]
    [InlineData("", "cat")]
    [InlineData(" ", "cat")]
    [InlineData("name", "")]
    public void Create_rejects_blank_name_or_category(string name, string category)
    {
        var act = () => Product.Create(Sku.Create("ABC"), name, null, Money.Of(1m), category, 0, _clock);
        act.Should().Throw<DomainException>().Which.Code.Should().Be("VALIDATION_FAILED");
    }

    [Fact]
    public void Create_enforces_length_limits()
    {
        var sku = Sku.Create("ABC");
        var tooLongName = () => Product.Create(sku, new string('n', 201), null, Money.Of(1m), "c", 0, _clock);
        var tooLongDesc = () => Product.Create(sku, "n", new string('d', 2001), Money.Of(1m), "c", 0, _clock);
        var tooLongCat = () => Product.Create(sku, "n", null, Money.Of(1m), new string('c', 101), 0, _clock);
        tooLongName.Should().Throw<DomainException>();
        tooLongDesc.Should().Throw<DomainException>();
        tooLongCat.Should().Throw<DomainException>();
        Product.Create(sku, new string('n', 200), new string('d', 2000), Money.Of(1m), new string('c', 100), 0, _clock)
            .Should().NotBeNull();
    }

    [Fact]
    public void Rename_ChangeDescription_ChangeCategory_update_and_touch()
    {
        var p = NewProduct();
        _clock.Advance(TimeSpan.FromMinutes(5));

        p.Rename("Tee", _clock);
        p.ChangeDescription(null, _clock);
        p.ChangeCategory("Clothing", _clock);

        p.Name.Should().Be("Tee");
        p.Description.Should().BeNull();
        p.Category.Should().Be("Clothing");
        p.UpdatedAt.Should().Be(_clock.UtcNow);
        p.CreatedAt.Should().BeBefore(p.UpdatedAt);
    }

    [Fact]
    public void Rename_rejects_blank_and_keeps_name()
    {
        var p = NewProduct();
        CodeOf(() => p.Rename(" ", _clock)).Should().Be("VALIDATION_FAILED");
        p.Name.Should().Be("T-Shirt");
    }

    [Fact]
    public void ChangePrice_updates_and_raises_event()
    {
        var p = NewProduct();
        p.ClearDomainEvents();
        _clock.Advance(TimeSpan.FromHours(1));

        p.ChangePrice(Money.Of(25m), _clock);

        p.Price.Should().Be(Money.Of(25m));
        p.UpdatedAt.Should().Be(_clock.UtcNow);
        var e = p.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<ProductPriceChanged>().Subject;
        e.OldPrice.Should().Be(Money.Of(19.99m));
        e.NewPrice.Should().Be(Money.Of(25m));
        e.ProductId.Should().Be(p.Id);
    }

    [Fact]
    public void ChangePrice_to_same_price_is_a_noop()
    {
        var p = NewProduct();
        p.ClearDomainEvents();
        var updated = p.UpdatedAt;
        _clock.Advance(TimeSpan.FromHours(1));

        p.ChangePrice(Money.Of(19.99m), _clock);

        p.DomainEvents.Should().BeEmpty();
        p.UpdatedAt.Should().Be(updated);
    }

    [Fact]
    public void ChangePrice_rejects_zero_and_keeps_price()
    {
        var p = NewProduct();
        p.ClearDomainEvents();
        CodeOf(() => p.ChangePrice(Money.Of(0m), _clock)).Should().Be("INVALID_MONEY");
        p.Price.Should().Be(Money.Of(19.99m));
        p.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Deactivate_then_Activate_toggle()
    {
        var p = NewProduct();
        _clock.Advance(TimeSpan.FromMinutes(1));
        p.Deactivate(_clock);
        p.IsActive.Should().BeFalse();
        p.UpdatedAt.Should().Be(_clock.UtcNow);

        _clock.Advance(TimeSpan.FromMinutes(1));
        p.Activate(_clock);
        p.IsActive.Should().BeTrue();
        p.UpdatedAt.Should().Be(_clock.UtcNow);
    }

    [Fact]
    public void Activate_and_Deactivate_are_idempotent()
    {
        var p = NewProduct();
        var created = p.UpdatedAt;
        _clock.Advance(TimeSpan.FromMinutes(1));

        p.Activate(_clock);
        p.IsActive.Should().BeTrue();
        p.UpdatedAt.Should().Be(created, "no change means no touch");

        p.Deactivate(_clock);
        var afterDeactivate = p.UpdatedAt;
        _clock.Advance(TimeSpan.FromMinutes(1));
        p.Deactivate(_clock);
        p.IsActive.Should().BeFalse();
        p.UpdatedAt.Should().Be(afterDeactivate);
    }

    [Theory]
    [InlineData(5, 15)]
    [InlineData(-10, 0)]
    [InlineData(0, 10)]
    public void AdjustStock_applies_delta(int delta, int expected)
    {
        var p = NewProduct(10);
        p.AdjustStock(delta, _clock);
        p.StockQuantity.Should().Be(expected);
    }

    [Fact]
    public void AdjustStock_below_zero_fails_and_leaves_stock()
    {
        var p = NewProduct(10);
        CodeOf(() => p.AdjustStock(-11, _clock)).Should().Be("INSUFFICIENT_STOCK");
        p.StockQuantity.Should().Be(10);
    }

    [Fact]
    public void AdjustStock_overflow_is_rejected()
    {
        var p = NewProduct(int.MaxValue);
        CodeOf(() => p.AdjustStock(1, _clock)).Should().Be("VALIDATION_FAILED");
        p.StockQuantity.Should().Be(int.MaxValue);
    }

    [Fact]
    public void DecreaseStock_reduces_quantity_and_touches()
    {
        var p = NewProduct(10);
        _clock.Advance(TimeSpan.FromMinutes(1));
        p.DecreaseStock(4, _clock);
        p.StockQuantity.Should().Be(6);
        p.UpdatedAt.Should().Be(_clock.UtcNow);
    }

    [Fact]
    public void DecreaseStock_exact_stock_is_allowed()
    {
        var p = NewProduct(3);
        p.DecreaseStock(3, _clock);
        p.StockQuantity.Should().Be(0);
    }

    [Fact]
    public void DecreaseStock_more_than_available_fails_and_leaves_stock()
    {
        var p = NewProduct(3);
        CodeOf(() => p.DecreaseStock(4, _clock)).Should().Be("INSUFFICIENT_STOCK");
        p.StockQuantity.Should().Be(3);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void DecreaseStock_requires_positive_quantity(int qty)
    {
        var p = NewProduct(3);
        CodeOf(() => p.DecreaseStock(qty, _clock)).Should().Be("VALIDATION_FAILED");
        p.StockQuantity.Should().Be(3);
    }
}
