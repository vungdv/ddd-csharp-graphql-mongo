using Shop.Api.GraphQL.Common;
using Shop.Application.Catalog;
using Shop.Application.Catalog.Queries;

namespace Shop.Api.GraphQL.Catalog.Types;

public sealed record ProductEdge(string Cursor, ProductDto Node);

public sealed record ProductConnection(IReadOnlyList<ProductEdge> Edges, PageInfo PageInfo, int TotalCount)
{
    public static ProductConnection From(ProductPageDto page) => new(
        page.Edges.Select(e => new ProductEdge(e.Cursor, e.Node)).ToList(),
        new PageInfo(page.HasNextPage, false, page.StartCursor, page.EndCursor),
        page.TotalCount);
}
