using FluentValidation;
using Shop.Domain.Catalog;

namespace Shop.Application.Catalog.Commands;

internal static class MoneyRules
{
    public static IRuleBuilderOptions<T, decimal> ValidPrice<T>(this IRuleBuilder<T, decimal> rule) =>
        rule.GreaterThan(0).WithMessage("Price must be greater than zero.")
            .Must(a => decimal.Round(a, 2) == a).WithMessage("Price must have at most 2 decimal places.");

    public static IRuleBuilderOptions<T, string?> ValidCurrency<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(c => c is { Length: 3 } && c.All(ch => char.IsAsciiLetter(ch)))
            .WithMessage("Currency must be a 3-letter ISO 4217 code.");
}

public sealed class CreateProductCommandValidator : AbstractValidator<CreateProductCommand>
{
    public CreateProductCommandValidator()
    {
        RuleFor(x => x.Sku).Matches("^[A-Z0-9-]{3,32}\\z").WithMessage("SKU must be 3-32 characters of A-Z, 0-9 or '-'.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Product.MaxNameLength);
        RuleFor(x => x.Description).MaximumLength(Product.MaxDescriptionLength);
        RuleFor(x => x.PriceAmount).ValidPrice();
        RuleFor(x => x.Currency).ValidCurrency();
        RuleFor(x => x.Category).NotEmpty().MaximumLength(Product.MaxCategoryLength);
        RuleFor(x => x.InitialStock).GreaterThanOrEqualTo(0);
    }
}

public sealed class UpdateProductCommandValidator : AbstractValidator<UpdateProductCommand>
{
    public UpdateProductCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Product.MaxNameLength).When(x => x.Name is not null);
        RuleFor(x => x.Description).MaximumLength(Product.MaxDescriptionLength);
        RuleFor(x => x.Category).NotEmpty().MaximumLength(Product.MaxCategoryLength).When(x => x.Category is not null);
    }
}

public sealed class ChangeProductPriceCommandValidator : AbstractValidator<ChangeProductPriceCommand>
{
    public ChangeProductPriceCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Amount).ValidPrice();
        RuleFor(x => x.Currency).ValidCurrency().When(x => x.Currency is not null);
    }
}

public sealed class SetProductActiveCommandValidator : AbstractValidator<SetProductActiveCommand>
{
    public SetProductActiveCommandValidator() => RuleFor(x => x.Id).NotEmpty();
}

public sealed class AdjustProductStockCommandValidator : AbstractValidator<AdjustProductStockCommand>
{
    public AdjustProductStockCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Delta).NotEqual(0).WithMessage("Delta must not be zero.");
    }
}
