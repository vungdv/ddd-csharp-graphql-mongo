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

    [Fact]
    public void Ordering_does_not_depend_on_the_Product_aggregate()
    {
        var forbidden = new HashSet<Type> { typeof(Shop.Domain.Catalog.Product), typeof(Shop.Domain.Catalog.IProductRepository) };

        var offenders = OrderingTypes()
            .SelectMany(t => ReferencedTypes(t).Select(r => (Type: t.FullName, Referenced: r)))
            .Where(x => forbidden.Contains(x.Referenced))
            .ToList();

        offenders.Should().BeEmpty("Ordering may reference products by ProductId/Sku only (spec 3.3)");
    }

    [Fact]
    public void Reference_scanner_detects_known_references_including_method_bodies()
    {
        var orderLine = ReferencedTypes(typeof(Shop.Domain.Ordering.OrderLine)).ToList();
        orderLine.Should().Contain(typeof(Shop.Domain.Catalog.ProductId), "signatures and property types");
        orderLine.Should().Contain(typeof(Shop.Domain.Catalog.Sku), "a static call inside OrderLine.Create");

        // Control with a known-bad body: a static call and a `new` that appear only in IL.
        ReferencedTypes(typeof(ScannerControl)).Should().Contain(typeof(Shop.Domain.Catalog.Product), "static call in body");
        ReferencedTypes(typeof(ScannerControl)).Should().Contain(typeof(Shop.Domain.Catalog.ProductCreated), "newobj in body");
    }

    private static class ScannerControl
    {
        public static void Run()
        {
            _ = Shop.Domain.Catalog.Product.Create(null!, "n", null, null!, "c", 0, null!);
            _ = new Shop.Domain.Catalog.ProductCreated(default, null!, default);
        }
    }

    private static IEnumerable<Type> OrderingTypes() =>
        typeof(AssemblyMarker).Assembly.GetTypes().Where(t => t.Namespace == "Shop.Domain.Ordering");

    private const System.Reflection.BindingFlags All = System.Reflection.BindingFlags.Public
        | System.Reflection.BindingFlags.NonPublic
        | System.Reflection.BindingFlags.Instance
        | System.Reflection.BindingFlags.Static
        | System.Reflection.BindingFlags.DeclaredOnly;

    private static readonly Dictionary<short, System.Reflection.Emit.OpCode> OpCodes = typeof(System.Reflection.Emit.OpCodes)
        .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
        .Select(f => (System.Reflection.Emit.OpCode)f.GetValue(null)!)
        .ToDictionary(o => o.Value);

    private static IEnumerable<Type> ReferencedTypes(Type type)
    {
        var types = new List<Type>();
        types.AddRange(type.GetFields(All).Select(f => f.FieldType));
        types.AddRange(type.GetProperties(All).Select(p => p.PropertyType));
        if (type.BaseType is not null)
        {
            types.Add(type.BaseType);
        }

        types.AddRange(type.GetInterfaces());
        foreach (var m in type.GetMethods(All).Cast<System.Reflection.MethodBase>().Concat(type.GetConstructors(All)))
        {
            types.AddRange(m.GetParameters().Select(p => p.ParameterType));
            if (m is System.Reflection.MethodInfo mi)
            {
                types.Add(mi.ReturnType);
            }

            var body = m.GetMethodBody();
            if (body is null)
            {
                continue;
            }

            types.AddRange(body.LocalVariables.Select(l => l.LocalType));
            types.AddRange(IlTokenTypes(type, m, body.GetILAsByteArray()!));
        }

        return types.SelectMany(Flatten).Distinct();
    }

    private static IEnumerable<Type> IlTokenTypes(Type owner, System.Reflection.MethodBase method, byte[] il)
    {
        var typeArgs = owner.IsGenericType ? owner.GetGenericArguments() : null;
        var methodArgs = method.IsGenericMethod ? method.GetGenericArguments() : null;
        var module = owner.Module;
        var result = new List<Type>();
        var pos = 0;
        while (pos < il.Length)
        {
            var code = il[pos++];
            var op = code == 0xFE ? OpCodes[(short)(0xFE00 | il[pos++])] : OpCodes[code];
            switch (op.OperandType)
            {
                case System.Reflection.Emit.OperandType.InlineMethod:
                case System.Reflection.Emit.OperandType.InlineField:
                case System.Reflection.Emit.OperandType.InlineType:
                case System.Reflection.Emit.OperandType.InlineTok:
                    var token = BitConverter.ToInt32(il, pos);
                    var member = module.ResolveMember(token, typeArgs, methodArgs);
                    switch (member)
                    {
                        case Type t:
                            result.Add(t);
                            break;
                        case System.Reflection.MethodBase mb:
                            if (mb.DeclaringType is not null) result.Add(mb.DeclaringType);
                            result.AddRange(mb.GetParameters().Select(p => p.ParameterType));
                            if (mb is System.Reflection.MethodInfo r) result.Add(r.ReturnType);
                            break;
                        case System.Reflection.FieldInfo f:
                            if (f.DeclaringType is not null) result.Add(f.DeclaringType);
                            result.Add(f.FieldType);
                            break;
                    }

                    pos += 4;
                    break;
                case System.Reflection.Emit.OperandType.InlineNone:
                    break;
                case System.Reflection.Emit.OperandType.ShortInlineBrTarget:
                case System.Reflection.Emit.OperandType.ShortInlineI:
                case System.Reflection.Emit.OperandType.ShortInlineVar:
                    pos += 1;
                    break;
                case System.Reflection.Emit.OperandType.InlineVar:
                    pos += 2;
                    break;
                case System.Reflection.Emit.OperandType.InlineI8:
                case System.Reflection.Emit.OperandType.InlineR:
                    pos += 8;
                    break;
                case System.Reflection.Emit.OperandType.InlineSwitch:
                    var n = BitConverter.ToInt32(il, pos);
                    pos += 4 + (4 * n);
                    break;
                default: // InlineI, InlineBrTarget, InlineString, InlineSig, ShortInlineR
                    pos += 4;
                    break;
            }
        }

        return result;
    }

    private static IEnumerable<Type> Flatten(Type t)
    {
        yield return t;
        if (t.HasElementType)
        {
            foreach (var e in Flatten(t.GetElementType()!)) yield return e;
        }

        if (t.IsGenericType)
        {
            foreach (var e in t.GetGenericArguments().SelectMany(Flatten)) yield return e;
        }
    }
}
