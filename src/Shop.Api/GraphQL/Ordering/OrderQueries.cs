using HotChocolate.CostAnalysis.Types;
using Shop.Api.GraphQL.Common;
using Shop.Api.GraphQL.Ordering.Inputs;
using Shop.Api.GraphQL.Ordering.Types;
using Shop.Application.Abstractions;
using Shop.Application.Ordering;
using Shop.Application.Ordering.Queries;

namespace Shop.Api.GraphQL.Ordering;

[ExtendObjectType(OperationTypeNames.Query)]
public sealed class OrderQueries
{
    /// <summary>Returns null (no error) for unknown or malformed ids.</summary>
    public async Task<OrderDto?> Order(
        [GraphQLType<NonNullType<IdType>>] string id,
        IDispatcher dispatcher,
        CancellationToken ct) =>
        Guid.TryParse(id, out var guid)
            ? (await dispatcher.Send(new GetOrderByIdQuery(guid), ct)).ValueOrThrow()
            : null;

    public async Task<OrderDto?> OrderByNumber(string orderNumber, IDispatcher dispatcher, CancellationToken ct) =>
        (await dispatcher.Send(new GetOrderByNumberQuery(orderNumber), ct)).ValueOrThrow();

    // Cost analysis: the page size is the "first" argument (default 20, capped at 100 by the handler).
    [ListSize(AssumedSize = 100, SlicingArguments = ["first"], SizedFields = ["edges"], RequireOneSlicingArgument = false)]
    public async Task<OrderConnection> Orders(
        IDispatcher dispatcher,
        CancellationToken ct,
        int? first = ListOrdersHandler.DefaultPageSize,
        string? after = null,
        OrderFilterInput? filter = null)
    {
        var result = await dispatcher.Send(
            new ListOrdersQuery(first ?? ListOrdersHandler.DefaultPageSize, after, filter?.ToFilter()),
            ct);
        return OrderConnection.From(result.ValueOrThrow()!);
    }
}
