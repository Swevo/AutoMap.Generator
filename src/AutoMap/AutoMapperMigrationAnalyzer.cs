using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace AutoMap;

/// <summary>
/// Suggests migrating AutoMapper CreateMap calls to AutoMap attributes.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AutoMapperMigrationAnalyzer : DiagnosticAnalyzer
{
    internal const string DiagnosticId = "AM009";

    private static readonly DiagnosticDescriptor AM009 = new(
        id: DiagnosticId,
        title: "AutoMapper CreateMap can be migrated to AutoMap",
        messageFormat: "'CreateMap<{0}, {1}>()' can be migrated to AutoMap — add [Map(typeof({1}))] to '{0}' instead",
        category: "AutoMap",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        helpLinkUri: "https://github.com/Swevo/AutoMap.Generator#am009");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(AM009);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, Microsoft.CodeAnalysis.CSharp.SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not InvocationExpressionSyntax invocation)
            return;

        if (!AutoMapperMigrationHelpers.TryGetCreateMapInvocationInfo(
                invocation,
                context.SemanticModel,
                context.CancellationToken,
                out var createMapName,
                out var sourceType,
                out var destinationType))
        {
            return;
        }

        if (HasExistingAutoMap(sourceType, destinationType))
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            AM009,
            createMapName.GetLocation(),
            sourceType.Name,
            destinationType.Name));
    }

    private static bool HasExistingAutoMap(INamedTypeSymbol sourceType, INamedTypeSymbol destinationType)
    {
        foreach (var attribute in sourceType.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() != "AutoMap.MapAttribute"
                || attribute.ConstructorArguments.Length == 0
                || attribute.ConstructorArguments[0].Value is not INamedTypeSymbol mappedDestination)
            {
                continue;
            }

            if (SymbolEqualityComparer.Default.Equals(mappedDestination, destinationType))
                return true;
        }

        foreach (var attribute in destinationType.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() != "AutoMap.MapFromAttribute"
                || attribute.ConstructorArguments.Length == 0
                || attribute.ConstructorArguments[0].Value is not INamedTypeSymbol mappedSource)
            {
                continue;
            }

            if (SymbolEqualityComparer.Default.Equals(mappedSource, sourceType))
                return true;
        }

        return false;
    }
}
