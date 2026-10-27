namespace Shop.Api.GraphQL.Common;

/// <summary>Server-side request limits (spec 7.3).</summary>
public static class GraphQlLimits
{
    /// <summary>Maximum selection depth of an executed operation.</summary>
    public const int MaxExecutionDepth = 10;

    /// <summary>Maximum summed field cost (execution cost) of one operation; a page of 100 orders with lines and product lookups costs about 2300.</summary>
    public const double MaxFieldCost = 5000;

    /// <summary>Maximum summed type cost (estimated objects returned) of one operation.</summary>
    public const double MaxTypeCost = 10000;
}

/// <summary>Effective limits; defaults come from <see cref="GraphQlLimits"/>, overridable via <c>GraphQL:*</c> configuration.</summary>
public sealed record GraphQlLimitsOptions(int MaxExecutionDepth, double MaxFieldCost, double MaxTypeCost)
{
    public const string SectionName = "GraphQL";

    public static GraphQlLimitsOptions From(IConfiguration configuration) => new(
        configuration.GetValue($"{SectionName}:MaxExecutionDepth", GraphQlLimits.MaxExecutionDepth),
        configuration.GetValue($"{SectionName}:MaxFieldCost", GraphQlLimits.MaxFieldCost),
        configuration.GetValue($"{SectionName}:MaxTypeCost", GraphQlLimits.MaxTypeCost));
}
