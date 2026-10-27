using System.Diagnostics;
using HotChocolate;

namespace Shop.Api.GraphQL.Common;

/// <summary>
/// Unexpected exceptions surface as a generic error carrying <c>extensions.traceId</c>; details go to logs only.
/// Errors raised deliberately (<see cref="GraphQLException"/>) and engine validation errors pass through.
/// </summary>
public sealed class UnexpectedErrorFilter(ILogger<UnexpectedErrorFilter> logger) : IErrorFilter
{
    public const string GenericMessage = "An unexpected error occurred.";
    public const string Code = "INTERNAL_ERROR";

    public IError OnError(IError error)
    {
        // Engine-generated errors (variable/input coercion, validation, ...) carry a code or no exception: pass through.
        if (error.Exception is null or GraphQLException || error.Code is not null)
        {
            return error;
        }

        var traceId = Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");
        logger.LogError(error.Exception, "Unhandled GraphQL exception. traceId={TraceId} path={Path}", traceId, error.Path?.Print());

        return ErrorBuilder.New()
            .SetMessage(GenericMessage)
            .SetCode(Code)
            .SetPath(error.Path)
            .SetExtension("traceId", traceId)
            .Build();
    }
}
