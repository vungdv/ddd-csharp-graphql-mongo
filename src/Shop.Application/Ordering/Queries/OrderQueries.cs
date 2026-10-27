using Shop.Application.Abstractions;

namespace Shop.Application.Ordering.Queries;

public sealed record GetOrderByIdQuery(Guid Id) : IRequest<OrderDto?>;

public sealed record GetOrderByNumberQuery(string OrderNumber) : IRequest<OrderDto?>;

/// <summary><paramref name="First"/> is capped at <see cref="ListOrdersHandler.MaxPageSize"/>.</summary>
public sealed record ListOrdersQuery(
    int First = ListOrdersHandler.DefaultPageSize,
    string? After = null,
    OrderFilter? Filter = null) : IRequest<OrderPageDto>;

public sealed record OrderEdgeDto(string Cursor, OrderDto Node);

public sealed record OrderPageDto(
    IReadOnlyList<OrderEdgeDto> Edges,
    bool HasNextPage,
    string? StartCursor,
    string? EndCursor,
    int TotalCount);
