using System.Globalization;

namespace Shop.Domain.SharedKernel;

/// <summary>Non-negative monetary amount with at most 2 decimal places and an ISO 4217 currency.</summary>
public sealed record Money
{
    public const string DefaultCurrency = "USD";

    public decimal Amount { get; }

    public string Currency { get; }

    private Money(decimal amount, string currency)
    {
        if (amount < 0)
        {
            throw new DomainException(DomainErrorCodes.InvalidMoney, "Amount must not be negative.");
        }

        if (decimal.Round(amount, 2) != amount)
        {
            throw new DomainException(DomainErrorCodes.InvalidMoney, "Amount must have at most 2 decimal places.");
        }

        var normalized = currency?.Trim().ToUpperInvariant();
        if (normalized is not { Length: 3 } || !normalized.All(c => c is >= 'A' and <= 'Z'))
        {
            throw new DomainException(DomainErrorCodes.InvalidMoney, "Currency must be a 3-letter ISO 4217 code.");
        }

        Amount = amount;
        Currency = normalized;
    }

    public static Money Of(decimal amount, string currency = DefaultCurrency) => new(amount, currency);

    public Money Add(Money other)
    {
        ArgumentNullException.ThrowIfNull(other);
        EnsureSameCurrency(other);
        return new Money(Amount + other.Amount, Currency);
    }

    public Money Multiply(int quantity)
    {
        if (quantity < 0)
        {
            throw new DomainException(DomainErrorCodes.InvalidMoney, "Quantity must not be negative.");
        }

        return new Money(Amount * quantity, Currency);
    }

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Amount:0.00} {Currency}");

    private void EnsureSameCurrency(Money other)
    {
        if (!string.Equals(Currency, other.Currency, StringComparison.Ordinal))
        {
            throw new DomainException(
                DomainErrorCodes.CurrencyMismatch,
                $"Cannot combine {Currency} with {other.Currency}.");
        }
    }
}
