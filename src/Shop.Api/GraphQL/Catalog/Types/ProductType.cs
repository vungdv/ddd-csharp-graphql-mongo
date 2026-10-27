using Shop.Application.Catalog;

namespace Shop.Api.GraphQL.Catalog.Types;

public sealed class ProductType : ObjectType<ProductDto>
{
    protected override void Configure(IObjectTypeDescriptor<ProductDto> descriptor)
    {
        descriptor.Name("Product");

        // IDs are exposed as the plain string form of the Guid (no global-ID encoding).
        descriptor.Field(p => p.Id)
            .Type<NonNullType<IdType>>()
            .Resolve(ctx => ctx.Parent<ProductDto>().Id.ToString());

        descriptor.Field(p => p.Price).Type<NonNullType<MoneyType>>();
        descriptor.Field(p => p.CreatedAt).Type<NonNullType<DateTimeType>>();
        descriptor.Field(p => p.UpdatedAt).Type<NonNullType<DateTimeType>>();
    }
}
