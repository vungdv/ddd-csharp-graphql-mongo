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

    private static string ToCamelCase(string name) =>
        name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name[1..];
}
