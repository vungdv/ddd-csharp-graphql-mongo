using FluentValidation;
using Shop.Application.Abstractions;
using Shop.Domain.SharedKernel;

namespace Shop.Application.Behaviors;

/// <summary>Runs all FluentValidation validators for the request; failures become VALIDATION_FAILED errors with the field.</summary>
public sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<Result<TResponse>> Handle(TRequest request, Func<Task<Result<TResponse>>> next, CancellationToken ct)
    {
        var context = new ValidationContext<TRequest>(request);
        var failures = new List<AppError>();
        foreach (var validator in validators)
        {
            var result = await validator.ValidateAsync(context, ct);
            failures.AddRange(result.Errors.Select(f =>
                new AppError(DomainErrorCodes.ValidationFailed, f.ErrorMessage, ToCamelCase(f.PropertyName))));
        }

        return failures.Count > 0 ? Result<TResponse>.Failure(failures) : await next();
    }

    /// <summary>Camel-cases every path segment so nested names match input names, e.g. <c>Lines[0].ProductId</c> -> <c>lines[0].productId</c>.</summary>
    internal static string ToCamelCase(string name) =>
        string.Join('.', name.Split('.').Select(s => s.Length == 0 ? s : char.ToLowerInvariant(s[0]) + s[1..]));
}
