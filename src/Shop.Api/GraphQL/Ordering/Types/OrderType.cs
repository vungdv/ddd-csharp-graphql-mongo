using Shop.Api.GraphQL.Catalog;
using Shop.Api.GraphQL.Catalog.Types;
using Shop.Api.GraphQL.Common;
using Shop.Application.Catalog;
using Shop.Application.Ordering;
using Shop.Domain.SharedKernel;

namespace Shop.Api.GraphQL.Ordering.Types;

public sealed class CustomerType : ObjectType<CustomerDto>
{
    protected override void Configure(IObjectTypeDescriptor<CustomerDto> descriptor) => descriptor.Name("Customer");
}

public sealed class AddressType : ObjectType<AddressDto>
{
    protected override void Configure(IObjectTypeDescriptor<AddressDto> descriptor) => descriptor.Name("Address");
}

public sealed class OrderLineType : ObjectType<OrderLineDto>
{
    protected override void Configure(IObjectTypeDescriptor<OrderLineDto> descriptor)
    {
        descriptor.Name("OrderLine");
        descriptor.Field(l => l.ProductId).Type<NonNullType<IdType>>().Resolve(ctx => ctx.Parent<OrderLineDto>().ProductId.ToString());
        descriptor.Field(l => l.UnitPrice).Type<NonNullType<MoneyType>>();
        descriptor.Field(l => l.LineTotal).Type<NonNullType<MoneyType>>();
    }
}

public sealed class OrderType : ObjectType<OrderDto>
{
    protected override void Configure(IObjectTypeDescriptor<OrderDto> descriptor)
    {
        descriptor.Name("Order");
        descriptor.Field(o => o.Id).Type<NonNullType<IdType>>().Resolve(ctx => ctx.Parent<OrderDto>().Id.ToString());
        descriptor.Field(o => o.Customer).Type<NonNullType<CustomerType>>();
        descriptor.Field(o => o.ShippingAddress).Type<NonNullType<AddressType>>();
        descriptor.Field(o => o.Lines).Type<NonNullType<ListType<NonNullType<OrderLineType>>>>();
        descriptor.Field(o => o.Total).Type<NonNullType<MoneyType>>();
        descriptor.Field(o => o.PlacedAt).Type<NonNullType<DateTimeType>>();
        descriptor.Field(o => o.CancelledAt).Type<DateTimeType>();

        // Optional resolver: the product behind a line, batched/cached per request via the product DataLoader.
        descriptor.Field("product")
            .Argument("lineIndex", a => a.Type<NonNullType<IntType>>())
            .Type<ProductType>()
            .Resolve(async ctx =>
            {
                var order = ctx.Parent<OrderDto>();
                var index = ctx.ArgumentValue<int>("lineIndex");
                if (index < 0 || index >= order.Lines.Count)
                {
                    throw ResultErrors.ToGraphQLException(
                    [
                        new Application.Abstractions.AppError(
                            DomainErrorCodes.ValidationFailed,
                            $"lineIndex must be between 0 and {order.Lines.Count - 1}.",
                            "lineIndex"),
                    ]);
                }

                var loader = ctx.Services.GetRequiredService<ProductByIdDataLoader>();
                return await loader.LoadAsync(order.Lines[index].ProductId, ctx.RequestAborted);
            });
    }
}
