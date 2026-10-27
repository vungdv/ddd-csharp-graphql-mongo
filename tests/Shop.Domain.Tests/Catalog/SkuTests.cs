using FluentAssertions;
using Shop.Domain.Catalog;
using Shop.Domain.SharedKernel;

namespace Shop.Domain.Tests.Catalog;

public class SkuTests
{
    [Theory]
    [InlineData("ABC")]
    [InlineData("TSHIRT-001")]
    [InlineData("A1-B2-C3")]
    public void Accepts_valid(string value) => Sku.Create(value).Value.Should().Be(value);

    [Fact]
    public void Accepts_max_length() => Sku.Create(new string('A', 32)).Value.Should().HaveLength(32);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("AB")]
    [InlineData("abc")]
    [InlineData("AB_C")]
    [InlineData("AB C")]
    [InlineData("ABC\n")]
    public void Rejects_invalid(string? value)
    {
        var act = () => Sku.Create(value);
        var ex = act.Should().Throw<DomainException>().Which;
        ex.Code.Should().Be("VALIDATION_FAILED");
        ex.Field.Should().Be("sku");
    }

    [Fact]
    public void Rejects_too_long()
    {
        var act = () => Sku.Create(new string('A', 33));
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Equal_by_value() => Sku.Create("ABC").Should().Be(Sku.Create("ABC"));
}
