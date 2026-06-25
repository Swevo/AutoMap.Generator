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
    public void Map_IncompatibleType_ReportsAM004()
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

        // AM004: same property name, incompatible types, no registered mapping
        Assert.Contains(result.Diagnostics, d => d.Id == "AM004");
    }

    // ── Nested object mapping ─────────────────────────────────────────────────

    [Fact]
    public void Map_NestedObject_ResolvesViaKnownMapping()
    {
        var source = @"
using AutoMap;
namespace MyApp
{
    public class AddressDto { public string City { get; set; } = """"; }
    public class OrderDto { public int Id { get; set; } public AddressDto? Address { get; set; } }

    [Map(typeof(AddressDto))]
    public class Address { public string City { get; set; } = """"; }

    [Map(typeof(OrderDto))]
    public class Order { public int Id { get; set; } public Address? Address { get; set; } }
}";
        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        var code = GetGeneratedSource(result, "AutoMapExtensions.g.cs");
        Assert.Contains("Address = src.Address?.ToAddressDto()", code);
        Assert.Contains("Id = src.Id", code);
    }

    // ── Collection mapping ────────────────────────────────────────────────────

    [Fact]
    public void Map_ListCollection_ResolvesViaKnownMapping()
    {
        var source = @"
using AutoMap;
using System.Collections.Generic;
namespace MyApp
{
    public class ItemDto { public int Id { get; set; } }
    public class OrderDto { public List<ItemDto> Items { get; set; } = new(); }

    [Map(typeof(ItemDto))]
    public class Item { public int Id { get; set; } }

    [Map(typeof(OrderDto))]
    public class Order { public List<Item> Items { get; set; } = new(); }
}";
        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        var code = GetGeneratedSource(result, "AutoMapExtensions.g.cs");
        Assert.Contains("using System.Linq", code);
        Assert.Contains("Items = src.Items?.Select(x => x.ToItemDto()).ToList()", code);
    }

    [Fact]
    public void Map_ArrayCollection_ResolvesViaKnownMapping()
    {
        var source = @"
using AutoMap;
namespace MyApp
{
    public class ItemDto { public int Id { get; set; } }
    public class OrderDto { public ItemDto[] Items { get; set; } = System.Array.Empty<ItemDto>(); }

    [Map(typeof(ItemDto))]
    public class Item { public int Id { get; set; } }

    [Map(typeof(OrderDto))]
    public class Order { public Item[] Items { get; set; } = System.Array.Empty<Item>(); }
}";
        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        var code = GetGeneratedSource(result, "AutoMapExtensions.g.cs");
        Assert.Contains("Items = src.Items?.Select(x => x.ToItemDto()).ToArray()", code);
    }

    [Fact]
    public void Map_CollectionNoMapping_ReportsAM004()
    {
        var source = @"
using AutoMap;
using System.Collections.Generic;
namespace MyApp
{
    public class ItemDto { public int Id { get; set; } }
    public class OrderDto { public List<ItemDto> Items { get; set; } = new(); }

    // Note: Item has NO [Map] attribute → can't auto-resolve
    public class Item { public int Id { get; set; } }

    [Map(typeof(OrderDto))]
    public class Order { public List<Item> Items { get; set; } = new(); }
}";
        var result = RunGenerator(source);

        Assert.Contains(result.Diagnostics, d => d.Id == "AM004");
    }

    // ── Reverse mapping ───────────────────────────────────────────────────────

    [Fact]
    public void Map_Reverse_GeneratesBothDirections()
    {
        var source = @"
using AutoMap;
namespace MyApp
{
    public class OrderDto { public int Id { get; set; } public string Name { get; set; } = """"; }

    [Map(typeof(OrderDto), Reverse = true)]
    public class Order { public int Id { get; set; } public string Name { get; set; } = """"; }
}";
        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics);
        var code = GetGeneratedSource(result, "AutoMapExtensions.g.cs");
        // Forward
        Assert.Contains("this global::MyApp.Order src", code);
        Assert.Contains("ToOrderDto", code);
        // Reverse
        Assert.Contains("this global::MyApp.OrderDto src", code);
        Assert.Contains("ToOrder", code);
    }

    [Fact]
    public void MapFrom_Reverse_GeneratesBothDirections()
    {
        var source = @"
using AutoMap;
namespace MyApp
{
    public class Order { public int Id { get; set; } }

    [MapFrom(typeof(Order), Reverse = true)]
    public class OrderDto { public int Id { get; set; } }
}";
        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics);
        var code = GetGeneratedSource(result, "AutoMapExtensions.g.cs");
        Assert.Contains("ToOrderDto", code);
        Assert.Contains("ToOrder", code);
    }

    // ── IAutoMapper<TSource, TResult> ─────────────────────────────────────────

    [Fact]
    public void Map_GeneratesIAutoMapperImplementation()
    {
        var source = @"
using AutoMap;
namespace MyApp
{
    public class OrderDto { public int Id { get; set; } }

    [Map(typeof(OrderDto))]
    public class Order { public int Id { get; set; } }
}";
        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics);
        var code = GetGeneratedSource(result, "AutoMapExtensions.g.cs");
        Assert.Contains("IAutoMapper<global::MyApp.Order, global::MyApp.OrderDto>", code);
        Assert.Contains("OrderToOrderDtoMapper", code);
        Assert.Contains("static readonly", code);
        Assert.Contains("Instance", code);
    }

    [Fact]
    public void Map_IAutoMapperInterface_IsEmitted()
    {
        var source = @"
using AutoMap;
namespace MyApp
{
    public class OrderDto { public int Id { get; set; } }
    [Map(typeof(OrderDto))]
    public class Order { public int Id { get; set; } }
}";
        var result = RunGenerator(source);

        var interfaceFile = GetGeneratedSource(result, "AutoMapInterface.g.cs");
        Assert.Contains("IAutoMapper<in TSource, out TResult>", interfaceFile);
        Assert.Contains("TResult Map(TSource source)", interfaceFile);
    }

    // ── Constructor mapping ───────────────────────────────────────────────────

    [Fact]
    public void Map_PositionalRecord_UsesConstructorSyntax()
    {
        var source = @"
using AutoMap;
namespace MyApp
{
    public record OrderDto(int Id, string Customer);

    [Map(typeof(OrderDto))]
    public class Order { public int Id { get; set; } public string Customer { get; set; } = """"; }
}";
        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        var code = GetGeneratedSource(result, "AutoMapExtensions.g.cs");
        // Should use ctor syntax, not object initializer
        Assert.Contains("new global::MyApp.OrderDto(src.Id, src.Customer)", code);
    }

    [Fact]
    public void Map_PositionalRecord_CaseInsensitiveCtorParam()
    {
        var source = @"
using AutoMap;
namespace MyApp
{
    public record OrderDto(int id, string customer);

    [Map(typeof(OrderDto))]
    public class Order { public int Id { get; set; } public string Customer { get; set; } = """"; }
}";
        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        var code = GetGeneratedSource(result, "AutoMapExtensions.g.cs");
        Assert.Contains("new global::MyApp.OrderDto(src.Id, src.Customer)", code);
    }

    [Fact]
    public void Map_CtorParam_NoSourceMatch_EmitsAM005()
    {
        var source = @"
using AutoMap;
namespace MyApp
{
    public record OrderDto(int Id, string MissingField);

    [Map(typeof(OrderDto))]
    public class Order { public int Id { get; set; } }
}";
        var result = RunGenerator(source);

        Assert.Contains(result.Diagnostics, d => d.Id == "AM005");
        var code = GetGeneratedSource(result, "AutoMapExtensions.g.cs");
        // Unmatched param gets 'default'
        Assert.Contains("default", code);
    }

    [Fact]
    public void Map_MapConstructorAttribute_ForcesCtorMapping()
    {
        var source = @"
using AutoMap;
namespace MyApp
{
    [MapConstructor]
    public class OrderDto
    {
        public int Id { get; }
        public string Name { get; }
        public OrderDto() { }
        public OrderDto(int id, string name) { Id = id; Name = name; }
    }

    [Map(typeof(OrderDto))]
    public class Order { public int Id { get; set; } public string Name { get; set; } = """"; }
}";
        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        var code = GetGeneratedSource(result, "AutoMapExtensions.g.cs");
        Assert.Contains("new global::MyApp.OrderDto(src.Id, src.Name)", code);
    }

    [Fact]
    public void Map_CtorWithExtraInitProps_EmitsMixed()
    {
        var source = @"
using AutoMap;
namespace MyApp
{
    public class OrderDto
    {
        public string Tag { get; set; } = """";
        public OrderDto(int id) { Id = id; }
        public int Id { get; }
    }

    [Map(typeof(OrderDto))]
    public class Order { public int Id { get; set; } public string Tag { get; set; } = """"; }
}";
        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        var code = GetGeneratedSource(result, "AutoMapExtensions.g.cs");
        // Ctor param + init property mixed
        Assert.Contains("new global::MyApp.OrderDto(src.Id)", code);
        Assert.Contains("Tag = src.Tag", code);
    }

    // ── [MapWith] custom expression ───────────────────────────────────────────

    [Fact]
    public void MapWith_EmitsCustomExpression()
    {
        var source = @"
using AutoMap;
namespace MyApp
{
    public class Order { public decimal Price { get; set; } public int Id { get; set; } }

    [MapFrom(typeof(Order))]
    public class OrderDto
    {
        public int Id { get; set; }
        [MapWith(""src.Price.ToString(\""C2\"")"")]
        public string PriceFormatted { get; set; } = """";
    }
}";
        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        var code = GetGeneratedSource(result, "AutoMapExtensions.g.cs");
        Assert.Contains(@"PriceFormatted = src.Price.ToString(""C2"")", code);
        Assert.Contains("Id = src.Id", code);
    }

    [Fact]
    public void MapWith_DoesNotRequireMatchingSourceProperty()
    {
        // [MapWith] should work even if no source property with that name exists
        var source = @"
using AutoMap;
namespace MyApp
{
    public class Order { public int Id { get; set; } }

    [MapFrom(typeof(Order))]
    public class OrderDto
    {
        public int Id { get; set; }
        [MapWith(""42"")]
        public int Computed { get; set; }
    }
}";
        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        var code = GetGeneratedSource(result, "AutoMapExtensions.g.cs");
        Assert.Contains("Computed = 42", code);
    }

    [Fact]
    public void MapWith_WithMapIgnore_MapIgnoreWins()
    {
        // [MapIgnore] takes priority over [MapWith] — both can't coexist but MapIgnore is checked first
        var source = @"
using AutoMap;
namespace MyApp
{
    public class Order { public int Id { get; set; } }

    [MapFrom(typeof(Order))]
    public class OrderDto
    {
        public int Id { get; set; }
        [MapIgnore]
        [MapWith(""99"")]
        public int ShouldBeIgnored { get; set; }
    }
}";
        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        var code = GetGeneratedSource(result, "AutoMapExtensions.g.cs");
        Assert.DoesNotContain("ShouldBeIgnored", code);
    }

    [Fact]
    public void MapWith_WithConstructorMapping_Works()
    {
        var source = @"
using AutoMap;
namespace MyApp
{
    public class Order { public int Id { get; set; } public decimal Price { get; set; } }

    [MapFrom(typeof(Order))]
    public class OrderDto
    {
        public int Id { get; set; }
        [MapWith(""src.Price * 100m"")]
        public decimal PricePence { get; set; }
    }
}";
        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        var code = GetGeneratedSource(result, "AutoMapExtensions.g.cs");
        Assert.Contains("PricePence = src.Price * 100m", code);
        Assert.Contains("Id = src.Id", code);
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
