using Shop.Application.Catalog;

namespace Shop.Api.GraphQL.Catalog.Inputs;

public sealed record ProductFilterInput(
    string? Category,
    bool? IsActive,
    string? Search,
    decimal? MinPrice,
    decimal? MaxPrice)
{
    public ProductFilter ToFilter() => new(Category, IsActive, Search, MinPrice, MaxPrice);
}

public sealed class ProductSortInput
{
    public ProductSortField Field { get; init; }

    [HotChocolate.Types.DefaultValue(SortDirection.Asc)]
    public SortDirection Direction { get; init; } = SortDirection.Asc;

    public ProductSort ToSort() => new(Field, Direction);
}
