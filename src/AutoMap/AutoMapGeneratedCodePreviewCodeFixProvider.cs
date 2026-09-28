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
/// Code fix for AM011 (the hidden IDE-only preview diagnostic) that inserts the full generated
/// mapping method body as a comment block directly above the <c>[Map]</c>/<c>[MapFrom]</c>-decorated
/// type — so the generated code can be reviewed inline without opening the generated
/// <c>AutoMapExtensions.g.cs</c> file. Reuses <see cref="AutoMapGenerator.BuildMappingInfo"/> and
/// <see cref="AutoMapGenerator.RenderMethodBodyPreview"/> so the preview can never drift from what
/// the generator actually emits (aside from the documented nested/collection resolution scope
/// limitation — see <see cref="AutoMapGenerator.RenderMethodBodyPreview"/>).
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AutoMapGeneratedCodePreviewCodeFixProvider))]
[Shared]
public sealed class AutoMapGeneratedCodePreviewCodeFixProvider : CodeFixProvider
{
    private const string PreviewMarker = "AutoMap.Generator preview:";

    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create("AM011");

    public override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document
            .GetSyntaxRootAsync(context.CancellationToken)
            .ConfigureAwait(false);
        if (root is null) return;

        var semanticModel = await context.Document
            .GetSemanticModelAsync(context.CancellationToken)
            .ConfigureAwait(false);
        if (semanticModel is null) return;

        var diagnostic = context.Diagnostics.First();
        var node = root.FindNode(diagnostic.Location.SourceSpan);
        var attrSyntax = node.AncestorsAndSelf().OfType<AttributeSyntax>().FirstOrDefault();
        var typeDecl = attrSyntax?.FirstAncestorOrSelf<TypeDeclarationSyntax>();
        if (attrSyntax is null || typeDecl is null) return;

        // Never offer the fix twice for the same attribute — a preview comment is already there.
        if (typeDecl.GetLeadingTrivia().Any(t => t.ToFullString().Contains(PreviewMarker)))
            return;

        if (semanticModel.GetDeclaredSymbol(typeDecl, context.CancellationToken) is not INamedTypeSymbol decoratedSymbol)
            return;

        AttributeData? attrData = null;
        foreach (var candidate in decoratedSymbol.GetAttributes())
        {
            var syntax = candidate.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken);
            if (ReferenceEquals(syntax, attrSyntax) || syntax == attrSyntax)
            {
                attrData = candidate;
                break;
            }
        }
        if (attrData is null) return;
        if (attrData.ConstructorArguments.Length == 0) return;
        if (attrData.ConstructorArguments[0].Value is not INamedTypeSymbol otherSymbol) return;

        var isMapFrom = attrData.AttributeClass?.ToDisplayString() == "AutoMap.MapFromAttribute";
        var sourceSymbol = isMapFrom ? otherSymbol : decoratedSymbol;
        var destSymbol = isMapFrom ? decoratedSymbol : otherSymbol;

        string? methodName = null;
        var strict = false;
        foreach (var na in attrData.NamedArguments)
        {
            if (na.Key == "MethodName") methodName = na.Value.Value as string;
            if (na.Key == "Strict") strict = na.Value.Value is true;
        }

        var mappingInfo = AutoMapGenerator.BuildMappingInfo(
            sourceSymbol, destSymbol, methodName, semanticModel.Compilation, strict);

        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Insert generated code preview as a comment",
                createChangedDocument: ct => InsertPreviewCommentAsync(context.Document, typeDecl, mappingInfo, ct),
                equivalenceKey: "InsertGeneratedCodePreviewAM011"),
            diagnostic);
    }

    private static async Task<Document> InsertPreviewCommentAsync(
        Document document,
        TypeDeclarationSyntax typeDecl,
        MappingInfo mappingInfo,
        CancellationToken cancellationToken)
    {
        var root = await document
            .GetSyntaxRootAsync(cancellationToken)
            .ConfigureAwait(false);
        if (root is null) return document;

        var body = AutoMapGenerator.RenderMethodBodyPreview(mappingInfo);

        var commentLines = new System.Collections.Generic.List<string>
        {
            $"// ── {PreviewMarker} {mappingInfo.MethodName} ──",
        };
        commentLines.AddRange(body.Split('\n').Select(l => l.Length == 0 ? "//" : "// " + l));
        commentLines.Add("// ── end preview ──");

        var newTrivia = new System.Collections.Generic.List<SyntaxTrivia>();
        foreach (var line in commentLines)
        {
            newTrivia.Add(SyntaxFactory.Comment(line));
            newTrivia.Add(SyntaxFactory.CarriageReturnLineFeed);
        }

        var newLeading = SyntaxFactory.TriviaList(newTrivia).AddRange(typeDecl.GetLeadingTrivia());
        var newTypeDecl = typeDecl.WithLeadingTrivia(newLeading);

        var newRoot = root.ReplaceNode(typeDecl, newTypeDecl);
        return document.WithSyntaxRoot(newRoot);
    }
}
