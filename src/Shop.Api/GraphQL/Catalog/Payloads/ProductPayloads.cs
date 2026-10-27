using Shop.Api.GraphQL.Common;
using Shop.Application.Catalog;

namespace Shop.Api.GraphQL.Catalog.Payloads;

/// <summary>Mutation payloads: <c>Product</c> is null when <c>Errors</c> is non-null.</summary>
public sealed record CreateProductPayload(ProductDto? Product, IReadOnlyList<UserError>? Errors);

public sealed record UpdateProductPayload(ProductDto? Product, IReadOnlyList<UserError>? Errors);

public sealed record ChangeProductPricePayload(ProductDto? Product, IReadOnlyList<UserError>? Errors);

public sealed record SetProductActivePayload(ProductDto? Product, IReadOnlyList<UserError>? Errors);

public sealed record AdjustProductStockPayload(ProductDto? Product, IReadOnlyList<UserError>? Errors);
