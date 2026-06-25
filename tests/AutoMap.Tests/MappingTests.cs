using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Immutable;
using System.Reflection;
using Xunit;

namespace AutoMap.Tests;

public class MappingTests
{
    // ── [Map] basic ───────────────────────────────────────────────────────────

    [Fact]
    public void Map_GeneratesExtensionMethod()
    {
        var source = @"
using AutoMap;
namespace MyApp
{
    public class OrderDto { public int Id { get; set; } public string Name { get; set; } = """"; }

    [Map(typeof(OrderDto))]
    public class Order { public int Id { get; set; } public string Name { get; set; } = """"; public string Internal { get; set; } = """"; }
}";
        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics);
        var code = GetGeneratedSource(result, "AutoMapExtensions.g.cs");
        Assert.Contains("ToOrderDto", code);
        Assert.Contains("Id = src.Id", code);
        Assert.Contains("Name = src.Name", code);
        Assert.DoesNotContain("Internal", code); // no dest property → not mapped
    }

    [Fact]
    public void Map_CustomMethodName()
    {
        var source = @"
using AutoMap;
namespace MyApp
{
    public class OrderDto { public int Id { get; set; } }

    [Map(typeof(OrderDto), MethodName = ""AsDto"")]
    public class Order { public int Id { get; set; } }
}";
        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics);
        var code = GetGeneratedSource(result, "AutoMapExtensions.g.cs");
        Assert.Contains("AsDto", code);
        Assert.DoesNotContain("ToOrderDto", code);
    }

    [Fact]
    public void Map_MultipleMappingsOnSameClass()
    {
        var source = @"
using AutoMap;
namespace MyApp
{
    public class OrderDto { public int Id { get; set; } }
    public class OrderSummary { public int Id { get; set; } }

    [Map(typeof(OrderDto))]
    [Map(typeof(OrderSummary))]
    public class Order { public int Id { get; set; } }
}";
        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics);
        var code = GetGeneratedSource(result, "AutoMapExtensions.g.cs");
        Assert.Contains("ToOrderDto", code);
        Assert.Contains("ToOrderSummary", code);
    }

    // ── [MapFrom] ─────────────────────────────────────────────────────────────

    [Fact]
    public void MapFrom_GeneratesExtensionOnSourceType()
    {
        var source = @"
using AutoMap;
namespace MyApp
{
    public class Order { public int Id { get; set; } public string Customer { get; set; } = """"; }

    [MapFrom(typeof(Order))]
    public class OrderDto { public int Id { get; set; } public string Customer { get; set; } = """"; }
}";
        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics);
        var code = GetGeneratedSource(result, "AutoMapExtensions.g.cs");
        // Extension method is on Order (source), returning OrderDto
        Assert.Contains("this global::MyApp.Order src", code);
        Assert.Contains("global::MyApp.OrderDto", code);
        Assert.Contains("ToOrderDto", code);
    }

    // ── [MapIgnore] ───────────────────────────────────────────────────────────

    [Fact]
    public void MapIgnore_ExcludesProperty()
    {
        var source = @"
using AutoMap;
namespace MyApp
{
    public class Order { public int Id { get; set; } public string Secret { get; set; } = """"; }

    [MapFrom(typeof(Order))]
    public class OrderDto
    {
        public int Id { get; set; }
        [MapIgnore] public string Secret { get; set; } = """";
    }
}";
        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics);
        var code = GetGeneratedSource(result, "AutoMapExtensions.g.cs");
        Assert.Contains("Id = src.Id", code);
        Assert.DoesNotContain("Secret", code);
    }

    // ── [MapProperty] ─────────────────────────────────────────────────────────

    [Fact]
    public void MapProperty_MapsFromDifferentName()
    {
        var source = @"
using AutoMap;
namespace MyApp
{
    public class Order { public string CustomerName { get; set; } = """"; }

    [MapFrom(typeof(Order))]
    public class OrderDto
    {
        [MapProperty(""CustomerName"")]
        public string Client { get; set; } = """";
    }
}";
        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics);
        var code = GetGeneratedSource(result, "AutoMapExtensions.g.cs");
        Assert.Contains("Client = src.CustomerName", code);
    }

    [Fact]
    public void MapProperty_InvalidSourceName_ReportsAM002()
    {
        var source = @"
using AutoMap;
namespace MyApp
{
    public class Order { public string Name { get; set; } = """"; }

    [MapFrom(typeof(Order))]
    public class OrderDto
    {
        [MapProperty(""NonExistent"")]
        public string Client { get; set; } = """";
    }
}";
        var result = RunGenerator(source);

        Assert.Contains(result.Diagnostics, d => d.Id == "AM002");
    }

    // ── Struct source (no null check) ─────────────────────────────────────────

    [Fact]
    public void Map_StructSource_NoNullCheck()
    {
        var source = @"
using AutoMap;
namespace MyApp
{
    public class PointDto { public int X { get; set; } public int Y { get; set; } }

    [Map(typeof(PointDto))]
    public struct Point { public int X { get; set; } public int Y { get; set; } }
}";
        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics);
        var code = GetGeneratedSource(result, "AutoMapExtensions.g.cs");
        Assert.DoesNotContain("ArgumentNullException", code);
        Assert.Contains("X = src.X", code);
    }

    // ── Init-only destination properties ─────────────────────────────────────

    [Fact]
    public void Map_InitOnlyDestination_Works()
    {
        var source = @"
using AutoMap;
namespace MyApp
{
    public class OrderDto { public int Id { get; init; } public string Name { get; init; } = """"; }

    [Map(typeof(OrderDto))]
    public class Order { public int Id { get; set; } public string Name { get; set; } = """"; }
}";
        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics);
        var code = GetGeneratedSource(result, "AutoMapExtensions.g.cs");
        Assert.Contains("Id = src.Id", code);
        Assert.Contains("Name = src.Name", code);
    }

    // ── Inherited properties ──────────────────────────────────────────────────

    [Fact]
    public void Map_InheritedProperties_AreMapped()
    {
        var source = @"
using AutoMap;
namespace MyApp
{
    public class EntityDto { public int Id { get; set; } public string Name { get; set; } = """"; }
    public abstract class Entity { public int Id { get; set; } }

    [Map(typeof(EntityDto))]
    public class Order : Entity { public string Name { get; set; } = """"; }
}";
        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics);
        var code = GetGeneratedSource(result, "AutoMapExtensions.g.cs");
        Assert.Contains("Id = src.Id", code);     // inherited
        Assert.Contains("Name = src.Name", code); // own
    }

    // ── AM001: no property matches ────────────────────────────────────────────

    [Fact]
    public void Map_NoMatchingProperties_ReportsAM001()
    {
        var source = @"
using AutoMap;
namespace MyApp
{
    public class OrderDto { public string Foo { get; set; } = """"; }

    [Map(typeof(OrderDto))]
    public class Order { public int Bar { get; set; } }
}";
        var result = RunGenerator(source);

        Assert.Contains(result.Diagnostics, d => d.Id == "AM001");
    }

    // ── Type mismatch silently skips ──────────────────────────────────────────

    [Fact]
    public void Map_IncompatibleType_PropertySkipped()
    {
        var source = @"
using AutoMap;
namespace MyApp
{
    public class OrderDto { public string Id { get; set; } = """"; }  // string

    [Map(typeof(OrderDto))]
    public class Order { public int Id { get; set; } }  // int — no implicit string→int
}";
        var result = RunGenerator(source);

        // AM001 because the only mappable property was skipped
        Assert.Contains(result.Diagnostics, d => d.Id == "AM001");
    }

    // ── Implicit numeric widening ─────────────────────────────────────────────

    [Fact]
    public void Map_ImplicitWidening_Mapped()
    {
        var source = @"
using AutoMap;
namespace MyApp
{
    public class OrderDto { public long Id { get; set; } }

    [Map(typeof(OrderDto))]
    public class Order { public int Id { get; set; } }  // int → long implicit
}";
        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics);
        var code = GetGeneratedSource(result, "AutoMapExtensions.g.cs");
        Assert.Contains("Id = src.Id", code);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static GeneratorDriverRunResult RunGenerator(string source)
    {
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { CSharpSyntaxTree.ParseText(source) },
            GetReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new AutoMapGenerator();
        var driver = CSharpGeneratorDriver
            .Create(generator)
            .RunGeneratorsAndUpdateCompilation(compilation, out _, out _);

        return driver.GetRunResult();
    }

    private static string GetGeneratedSource(GeneratorDriverRunResult result, string hintName)
    {
        var file = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.EndsWith(hintName, StringComparison.OrdinalIgnoreCase));
        return file?.GetText().ToString() ?? string.Empty;
    }

    private static IEnumerable<MetadataReference> GetReferences()
    {
        var refs = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Attribute).Assembly.Location),
        };

        // Add System.Runtime
        var runtimeAssembly = Assembly.Load("System.Runtime");
        refs.Add(MetadataReference.CreateFromFile(runtimeAssembly.Location));

        return refs;
    }
}
