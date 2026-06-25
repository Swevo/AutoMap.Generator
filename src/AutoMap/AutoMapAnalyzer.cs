using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.CSharp;

namespace AutoMap;

/// <summary>
/// Re-reports AM004 diagnostics with real property syntax locations so that
/// IDE code-fix lightbulbs appear on the affected destination property.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AutoMapAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor AM004 = new(
        id: "AM004",
        title: "Property skipped due to type incompatibility",
        messageFormat: "Property '{0}' on '{1}' was skipped: source property '{2}' on '{3}' has an incompatible type with no registered mapping. Use [MapIgnore] to suppress, or add [Map] on the source type.",
        category: "AutoMap",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: "https://github.com/Swevo/AutoMap.Generator#am004");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(AM004);

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
        }

        // [Map(typeof(Dest))] — source type decorated
        foreach (var attr in typeSymbol.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() != "AutoMap.MapAttribute") continue;
            if (attr.ConstructorArguments.Length == 0) continue;
            if (attr.ConstructorArguments[0].Value is not INamedTypeSymbol destType) continue;
            CheckProperties(ctx, typeSymbol, destType);
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
}
