using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoMap;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace AutoMap.Tests;

/// <summary>
/// Exercises the AM005/AM006/AM008 re-detection in <see cref="AutoMapAnalyzer"/> (real syntax
/// locations instead of the generator's Location: null) and their corresponding code fixes.
/// </summary>
public class AutoMapAdvancedCodeFixTests
{
    [Fact]
    public async Task AM005_ReportedWithRealLocation_OnCtorParameter()
    {
        var project = CreateProject(new Dictionary<string, string>
        {
            ["AutoMapStubs.cs"] = AutoMapStubs,
            ["Types.cs"] = @"
namespace MyApp;

public sealed class Order
{
    public int Id { get; set; }
    public string CustomerName { get; set; } = """";
}

[AutoMap.MapFrom(typeof(Order))]
public sealed record OrderDto(int Id, string Client);"
        });

        var diagnostics = await GetDiagnosticsAsync(project, new AutoMapAnalyzer());

        var diagnostic = Assert.Single(diagnostics, d => d.Id == "AM005");
        Assert.NotEqual(Location.None, diagnostic.Location);
        Assert.Contains("Client", diagnostic.Location.SourceTree!.GetText().ToString());
    }

    [Fact]
    public async Task AM005_CodeFix_AddsMapPropertyToClosestMatch()
    {
        var project = CreateProject(new Dictionary<string, string>
        {
            ["AutoMapStubs.cs"] = AutoMapStubs,
            ["Types.cs"] = @"
namespace MyApp;

public sealed class Order
{
    public int Id { get; set; }
    public string Name { get; set; } = """";
}

[AutoMap.MapFrom(typeof(Order))]
public sealed record OrderDto(int Id, string Nam);"
        });

        var diagnostics = await GetDiagnosticsAsync(project, new AutoMapAnalyzer());
        var diagnostic = Assert.Single(diagnostics, d => d.Id == "AM005");

        var changedSolution = await ApplyFirstCodeFixAsync(project, diagnostic, new AutoMapConstructorCodeFixProvider());
        var updatedDocument = changedSolution.Projects.Single().Documents.Single(d => d.Name == "Types.cs");
        var updatedText = (await updatedDocument.GetTextAsync()).ToString();

        Assert.Contains("[property: MapProperty(\"Name\")]", updatedText);
    }

    [Fact]
    public async Task AM005_CodeFix_NotOfferedWhenNoCloseMatch()
    {
        var project = CreateProject(new Dictionary<string, string>
        {
            ["AutoMapStubs.cs"] = AutoMapStubs,
            ["Types.cs"] = @"
namespace MyApp;

public sealed class Order
{
    public int Id { get; set; }
}

[AutoMap.MapFrom(typeof(Order))]
public sealed record OrderDto(int Id, string CompletelyUnrelatedFieldXyz);"
        });

        var diagnostics = await GetDiagnosticsAsync(project, new AutoMapAnalyzer());
        var diagnostic = Assert.Single(diagnostics, d => d.Id == "AM005");

        var document = project.Solution.GetDocument(diagnostic.Location.SourceTree)!;
        var actions = new List<CodeAction>();
        var context = new CodeFixContext(document, diagnostic, (action, _) => actions.Add(action), CancellationToken.None);
        await new AutoMapConstructorCodeFixProvider().RegisterCodeFixesAsync(context);

        Assert.Empty(actions);
    }

