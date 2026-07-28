using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AutoMap;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace AutoMap.Tests;

/// <summary>
/// Exercises the "delight" analyzers: AM010 (<see cref="AutoMapUnusedMappingAnalyzer"/> — flags a
/// generated mapping method that appears unreferenced anywhere in the compilation) and AM011
/// (<see cref="AutoMapPreviewAnalyzer"/> — a Hidden-severity one-line preview of what the generator
/// will emit, shown directly on the `[Map]`/`[MapFrom]` attribute).
/// </summary>
public class AutoMapDelightAnalyzerTests
{
    // ── AM010 — unused mapping ──────────────────────────────────────────────

    [Fact]
    public async Task AM010_NotReported_WhenExtensionMethodIsCalled()
    {
        var project = CreateProject(new Dictionary<string, string>
        {
            ["AutoMapStubs.cs"] = AutoMapStubs,
            ["Types.cs"] = @"
namespace MyApp;

[AutoMap.Map(typeof(OrderDto))]
public sealed class Order { public int Id { get; set; } }

public sealed class OrderDto { public int Id { get; set; } }

public static class Usage
{
    public static OrderDto Convert(Order o) => o.ToOrderDto();
}"
        });

        var diagnostics = await GetDiagnosticsAsync(project, new AutoMapUnusedMappingAnalyzer());

        Assert.DoesNotContain(diagnostics, d => d.Id == "AM010");
    }

    [Fact]
    public async Task AM010_NotReported_WhenOnlyMapperInstanceIsUsed()
    {
        var project = CreateProject(new Dictionary<string, string>
        {
            ["AutoMapStubs.cs"] = AutoMapStubs,
            ["Types.cs"] = @"
namespace MyApp;

[AutoMap.Map(typeof(OrderDto))]
public sealed class Order { public int Id { get; set; } }

public sealed class OrderDto { public int Id { get; set; } }

public static class Usage
{
    // Legitimate usage path: DI registration referencing the generated IAutoMapper<,> singleton
    // instead of calling the extension method directly.
    public static object Registration() => AutoMap.AutoMapExtensions.OrderToOrderDtoMapper.Instance;
}"
        });

        var diagnostics = await GetDiagnosticsAsync(project, new AutoMapUnusedMappingAnalyzer());

        Assert.DoesNotContain(diagnostics, d => d.Id == "AM010");
    }

    [Fact]
    public async Task AM010_Reported_WhenMappingIsNeverReferenced()
    {
        var project = CreateProject(new Dictionary<string, string>
        {
            ["AutoMapStubs.cs"] = AutoMapStubs,
            ["Types.cs"] = @"
namespace MyApp;

[AutoMap.Map(typeof(OrderDto))]
public sealed class Order { public int Id { get; set; } }

public sealed class OrderDto { public int Id { get; set; } }"
        });

        var diagnostics = await GetDiagnosticsAsync(project, new AutoMapUnusedMappingAnalyzer());

        var diagnostic = Assert.Single(diagnostics, d => d.Id == "AM010");
        Assert.Equal(DiagnosticSeverity.Info, diagnostic.Severity);
        Assert.Contains("ToOrderDto", diagnostic.GetMessage());
    }

    [Fact]
    public async Task AM010_OnlyFlagsUnusedAttribute_WhenClassHasMultipleMapAttributes()
    {
        var project = CreateProject(new Dictionary<string, string>
        {
            ["AutoMapStubs.cs"] = AutoMapStubs,
            ["Types.cs"] = @"
namespace MyApp;

public sealed class OrderDtoA { public int Id { get; set; } }
public sealed class OrderDtoB { public int Id { get; set; } }

[AutoMap.Map(typeof(OrderDtoA))]
[AutoMap.Map(typeof(OrderDtoB))]
public sealed class Order { public int Id { get; set; } }

public static class Usage
{
    // Only the OrderDtoA mapping is used — OrderDtoB's should still be flagged.
    public static OrderDtoA Convert(Order o) => o.ToOrderDtoA();
}"
        });

        var diagnostics = await GetDiagnosticsAsync(project, new AutoMapUnusedMappingAnalyzer());

        var diagnostic = Assert.Single(diagnostics, d => d.Id == "AM010");
        Assert.Contains("ToOrderDtoB", diagnostic.GetMessage());
        Assert.DoesNotContain("ToOrderDtoA", diagnostic.GetMessage());
    }

