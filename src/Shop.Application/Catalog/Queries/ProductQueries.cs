using Shop.Application.Abstractions;

namespace Shop.Application.Catalog.Queries;

public sealed record GetProductByIdQuery(Guid Id) : IRequest<ProductDto?>;

public sealed record GetProductBySkuQuery(string Sku) : IRequest<ProductDto?>;

/// <summary><paramref name="First"/> is capped at <see cref="ListProductsHandler.MaxPageSize"/>.</summary>
public sealed record ListProductsQuery(
    int First = ListProductsHandler.DefaultPageSize,
    string? After = null,
    ProductFilter? Filter = null,
    ProductSort? Sort = null) : IRequest<ProductPageDto>;

public sealed record ProductEdgeDto(string Cursor, ProductDto Node);

public sealed record ProductPageDto(
    IReadOnlyList<ProductEdgeDto> Edges,
    bool HasNextPage,
    string? StartCursor,
    string? EndCursor,
    int TotalCount);