    [Fact]
    public async Task AM006_ReportedWithRealLocation_OnEnumMember()
    {
        var project = CreateProject(new Dictionary<string, string>
        {
            ["AutoMapStubs.cs"] = AutoMapStubs,
            ["Types.cs"] = @"
namespace MyApp;

public enum SrcStatus { Active, Unknown }
public enum DstStatus { Active, Running }

public sealed class Order { public SrcStatus Status { get; set; } }

[AutoMap.MapFrom(typeof(Order))]
public sealed class OrderDto { public DstStatus Status { get; set; } }"
        });

        var diagnostics = await GetDiagnosticsAsync(project, new AutoMapAnalyzer());

        var diagnostic = Assert.Single(diagnostics, d => d.Id == "AM006");
        Assert.Contains("Unknown", diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan));
    }

    [Fact]
    public async Task AM006_CodeFix_OffersOneActionPerDestinationMember()
    {
        var project = CreateProject(new Dictionary<string, string>
        {
            ["AutoMapStubs.cs"] = AutoMapStubs,
            ["Types.cs"] = @"
namespace MyApp;

public enum SrcStatus { Active, Unknown }
public enum DstStatus { Active, Running }

public sealed class Order { public SrcStatus Status { get; set; } }

[AutoMap.MapFrom(typeof(Order))]
public sealed class OrderDto { public DstStatus Status { get; set; } }"
        });

        var diagnostics = await GetDiagnosticsAsync(project, new AutoMapAnalyzer());
        var diagnostic = Assert.Single(diagnostics, d => d.Id == "AM006");

        var document = project.Solution.GetDocument(diagnostic.Location.SourceTree)!;
        var actions = new List<CodeAction>();
        var context = new CodeFixContext(document, diagnostic, (action, _) => actions.Add(action), CancellationToken.None);
        await new AutoMapEnumCodeFixProvider().RegisterCodeFixesAsync(context);

        Assert.Equal(2, actions.Count); // DstStatus has 2 members: Active, Running
        Assert.Contains(actions, a => a.Title == "Add [MapEnum(\"Active\")]");
        Assert.Contains(actions, a => a.Title == "Add [MapEnum(\"Running\")]");

        var chosen = actions.Single(a => a.Title == "Add [MapEnum(\"Running\")]");
        var operations = await chosen.GetOperationsAsync(CancellationToken.None);
        var applyOp = Assert.Single(operations.OfType<ApplyChangesOperation>());
        var updatedDocument = applyOp.ChangedSolution.Projects.Single().Documents.Single(d => d.Name == "Types.cs");
        var updatedText = (await updatedDocument.GetTextAsync()).ToString();

        Assert.Contains("[MapEnum(\"Running\")]", updatedText);
    }

    [Fact]
    public async Task AM008_ReportedWithRealLocation_OnMapAttribute()
    {
        var project = CreateProject(new Dictionary<string, string>
        {
            ["AutoMapStubs.cs"] = AutoMapStubs,
            ["Types.cs"] = @"
namespace MyApp;

public sealed class Customer { public string Name { get; set; } = """"; }
public sealed class Order { public Customer Customer { get; set; } = new(); }

[AutoMap.MapFrom(typeof(Order), GenerateProjection = true)]
public sealed class OrderDto { public string CustomerName { get; set; } = """"; }"
        });

        var diagnostics = await GetDiagnosticsAsync(project, new AutoMapAnalyzer());

        var diagnostic = Assert.Single(diagnostics, d => d.Id == "AM008");
        Assert.Contains("GenerateProjection", diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan));
    }

    [Fact]
    public async Task AM008_CodeFix_RemovesGenerateProjectionArgument()
    {
        var project = CreateProject(new Dictionary<string, string>
        {
            ["AutoMapStubs.cs"] = AutoMapStubs,
            ["Types.cs"] = @"
namespace MyApp;

public sealed class Customer { public string Name { get; set; } = """"; }
public sealed class Order { public Customer Customer { get; set; } = new(); }

[AutoMap.MapFrom(typeof(Order), GenerateProjection = true)]
public sealed class OrderDto { public string CustomerName { get; set; } = """"; }"
        });

        var diagnostics = await GetDiagnosticsAsync(project, new AutoMapAnalyzer());
        var diagnostic = Assert.Single(diagnostics, d => d.Id == "AM008");

        var changedSolution = await ApplyFirstCodeFixAsync(project, diagnostic, new AutoMapProjectionCodeFixProvider());
        var updatedDocument = changedSolution.Projects.Single().Documents.Single(d => d.Name == "Types.cs");
        var updatedText = (await updatedDocument.GetTextAsync()).ToString();

        Assert.DoesNotContain("GenerateProjection", updatedText);
        Assert.Contains("[AutoMap.MapFrom(typeof(Order))]", updatedText);
    }

    // ── AM011 — full generated-code preview comment ────────────────────────

    [Fact]
    public async Task AM011_CodeFix_InsertsFullMethodBodyAsCommentAboveType()
    {
        var project = CreateProject(new Dictionary<string, string>
        {
            ["AutoMapStubs.cs"] = AutoMapStubs,
            ["Types.cs"] = @"
namespace MyApp;

public sealed class Order
{
    public int Id { get; set; }
    public string CustomerName { get; set; } = """";
}

[AutoMap.MapFrom(typeof(Order))]
public sealed class OrderDto
{
    public int Id { get; set; }
    public string CustomerName { get; set; } = """";
}"
        });

        var diagnostics = await GetDiagnosticsAsync(project, new AutoMapPreviewAnalyzer());
        var diagnostic = Assert.Single(diagnostics, d => d.Id == "AM011");

        var changedSolution = await ApplyFirstCodeFixAsync(project, diagnostic, new AutoMapGeneratedCodePreviewCodeFixProvider());
        var updatedDocument = changedSolution.Projects.Single().Documents.Single(d => d.Name == "Types.cs");
        var updatedText = (await updatedDocument.GetTextAsync()).ToString();

        Assert.Contains("AutoMap.Generator preview:", updatedText);
        Assert.Contains("var result = new OrderDto", updatedText);
        Assert.Contains("Id = src.Id,", updatedText);
        Assert.Contains("CustomerName = src.CustomerName,", updatedText);
        // The comment must be inserted above the decorated type, not replacing it.
        Assert.Contains("[AutoMap.MapFrom(typeof(Order))]", updatedText);
        Assert.Contains("public sealed class OrderDto", updatedText);
        Assert.True(
            updatedText.IndexOf("AutoMap.Generator preview:", StringComparison.Ordinal)
                < updatedText.IndexOf("[AutoMap.MapFrom(typeof(Order))]", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AM011_CodeFix_NotOfferedTwice_WhenPreviewCommentAlreadyPresent()
    {
        var project = CreateProject(new Dictionary<string, string>
        {
            ["AutoMapStubs.cs"] = AutoMapStubs,
            ["Types.cs"] = @"
namespace MyApp;

public sealed class Order
{
    public int Id { get; set; }
}

// ── AutoMap.Generator preview: ToOrderDto ──
// (already inserted)
// ── end preview ──
[AutoMap.MapFrom(typeof(Order))]
public sealed class OrderDto
{
    public int Id { get; set; }
}"
        });

        var diagnostics = await GetDiagnosticsAsync(project, new AutoMapPreviewAnalyzer());
        var diagnostic = Assert.Single(diagnostics, d => d.Id == "AM011");

        var document = project.Solution.GetDocument(diagnostic.Location.SourceTree);
        var actions = new List<CodeAction>();
        var context = new CodeFixContext(
            document!,
            diagnostic,
            (action, _) => actions.Add(action),
            CancellationToken.None);

        await new AutoMapGeneratedCodePreviewCodeFixProvider().RegisterCodeFixesAsync(context);

        Assert.Empty(actions);
    }

    private static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(Project project, DiagnosticAnalyzer analyzer)
    {
        var compilation = await project.GetCompilationAsync();
        Assert.NotNull(compilation);

        return await compilation!
            .WithAnalyzers(ImmutableArray.Create(analyzer))
            .GetAnalyzerDiagnosticsAsync();
    }

    private static async Task<Solution> ApplyFirstCodeFixAsync(Project project, Diagnostic diagnostic, CodeFixProvider codeFixProvider)
    {
        var document = project.Solution.GetDocument(diagnostic.Location.SourceTree);
        Assert.NotNull(document);

        var actions = new List<CodeAction>();
        var context = new CodeFixContext(
            document!,
            diagnostic,
            (action, _) => actions.Add(action),
            CancellationToken.None);

        await codeFixProvider.RegisterCodeFixesAsync(context);

        var action = Assert.Single(actions);
        var operations = await action.GetOperationsAsync(CancellationToken.None);
        var applyChangesOperation = Assert.Single(operations.OfType<ApplyChangesOperation>());
        return applyChangesOperation.ChangedSolution;
    }

    private static Project CreateProject(IReadOnlyDictionary<string, string> sources)
    {
        var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var solution = workspace.CurrentSolution.AddProject(
            ProjectInfo.Create(
                projectId,
                VersionStamp.Create(),
                "AdvancedAnalyzerTests",
                "AdvancedAnalyzerTests",
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
    // compilation (see AutoMapGenerator.AttributeSource) — sufficient for the analyzer, which
    // only matches on fully-qualified attribute type name and constructor/named arguments.
    private const string AutoMapStubs = @"
namespace AutoMap;

[System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct, AllowMultiple = true)]
public sealed class MapAttribute : System.Attribute
{
    public System.Type DestinationType { get; }
    public bool GenerateProjection { get; set; }
    public MapAttribute(System.Type destinationType) { DestinationType = destinationType; }
}

[System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct, AllowMultiple = true)]
public sealed class MapFromAttribute : System.Attribute
{
    public System.Type SourceType { get; }
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
}";
}