    // ── AM011 — generated code preview ──────────────────────────────────────

    [Fact]
    public async Task AM011_PreviewMessage_IsBareSignature_WhenNothingSpecial()
    {
        var project = CreateProject(new Dictionary<string, string>
        {
            ["AutoMapStubs.cs"] = AutoMapStubs,
            ["Types.cs"] = @"
namespace MyApp;

public sealed class Order { public int Id { get; set; } public string Name { get; set; } = """"; }

[AutoMap.MapFrom(typeof(Order))]
public sealed class OrderDto { public int Id { get; set; } public string Name { get; set; } = """"; }"
        });

        var diagnostics = await GetDiagnosticsAsync(project, new AutoMapPreviewAnalyzer());

        var diagnostic = Assert.Single(diagnostics, d => d.Id == "AM011");
        Assert.Equal(DiagnosticSeverity.Hidden, diagnostic.Severity);
        Assert.Equal("Generates: public static OrderDto ToOrderDto(this Order src)", diagnostic.GetMessage());
    }

    [Fact]
    public async Task AM011_PreviewMessage_ListsIgnoredProperty()
    {
        var project = CreateProject(new Dictionary<string, string>
        {
            ["AutoMapStubs.cs"] = AutoMapStubs,
            ["Types.cs"] = @"
namespace MyApp;

public sealed class Order { public int Id { get; set; } }

[AutoMap.MapFrom(typeof(Order))]
public sealed class OrderDto
{
    public int Id { get; set; }
    [AutoMap.MapIgnore] public string InternalNotes { get; set; } = """";
}"
        });

        var diagnostics = await GetDiagnosticsAsync(project, new AutoMapPreviewAnalyzer());

        var diagnostic = Assert.Single(diagnostics, d => d.Id == "AM011");
        Assert.Contains("Ignored: InternalNotes", diagnostic.GetMessage());
    }

    [Fact]
    public async Task AM011_PreviewMessage_ListsCustomMapWithProperty()
    {
        var project = CreateProject(new Dictionary<string, string>
        {
            ["AutoMapStubs.cs"] = AutoMapStubs,
            ["Types.cs"] = @"
namespace MyApp;

public sealed class Order { public decimal Price { get; set; } }

[AutoMap.MapFrom(typeof(Order))]
public sealed class OrderDto
{
    [AutoMap.MapWith(""src.Price.ToString(\""C\"")"")]
    public string PriceFormatted { get; set; } = """";
}"
        });

        var diagnostics = await GetDiagnosticsAsync(project, new AutoMapPreviewAnalyzer());

        var diagnostic = Assert.Single(diagnostics, d => d.Id == "AM011");
        Assert.Contains("Custom: PriceFormatted", diagnostic.GetMessage());
    }

    [Fact]
    public async Task AM011_PreviewMessage_ListsDefaultedProperty()
    {
        var project = CreateProject(new Dictionary<string, string>
        {
            ["AutoMapStubs.cs"] = AutoMapStubs,
            ["Types.cs"] = @"
namespace MyApp;

public sealed class Order { public string? Name { get; set; } }

[AutoMap.MapFrom(typeof(Order))]
public sealed class OrderDto
{
    [AutoMap.MapDefault(""string.Empty"")]
    public string Name { get; set; } = """";
}"
        });

        var diagnostics = await GetDiagnosticsAsync(project, new AutoMapPreviewAnalyzer());

        var diagnostic = Assert.Single(diagnostics, d => d.Id == "AM011");
        Assert.Contains("Defaulted: Name", diagnostic.GetMessage());
    }

    // ── Shared Roslyn workspace harness (duplicated from AutoMapAdvancedCodeFixTests to keep
    // each analyzer test file self-contained, per existing project convention) ─────────────────

    private static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(Project project, DiagnosticAnalyzer analyzer)
    {
        var compilation = await project.GetCompilationAsync();
        Assert.NotNull(compilation);

        return await compilation!
            .WithAnalyzers(ImmutableArray.Create(analyzer))
            .GetAnalyzerDiagnosticsAsync();
    }

    private static Project CreateProject(IReadOnlyDictionary<string, string> sources)
    {
        var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var solution = workspace.CurrentSolution.AddProject(
            ProjectInfo.Create(
                projectId,
                VersionStamp.Create(),
                "DelightAnalyzerTests",
                "DelightAnalyzerTests",
                LanguageNames.CSharp,
                compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
                parseOptions: new CSharpParseOptions(LanguageVersion.Preview)));

        foreach (var reference in GetFrameworkReferences())
            solution = solution.AddMetadataReference(projectId, reference);

        foreach (var source in sources)
            solution = solution.AddDocument(DocumentId.CreateNewId(projectId), source.Key, SourceText.From(source.Value));

        return solution.GetProject(projectId)!;
    }

    private static IEnumerable<MetadataReference> GetFrameworkReferences()
    {
        var trustedPlatformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
        Assert.False(string.IsNullOrWhiteSpace(trustedPlatformAssemblies));

        return trustedPlatformAssemblies!
            .Split(Path.PathSeparator)
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(static path => MetadataReference.CreateFromFile(path))
            .ToArray();
    }

    // Minimal stand-ins for the real attributes AutoMap.Generator emits into the user's
    // compilation (see AutoMapGenerator.AttributeSource) — sufficient for the analyzers, which
    // only match on fully-qualified attribute type name and constructor/named arguments.
    private const string AutoMapStubs = @"
namespace AutoMap;

[System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct, AllowMultiple = true)]
public sealed class MapAttribute : System.Attribute
{
    public System.Type DestinationType { get; }
    public string? MethodName { get; set; }
    public bool Reverse { get; set; }
    public bool Strict { get; set; }
    public bool GenerateProjection { get; set; }
    public MapAttribute(System.Type destinationType) { DestinationType = destinationType; }
}

[System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct, AllowMultiple = true)]
public sealed class MapFromAttribute : System.Attribute
{
    public System.Type SourceType { get; }
    public string? MethodName { get; set; }
    public bool Reverse { get; set; }
    public bool Strict { get; set; }
    public bool GenerateProjection { get; set; }
    public MapFromAttribute(System.Type sourceType) { SourceType = sourceType; }
}

[System.AttributeUsage(System.AttributeTargets.Property, AllowMultiple = false)]
public sealed class MapIgnoreAttribute : System.Attribute { }

[System.AttributeUsage(System.AttributeTargets.Property, AllowMultiple = false)]
public sealed class MapPropertyAttribute : System.Attribute
{
    public string SourceName { get; }
    public MapPropertyAttribute(string sourceName) { SourceName = sourceName; }
}

[System.AttributeUsage(System.AttributeTargets.Property, AllowMultiple = false)]
public sealed class MapWithAttribute : System.Attribute
{
    public string Expression { get; }
    public MapWithAttribute(string expression) { Expression = expression; }
}

[System.AttributeUsage(System.AttributeTargets.Property, AllowMultiple = false)]
public sealed class MapDefaultAttribute : System.Attribute
{
    public string DefaultExpression { get; }
    public MapDefaultAttribute(string defaultExpression) { DefaultExpression = defaultExpression; }
}

[System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct, AllowMultiple = false)]
public sealed class MapConstructorAttribute : System.Attribute { }

[System.AttributeUsage(System.AttributeTargets.Field, AllowMultiple = false)]
public sealed class MapEnumAttribute : System.Attribute
{
    public string DestValueName { get; }
    public MapEnumAttribute(string destValueName) { DestValueName = destValueName; }
}

[System.AttributeUsage(System.AttributeTargets.Property, AllowMultiple = false)]
public sealed class MapWhenAttribute : System.Attribute
{
    public string Condition { get; }
    public string? Fallback { get; set; }
    public MapWhenAttribute(string condition) { Condition = condition; }
}

[System.AttributeUsage(System.AttributeTargets.Property, AllowMultiple = false)]
public sealed class MapFormatAttribute : System.Attribute
{
    public string Format { get; }
    public MapFormatAttribute(string format) { Format = format; }
}

[System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct, AllowMultiple = false)]
public sealed class TrimStringsAttribute : System.Attribute { }

public interface IAutoMapper<in TSource, out TResult>
{
    TResult Map(TSource source);
}";
}
