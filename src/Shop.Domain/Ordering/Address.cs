using Shop.Domain.SharedKernel;

namespace Shop.Domain.Ordering;

/// <summary>Shipping address. Line1, City, PostalCode and Country are required; Line2 and State are optional.</summary>
public sealed record Address
{
    public const int MaxFieldLength = 200;

    public string Line1 { get; }

    public string? Line2 { get; }

    public string City { get; }

    public string? State { get; }

    public string PostalCode { get; }

    public string Country { get; }

    private Address(string line1, string? line2, string city, string? state, string postalCode, string country)
    {
        Line1 = line1;
        Line2 = line2;
        City = city;
        State = state;
        PostalCode = postalCode;
        Country = country;
    }

    public static Address Create(
        string? line1,
        string? line2,
        string? city,
        string? state,
        string? postalCode,
        string? country) =>
        new(
            Required(line1, "shippingAddress.line1"),
            Optional(line2, "shippingAddress.line2"),
            Required(city, "shippingAddress.city"),
            Optional(state, "shippingAddress.state"),
            Required(postalCode, "shippingAddress.postalCode"),
            Required(country, "shippingAddress.country"));

    private static string Required(string? value, string field) =>
        string.IsNullOrWhiteSpace(value) || value.Trim().Length > MaxFieldLength
            ? throw DomainException.Validation(field, $"{field} is required and at most {MaxFieldLength} characters.")
            : value.Trim();

    private static string? Optional(string? value, string field) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().Length > MaxFieldLength
                ? throw DomainException.Validation(field, $"{field} must be at most {MaxFieldLength} characters.")
                : value.Trim();
}
