using FluentValidation;
using Shop.Application.Abstractions;
using Shop.Domain.SharedKernel;

namespace Shop.Application.Catalog.Queries;

public sealed class GetProductByIdHandler(IProductReadService reads) : IRequestHandler<GetProductByIdQuery, ProductDto?>
{
    public async Task<Result<ProductDto?>> Handle(GetProductByIdQuery request, CancellationToken ct) =>
        Result<ProductDto?>.Success(await reads.GetById(request.Id, ct));
}

public sealed class GetProductBySkuHandler(IProductReadService reads) : IRequestHandler<GetProductBySkuQuery, ProductDto?>
{
    public async Task<Result<ProductDto?>> Handle(GetProductBySkuQuery request, CancellationToken ct) =>
        Result<ProductDto?>.Success(await reads.GetBySku(request.Sku, ct));
}

public sealed class ListProductsQueryValidator : AbstractValidator<ListProductsQuery>
{
    public const int MaxSearchLength = 100;

    public ListProductsQueryValidator()
    {
        RuleFor(x => x.First).GreaterThanOrEqualTo(0).WithMessage("'first' must not be negative.");
        RuleFor(x => x.Filter!.Search).MaximumLength(MaxSearchLength)
            .When(x => x.Filter?.Search is not null)
            .WithName("search")
            .WithMessage($"'search' must be at most {MaxSearchLength} characters.");
    }
}

public sealed class ListProductsHandler(IProductReadService reads) : IRequestHandler<ListProductsQuery, ProductPageDto>
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public async Task<Result<ProductPageDto>> Handle(ListProductsQuery request, CancellationToken ct)
    {
        var sort = request.Sort ?? new ProductSort();
        var first = Math.Min(request.First, MaxPageSize);

        ProductCursor? after = null;
        if (request.After is not null)
        {
            if (!ProductCursor.TryDecode(request.After, out after)
                || after!.Field != sort.Field
                || after.Direction != sort.Direction)
            {
                return Result<ProductPageDto>.Failure(new AppError(
                    DomainErrorCodes.ValidationFailed,
                    "The 'after' cursor is invalid or does not match the requested sort.",
                    "after"));
            }
        }

        var slice = await reads.List(
            new ProductPageRequest(first + 1, request.Filter ?? new ProductFilter(), sort, after),
            ct);

        var hasNext = slice.Items.Count > first;
        var edges = slice.Items
            .Take(first)
            .Select(p => new ProductEdgeDto(ProductCursor.For(sort, p).Encode(), p))
            .ToList();

        return Result<ProductPageDto>.Success(new ProductPageDto(
            edges,
            hasNext,
            edges.Count > 0 ? edges[0].Cursor : null,
            edges.Count > 0 ? edges[^1].Cursor : null,
            slice.TotalCount));
    }
}
