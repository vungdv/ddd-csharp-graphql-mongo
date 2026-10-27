using Shop.Api.GraphQL.Common;
using Shop.Application.Ordering;

namespace Shop.Api.GraphQL.Ordering.Payloads;

/// <summary>Mutation payloads: <c>Order</c> is null when <c>Errors</c> is non-null.</summary>
public sealed record PlaceOrderPayload(OrderDto? Order, IReadOnlyList<UserError>? Errors);

public sealed record CancelOrderPayload(OrderDto? Order, IReadOnlyList<UserError>? Errors);
