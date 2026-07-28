using System;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AutoMap;

/// <summary>
/// Code fix for AM005 — adds <c>[property: MapProperty("ClosestSourceName")]</c> to the
/// unmatched constructor parameter, when a reasonably close source property name can be found.
/// Only offered on positional-record / primary-constructor parameters, since the
/// <c>property:</c> attribute target is only meaningful there. No fix is registered when no
/// close match exists, rather than guessing wrong.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AutoMapConstructorCodeFixProvider))]
[Shared]
public sealed class AutoMapConstructorCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create("AM005");

    public override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document
            .GetSyntaxRootAsync(context.CancellationToken)
            .ConfigureAwait(false);
        if (root is null) return;

        var diagnostic = context.Diagnostics.First();
        var node = root.FindNode(diagnostic.Location.SourceSpan);
        var paramSyntax = node.AncestorsAndSelf().OfType<ParameterSyntax>().FirstOrDefault();
        if (paramSyntax is null) return;

        // Only positional record / primary-constructor parameters support the `property:`
        // attribute target — a parameter on a regular constructor declaration cannot.
        if (paramSyntax.Parent is not ParameterListSyntax paramList) return;
        if (paramList.Parent is ConstructorDeclarationSyntax) return;

        var semanticModel = await context.Document
            .GetSemanticModelAsync(context.CancellationToken)
            .ConfigureAwait(false);
        if (semanticModel is null) return;

        if (semanticModel.GetDeclaredSymbol(paramSyntax, context.CancellationToken) is not IParameterSymbol paramSymbol)
            return;

        var destType = paramSymbol.ContainingType;
        if (destType is null) return;

        var srcType = FindSourceType(destType, semanticModel.Compilation, context.CancellationToken);
        if (srcType is null) return;

        var candidate = FindClosestPropertyName(paramSymbol.Name, srcType);
        if (candidate is null) return; // no reasonably close match — don't guess

        context.RegisterCodeFix(
            CodeAction.Create(
                title: $"Add [property: MapProperty(\"{candidate}\")]",
                createChangedDocument: ct => AddMapPropertyAsync(context.Document, paramSyntax, candidate, ct),
                equivalenceKey: "AddMapPropertyAM005"),
            diagnostic);
    }

    private static async Task<Document> AddMapPropertyAsync(
        Document document,
        ParameterSyntax paramSyntax,
        string sourceName,
        CancellationToken cancellationToken)
    {
        var root = await document
            .GetSyntaxRootAsync(cancellationToken)
            .ConfigureAwait(false);
        if (root is null) return document;

        var arg = SyntaxFactory.AttributeArgument(
            SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(sourceName)));
        var attr = SyntaxFactory.Attribute(
            SyntaxFactory.ParseName("MapProperty"),
            SyntaxFactory.AttributeArgumentList(SyntaxFactory.SingletonSeparatedList(arg)));
        var attrList = SyntaxFactory.AttributeList(
            SyntaxFactory.AttributeTargetSpecifier(SyntaxFactory.Token(SyntaxKind.PropertyKeyword)),
            SyntaxFactory.SingletonSeparatedList(attr));

        var newParam = paramSyntax.AddAttributeLists(attrList);
        var newRoot = root.ReplaceNode(paramSyntax, newParam);
        return document.WithSyntaxRoot(newRoot);
    }

    /// <summary>
    /// Finds the source type feeding <paramref name="destType"/> — either <paramref name="destType"/>'s
    /// own [MapFrom(typeof(Src))], or a type elsewhere in the compilation decorated with
    /// [Map(typeof(destType))].
    /// </summary>
    private static INamedTypeSymbol? FindSourceType(
        INamedTypeSymbol destType, Compilation compilation, CancellationToken cancellationToken)
    {
        foreach (var attr in destType.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() == "AutoMap.MapFromAttribute"
                && attr.ConstructorArguments.Length > 0
                && attr.ConstructorArguments[0].Value is INamedTypeSymbol src)
                return src;
        }

        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var typeDecl in tree.GetRoot(cancellationToken).DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(typeDecl, cancellationToken) is not INamedTypeSymbol sym) continue;
                foreach (var attr in sym.GetAttributes())
                {
                    if (attr.AttributeClass?.ToDisplayString() == "AutoMap.MapAttribute"
                        && attr.ConstructorArguments.Length > 0
                        && SymbolEqualityComparer.Default.Equals(attr.ConstructorArguments[0].Value as INamedTypeSymbol, destType))
                        return sym;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Finds the source property whose name is closest to <paramref name="paramName"/>, using a
    /// substring check plus Levenshtein distance. Returns null when nothing is reasonably close —
    /// callers should not register a fix in that case.
    /// </summary>
    private static string? FindClosestPropertyName(string paramName, INamedTypeSymbol srcType)
    {
        string? best = null;
        var bestDistance = int.MaxValue;

        foreach (var prop in AutoMapGenerator.GetAllProperties(srcType))
        {
            if (prop.DeclaredAccessibility != Accessibility.Public || prop.IsStatic) continue;
            // An exact case-insensitive match would already have satisfied the generator's lookup —
            // AM005 only fires when no such match exists, but guard anyway.
            if (string.Equals(prop.Name, paramName, StringComparison.OrdinalIgnoreCase)) continue;

            var distance = LevenshteinDistance(paramName.ToLowerInvariant(), prop.Name.ToLowerInvariant());
            var substringMatch = prop.Name.IndexOf(paramName, StringComparison.OrdinalIgnoreCase) >= 0
                                  || paramName.IndexOf(prop.Name, StringComparison.OrdinalIgnoreCase) >= 0;

            var threshold = Math.Max(2, paramName.Length / 3);
            if (!substringMatch && distance > threshold) continue;

            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = prop.Name;
            }
        }

        return best;
    }

    private static int LevenshteinDistance(string a, string b)
    {
        var dp = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) dp[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) dp[0, j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                dp[i, j] = Math.Min(Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1), dp[i - 1, j - 1] + cost);
            }
        }

        return dp[a.Length, b.Length];
    }
}
