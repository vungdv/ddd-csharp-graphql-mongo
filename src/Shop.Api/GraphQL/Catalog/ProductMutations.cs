using Shop.Api.GraphQL.Catalog.Inputs;
using Shop.Api.GraphQL.Catalog.Payloads;
using Shop.Api.GraphQL.Common;
using Shop.Application.Abstractions;
using Shop.Application.Catalog;
using Shop.Application.Catalog.Commands;
using Shop.Domain.SharedKernel;

namespace Shop.Api.GraphQL.Catalog;

[ExtendObjectType(OperationTypeNames.Mutation)]
public sealed class ProductMutations
{
    public async Task<CreateProductPayload> CreateProduct(
        CreateProductInput input,
        IDispatcher dispatcher,
        CancellationToken ct)
    {
        var result = await dispatcher.Send(
            new CreateProductCommand(
                input.Sku,
                input.Name,
                input.Description,
                input.PriceAmount,
                input.Currency,
                input.Category,
                input.InitialStock),
            ct);
        return result.IsSuccess
            ? new CreateProductPayload(result.Value, null)
            : new CreateProductPayload(null, UserError.From(result.Errors));
    }

    public Task<UpdateProductPayload> UpdateProduct(UpdateProductInput input, IDispatcher dispatcher, CancellationToken ct) =>
        Run(input.Id, id => new UpdateProductCommand(id, input.Name, input.Description, input.Category), dispatcher, ct,
            (p, e) => new UpdateProductPayload(p, e));

    public Task<ChangeProductPricePayload> ChangeProductPrice(
        ChangeProductPriceInput input,
        IDispatcher dispatcher,
        CancellationToken ct) =>
        Run(input.Id, id => new ChangeProductPriceCommand(id, input.Amount, input.Currency), dispatcher, ct,
            (p, e) => new ChangeProductPricePayload(p, e));

    public Task<SetProductActivePayload> SetProductActive(
        SetProductActiveInput input,
        IDispatcher dispatcher,
        CancellationToken ct) =>
        Run(input.Id, id => new SetProductActiveCommand(id, input.IsActive), dispatcher, ct,
            (p, e) => new SetProductActivePayload(p, e));

    public Task<AdjustProductStockPayload> AdjustProductStock(
        AdjustProductStockInput input,
        IDispatcher dispatcher,
        CancellationToken ct) =>
        Run(input.Id, id => new AdjustProductStockCommand(id, input.Delta), dispatcher, ct,
            (p, e) => new AdjustProductStockPayload(p, e));

    /// <summary>Parses the id, dispatches the command and shapes the payload (entity null when errors present).</summary>
    private static async Task<TPayload> Run<TPayload>(
        string rawId,
        Func<Guid, IRequest<ProductDto>> command,
        IDispatcher dispatcher,
        CancellationToken ct,
        Func<ProductDto?, IReadOnlyList<UserError>?, TPayload> payload)
    {
        if (!Guid.TryParse(rawId, out var id))
        {
            return payload(null, [new UserError(DomainErrorCodes.ValidationFailed, "Id must be a valid GUID.", "id")]);
        }

        var result = await dispatcher.Send(command(id), ct);
        return result.IsSuccess ? payload(result.Value, null) : payload(null, UserError.From(result.Errors));
    }
}
