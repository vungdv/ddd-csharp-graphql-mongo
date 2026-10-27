using System.Text.RegularExpressions;
using Shop.Domain.SharedKernel;

namespace Shop.Domain.Catalog;

/// <summary>Stock keeping unit: 3-32 characters of [A-Z0-9-].</summary>
public sealed partial record Sku
{
    public string Value { get; }

    private Sku(string value) => Value = value;

    public static Sku Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !SkuPattern().IsMatch(value))
        {
            throw DomainException.Validation("sku", "SKU must be 3-32 characters of A-Z, 0-9 or '-'.");
        }

        return new Sku(value);
    }

    public override string ToString() => Value;

    [GeneratedRegex("^[A-Z0-9-]{3,32}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex SkuPattern();
}
