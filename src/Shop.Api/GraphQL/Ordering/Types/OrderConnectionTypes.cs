using Shop.Api.GraphQL.Common;
using Shop.Application.Ordering;
using Shop.Application.Ordering.Queries;

namespace Shop.Api.GraphQL.Ordering.Types;

public sealed record OrderEdge(string Cursor, OrderDto Node);

public sealed record OrderConnection(IReadOnlyList<OrderEdge> Edges, PageInfo PageInfo, int TotalCount)
{
    public static OrderConnection From(OrderPageDto page) => new(
        page.Edges.Select(e => new OrderEdge(e.Cursor, e.Node)).ToList(),
        new PageInfo(page.HasNextPage, false, page.StartCursor, page.EndCursor),
        page.TotalCount);
}
