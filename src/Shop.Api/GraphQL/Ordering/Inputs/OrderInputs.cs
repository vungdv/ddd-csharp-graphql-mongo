using HotChocolate.Types;
using Shop.Application.Ordering;
using Shop.Application.Ordering.Commands;
using Shop.Domain.Ordering;

namespace Shop.Api.GraphQL.Ordering.Inputs;

public sealed class CustomerInput
{
    public string Name { get; init; } = string.Empty;

    public string Email { get; init; } = string.Empty;
}

public sealed class AddressInput
{
    public string Line1 { get; init; } = string.Empty;

    public string? Line2 { get; init; }

    public string City { get; init; } = string.Empty;

    public string? State { get; init; }

    public string PostalCode { get; init; } = string.Empty;

    public string Country { get; init; } = string.Empty;
}

public sealed class OrderLineInput
{
    [GraphQLType<NonNullType<IdType>>]
    public string ProductId { get; init; } = string.Empty;

    public int Quantity { get; init; }
}

public sealed class PlaceOrderInput
{
    public CustomerInput Customer { get; init; } = new();

    public AddressInput ShippingAddress { get; init; } = new();

    public IReadOnlyList<OrderLineInput> Lines { get; init; } = [];
}

public sealed class CancelOrderInput
{
    [GraphQLType<NonNullType<IdType>>]
    public string Id { get; init; } = string.Empty;

    public string? Reason { get; init; }
}

public sealed record OrderFilterInput(
    OrderStatus? Status,
    string? CustomerEmail,
    DateTimeOffset? PlacedFrom,
    DateTimeOffset? PlacedTo)
{
    public OrderFilter ToFilter() => new(Status, CustomerEmail, PlacedFrom, PlacedTo);
}
