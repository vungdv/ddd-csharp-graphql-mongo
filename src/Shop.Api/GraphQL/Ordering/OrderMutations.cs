using Shop.Api.GraphQL.Common;
using Shop.Api.GraphQL.Ordering.Inputs;
using Shop.Api.GraphQL.Ordering.Payloads;
using Shop.Application.Abstractions;
using Shop.Application.Ordering.Commands;
using Shop.Domain.SharedKernel;

namespace Shop.Api.GraphQL.Ordering;

[ExtendObjectType(OperationTypeNames.Mutation)]
public sealed class OrderMutations
{
    public async Task<PlaceOrderPayload> PlaceOrder(PlaceOrderInput input, IDispatcher dispatcher, CancellationToken ct)
    {
        var idErrors = new List<UserError>();
        var lines = new List<Application.Ordering.Commands.OrderLineInput>();
        for (var i = 0; i < input.Lines.Count; i++)
        {
            if (Guid.TryParse(input.Lines[i].ProductId, out var productId))
            {
                lines.Add(new(productId, input.Lines[i].Quantity));
            }
            else
            {
                idErrors.Add(new UserError(DomainErrorCodes.ValidationFailed, "Product id must be a valid GUID.", $"lines[{i}].productId"));
            }
        }

        if (idErrors.Count > 0)
        {
            return new PlaceOrderPayload(null, idErrors);
        }

        var command = new PlaceOrderCommand(
            new(input.Customer.Name, input.Customer.Email),
            new(
                input.ShippingAddress.Line1,
                input.ShippingAddress.Line2,
                input.ShippingAddress.City,
                input.ShippingAddress.State,
                input.ShippingAddress.PostalCode,
                input.ShippingAddress.Country),
            lines);
        var result = await dispatcher.Send(command, ct);
        return result.IsSuccess
            ? new PlaceOrderPayload(result.Value, null)
            : new PlaceOrderPayload(null, UserError.From(result.Errors));
    }

    public async Task<CancelOrderPayload> CancelOrder(CancelOrderInput input, IDispatcher dispatcher, CancellationToken ct)
    {
        if (!Guid.TryParse(input.Id, out var id))
        {
            return new CancelOrderPayload(null, [new UserError(DomainErrorCodes.ValidationFailed, "Id must be a valid GUID.", "id")]);
        }

        var result = await dispatcher.Send(new CancelOrderCommand(id, input.Reason), ct);
        return result.IsSuccess
            ? new CancelOrderPayload(result.Value, null)
            : new CancelOrderPayload(null, UserError.From(result.Errors));
    }
}
