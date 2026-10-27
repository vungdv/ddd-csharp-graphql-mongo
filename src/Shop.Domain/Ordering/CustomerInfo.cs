using System.Text.RegularExpressions;
using Shop.Domain.SharedKernel;

namespace Shop.Domain.Ordering;

/// <summary>Customer name and email. Email is trimmed and lower-cased so equality filters on it are exact.</summary>
public sealed partial record CustomerInfo
{
    public const int MaxNameLength = 200;
    public const int MaxEmailLength = 254;

    public string Name { get; }

    public string Email { get; }

    private CustomerInfo(string name, string email)
    {
        Name = name;
        Email = email;
    }

    public static CustomerInfo Create(string? name, string? email)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > MaxNameLength)
        {
            throw DomainException.Validation("customer.name", $"Customer name is required and at most {MaxNameLength} characters.");
        }

        var normalized = email?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(normalized) || normalized.Length > MaxEmailLength || !EmailPattern().IsMatch(normalized))
        {
            throw DomainException.Validation("customer.email", "Customer email is required and must be a valid email address.");
        }

        return new CustomerInfo(name.Trim(), normalized);
    }

    [GeneratedRegex(@"^[^@\s]+@[^@\s.]+(\.[^@\s.]+)+$", RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();
}
