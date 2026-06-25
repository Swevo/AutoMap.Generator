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
/// Code fix for AM004 — adds [MapIgnore] to the destination property that triggered
/// the "type incompatibility" warning, suppressing the diagnostic.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AutoMapCodeFixProvider))]
[Shared]
public sealed class AutoMapCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create("AM004");

    public override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document
            .GetSyntaxRootAsync(context.CancellationToken)
            .ConfigureAwait(false);

        if (root is null) return;

        var diagnostic = context.Diagnostics.First();
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        // Find the property declaration at the diagnostic location
        var node = root.FindNode(diagnosticSpan);
        var propDecl = node.AncestorsAndSelf()
            .OfType<PropertyDeclarationSyntax>()
            .FirstOrDefault();

        if (propDecl is null) return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Add [MapIgnore] to suppress this mapping",
                createChangedDocument: ct =>
                    AddMapIgnoreAsync(context.Document, propDecl, ct),
                equivalenceKey: "AddMapIgnoreAM004"),
            diagnostic);
    }

    private static async Task<Document> AddMapIgnoreAsync(
        Document document,
        PropertyDeclarationSyntax propDecl,
        CancellationToken cancellationToken)
    {
        var root = await document
            .GetSyntaxRootAsync(cancellationToken)
            .ConfigureAwait(false);

        if (root is null) return document;

        // Build [MapIgnore] attribute list
        var attrName = SyntaxFactory.ParseName("MapIgnore");
        var attr = SyntaxFactory.Attribute(attrName);
        var attrList = SyntaxFactory.AttributeList(
            SyntaxFactory.SingletonSeparatedList(attr))
            .WithTrailingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed);

        // Preserve existing leading trivia on the property
        var leadingTrivia = propDecl.GetLeadingTrivia();
        var newAttrList = attrList.WithLeadingTrivia(leadingTrivia);

        // Attach the attribute list; strip leading trivia from the property itself
        var newPropDecl = propDecl
            .WithLeadingTrivia(SyntaxFactory.Whitespace(GetIndent(propDecl)))
            .AddAttributeLists(newAttrList);

        var newRoot = root.ReplaceNode(propDecl, newPropDecl);
        return document.WithSyntaxRoot(newRoot);
    }

    /// <summary>Returns the indentation whitespace of a node's first line.</summary>
    private static string GetIndent(SyntaxNode node)
    {
        var leading = node.GetLeadingTrivia().ToString();
        // Take only the last line of leading trivia (the indentation)
        var lines = leading.Split('\n');
        return lines[lines.Length - 1];
    }
}
