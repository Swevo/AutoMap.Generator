using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AutoMap;

/// <summary>
/// Code fix for AM008 — removes the <c>GenerateProjection = true</c> named argument from the
/// <c>[Map]</c>/<c>[MapFrom]</c> attribute that triggered the "projection unsupported" warning.
/// The instance <c>ToXxx()</c> extension method is unaffected either way; this only stops
/// AutoMap.Generator from trying (and failing) to also emit an Expression&lt;Func&lt;,&gt;&gt;.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AutoMapProjectionCodeFixProvider))]
[Shared]
public sealed class AutoMapProjectionCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create("AM008");

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
        var attrSyntax = node.AncestorsAndSelf().OfType<AttributeSyntax>().FirstOrDefault();
        if (attrSyntax?.ArgumentList is null) return;

        var hasGenerateProjectionArg = attrSyntax.ArgumentList.Arguments
            .Any(a => a.NameEquals?.Name.Identifier.Text == "GenerateProjection");
        if (!hasGenerateProjectionArg) return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Remove GenerateProjection = true",
                createChangedDocument: ct => RemoveGenerateProjectionAsync(context.Document, attrSyntax, ct),
                equivalenceKey: "RemoveGenerateProjectionAM008"),
            diagnostic);
    }

    private static async Task<Document> RemoveGenerateProjectionAsync(
        Document document,
        AttributeSyntax attrSyntax,
        CancellationToken cancellationToken)
    {
        var root = await document
            .GetSyntaxRootAsync(cancellationToken)
            .ConfigureAwait(false);
        if (root is null || attrSyntax.ArgumentList is null) return document;

        var argToRemove = attrSyntax.ArgumentList.Arguments
            .FirstOrDefault(a => a.NameEquals?.Name.Identifier.Text == "GenerateProjection");
        if (argToRemove is null) return document;

        var newArgList = attrSyntax.ArgumentList.RemoveNode(argToRemove, SyntaxRemoveOptions.KeepNoTrivia);

        SyntaxNode newAttr = newArgList is null || newArgList.Arguments.Count == 0
            ? attrSyntax.WithArgumentList(null)
            : attrSyntax.WithArgumentList(newArgList);

        var newRoot = root.ReplaceNode(attrSyntax, newAttr);
        return document.WithSyntaxRoot(newRoot);
    }
}
