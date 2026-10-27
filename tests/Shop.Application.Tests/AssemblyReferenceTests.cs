using FluentAssertions;

namespace Shop.Application.Tests;

public class AssemblyReferenceTests
{
    private static readonly string[] ForbiddenPrefixes =
    [
        "Shop.Infrastructure",
        "Shop.Api",
        "MongoDB",
        "HotChocolate",
    ];

    [Fact]
    public void Application_references_only_Domain_among_shop_assemblies()
    {
        var shopRefs = typeof(AssemblyMarker).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(n => n.StartsWith("Shop.", StringComparison.Ordinal))
            .ToList();

        shopRefs.Where(n => n != "Shop.Domain").Should().BeEmpty();
    }

    [Fact]
    public void Application_does_not_reference_Mongo_or_HotChocolate_or_outer_layers()
    {
        typeof(AssemblyMarker).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(n => ForbiddenPrefixes.Any(p => n.StartsWith(p, StringComparison.Ordinal)))
            .Should().BeEmpty();
    }
}
