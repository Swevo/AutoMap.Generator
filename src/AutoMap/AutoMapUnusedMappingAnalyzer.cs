using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace AutoMap;

/// <summary>
/// Reports AM010 — an informational suggestion that a `[Map]`/`[MapFrom]` attribute's generated
/// mapping method (and its collection-helper / <see cref="IAutoMapper{TSource, TResult}"/> singleton)
/// does not appear to be referenced anywhere in this compilation. Runs as a single compilation-wide
/// pass (<see cref="AnalysisContext.RegisterCompilationStartAction"/> + a compilation-end action) so
/// it always sees every syntax tree before deciding a mapping looks unused — this is required for
/// correctness since usage could be in any file in the project.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AutoMapUnusedMappingAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor AM010 = new(
        id: "AM010",
        title: "Generated mapping method appears unused",
        messageFormat: "Generated mapping method '{0}' from this attribute does not appear to be used anywhere in this project. If it's called via reflection, DI, or another project, this can be ignored.",
        category: "AutoMap",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        helpLinkUri: "https://github.com/Swevo/AutoMap.Generator#am010",
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(AM010);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(startCtx =>
        {
            var candidates = new ConcurrentBag<Candidate>();

            startCtx.RegisterSymbolAction(symCtx => CollectCandidates(symCtx, candidates), SymbolKind.NamedType);
            startCtx.RegisterCompilationEndAction(endCtx => ReportUnused(endCtx, candidates));
        });
    }

    private readonly struct Candidate
    {
        public Candidate(string methodName, string mapperName, string pluralMethodName, Location location)
        {
            MethodName = methodName;
            MapperName = mapperName;
            PluralMethodName = pluralMethodName;
            Location = location;
        }

        public string MethodName { get; }
        public string MapperName { get; }
        public string PluralMethodName { get; }
        public Location Location { get; }
    }

    /// <summary>
    /// For every `[Map(typeof(Dest))]` / `[MapFrom(typeof(Source))]` attribute on this type, computes
    /// the exact method/mapper names <see cref="AutoMapGenerator"/> would emit — mirroring its
    /// "MethodName override, else 'To' + destination type name" derivation exactly, including the
    /// `Reverse = true` second direction (which the generator always names 'To' + original source name,
    /// with no MethodName override) — so we never flag a name the generator wouldn't actually produce.
    /// </summary>
    private static void CollectCandidates(SymbolAnalysisContext ctx, ConcurrentBag<Candidate> candidates)
    {
        var typeSymbol = (INamedTypeSymbol)ctx.Symbol;

        foreach (var attr in typeSymbol.GetAttributes())
        {
            var attrFqn = attr.AttributeClass?.ToDisplayString();
            bool isMapFrom = attrFqn == "AutoMap.MapFromAttribute";
            bool isMap = attrFqn == "AutoMap.MapAttribute";
            if (!isMap && !isMapFrom) continue;
            if (attr.ConstructorArguments.Length == 0) continue;
            if (attr.ConstructorArguments[0].Value is not INamedTypeSymbol otherType) continue;

            var sourceType = isMapFrom ? otherType : typeSymbol;
            var destType = isMapFrom ? typeSymbol : otherType;

            string? methodNameOverride = null;
            bool reverse = false;
            foreach (var na in attr.NamedArguments)
            {
                if (na.Key == "MethodName") methodNameOverride = na.Value.Value as string;
                if (na.Key == "Reverse") reverse = na.Value.Value is true;
            }

            var location = attr.ApplicationSyntaxReference?.GetSyntax(ctx.CancellationToken).GetLocation();
            if (location == null) continue;

            var methodName = methodNameOverride ?? "To" + destType.Name;
            AddCandidate(candidates, sourceType.Name, methodName, location);

            // Reverse = true also generates the opposite direction — always "To" + original source
            // type name (the generator's BuildReverseMappingInfo never honors a MethodName override).
            if (reverse)
                AddCandidate(candidates, destType.Name, "To" + sourceType.Name, location);
        }
    }

    private static void AddCandidate(ConcurrentBag<Candidate> candidates, string sourceSimpleName, string methodName, Location location)
    {
        var mapperName = $"{sourceSimpleName}{methodName}Mapper";
        var pluralMethodName = methodName + "s";
        candidates.Add(new Candidate(methodName, mapperName, pluralMethodName, location));
    }

    /// <summary>
    /// Scans every non-generated syntax tree in the compilation for a plain identifier matching the
    /// generated method name, its plural collection-helper name (e.g. "ToOrderDtos"), or its
    /// <see cref="IAutoMapper{TSource, TResult}"/> singleton class name (e.g. "OrderToOrderDtoMapper")
    /// — any of these being referenced anywhere (a call, a `nameof(...)`, a DI registration, etc.)
    /// counts as "used" and suppresses AM010 for that mapping. Generated (*.g.cs) trees are excluded
    /// from the scan since the generator's own collection-helper method always calls the instance
    /// method internally, which would otherwise make every mapping look "used".
    /// </summary>
    private static void ReportUnused(CompilationAnalysisContext ctx, ConcurrentBag<Candidate> candidates)
    {
        if (candidates.IsEmpty) return;

        var usedIdentifiers = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (var tree in ctx.Compilation.SyntaxTrees)
        {
            if (IsGeneratedFile(tree)) continue;

            var root = tree.GetRoot(ctx.CancellationToken);
            foreach (var id in root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.IdentifierNameSyntax>())
                usedIdentifiers.Add(id.Identifier.Text);
        }

        foreach (var candidate in candidates)
        {
            bool used = usedIdentifiers.Contains(candidate.MethodName)
                     || usedIdentifiers.Contains(candidate.MapperName)
                     || usedIdentifiers.Contains(candidate.PluralMethodName);
            if (used) continue;

            ctx.ReportDiagnostic(Diagnostic.Create(AM010, candidate.Location, candidate.MethodName));
        }
    }

    private static bool IsGeneratedFile(SyntaxTree tree) =>
        tree.FilePath.EndsWith(".g.cs", System.StringComparison.OrdinalIgnoreCase);
}
