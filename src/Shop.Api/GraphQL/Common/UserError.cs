using Shop.Application.Abstractions;

namespace Shop.Api.GraphQL.Common;

/// <summary>Expected, client-actionable failure returned in a mutation payload's <c>errors</c>.</summary>
public sealed record UserError(string Code, string Message, string? Field = null)
{
    public static IReadOnlyList<UserError> From(IEnumerable<AppError> errors) =>
        errors.Select(e => new UserError(e.Code, e.Message, e.Field)).ToList();
}
