using FluentValidation;
using Shop.Domain.Ordering;

namespace Shop.Application.Ordering.Commands;

/// <summary>
/// Field names follow the GraphQL input shape (<c>customer.email</c>, <c>lines[0].quantity</c>) and match the names the
/// domain value objects use. An empty <c>lines</c> list is left to the domain (ORDER_EMPTY).
/// </summary>
public sealed class PlaceOrderCommandValidator : AbstractValidator<PlaceOrderCommand>
{
    public PlaceOrderCommandValidator()
    {
        RuleFor(x => x.Customer).NotNull();
        When(x => x.Customer is not null, () =>
        {
            RuleFor(x => x.Customer.Name).NotEmpty().MaximumLength(CustomerInfo.MaxNameLength);
            RuleFor(x => x.Customer.Email).NotEmpty().MaximumLength(CustomerInfo.MaxEmailLength)
                .EmailAddress();
        });

        RuleFor(x => x.ShippingAddress).NotNull();
        When(x => x.ShippingAddress is not null, () =>
        {
            RuleFor(x => x.ShippingAddress.Line1).NotEmpty().MaximumLength(Address.MaxFieldLength);
            RuleFor(x => x.ShippingAddress.Line2).MaximumLength(Address.MaxFieldLength);
            RuleFor(x => x.ShippingAddress.City).NotEmpty().MaximumLength(Address.MaxFieldLength);
            RuleFor(x => x.ShippingAddress.State).MaximumLength(Address.MaxFieldLength);
            RuleFor(x => x.ShippingAddress.PostalCode).NotEmpty().MaximumLength(Address.MaxFieldLength);
            RuleFor(x => x.ShippingAddress.Country).NotEmpty().MaximumLength(Address.MaxFieldLength);
        });

        RuleFor(x => x.Lines).NotNull();
        RuleFor(x => x.Lines.Count).LessThanOrEqualTo(Order.MaxLines)
            .When(x => x.Lines is not null)
            .OverridePropertyName("Lines")
            .WithMessage($"An order can contain at most {Order.MaxLines} lines.");
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.ProductId).NotEmpty();
            line.RuleFor(l => l.Quantity).InclusiveBetween(OrderLine.MinQuantity, OrderLine.MaxQuantity);
        }).When(x => x.Lines is not null);
    }
}

public sealed class CancelOrderCommandValidator : AbstractValidator<CancelOrderCommand>
{
    public CancelOrderCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Reason).MaximumLength(Order.MaxReasonLength);
    }
}
