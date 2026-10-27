using Shop.Api.GraphQL.Catalog.Inputs;
using Shop.Api.GraphQL.Catalog.Types;
using Shop.Api.GraphQL.Common;
using Shop.Application.Abstractions;
using Shop.Application.Catalog;
using Shop.Application.Catalog.Queries;

namespace Shop.Api.GraphQL.Catalog;

[ExtendObjectType(OperationTypeNames.Query)]
public sealed class ProductQueries
{
    /// <summary>Returns null (no error) for unknown or malformed ids.</summary>
    public async Task<ProductDto?> Product(
        [GraphQLType<NonNullType<IdType>>] string id,
        ProductByIdDataLoader loader,
        CancellationToken ct) =>
        Guid.TryParse(id, out var guid) ? await loader.LoadAsync(guid, ct) : null;

    public async Task<ProductDto?> ProductBySku(string sku, IDispatcher dispatcher, CancellationToken ct) =>
        (await dispatcher.Send(new GetProductBySkuQuery(sku), ct)).ValueOrThrow();

    public async Task<ProductConnection> Products(
        IDispatcher dispatcher,
        CancellationToken ct,
        int? first = ListProductsHandler.DefaultPageSize,
        string? after = null,
        ProductFilterInput? filter = null,
        [GraphQLName("order")] ProductSortInput? order = null)
    {
        var result = await dispatcher.Send(
            new ListProductsQuery(first ?? ListProductsHandler.DefaultPageSize, after, filter?.ToFilter(), order?.ToSort()),
            ct);
        return ProductConnection.From(result.ValueOrThrow()!);
    }
}
