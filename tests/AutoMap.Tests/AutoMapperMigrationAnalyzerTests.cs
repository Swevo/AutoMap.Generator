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

public class AutoMapperMigrationAnalyzerTests
{
    [Fact]
    public async Task CreateMap_ReportsAM009()
    {
        var project = CreateProject(new Dictionary<string, string>
        {
            ["AutoMapperStubs.cs"] = AutoMapperStubs,
            ["AutoMapStubs.cs"] = AutoMapStubs,
            ["Profile.cs"] = @"
using AutoMapper;

namespace MyApp;

public sealed class MappingProfile : Profile
{
    public MappingProfile()
    {
        CreateMap<Order, OrderDto>();
    }
}

public sealed class Order { }
public sealed class OrderDto { }"
        });

        var diagnostics = await GetDiagnosticsAsync(project, new AutoMapperMigrationAnalyzer());

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("AM009", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Info, diagnostic.Severity);
    }

    [Fact]
    public async Task CreateMap_CodeFix_AddsMapAttributeToSourceType()
    {
        var project = CreateProject(new Dictionary<string, string>
        {
            ["AutoMapperStubs.cs"] = AutoMapperStubs,
            ["AutoMapStubs.cs"] = AutoMapStubs,
            ["Profile.cs"] = @"
using AutoMapper;

namespace MyApp;

public sealed class MappingProfile : Profile
{
    public MappingProfile()
    {
        CreateMap<Order, OrderDto>();
    }
}

public sealed class Order
{
    public int Id { get; set; }
}

public sealed class OrderDto
{
    public int Id { get; set; }
}"
        });

        var diagnostics = await GetDiagnosticsAsync(project, new AutoMapperMigrationAnalyzer());
        var diagnostic = Assert.Single(diagnostics);

        var changedSolution = await ApplyFirstCodeFixAsync(project, diagnostic, new AutoMapperMigrationCodeFixProvider());
        var updatedDocument = changedSolution.Projects.Single().Documents.Single(d => d.Name == "Profile.cs");
        var updatedText = (await updatedDocument.GetTextAsync()).ToString();

        Assert.Contains("[global::AutoMap.Map(typeof(global::MyApp.OrderDto))]", updatedText);
        Assert.Contains("public sealed class Order", updatedText);
    }

    [Fact]
    public async Task UnrelatedCreateMap_DoesNotReportDiagnostic()
    {
        var project = CreateProject(new Dictionary<string, string>
        {
            ["Program.cs"] = @"
namespace MyApp;

public sealed class Mapper
{
    public void CreateMap<TSource, TDestination>() { }
}

public sealed class Demo
{
    public void Configure()
    {
        var mapper = new Mapper();
        mapper.CreateMap<Order, OrderDto>();
    }
}

public sealed class Order { }
public sealed class OrderDto { }"
        });

        var diagnostics = await GetDiagnosticsAsync(project, new AutoMapperMigrationAnalyzer());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task CreateMap_CodeFix_UpdatesSourceTypeInDifferentFile()
    {
        var project = CreateProject(new Dictionary<string, string>
        {
            ["AutoMapperStubs.cs"] = AutoMapperStubs,
            ["AutoMapStubs.cs"] = AutoMapStubs,
            ["Profile.cs"] = @"
using AutoMapper;

namespace MyApp;

public sealed class MappingProfile : Profile
{
    public MappingProfile()
    {
        CreateMap<Order, OrderDto>();
    }
}",
            ["Order.cs"] = @"
namespace MyApp;

public sealed class Order
{
    public int Id { get; set; }
}",
            ["OrderDto.cs"] = @"
namespace MyApp;

public sealed class OrderDto
{
    public int Id { get; set; }
}"
        });

        var diagnostics = await GetDiagnosticsAsync(project, new AutoMapperMigrationAnalyzer());
        var diagnostic = Assert.Single(diagnostics);

        var changedSolution = await ApplyFirstCodeFixAsync(project, diagnostic, new AutoMapperMigrationCodeFixProvider());
        var updatedOrderDocument = changedSolution.Projects.Single().Documents.Single(d => d.Name == "Order.cs");
        var updatedProfileDocument = changedSolution.Projects.Single().Documents.Single(d => d.Name == "Profile.cs");

        var updatedOrderText = (await updatedOrderDocument.GetTextAsync()).ToString();
        var updatedProfileText = (await updatedProfileDocument.GetTextAsync()).ToString();

        Assert.Contains("[global::AutoMap.Map(typeof(global::MyApp.OrderDto))]", updatedOrderText);
        Assert.DoesNotContain("[global::AutoMap.Map", updatedProfileText);
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
                "AnalyzerTests",
                "AnalyzerTests",
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

    private const string AutoMapStubs = @"
namespace AutoMap;

[System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct, AllowMultiple = true)]
public sealed class MapAttribute : System.Attribute
{
    public MapAttribute(System.Type destinationType) { }
}

[System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct, AllowMultiple = true)]
public sealed class MapFromAttribute : System.Attribute
{
    public MapFromAttribute(System.Type sourceType) { }
}";

    private const string AutoMapperStubs = @"
namespace AutoMapper;

public class Profile
{
    protected MappingExpression<TSource, TDestination> CreateMap<TSource, TDestination>()
        where TSource : class
        where TDestination : class
        => new();
}

public sealed class MappingExpression<TSource, TDestination>
    where TSource : class
    where TDestination : class
{
}";
}
