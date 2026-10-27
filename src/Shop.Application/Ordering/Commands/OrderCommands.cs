using Shop.Application.Abstractions;

namespace Shop.Application.Ordering.Commands;

public sealed record CustomerInput(string? Name, string? Email);

public sealed record AddressInput(string? Line1, string? Line2, string? City, string? State, string? PostalCode, string? Country);

public sealed record OrderLineInput(Guid ProductId, int Quantity);

public sealed record PlaceOrderCommand(
    CustomerInput Customer,
    AddressInput ShippingAddress,
    IReadOnlyList<OrderLineInput> Lines) : IRequest<OrderDto>;

public sealed record CancelOrderCommand(Guid Id, string? Reason) : IRequest<OrderDto>;
