using Shop.Api.GraphQL.Catalog;
using Shop.Api.GraphQL.Catalog.Types;
using Shop.Api.GraphQL.Common;
using Shop.Api.GraphQL.Ordering;
using Shop.Api.GraphQL.Ordering.Types;
using Shop.Application;
using Shop.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(o =>
{
    o.IncludeScopes = true;
    o.UseUtcTimestamp = true;
    o.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ ";
});

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddCatalogSeeding();
}

builder.Services
    .AddGraphQLServer()
    .AddQueryType()
    .AddMutationType()
    .AddTypeExtension<ProductQueries>()
    .AddTypeExtension<ProductMutations>()
    .AddTypeExtension<OrderQueries>()
    .AddTypeExtension<OrderMutations>()
    .AddType<OrderType>()
    .AddType<OrderLineType>()
    .AddType<CustomerType>()
    .AddType<AddressType>()
    .AddType<ProductType>()
    .AddType<MoneyType>()
    .AddDataLoader<ProductByIdDataLoader>()
    .AddErrorFilter<UnexpectedErrorFilter>();

var app = builder.Build();

app.MapHealthChecks("/health");
app.MapGraphQL("/graphql");

app.Run();

public partial class Program;
