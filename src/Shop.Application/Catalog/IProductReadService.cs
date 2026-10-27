namespace Shop.Application.Catalog;

/// <summary>Read-side port (CQRS): projects straight from storage into DTOs, bypassing the aggregate.</summary>
public interface IProductReadService
{
    Task<ProductDto?> GetById(Guid id, CancellationToken ct = default);

    Task<ProductDto?> GetBySku(string sku, CancellationToken ct = default);

    Task<IReadOnlyList<ProductDto>> GetByIds(IReadOnlyCollection<Guid> ids, CancellationToken ct = default);

    /// <summary>Returns up to <see cref="ProductPageRequest.Take"/> items after the cursor plus the filtered total.</summary>
    Task<ProductPageSlice> List(ProductPageRequest request, CancellationToken ct = default);
}

public enum ProductSortField
{
    Name,
    Price,
    CreatedAt,
}

public enum SortDirection
{
    Asc,
    Desc,
}

public sealed record ProductFilter(
    string? Category = null,
    bool? IsActive = null,
    string? Search = null,
    decimal? MinPrice = null,
    decimal? MaxPrice = null);

public sealed record ProductSort(ProductSortField Field = ProductSortField.Name, SortDirection Direction = SortDirection.Asc);

public sealed record ProductPageRequest(int Take, ProductFilter Filter, ProductSort Sort, ProductCursor? After);

/// <summary>Items fetched (at most Take + 1 so the caller can detect a next page) and the filtered total count.</summary>
public sealed record ProductPageSlice(IReadOnlyList<ProductDto> Items, int TotalCount);
