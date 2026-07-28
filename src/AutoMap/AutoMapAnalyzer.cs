using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.CSharp;

namespace AutoMap;

/// <summary>
/// Re-reports AM004, AM005, AM006 and AM008 diagnostics (already reported by the generator with
/// Location: null) with real syntax locations so that IDE code-fix lightbulbs appear on the
/// affected property, constructor parameter, enum member, or [Map]/[MapFrom] attribute.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AutoMapAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor AM004 = new(
        id: "AM004",
        title: "Property skipped due to type incompatibility",
        messageFormat: "Property '{0}' on '{1}' was skipped because '{2}' on '{3}' has an incompatible type. Fix: add `[MapIgnore]` above `{0}`, or add `[MapWith(\"src.{2}.ToString()\")]` for a custom conversion.",
        category: "AutoMap",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: "https://github.com/Swevo/AutoMap.Generator#am004");

    private static readonly DiagnosticDescriptor AM005 = new(
        id: "AM005",
        title: "Constructor parameter has no matching source property",
        messageFormat: "Constructor parameter '{0}' on destination type '{1}' has no matching property on source type '{2}' — it will receive 'default'. Fix: add `[property: MapProperty(\"MatchingSourceName\")]` to the '{0}' parameter, or add a source property named '{0}'.",
        category: "AutoMap",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: "https://github.com/Swevo/AutoMap.Generator#am005");

    private static readonly DiagnosticDescriptor AM006 = new(
        id: "AM006",
        title: "Enum value has no matching destination enum member",
        messageFormat: "Source enum member '{0}' on '{1}' has no matching member in destination enum '{2}' — the `_ => default` fallback will be used. Fix: add `[MapEnum(\"DestValueName\")]` above `{0}` (e.g. `[MapEnum(\"Active\")] {0}`), or add a same-named member to '{2}'.",
        category: "AutoMap",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: "https://github.com/Swevo/AutoMap.Generator#am006");

    private static readonly DiagnosticDescriptor AM008 = new(
        id: "AM008",
        title: "Projection expression could not be generated",
        messageFormat: "GenerateProjection = true specified for mapping from '{0}' to '{1}', but property '{2}' requires null-conditional access, a switch expression, or a nested/collection mapping — none of which are supported inside an Expression<Func<,>>. Fix: remove `GenerateProjection = true` from the `[Map]`/`[MapFrom]` attribute (the instance `ToXxx()` extension method is unaffected), or restructure '{2}' to avoid `?.`/`switch`.",
        category: "AutoMap",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: "https://github.com/Swevo/AutoMap.Generator#am008");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(AM004, AM005, AM006, AM008);

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
            CheckProperties(ctx, srcType, typeSymbol);
            CheckConstructorParams(ctx, srcType, typeSymbol);
            CheckEnumMembers(ctx, srcType, typeSymbol);
            CheckProjection(ctx, srcType, typeSymbol, attr);
        }

        // [Map(typeof(Dest))] — source type decorated
        foreach (var attr in typeSymbol.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() != "AutoMap.MapAttribute") continue;
            if (attr.ConstructorArguments.Length == 0) continue;
            if (attr.ConstructorArguments[0].Value is not INamedTypeSymbol destType) continue;
            CheckProperties(ctx, typeSymbol, destType);
            CheckConstructorParams(ctx, typeSymbol, destType);
            CheckEnumMembers(ctx, typeSymbol, destType);
            CheckProjection(ctx, typeSymbol, destType, attr);
        }
    }

    private static void CheckProperties(
        SymbolAnalysisContext ctx,
        INamedTypeSymbol srcType,
        INamedTypeSymbol destType)
    {
        // Build source property lookup
        var srcProps = new Dictionary<string, IPropertySymbol>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var p in GetAllPublicProperties(srcType))
            srcProps[p.Name] = p;

        var csharp = ctx.Compilation as CSharpCompilation;

        foreach (var destProp in GetAllPublicProperties(destType))
        {
            // Skip if opted out or has custom mapping
            if (HasAttr(destProp, "AutoMap.MapIgnoreAttribute")) continue;
            if (HasAttr(destProp, "AutoMap.MapWithAttribute")) continue;
            if (HasAttr(destProp, "AutoMap.MapFormatAttribute")) continue;
            if (HasAttr(destProp, "AutoMap.MapWhenAttribute")) continue;

            // Resolve source name (possibly via [MapProperty])
            string lookupName = destProp.Name;
            foreach (var a in destProp.GetAttributes())
            {
                if (a.AttributeClass?.ToDisplayString() == "AutoMap.MapPropertyAttribute"
                    && a.ConstructorArguments.Length > 0)
                {
                    lookupName = a.ConstructorArguments[0].Value as string ?? destProp.Name;
                    break;
                }
            }

            if (!srcProps.TryGetValue(lookupName, out var srcProp)) continue;

            // Enum-to-enum: generator handles this with switch expressions — skip
            if (srcProp.Type.TypeKind == TypeKind.Enum && destProp.Type.TypeKind == TypeKind.Enum) continue;

            // Collection types: generator handles via registered mappings — skip
            if (IsCollection(srcProp.Type) || IsCollection(destProp.Type)) continue;

            // Check type compatibility
            bool compatible;
            if (csharp != null)
            {
                var conv = csharp.ClassifyConversion(srcProp.Type, destProp.Type);
                compatible = conv.IsIdentity || conv.IsImplicit;
            }
            else
            {
                compatible = SymbolEqualityComparer.Default.Equals(srcProp.Type, destProp.Type);
            }
            if (compatible) continue;

            // Report on the property declaration syntax
            foreach (var synRef in destProp.DeclaringSyntaxReferences)
            {
                var syntax = synRef.GetSyntax(ctx.CancellationToken);
                ctx.ReportDiagnostic(Diagnostic.Create(
                    AM004, syntax.GetLocation(),
                    destProp.Name, destType.Name,
                    srcProp.Name, srcType.Name));
            }
        }
    }

    /// <summary>
    /// Re-detects AM005 — mirrors AutoMapGenerator's "pick the longest public constructor"
    /// logic, then reports any parameter that has no matching source property with a real
    /// location (the parameter's own declaration syntax) so a code fix can target it.
    /// </summary>
    private static void CheckConstructorParams(
        SymbolAnalysisContext ctx,
        INamedTypeSymbol srcType,
        INamedTypeSymbol destType)
    {
        var srcProps = new Dictionary<string, IPropertySymbol>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var p in GetAllPublicProperties(srcType))
            srcProps[p.Name] = p;

        bool hasParamlessCtor = destType.Constructors.Any(c =>
            !c.IsStatic && c.DeclaredAccessibility == Accessibility.Public && c.Parameters.Length == 0);
        bool hasMapConstructorAttr = AutoMapGenerator.HasAttribute(destType, "AutoMap.MapConstructorAttribute");

        if (hasParamlessCtor && !hasMapConstructorAttr) return;

        IMethodSymbol? bestCtor = null;
        foreach (var ctor in destType.Constructors)
        {
            if (ctor.IsStatic || ctor.DeclaredAccessibility != Accessibility.Public) continue;
            if (bestCtor == null || ctor.Parameters.Length > bestCtor.Parameters.Length)
                bestCtor = ctor;
        }
        if (bestCtor == null) return;

        foreach (var param in bestCtor.Parameters)
        {
            if (srcProps.ContainsKey(param.Name)) continue;

            foreach (var synRef in param.DeclaringSyntaxReferences)
            {
                var syntax = synRef.GetSyntax(ctx.CancellationToken);
                ctx.ReportDiagnostic(Diagnostic.Create(
                    AM005, syntax.GetLocation(),
                    param.Name, destType.Name, srcType.Name));
            }
        }
    }

    /// <summary>
    /// Re-detects AM006 — for every destination property whose matching source property is an
    /// incompatible enum type, walks the source enum's members looking for ones with no
    /// [MapEnum] redirect and no same-named destination member, reporting AM006 on the source
    /// enum member's own declaration syntax.
    /// </summary>
    private static void CheckEnumMembers(
        SymbolAnalysisContext ctx,
        INamedTypeSymbol srcType,
        INamedTypeSymbol destType)
    {
        var srcProps = new Dictionary<string, IPropertySymbol>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var p in GetAllPublicProperties(srcType))
            srcProps[p.Name] = p;

        var reportedPairs = new HashSet<(ISymbol, ISymbol)>(SymbolPairComparer.Instance);

        foreach (var destProp in GetAllPublicProperties(destType))
        {
            if (HasAttr(destProp, "AutoMap.MapIgnoreAttribute")) continue;
            if (HasAttr(destProp, "AutoMap.MapWithAttribute")) continue;

            string lookupName = destProp.Name;
            foreach (var a in destProp.GetAttributes())
            {
                if (a.AttributeClass?.ToDisplayString() == "AutoMap.MapPropertyAttribute"
                    && a.ConstructorArguments.Length > 0)
                {
                    lookupName = a.ConstructorArguments[0].Value as string ?? destProp.Name;
                    break;
                }
            }

            if (!srcProps.TryGetValue(lookupName, out var srcProp)) continue;
            if (srcProp.Type.TypeKind != TypeKind.Enum || destProp.Type.TypeKind != TypeKind.Enum) continue;
            if (SymbolEqualityComparer.Default.Equals(srcProp.Type, destProp.Type)) continue; // identical enum — compatible

            if (srcProp.Type is not INamedTypeSymbol srcEnum || destProp.Type is not INamedTypeSymbol destEnum) continue;
            if (!reportedPairs.Add((srcEnum, destEnum))) continue; // avoid duplicate reports for the same enum pair

            var destMembers = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var m in destEnum.GetMembers())
                if (m is IFieldSymbol df && df.HasConstantValue)
                    destMembers.Add(df.Name);

            foreach (var m in srcEnum.GetMembers())
            {
                if (m is not IFieldSymbol f || !f.HasConstantValue) continue;

                bool hasRedirect = f.GetAttributes().Any(a =>
                    a.AttributeClass?.ToDisplayString() == "AutoMap.MapEnumAttribute" && a.ConstructorArguments.Length > 0);
                if (hasRedirect) continue;
                if (destMembers.Contains(f.Name)) continue;

                foreach (var synRef in f.DeclaringSyntaxReferences)
                {
                    var syntax = synRef.GetSyntax(ctx.CancellationToken);
                    ctx.ReportDiagnostic(Diagnostic.Create(
                        AM006, syntax.GetLocation(),
                        f.Name, srcEnum.Name, destEnum.Name));
                }
            }
        }
    }

    /// <summary>
    /// Re-detects AM008 — replays a simplified version of the generator's projection-expression
    /// build to find the first destination property whose expression would require `?.` or a
    /// `switch` expression (unsupported inside Expression&lt;Func&lt;,&gt;&gt;), reporting AM008
    /// on the `[Map]`/`[MapFrom]` attribute syntax so a code fix can remove GenerateProjection.
    /// </summary>
    private static void CheckProjection(
        SymbolAnalysisContext ctx,
        INamedTypeSymbol srcType,
        INamedTypeSymbol destType,
        AttributeData attr)
    {
        bool generateProjection = false;
        foreach (var na in attr.NamedArguments)
            if (na.Key == "GenerateProjection") generateProjection = na.Value.Value is true;
        if (!generateProjection) return;

        var srcProps = new Dictionary<string, IPropertySymbol>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var p in GetAllPublicProperties(srcType))
            srcProps[p.Name] = p;

        var csharp = ctx.Compilation as CSharpCompilation;
        string? incompatibleProp = null;

        foreach (var destProp in GetAllPublicProperties(destType))
        {
            if (HasAttr(destProp, "AutoMap.MapIgnoreAttribute")) continue;

            string? mapWithExpr = null;
            string? mapDefaultExpr = null;
            string? mapWhenCond = null;
            string? mapWhenFallback = null;
            string? srcOverrideName = null;
            string? mapFormatStr = null;

            foreach (var a in destProp.GetAttributes())
            {
                var fqn = a.AttributeClass?.ToDisplayString();
                if (fqn == "AutoMap.MapWithAttribute" && a.ConstructorArguments.Length > 0)
                    mapWithExpr = a.ConstructorArguments[0].Value as string;
                else if (fqn == "AutoMap.MapDefaultAttribute" && a.ConstructorArguments.Length > 0)
                    mapDefaultExpr = a.ConstructorArguments[0].Value as string;
                else if (fqn == "AutoMap.MapPropertyAttribute" && a.ConstructorArguments.Length > 0)
                    srcOverrideName = a.ConstructorArguments[0].Value as string;
                else if (fqn == "AutoMap.MapWhenAttribute" && a.ConstructorArguments.Length > 0)
                {
                    mapWhenCond = a.ConstructorArguments[0].Value as string;
                    foreach (var na in a.NamedArguments)
                        if (na.Key == "Fallback") mapWhenFallback = na.Value.Value as string;
                }
                else if (fqn == "AutoMap.MapFormatAttribute" && a.ConstructorArguments.Length > 0)
                    mapFormatStr = a.ConstructorArguments[0].Value as string;
            }

            string? expr;

            if (mapWithExpr != null)
            {
                expr = mapWhenCond != null
                    ? $"{mapWhenCond} ? {mapWithExpr} : {mapWhenFallback ?? "default"}"
                    : mapWithExpr;
            }
            else
            {
                var lookupName = srcOverrideName ?? destProp.Name;
                if (!srcProps.TryGetValue(lookupName, out var srcProp))
                {
                    if (srcOverrideName != null) continue; // AM002 case — unrelated to projection

                    var flatPath = AutoMapGenerator.TryFlattenPath(destProp.Name, srcProps, "src", false, 0);
                    if (flatPath == null) continue;
                    var flatExpr = mapDefaultExpr != null ? $"{flatPath} ?? {mapDefaultExpr}" : flatPath;
                    expr = mapWhenCond != null ? $"{mapWhenCond} ? {flatExpr} : {mapWhenFallback ?? "default"}" : flatExpr;
                }
                else if (mapFormatStr != null)
                {
                    bool isNullableStruct = srcProp.Type is INamedTypeSymbol ntf &&
                        ntf.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
                    var accessOp = (!srcProp.Type.IsValueType || isNullableStruct) ? "?." : ".";
                    var formatted = $"src.{lookupName}{accessOp}ToString(\"{mapFormatStr}\")";
                    expr = mapWhenCond != null ? $"{mapWhenCond} ? {formatted} : {mapWhenFallback ?? "default"}" : formatted;
                }
                else
                {
                    bool typeCompatible = csharp != null
                        ? (csharp.ClassifyConversion(srcProp.Type, destProp.Type).IsIdentity
                           || csharp.ClassifyConversion(srcProp.Type, destProp.Type).IsImplicit)
                        : SymbolEqualityComparer.Default.Equals(srcProp.Type, destProp.Type);

                    if (typeCompatible)
                    {
                        var baseExpr = mapDefaultExpr != null ? $"src.{lookupName} ?? {mapDefaultExpr}" : $"src.{lookupName}";
                        expr = mapWhenCond != null ? $"{mapWhenCond} ? {baseExpr} : {mapWhenFallback ?? "default"}" : baseExpr;
                    }
                    else if (srcProp.Type.TypeKind == TypeKind.Enum && destProp.Type.TypeKind == TypeKind.Enum)
                    {
                        expr = "src switch { }"; // enum mismatch always emits a switch expression
                    }
                    else
                    {
                        // Nested/collection mapping — generator resolves this via `?.` in most cases,
                        // but we can't cheaply reproduce that here without false positives; skip.
                        continue;
                    }
                }
            }

            if (!AutoMapGenerator.IsExpressionTreeCompatible(expr))
            {
                incompatibleProp = destProp.Name;
                break;
            }
        }

        if (incompatibleProp == null) return;

        var location = attr.ApplicationSyntaxReference?.GetSyntax(ctx.CancellationToken).GetLocation();
        ctx.ReportDiagnostic(Diagnostic.Create(AM008, location, srcType.Name, destType.Name, incompatibleProp));
    }

    private static IEnumerable<IPropertySymbol> GetAllPublicProperties(INamedTypeSymbol type)
    {
        var seen = new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);
        var current = (INamedTypeSymbol?)type;
        while (current != null && current.SpecialType != SpecialType.System_Object)
        {
            foreach (var m in current.GetMembers())
                if (m is IPropertySymbol p &&
                    p.DeclaredAccessibility == Accessibility.Public &&
                    seen.Add(p.Name))
                    yield return p;
            current = current.BaseType;
        }
    }

    private static bool HasAttr(ISymbol symbol, string fqn) =>
        symbol.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == fqn);

    private static bool IsCollection(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol) return true;
        if (type is INamedTypeSymbol named && named.IsGenericType && named.TypeArguments.Length == 1)
        {
            var def = named.OriginalDefinition.ToDisplayString();
            return def is "System.Collections.Generic.List<T>"
                       or "System.Collections.Generic.IEnumerable<T>"
                       or "System.Collections.Generic.ICollection<T>"
                       or "System.Collections.Generic.IList<T>"
                       or "System.Collections.Generic.IReadOnlyList<T>"
                       or "System.Collections.Generic.IReadOnlyCollection<T>";
        }
        return false;
    }

    /// <summary>Equates (Src, Dest) enum-type pairs by reference identity, used to dedupe AM006 scans.</summary>
    private sealed class SymbolPairComparer : IEqualityComparer<(ISymbol, ISymbol)>
    {
        public static readonly SymbolPairComparer Instance = new();
        public bool Equals((ISymbol, ISymbol) x, (ISymbol, ISymbol) y) =>
            SymbolEqualityComparer.Default.Equals(x.Item1, y.Item1) && SymbolEqualityComparer.Default.Equals(x.Item2, y.Item2);
        public int GetHashCode((ISymbol, ISymbol) obj) =>
            SymbolEqualityComparer.Default.GetHashCode(obj.Item1) * 397 ^ SymbolEqualityComparer.Default.GetHashCode(obj.Item2);
    }
}

