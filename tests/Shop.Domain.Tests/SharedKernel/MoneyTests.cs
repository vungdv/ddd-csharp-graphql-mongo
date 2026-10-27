using FluentAssertions;
using Shop.Domain.SharedKernel;

namespace Shop.Domain.Tests.SharedKernel;

public class MoneyTests
{
    [Fact]
    public void Defaults_to_USD() => Money.Of(1m).Currency.Should().Be("USD");

    [Fact]
    public void Normalizes_currency_to_upper_case() => Money.Of(1m, "eur").Currency.Should().Be("EUR");

    [Fact]
    public void Allows_zero() => Money.Of(0m).Amount.Should().Be(0m);

    [Theory]
    [InlineData("-0.01")]
    [InlineData("1.001")]
    public void Rejects_invalid_amounts(string amount)
    {
        var act = () => Money.Of(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture));
        act.Should().Throw<DomainException>().Which.Code.Should().Be("INVALID_MONEY");
    }

    [Theory]
    [InlineData("")]
    [InlineData("US")]
    [InlineData("USDD")]
    [InlineData("U1D")]
    public void Rejects_invalid_currency(string currency)
    {
        var act = () => Money.Of(1m, currency);
        act.Should().Throw<DomainException>().Which.Code.Should().Be("INVALID_MONEY");
    }

    [Fact]
    public void Adds_same_currency() =>
        Money.Of(1.10m).Add(Money.Of(2.25m)).Should().Be(Money.Of(3.35m));

    [Fact]
    public void Add_across_currencies_throws()
    {
        var act = () => Money.Of(1m, "USD").Add(Money.Of(1m, "EUR"));
        act.Should().Throw<DomainException>().Which.Code.Should().Be("CURRENCY_MISMATCH");
    }

    [Fact]
    public void Multiplies_by_quantity() =>
        Money.Of(19.99m).Multiply(3).Should().Be(Money.Of(59.97m));

    [Fact]
    public void Multiply_by_negative_throws()
    {
        var act = () => Money.Of(1m).Multiply(-1);
        act.Should().Throw<DomainException>().Which.Code.Should().Be("INVALID_MONEY");
    }

    [Fact]
    public void Equality_ignores_trailing_zeros() => Money.Of(19.990m).Should().Be(Money.Of(19.99m));
}
