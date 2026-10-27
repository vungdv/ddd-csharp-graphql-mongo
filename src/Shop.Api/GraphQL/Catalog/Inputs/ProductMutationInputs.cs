using HotChocolate.Types;

namespace Shop.Api.GraphQL.Catalog.Inputs;

public sealed class CreateProductInput
{
    public string Sku { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    public decimal PriceAmount { get; init; }

    [GraphQLType<NonNullType<StringType>>]
    [DefaultValue("USD")]
    public string Currency { get; init; } = "USD";

    public string Category { get; init; } = string.Empty;

    [DefaultValue(0)]
    public int InitialStock { get; init; }
}

public sealed class UpdateProductInput
{
    [GraphQLType<NonNullType<IdType>>]
    public string Id { get; init; } = string.Empty;

    public string? Name { get; init; }

    public string? Description { get; init; }

    public string? Category { get; init; }
}

public sealed class ChangeProductPriceInput
{
    [GraphQLType<NonNullType<IdType>>]
    public string Id { get; init; } = string.Empty;

    public decimal Amount { get; init; }

    public string? Currency { get; init; }
}

public sealed class SetProductActiveInput
{
    [GraphQLType<NonNullType<IdType>>]
    public string Id { get; init; } = string.Empty;

    public bool IsActive { get; init; }
}

public sealed class AdjustProductStockInput
{
    [GraphQLType<NonNullType<IdType>>]
    public string Id { get; init; } = string.Empty;

    public int Delta { get; init; }
}
