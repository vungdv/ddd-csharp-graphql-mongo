namespace Shop.Api.GraphQL.Common;

public sealed record PageInfo(bool HasNextPage, bool HasPreviousPage, string? StartCursor, string? EndCursor);
