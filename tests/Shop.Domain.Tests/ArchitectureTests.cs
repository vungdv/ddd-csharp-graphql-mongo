using System.Xml.Linq;
using FluentAssertions;

namespace Shop.Domain.Tests;

public class ArchitectureTests
{
    private static readonly string[] ForbiddenPrefixes =
    [
        "Shop.Application",
        "Shop.Infrastructure",
        "Shop.Api",
        "HotChocolate",
        "MongoDB",
        "Microsoft.Extensions.",
        "Microsoft.AspNetCore.",
    ];

    [Fact]
    public void Domain_does_not_reference_outer_layers_or_frameworks()
    {
        var referenced = typeof(AssemblyMarker).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name!)
            .ToList();

        referenced
            .Where(name => ForbiddenPrefixes.Any(p => name.StartsWith(p, StringComparison.Ordinal)))
            .Should().BeEmpty("the Domain layer must stay persistence- and transport-ignorant");
    }

    [Fact]
    public void Domain_project_has_no_package_or_project_references()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Shop.sln")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("the solution root must be discoverable");
        var csproj = Path.Combine(dir!.FullName, "src", "Shop.Domain", "Shop.Domain.csproj");
        var doc = XDocument.Load(csproj);

        doc.Descendants("PackageReference").Should().BeEmpty();
        doc.Descendants("ProjectReference").Should().BeEmpty();
    }
}
