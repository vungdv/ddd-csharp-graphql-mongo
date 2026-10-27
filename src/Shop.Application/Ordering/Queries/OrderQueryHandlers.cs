using FluentValidation;
using Shop.Application.Abstractions;
using Shop.Domain.SharedKernel;

namespace Shop.Application.Ordering.Queries;

public sealed class GetOrderByIdHandler(IOrderReadService reads) : IRequestHandler<GetOrderByIdQuery, OrderDto?>
{
    public async Task<Result<OrderDto?>> Handle(GetOrderByIdQuery request, CancellationToken ct) =>
        Result<OrderDto?>.Success(await reads.GetById(request.Id, ct));
}

public sealed class GetOrderByNumberHandler(IOrderReadService reads) : IRequestHandler<GetOrderByNumberQuery, OrderDto?>
{
    public async Task<Result<OrderDto?>> Handle(GetOrderByNumberQuery request, CancellationToken ct) =>
        Result<OrderDto?>.Success(await reads.GetByOrderNumber(request.OrderNumber.Trim().ToUpperInvariant(), ct));
}

public sealed class ListOrdersQueryValidator : AbstractValidator<ListOrdersQuery>
{
    public ListOrdersQueryValidator()
    {
        RuleFor(x => x.First).GreaterThanOrEqualTo(0).WithMessage("'first' must not be negative.");
        RuleFor(x => x.Filter!.PlacedTo)
            .GreaterThanOrEqualTo(x => x.Filter!.PlacedFrom)
            .When(x => x.Filter?.PlacedFrom is not null && x.Filter.PlacedTo is not null)
            .WithName("placedTo")
            .WithMessage("'placedTo' must not be before 'placedFrom'.");
    }
}

public sealed class ListOrdersHandler(IOrderReadService reads) : IRequestHandler<ListOrdersQuery, OrderPageDto>
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public async Task<Result<OrderPageDto>> Handle(ListOrdersQuery request, CancellationToken ct)
    {
        var first = Math.Min(request.First, MaxPageSize);

        OrderCursor? after = null;
        if (request.After is not null && !OrderCursor.TryDecode(request.After, out after))
        {
            return Result<OrderPageDto>.Failure(new AppError(
                DomainErrorCodes.ValidationFailed,
                "The 'after' cursor is invalid.",
                "after"));
        }

        var filter = request.Filter ?? new OrderFilter();
        if (filter.CustomerEmail is not null)
        {
            filter = filter with { CustomerEmail = filter.CustomerEmail.Trim().ToLowerInvariant() };
        }

        var slice = await reads.List(new OrderPageRequest(first + 1, filter, after), ct);

        var hasNext = slice.Items.Count > first;
        var edges = slice.Items
            .Take(first)
            .Select(o => new OrderEdgeDto(OrderCursor.For(o).Encode(), o))
            .ToList();

        return Result<OrderPageDto>.Success(new OrderPageDto(
            edges,
            hasNext,
            edges.Count > 0 ? edges[0].Cursor : null,
            edges.Count > 0 ? edges[^1].Cursor : null,
            slice.TotalCount));
    }
}
