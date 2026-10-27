using HotChocolate;
using Shop.Application.Abstractions;

namespace Shop.Api.GraphQL.Common;

public static class ResultErrors
{
    /// <summary>For queries: the value on success, otherwise a coded top-level GraphQL error.</summary>
    public static T? ValueOrThrow<T>(this Shop.Application.Abstractions.Result<T> result) =>
        result.IsSuccess ? result.Value : throw ToGraphQLException(result.Errors);

    /// <summary>For queries: expected failures (e.g. invalid cursor) become a coded top-level GraphQL error.</summary>
    public static GraphQLException ToGraphQLException(IEnumerable<AppError> errors) =>
        new(errors.Select(e =>
        {
            var b = ErrorBuilder.New().SetMessage(e.Message).SetCode(e.Code);
            if (e.Field is not null)
            {
                b.SetExtension("field", e.Field);
            }

            return b.Build();
        }));
}
