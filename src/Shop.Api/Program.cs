using Shop.Api.GraphQL.Catalog;
using Shop.Api.GraphQL.Catalog.Types;
using Shop.Api.GraphQL.Common;
using Shop.Api.GraphQL.Ordering;
using Shop.Api.GraphQL.Ordering.Types;
using HotChocolate.AspNetCore;
using Shop.Api.Observability;
using Shop.Application;
using Shop.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddTraceJsonConsole();
builder.Services.AddShopTelemetry(builder.Configuration);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddCatalogSeeding();
}

var limits = GraphQlLimitsOptions.From(builder.Configuration);
builder.Services.AddSingleton(limits);
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
    .AddErrorFilter<UnexpectedErrorFilter>()
    .DisableIntrospection(!builder.Environment.IsDevelopment())
    .AddInstrumentation()
    .AddMaxExecutionDepthRule(limits.MaxExecutionDepth, skipIntrospectionFields: true)
    .ModifyCostOptions(o =>
    {
        o.EnforceCostLimits = true;
        o.MaxFieldCost = limits.MaxFieldCost;
        o.MaxTypeCost = limits.MaxTypeCost;
    });

var app = builder.Build();

app.MapHealthChecks("/health");
// Banana Cake Pop (the GraphQL playground) is served from GET /graphql in Development only.
app.MapGraphQL("/graphql").WithOptions(new GraphQLServerOptions
{
    Tool = { Enable = app.Environment.IsDevelopment() },
});

app.Run();

public partial class Program;
