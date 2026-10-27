using Shop.Application.Abstractions;
using Shop.Domain.Catalog;
using Shop.Domain.SharedKernel;

namespace Shop.Application.Catalog.Commands;

public sealed class CreateProductHandler(IProductRepository products, IClock clock)
    : IRequestHandler<CreateProductCommand, ProductDto>
{
    public async Task<Result<ProductDto>> Handle(CreateProductCommand request, CancellationToken ct)
    {
        var sku = Sku.Create(request.Sku);
        if (await products.ExistsBySku(sku, ct))
        {
            throw SkuExists(sku);
        }

        var product = Product.Create(
            sku,
            request.Name,
            request.Description,
            Money.Of(request.PriceAmount, request.Currency),
            request.Category,
            request.InitialStock,
            clock);
        await products.Add(product, ct);
        return Result<ProductDto>.Success(ProductDto.From(product));
    }

    private static DomainException SkuExists(Sku sku) =>
        new(DomainErrorCodes.ProductSkuAlreadyExists, $"A product with SKU '{sku}' already exists.", "sku");
}

public sealed class UpdateProductHandler(IProductRepository products, IClock clock)
    : IRequestHandler<UpdateProductCommand, ProductDto>
{
    public Task<Result<ProductDto>> Handle(UpdateProductCommand request, CancellationToken ct) =>
        ProductCommandSupport.Modify(products, request.Id, p =>
        {
            if (request.Name is not null)
            {
                p.Rename(request.Name, clock);
            }

            if (request.Description is not null)
            {
                p.ChangeDescription(request.Description, clock);
            }

            if (request.Category is not null)
            {
                p.ChangeCategory(request.Category, clock);
            }
        }, ct);
}

public sealed class ChangeProductPriceHandler(IProductRepository products, IClock clock)
    : IRequestHandler<ChangeProductPriceCommand, ProductDto>
{
    public Task<Result<ProductDto>> Handle(ChangeProductPriceCommand request, CancellationToken ct) =>
        ProductCommandSupport.Modify(
            products,
            request.Id,
            p => p.ChangePrice(Money.Of(request.Amount, request.Currency ?? p.Price.Currency), clock),
            ct);
}

public sealed class SetProductActiveHandler(IProductRepository products, IClock clock)
    : IRequestHandler<SetProductActiveCommand, ProductDto>
{
    public Task<Result<ProductDto>> Handle(SetProductActiveCommand request, CancellationToken ct) =>
        ProductCommandSupport.Modify(products, request.Id, p =>
        {
            if (request.IsActive)
            {
                p.Activate(clock);
            }
            else
            {
                p.Deactivate(clock);
            }
        }, ct);
}

public sealed class AdjustProductStockHandler(IProductRepository products, IClock clock)
    : IRequestHandler<AdjustProductStockCommand, ProductDto>
{
    public Task<Result<ProductDto>> Handle(AdjustProductStockCommand request, CancellationToken ct) =>
        ProductCommandSupport.Modify(products, request.Id, p => p.AdjustStock(request.Delta, clock), ct);
}

internal static class ProductCommandSupport
{
    /// <summary>Load, mutate and save an existing product; PRODUCT_NOT_FOUND if absent.</summary>
    public static async Task<Result<ProductDto>> Modify(
        IProductRepository products,
        Guid id,
        Action<Product> mutate,
        CancellationToken ct)
    {
        var product = await products.GetById(new ProductId(id), ct);
        if (product is null)
        {
            return Result<ProductDto>.Failure(
                new AppError(DomainErrorCodes.ProductNotFound, $"Product {id} was not found.", "id"));
        }

        mutate(product);
        await products.Update(product, ct);
        return Result<ProductDto>.Success(ProductDto.From(product));
    }
}
