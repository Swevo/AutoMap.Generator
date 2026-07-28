using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace AutoMap;

/// <summary>
/// Reports AM011 — a hidden (IDE-only) preview of the generated mapping method's signature and
/// its flattened/defaulted/ignored/custom property categories, surfaced directly on the
/// `[Map]`/`[MapFrom]` attribute so it's visible via hover/lightbulb/Error List ("Show items in
/// this list" toggle) without opening the generated `.g.cs` file. This is the closest in-package
/// equivalent to a CodeLens annotation that a source-generator NuGet package can offer without a
/// separate VSIX/VS Code extension.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AutoMapPreviewAnalyzer : DiagnosticAnalyzer
{
    private const int MaxMessageLength = 200;

    private static readonly DiagnosticDescriptor AM011 = new(
        id: "AM011",
        title: "Preview of generated mapping method",
        messageFormat: "{0}",
        category: "AutoMap",
        defaultSeverity: DiagnosticSeverity.Hidden,
        isEnabledByDefault: true,
        helpLinkUri: "https://github.com/Swevo/AutoMap.Generator#am011");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(AM011);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeType, SymbolKind.NamedType);
    }

    private static void AnalyzeType(SymbolAnalysisContext ctx)
    {
        var typeSymbol = (INamedTypeSymbol)ctx.Symbol;

        // [MapFrom(typeof(Src))] — dest type decorated
        foreach (var attr in typeSymbol.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() != "AutoMap.MapFromAttribute") continue;
            if (attr.ConstructorArguments.Length == 0) continue;
            if (attr.ConstructorArguments[0].Value is not INamedTypeSymbol srcType) continue;
            ReportPreview(ctx, srcType, typeSymbol, attr);
        }

        // [Map(typeof(Dest))] — source type decorated
        foreach (var attr in typeSymbol.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() != "AutoMap.MapAttribute") continue;
            if (attr.ConstructorArguments.Length == 0) continue;
            if (attr.ConstructorArguments[0].Value is not INamedTypeSymbol destType) continue;
            ReportPreview(ctx, typeSymbol, destType, attr);
        }
    }

    private static void ReportPreview(
        SymbolAnalysisContext ctx,
        INamedTypeSymbol srcType,
        INamedTypeSymbol destType,
        AttributeData attr)
    {
        string? methodName = null;
        bool strict = false;
        foreach (var na in attr.NamedArguments)
        {
            if (na.Key == "MethodName") methodName = na.Value.Value as string;
            if (na.Key == "Strict") strict = na.Value.Value is true;
        }

        // Reuse the exact same mapping-build logic as the generator (property matching,
        // flatten/default/ignore/custom bookkeeping) so the preview can never drift from what
        // is actually emitted.
        var mappingInfo = AutoMapGenerator.BuildMappingInfo(srcType, destType, methodName, ctx.Compilation, strict);

        var location = attr.ApplicationSyntaxReference?.GetSyntax(ctx.CancellationToken).GetLocation();
        if (location == null) return;

        var signature = $"public static {destType.Name} {mappingInfo.MethodName}(this {srcType.Name} src)";
        var categories = AutoMapGenerator.GetDocCategories(mappingInfo);

        var message = categories.Count > 0
            ? $"Generates: {signature} — {string.Join("; ", categories.Select(c => $"{c.Label}: {c.Value}"))}"
            : $"Generates: {signature}";

        if (message.Length > MaxMessageLength)
            message = message.Substring(0, MaxMessageLength - 1) + "…";

        ctx.ReportDiagnostic(Diagnostic.Create(AM011, location, message));
    }
}
