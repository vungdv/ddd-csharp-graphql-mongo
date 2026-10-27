using Shop.Application.Catalog;

namespace Shop.Api.GraphQL.Catalog.Types;

public sealed class MoneyType : ObjectType<MoneyDto>
{
    protected override void Configure(IObjectTypeDescriptor<MoneyDto> descriptor)
    {
        descriptor.Name("Money");
        descriptor.Field(m => m.Amount).Type<NonNullType<DecimalType>>();
        descriptor.Field(m => m.Currency).Type<NonNullType<StringType>>();
    }
}
