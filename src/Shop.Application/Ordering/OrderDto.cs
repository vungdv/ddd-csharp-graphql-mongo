using Shop.Application.Catalog;
using Shop.Domain.Ordering;

namespace Shop.Application.Ordering;

public sealed record CustomerDto(string Name, string Email);

public sealed record AddressDto(string Line1, string? Line2, string City, string? State, string PostalCode, string Country);

public sealed record OrderLineDto(
    Guid ProductId,
    string Sku,
    string ProductName,
    MoneyDto UnitPrice,
    int Quantity,
    MoneyDto LineTotal);

public sealed record OrderDto(
    Guid Id,
    string OrderNumber,
    CustomerDto Customer,
    AddressDto ShippingAddress,
    IReadOnlyList<OrderLineDto> Lines,
    OrderStatus Status,
    MoneyDto Total,
    DateTimeOffset PlacedAt,
    DateTimeOffset? CancelledAt,
    string? CancellationReason)
{
    public static OrderDto From(Order o) => new(
        o.Id.Value,
        o.OrderNumber,
        new CustomerDto(o.Customer.Name, o.Customer.Email),
        new AddressDto(
            o.ShippingAddress.Line1,
            o.ShippingAddress.Line2,
            o.ShippingAddress.City,
            o.ShippingAddress.State,
            o.ShippingAddress.PostalCode,
            o.ShippingAddress.Country),
        o.Lines.Select(l => new OrderLineDto(
            l.ProductId.Value,
            l.Sku.Value,
            l.ProductName,
            new MoneyDto(l.UnitPrice.Amount, l.UnitPrice.Currency),
            l.Quantity,
            new MoneyDto(l.LineTotal.Amount, l.LineTotal.Currency))).ToList(),
        o.Status,
        new MoneyDto(o.Total.Amount, o.Total.Currency),
        o.PlacedAt,
        o.CancelledAt,
        o.CancellationReason);
}
