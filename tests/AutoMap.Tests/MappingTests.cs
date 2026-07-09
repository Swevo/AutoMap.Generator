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

    [Fact]
    public void Map_CollectionHelper_GeneratesIEnumerableExtension()
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
        var code = GetGeneratedSource(result, "AutoMapExtensions.g.cs");
        Assert.Contains("ToOrderDtos(", code);
        Assert.Contains("IEnumerable<global::MyApp.OrderDto>", code);
        Assert.Contains("IEnumerable<global::MyApp.Order>", code);
        Assert.Contains("src.Select(x => x.ToOrderDto())", code);
    }

    [Fact]
    public void Map_CollectionHelper_UsesCorrectMethodName()
    {
        var source = @"
using AutoMap;
namespace MyApp
{
    public class CustomerSummaryDto { public string Name { get; set; } = """"; }
    [Map(typeof(CustomerSummaryDto))]
    public class Customer { public string Name { get; set; } = """"; }
}";
        var result = RunGenerator(source);
        var code = GetGeneratedSource(result, "AutoMapExtensions.g.cs");
        Assert.Contains("ToCustomerSummaryDtos(", code);
        Assert.Contains("src.Select(x => x.ToCustomerSummaryDto())", code);
    }

    // ── Reverse mapping ───────────────────────────────────────────────────────

    [Fact]
    public void Reverse_SimpleRecord_GeneratesReverseMethod()
    {
        var source = @"
using AutoMap;
namespace MyApp
{
    public record OrderDto(int Id, string CustomerName);
    [Map(typeof(OrderDto), Reverse = true)]
    public record Order(int Id, string CustomerName);
}";
        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics);
        var code = GetGeneratedSource(result, "AutoMapExtensions.g.cs");
        Assert.Contains("public static global::MyApp.Order ToOrder(this global::MyApp.OrderDto src)", code);
        Assert.Contains("new global::MyApp.Order(src.Id, src.CustomerName)", code);
    }

    [Fact]
    public void Reverse_BothDirectionsWork()
    {
        var source = @"
using AutoMap;
namespace MyApp
{
    public class Order { public int Id { get; set; } public string CustomerName { get; set; } = """"; }
    [MapFrom(typeof(Order), Reverse = true)]
    public class OrderDto { public int Id { get; set; } public string CustomerName { get; set; } = """"; }
}";
        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics);
        var code = GetGeneratedSource(result, "AutoMapExtensions.g.cs");
        Assert.Contains("public static global::MyApp.OrderDto ToOrderDto(this global::MyApp.Order src)", code);
        Assert.Contains("public static global::MyApp.Order ToOrder(this global::MyApp.OrderDto src)", code);
        Assert.Contains("CustomerName = src.CustomerName", code);
    }

    [Fact]
    public void Reverse_MapIgnore_SkippedInReverse()
    {
        var source = @"
using AutoMap;
namespace MyApp
{
    public class Order { public int Id { get; set; } public string Secret { get; set; } = """"; }

    [MapFrom(typeof(Order), Reverse = true)]
    public class OrderDto
    {
        public int Id { get; set; }
        [MapIgnore] public string Secret { get; set; } = """";
    }
}";
        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var code = GetGeneratedSource(result, "AutoMapExtensions.g.cs");
        Assert.Contains("public static global::MyApp.Order ToOrder(this global::MyApp.OrderDto src)", code);
        Assert.Contains("Id = src.Id", code);
        Assert.DoesNotContain("Secret = src.Secret", code);
    }

    [Fact]
    public void Reverse_MapWith_SkippedInReverse()
    {
        var source = @"
using AutoMap;
namespace MyApp
{
    public class Order { public int Id { get; set; } public decimal Price { get; set; } }

    [MapFrom(typeof(Order), Reverse = true)]
    public class OrderDto
    {
        public int Id { get; set; }
        [MapWith(""src.Price.ToString(\""C2\"")"")]
        public string PriceLabel { get; set; } = """";
    }
}";
        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var code = GetGeneratedSource(result, "AutoMapExtensions.g.cs");
        Assert.Contains("public static global::MyApp.Order ToOrder(this global::MyApp.OrderDto src)", code);
        Assert.Contains(@"PriceLabel = src.Price.ToString(""C2"")", code);
        Assert.DoesNotContain("Price = src.PriceLabel", code);
    }

    [Fact]
    public void Reverse_MethodNameCorrect()
    {
        var source = @"
using AutoMap;
namespace MyApp
{
    public class OrderDto { public int Id { get; set; } }

    [Map(typeof(OrderDto), Reverse = true)]
    public class Order { public int Id { get; set; } }
}";
        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics);
        var code = GetGeneratedSource(result, "AutoMapExtensions.g.cs");
        Assert.Contains("public static global::MyApp.Order ToOrder(this global::MyApp.OrderDto src)", code);
        Assert.DoesNotContain("public static global::MyApp.Order ToOrderDto(this global::MyApp.OrderDto src)", code);
    }

    [Fact]
    public void Reverse_MapProperty_UsesOriginalSourceName()
    {
        var source = @"
using AutoMap;
namespace MyApp
{
    public class Order { public string CustomerName { get; set; } = """"; }

    [MapFrom(typeof(Order), Reverse = true)]
    public class OrderDto
    {
        [MapProperty(""CustomerName"")]
        public string Client { get; set; } = """";
    }
}";
        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var code = GetGeneratedSource(result, "AutoMapExtensions.g.cs");
        Assert.Contains("Client = src.CustomerName", code);
        Assert.Contains("CustomerName = src.Client", code);
    }

    [Fact]
    public void Reverse_NoReverseProperties_ReportsAM007()
    {
        var source = @"
using AutoMap;
namespace MyApp
{
    public class Order { public decimal Price { get; set; } }

    [MapFrom(typeof(Order), Reverse = true)]
    public class OrderDto
    {
        [MapWith(""src.Price.ToString(\""C2\"")"")]
        public string PriceLabel { get; set; } = """";
    }
}";
        var result = RunGenerator(source);

        Assert.Contains(result.Diagnostics, d => d.Id == "AM007");
        var code = GetGeneratedSource(result, "AutoMapExtensions.g.cs");
        Assert.DoesNotContain("public static global::MyApp.Order ToOrder(this global::MyApp.OrderDto src)", code);
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

    // ── [MapWhen] conditional mapping ─────────────────────────────────────────

    [Fact]
    public void MapWhen_BasicCondition_WrapsInTernary()
    {
        var source = @"
using AutoMap;
namespace MyApp
{
    public class Order { public bool IsActive { get; set; } public string Name { get; set; } = """"; }

    [MapFrom(typeof(Order))]
    public class OrderDto
    {
        [MapWhen(""src.IsActive"")]
        public string Name { get; set; } = """";
    }
}";
        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        var code = GetGeneratedSource(result, "AutoMapExtensions.g.cs");
        Assert.Contains("src.IsActive ? src.Name : default", code);
    }

    [Fact]
    public void MapWhen_WithFallback_UsesFallback()
    {
        var source = @"
using AutoMap;
namespace MyApp
{
    public class Order { public bool IsPremium { get; set; } public string Tag { get; set; } = """"; }

    [MapFrom(typeof(Order))]
    public class OrderDto
    {
        [MapWhen(""src.IsPremium"", Fallback = ""\""Standard\"""")]
        public string Tag { get; set; } = """";
    }
}";
        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        var code = GetGeneratedSource(result, "AutoMapExtensions.g.cs");
        Assert.Contains(@"src.IsPremium ? src.Tag : ""Standard""", code);
    }

    [Fact]
    public void MapWhen_WithMapWith_MapWithBecomesTrue()
    {
        var source = @"
using AutoMap;
namespace MyApp
{
    public class Order { public bool IsActive { get; set; } public decimal Price { get; set; } }

    [MapFrom(typeof(Order))]
    public class OrderDto
    {
        [MapWhen(""src.IsActive"")]
        [MapWith(""src.Price.ToString(\""C2\"")"")]
        public string PriceFormatted { get; set; } = """";
    }
}";
        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        var code = GetGeneratedSource(result, "AutoMapExtensions.g.cs");
        Assert.Contains(@"src.IsActive ? src.Price.ToString(""C2"") : default", code);
    }

    [Fact]
    public void MapWhen_OnFlattenedProp_WrapsFlattened()
    {
        var source = @"
using AutoMap;
namespace MyApp
{
    public class Customer { public string Name { get; set; } = """"; }
    public class Order    { public bool IsKnown { get; set; } public Customer? Customer { get; set; } }

    [MapFrom(typeof(Order))]
    public class OrderDto
    {
        [MapWhen(""src.IsKnown"", Fallback = ""\""Anonymous\"""")]
        public string CustomerName { get; set; } = """";
    }
}";
        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        var code = GetGeneratedSource(result, "AutoMapExtensions.g.cs");
        Assert.Contains(@"src.IsKnown ? src.Customer?.Name : ""Anonymous""", code);
    }

    [Fact]
    public void MapWhen_WithMapIgnore_IgnoreWins()
    {
        var source = @"
using AutoMap;
namespace MyApp
{
    public class Order { public bool Flag { get; set; } public string Tag { get; set; } = """"; }

    [MapFrom(typeof(Order))]
    public class OrderDto
    {
        [MapIgnore]
        [MapWhen(""src.Flag"")]
        public string Tag { get; set; } = """";
    }
}";
        var result = RunGenerator(source);

        var code = GetGeneratedSource(result, "AutoMapExtensions.g.cs");
        Assert.DoesNotContain("Tag", code);
    }

    [Fact]
    public void MapWhen_NoFallback_EmitsDefault()
    {
        var source = @"
using AutoMap;
namespace MyApp
{
    public class Order { public bool IsActive { get; set; } public int Count { get; set; } }

    [MapFrom(typeof(Order))]
    public class OrderDto
    {
        [MapWhen(""src.IsActive"")]
        public int Count { get; set; }
    }
}";
        var result = RunGenerator(source);

        var code = GetGeneratedSource(result, "AutoMapExtensions.g.cs");
        Assert.Contains("src.IsActive ? src.Count : default", code);
    }

    // ── Enum mapping ──────────────────────────────────────────────────────────

    [Fact]
    public void Map_CrossEnum_MappedByName()
    {
        var source = @"
using AutoMap;
namespace MyApp
{
    public enum OrderStatus    { Pending, Active, Cancelled }
    public enum OrderStatusDto { Pending, Active, Cancelled }

    public class Order    { public int Id { get; set; } public OrderStatus Status { get; set; } }
    public class OrderDtoC { public int Id { get; set; } public OrderStatusDto Status { get; set; } }

    [Map(typeof(OrderDtoC))]
    public class OrderSrc { public int Id { get; set; } public OrderStatus Status { get; set; } }
}";
        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        var code = GetGeneratedSource(result, "AutoMapExtensions.g.cs");
        Assert.Contains("switch", code);
        Assert.Contains("global::MyApp.OrderStatus.Pending => global::MyApp.OrderStatusDto.Pending", code);
        Assert.Contains("global::MyApp.OrderStatus.Active => global::MyApp.OrderStatusDto.Active", code);
        Assert.Contains("_ => default", code);
    }

    [Fact]
    public void Map_CrossEnum_MapEnumRename()
    {
        var source = @"
using AutoMap;
namespace MyApp
{
    public enum SrcStatus { [MapEnum(""Running"")] Active, Done }
    public enum DstStatus { Running, Done }

    [MapFrom(typeof(SrcStatus))]
    public class Dummy { }  // just to pull in the attribute

    public class Order    { public SrcStatus Status { get; set; } }
    public class OrderDto { public DstStatus Status { get; set; } }

    [Map(typeof(OrderDto))]
    public class OrderSrc { public SrcStatus Status { get; set; } }
}";
        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        var code = GetGeneratedSource(result, "AutoMapExtensions.g.cs");
        // [MapEnum("Running")] on Active → should map to DstStatus.Running
        Assert.Contains("global::MyApp.SrcStatus.Active => global::MyApp.DstStatus.Running", code);
        Assert.Contains("global::MyApp.SrcStatus.Done => global::MyApp.DstStatus.Done", code);
    }

    [Fact]
    public void Map_CrossEnum_UnmatchedValue_EmitsAM006()
    {
        var source = @"
using AutoMap;
namespace MyApp
{
    public enum SrcStatus { Active, Pending, Unknown }
    public enum DstStatus { Active, Pending }  // no Unknown

    public class OrderSrc { public SrcStatus Status { get; set; } }
    public class OrderDto { public DstStatus Status { get; set; } }

    [Map(typeof(OrderDto))]
    public class OrderMap { public SrcStatus Status { get; set; } }
}";
        var result = RunGenerator(source);

        Assert.Contains(result.Diagnostics, d => d.Id == "AM006");
        var code = GetGeneratedSource(result, "AutoMapExtensions.g.cs");
        Assert.Contains("_ => default", code);
    }

    [Fact]
    public void Map_SameEnumType_StillMapsDirectly()
    {
        // Same enum type → identity conversion → should NOT emit switch expression
        var source = @"
using AutoMap;
namespace MyApp
{
    public enum Status { Active, Inactive }
    public class OrderDto { public Status Status { get; set; } }

    [Map(typeof(OrderDto))]
    public class Order { public Status Status { get; set; } }
}";
        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics);
        var code = GetGeneratedSource(result, "AutoMapExtensions.g.cs");
        Assert.Contains("Status = src.Status", code);
        Assert.DoesNotContain("switch", code);
    }

    // ── Flattening ────────────────────────────────────────────────────────────

    [Fact]
    public void Flatten_OneLevel_MapsAutomatically()
    {
        var source = @"
using AutoMap;
namespace MyApp
{
    public class Customer { public string Name { get; set; } = """"; }
    public class Order    { public int Id { get; set; } public Customer Customer { get; set; } = new(); }

    [MapFrom(typeof(Order))]
    public class OrderDto { public int Id { get; set; } public string CustomerName { get; set; } = """"; }
}";
        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        var code = GetGeneratedSource(result, "AutoMapExtensions.g.cs");
        Assert.Contains("CustomerName = src.Customer?.Name", code);
        Assert.Contains("Id = src.Id", code);
    }

    [Fact]
    public void Flatten_TwoLevels_MapsAutomatically()
    {
        var source = @"
using AutoMap;
namespace MyApp
{
    public class Address  { public string City { get; set; } = """"; }
    public class Customer { public Address Address { get; set; } = new(); }
    public class Order    { public Customer Customer { get; set; } = new(); }

    [MapFrom(typeof(Order))]
    public class OrderDto { public string CustomerAddressCity { get; set; } = """"; }
}";
        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        var code = GetGeneratedSource(result, "AutoMapExtensions.g.cs");
        Assert.Contains("CustomerAddressCity = src.Customer?.Address?.City", code);
    }

    [Fact]
    public void Flatten_DirectMatchTakesPriorityOverFlatten()
    {
        // If source has a direct property CustomerName, use it — don't flatten
        var source = @"
using AutoMap;
namespace MyApp
{
    public class Customer { public string Name { get; set; } = """"; }
    public class Order
    {
        public string CustomerName { get; set; } = """"; // direct match
        public Customer Customer { get; set; } = new(); // flatten candidate
    }

    [MapFrom(typeof(Order))]
    public class OrderDto { public string CustomerName { get; set; } = """"; }
}";
        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        var code = GetGeneratedSource(result, "AutoMapExtensions.g.cs");
        // Direct match wins — no null-conditional
        Assert.Contains("CustomerName = src.CustomerName", code);
        Assert.DoesNotContain("src.Customer?.Name", code);
    }

    [Fact]
    public void Flatten_ValueTypeIntermediate_NoNullConditional()
    {
        var source = @"
using AutoMap;
namespace MyApp
{
    public struct Point { public int X { get; set; } public int Y { get; set; } }
    public class Shape  { public Point Center { get; set; } }

    [MapFrom(typeof(Shape))]
    public class ShapeDto { public int CenterX { get; set; } public int CenterY { get; set; } }
}";
        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        var code = GetGeneratedSource(result, "AutoMapExtensions.g.cs");
        // Struct → no null-conditional
        Assert.Contains("CenterX = src.Center.X", code);
        Assert.Contains("CenterY = src.Center.Y", code);
    }

    // ── [MapDefault] null substitution ───────────────────────────────────────

    [Fact]
    public void MapDefault_OnNormalProp_EmitsNullCoalescing()
    {
        var source = @"
using AutoMap;
namespace MyApp
{
    public class Order { public string? Name { get; set; } }

    [MapFrom(typeof(Order))]
    public class OrderDto
    {
        [MapDefault(""\""Unknown\"""")]
        public string Name { get; set; } = """";
    }
}";
        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        var code = GetGeneratedSource(result, "AutoMapExtensions.g.cs");
        Assert.Contains(@"Name = src.Name ?? ""Unknown""", code);
    }

    [Fact]
    public void MapDefault_OnFlattenedProp_EmitsNullCoalescing()
    {
        var source = @"
using AutoMap;
namespace MyApp
{
    public class Customer { public string? Name { get; set; } }
    public class Order    { public Customer? Customer { get; set; } }

    [MapFrom(typeof(Order))]
    public class OrderDto
    {
        [MapDefault(""\""Guest\"""")]
        public string CustomerName { get; set; } = """";
    }
}";
        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        var code = GetGeneratedSource(result, "AutoMapExtensions.g.cs");
        Assert.Contains(@"CustomerName = src.Customer?.Name ?? ""Guest""", code);
    }

    [Fact]
    public void MapDefault_WithMapIgnore_IgnoreWins()
    {
        var source = @"
using AutoMap;
namespace MyApp
{
    public class Order { public string? Tag { get; set; } }

    [MapFrom(typeof(Order))]
    public class OrderDto
    {
        [MapIgnore]
        [MapDefault(""\""x\"""")]
        public string Tag { get; set; } = """";
    }
}";
        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        var code = GetGeneratedSource(result, "AutoMapExtensions.g.cs");
        Assert.DoesNotContain("Tag", code);
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

    // ── [TrimStrings] value transformer ──────────────────────────────────────

    [Fact]
    public void TrimStrings_OnSourceType_TrimsAllStringProps()
    {
        var source = @"
using AutoMap;
namespace MyApp
{
    public class OrderDto { public string Name { get; set; } = """"; public string Tag { get; set; } = """"; public int Id { get; set; } }

    [Map(typeof(OrderDto))]
    [TrimStrings]
    public class Order { public string Name { get; set; } = """"; public string Tag { get; set; } = """"; public int Id { get; set; } }
}";
        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        var code = GetGeneratedSource(result, "AutoMapExtensions.g.cs");
        Assert.Contains("Name = src.Name?.Trim()", code);
        Assert.Contains("Tag = src.Tag?.Trim()", code);
        Assert.Contains("Id = src.Id", code); // non-string not trimmed
    }

    [Fact]
    public void TrimStrings_OnDestType_TrimsAllStringProps()
    {
        var source = @"
using AutoMap;
namespace MyApp
{
    public class Order { public string Name { get; set; } = """"; }

    [MapFrom(typeof(Order))]
    [TrimStrings]
    public class OrderDto { public string Name { get; set; } = """"; }
}";
        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        var code = GetGeneratedSource(result, "AutoMapExtensions.g.cs");
        Assert.Contains("Name = src.Name?.Trim()", code);
    }

    [Fact]
    public void TrimStrings_MapWithOverrides_TrimNotApplied()
    {
        // [MapWith] takes priority — user-supplied expression should not be trimmed
        var source = @"
using AutoMap;
namespace MyApp
{
    public class Order { public string Name { get; set; } = """"; }

    [MapFrom(typeof(Order))]
    [TrimStrings]
    public class OrderDto
    {
        [MapWith(""src.Name.ToUpper()"")]
        public string Name { get; set; } = """";
    }
}";
        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        var code = GetGeneratedSource(result, "AutoMapExtensions.g.cs");
        Assert.Contains("src.Name.ToUpper()", code);
        Assert.DoesNotContain("Trim()", code);
    }

    // ── [MapFormat] formatting shorthand ─────────────────────────────────────

    [Fact]
    public void MapFormat_ValueType_EmitsDotToString()
    {
        var source = @"
using AutoMap;
namespace MyApp
{
    public class Order { public decimal Price { get; set; } }

    [MapFrom(typeof(Order))]
    public class OrderDto
    {
        [MapFormat(""C2"")]
        public string Price { get; set; } = """";
    }
}";
        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        var code = GetGeneratedSource(result, "AutoMapExtensions.g.cs");
        Assert.Contains(@"Price = src.Price.ToString(""C2"")", code);
    }

    [Fact]
    public void MapFormat_ReferenceType_EmitsNullConditionalToString()
    {
        var source = @"
using AutoMap;
namespace MyApp
{
    public class Order { public System.DateTime? ShippedAt { get; set; } }

    [MapFrom(typeof(Order))]
    public class OrderDto
    {
        [MapFormat(""yyyy-MM-dd"")]
        public string ShippedAt { get; set; } = """";
    }
}";
        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        var code = GetGeneratedSource(result, "AutoMapExtensions.g.cs");
        Assert.Contains(@"ShippedAt = src.ShippedAt?.ToString(""yyyy-MM-dd"")", code);
    }

    [Fact]
    public void MapFormat_WithMapWhen_WrapsInTernary()
    {
        var source = @"
using AutoMap;
namespace MyApp
{
    public class Order { public bool IsShipped { get; set; } public System.DateTime ShippedAt { get; set; } }

    [MapFrom(typeof(Order))]
    public class OrderDto
    {
        [MapFormat(""yyyy-MM-dd"")]
        [MapWhen(""src.IsShipped"", Fallback = ""\""N/A\"""")]
        public string ShippedAt { get; set; } = """";
    }
}";
        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        var code = GetGeneratedSource(result, "AutoMapExtensions.g.cs");
        Assert.Contains(@"src.IsShipped ? src.ShippedAt.ToString(""yyyy-MM-dd"") : ""N/A""", code);
    }

    // ── IMapFrom<T> convention ────────────────────────────────────────────────

    [Fact]
    public void IMapFrom_Interface_GeneratesMapping()
    {
        var source = @"
using AutoMap;
namespace MyApp
{
    public class Order { public int Id { get; set; } public string Name { get; set; } = """"; }

    public class OrderDto : IMapFrom<Order>
    {
        public int Id { get; set; }
        public string Name { get; set; } = """";
    }
}";
        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        var code = GetGeneratedSource(result, "AutoMapExtensions.g.cs");
        Assert.Contains("ToOrderDto", code);
        Assert.Contains("Id = src.Id", code);
        Assert.Contains("Name = src.Name", code);
    }

    [Fact]
    public void IMapFrom_AndMapFrom_NoDuplicate()
    {
        // When both [MapFrom] and IMapFrom<T> are present, should only generate one method
        var source = @"
using AutoMap;
namespace MyApp
{
    public class Order { public int Id { get; set; } }

    [MapFrom(typeof(Order))]
    public class OrderDto : IMapFrom<Order>
    {
        public int Id { get; set; }
    }
}";
        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        var code = GetGeneratedSource(result, "AutoMapExtensions.g.cs");
        // Count occurrences of ToOrderDto method signatures — should be exactly 1
        var count = System.Text.RegularExpressions.Regex.Matches(code, @"public static.*ToOrderDto\(").Count;
        Assert.Equal(1, count);
    }

    // ── Partial method hooks ──────────────────────────────────────────────────

    [Fact]
    public void PartialHook_DeclarationEmitted()
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

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        var code = GetGeneratedSource(result, "AutoMapExtensions.g.cs");
        Assert.Contains("static partial void OnToOrderDto(", code);
        Assert.Contains("var result = new", code);
        Assert.Contains("OnToOrderDto(src, result);", code);
        Assert.Contains("return result;", code);
    }

    [Fact]
    public void PartialHook_CustomMethodName_UsesMethodName()
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

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        var code = GetGeneratedSource(result, "AutoMapExtensions.g.cs");
        Assert.Contains("static partial void OnAsDto(", code);
        Assert.Contains("OnAsDto(src, result);", code);
    }

    // ── Strict mode ───────────────────────────────────────────────────────────

    [Fact]
    public void GlobalNamespace_IAutoMapper_ClassNameHasNoGlobalPrefix()
    {
        // Regression: types in the global namespace (no enclosing namespace declaration)
        // caused SimpleName() to return "global::Order" verbatim, producing invalid
        // "public sealed class global::OrderToOrderDtoMapper" in the generated file.
        var source = @"
using AutoMap;

public class OrderDto { public int Id { get; set; } }

[Map(typeof(OrderDto))]
public class Order { public int Id { get; set; } }
";
        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var code = GetGeneratedSource(result, "AutoMapExtensions.g.cs");
        // The mapper class name must not contain the global:: qualifier
        Assert.DoesNotContain("class global::", code);
        Assert.Contains("sealed class OrderToOrderDtoMapper", code);
        Assert.Contains("ToOrderDto", code);
    }


    [Fact]
    public void Strict_NoMappedProps_IsError()
    {
        var source = @"
using AutoMap;
namespace MyApp
{
    public class OrderDto { public string Description { get; set; } = """"; }

    [Map(typeof(OrderDto), Strict = true)]
    public class Order { public int Id { get; set; } } // no matching props
}";
        var result = RunGenerator(source);

        Assert.Contains(result.Diagnostics, d => d.Id == "AM001" && d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
    }

    [Fact]
    public void Strict_False_NoMappedProps_IsWarning()
    {
        var source = @"
using AutoMap;
namespace MyApp
{
    public class OrderDto { public string Description { get; set; } = """"; }

    [Map(typeof(OrderDto), Strict = false)]
    public class Order { public int Id { get; set; } }
}";
        var result = RunGenerator(source);

        Assert.Contains(result.Diagnostics, d => d.Id == "AM001" && d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Warning);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "AM001" && d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
    }

    // ── GenerateProjection (IQueryable projection expressions) ──────────────

    [Fact]
    public void GenerateProjection_SimplePropertyMapping_GeneratesExpressionAndQueryableHelper()
    {
        var source = @"
using AutoMap;
namespace MyApp
{
    public class OrderDto { public int Id { get; set; } public string Name { get; set; } = """"; }

    [Map(typeof(OrderDto), GenerateProjection = true)]
    public class Order { public int Id { get; set; } public string Name { get; set; } = """"; }
}";
        var result = RunGenerator(source);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "AM008");
        var code = GetGeneratedSource(result, "AutoMapExtensions.g.cs");
        Assert.Contains("Expression<Func<global::MyApp.Order, global::MyApp.OrderDto>> ToOrderDtoExpression", code);
        Assert.Contains("Id = src.Id", code);
        Assert.Contains("Name = src.Name", code);
        Assert.Contains("IQueryable<global::MyApp.OrderDto> ProjectToOrderDto(this IQueryable<global::MyApp.Order> source)", code);
        Assert.Contains("source.Select(ToOrderDtoExpression)", code);
    }

    [Fact]
    public void GenerateProjection_NotRequested_NoExpressionEmitted()
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

        var code = GetGeneratedSource(result, "AutoMapExtensions.g.cs");
        Assert.DoesNotContain("Expression<Func<", code);
        Assert.DoesNotContain("ProjectToOrderDto", code);
    }

    [Fact]
    public void GenerateProjection_ConstructorMapping_IsSupported()
    {
        var source = @"
using AutoMap;
namespace MyApp
{
    public class OrderDto
    {
        public OrderDto(int id, string name) { Id = id; Name = name; }
        public int Id { get; }
        public string Name { get; }
    }

    [Map(typeof(OrderDto), GenerateProjection = true)]
    public class Order { public int Id { get; set; } public string Name { get; set; } = """"; }
}";
        var result = RunGenerator(source);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "AM008");
        var code = GetGeneratedSource(result, "AutoMapExtensions.g.cs");
        Assert.Contains("Expression<Func<global::MyApp.Order, global::MyApp.OrderDto>> ToOrderDtoExpression", code);
        Assert.Contains("new global::MyApp.OrderDto(src.Id, src.Name)", code);
    }

    [Fact]
    public void GenerateProjection_NestedMapping_ReportsAM008AndSkipsExpression()
    {
        var source = @"
using AutoMap;
namespace MyApp
{
    public class AddressDto { public string City { get; set; } = """"; }
    [Map(typeof(AddressDto))]
    public class Address { public string City { get; set; } = """"; }

    public class OrderDto { public int Id { get; set; } public AddressDto ShippingAddress { get; set; } = new(); }

    [Map(typeof(OrderDto), GenerateProjection = true)]
    public class Order { public int Id { get; set; } public Address ShippingAddress { get; set; } = new(); }
}";
        var result = RunGenerator(source);

        Assert.Contains(result.Diagnostics, d => d.Id == "AM008");
        var code = GetGeneratedSource(result, "AutoMapExtensions.g.cs");
        Assert.DoesNotContain("ToOrderDtoExpression", code);
        // The instance ToOrderDto() extension method is unaffected by the skipped projection.
        Assert.Contains("public static global::MyApp.OrderDto ToOrderDto(this global::MyApp.Order src)", code);
    }

    [Fact]
    public void GenerateProjection_TrimStrings_ReportsAM008AndSkipsExpression()
    {
        var source = @"
using AutoMap;
namespace MyApp
{
    public class OrderDto { public string Name { get; set; } = """"; }

    [Map(typeof(OrderDto), GenerateProjection = true)]
    [TrimStrings]
    public class Order { public string Name { get; set; } = """"; }
}";
        var result = RunGenerator(source);

        Assert.Contains(result.Diagnostics, d => d.Id == "AM008");
        var code = GetGeneratedSource(result, "AutoMapExtensions.g.cs");
        Assert.DoesNotContain("ToOrderDtoExpression", code);
    }

    [Fact]
    public void GenerateProjection_MapDefault_IsSupported()
    {
        var source = @"
using AutoMap;
namespace MyApp
{
    public class OrderDto { [MapDefault(""\""N/A\"""")] public string Name { get; set; } = """"; }

    [Map(typeof(OrderDto), GenerateProjection = true)]
    public class Order { public string? Name { get; set; } }
}";
        var result = RunGenerator(source);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "AM008");
        var code = GetGeneratedSource(result, "AutoMapExtensions.g.cs");
        Assert.Contains("ToOrderDtoExpression", code);
        Assert.Contains("src.Name ?? \"N/A\"", code);
    }

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

