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
/// Code fix for AM009 — adds [Map(typeof(TDestination))] to the source type.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AutoMapperMigrationCodeFixProvider))]
[Shared]
public sealed class AutoMapperMigrationCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(AutoMapperMigrationAnalyzer.DiagnosticId);

    public override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document
            .GetSyntaxRootAsync(context.CancellationToken)
            .ConfigureAwait(false);

        if (root is null)
            return;

        var diagnostic = context.Diagnostics.First();
        var node = root.FindNode(diagnostic.Location.SourceSpan);
        var invocation = node.AncestorsAndSelf().OfType<InvocationExpressionSyntax>().FirstOrDefault();
        if (invocation is null)
            return;

        var semanticModel = await context.Document
            .GetSemanticModelAsync(context.CancellationToken)
            .ConfigureAwait(false);

        if (semanticModel is null
            || !AutoMapperMigrationHelpers.TryGetCreateMapInvocationInfo(
                invocation,
                semanticModel,
                context.CancellationToken,
                out _,
                out var sourceType,
                out var destinationType))
        {
            return;
        }

        var sourceDeclaration = await GetSourceDeclarationAsync(sourceType, context.Document.Project.Solution, context.CancellationToken)
            .ConfigureAwait(false);
        if (sourceDeclaration is null)
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Migrate to AutoMap [Map] attribute",
                createChangedSolution: ct => AddMapAttributeAsync(context.Document.Project.Solution, sourceType, destinationType, ct),
                equivalenceKey: "MigrateCreateMapToAutoMap"),
            diagnostic);
    }

    private static async Task<Solution> AddMapAttributeAsync(
        Solution solution,
        INamedTypeSymbol sourceType,
        INamedTypeSymbol destinationType,
        CancellationToken cancellationToken)
    {
        var sourceDeclaration = await GetSourceDeclarationAsync(sourceType, solution, cancellationToken).ConfigureAwait(false);
        if (sourceDeclaration is null)
            return solution;

        var (document, declaration) = sourceDeclaration.Value;
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
            return solution;

        var destinationTypeName = destinationType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var attribute = SyntaxFactory.Attribute(
                SyntaxFactory.ParseName("global::AutoMap.Map"),
                SyntaxFactory.AttributeArgumentList(
                    SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.AttributeArgument(
                            SyntaxFactory.TypeOfExpression(
                                SyntaxFactory.ParseTypeName(destinationTypeName))))))
            .WithTrailingTrivia(SyntaxFactory.ElasticMarker);

        var attributeList = SyntaxFactory.AttributeList(
                SyntaxFactory.SingletonSeparatedList(attribute))
            .WithTrailingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed);

        TypeDeclarationSyntax updatedDeclaration;
        if (declaration.AttributeLists.Count == 0)
        {
            var leadingTrivia = declaration.GetLeadingTrivia();
            updatedDeclaration = declaration
                .WithLeadingTrivia(SyntaxFactory.Whitespace(GetIndent(declaration)))
                .AddAttributeLists(attributeList.WithLeadingTrivia(leadingTrivia));
        }
        else
        {
            updatedDeclaration = declaration.AddAttributeLists(
                attributeList.WithLeadingTrivia(SyntaxFactory.Whitespace(GetIndent(declaration.AttributeLists.Last()))));
        }

        var updatedRoot = root.ReplaceNode(declaration, updatedDeclaration);
        return document.WithSyntaxRoot(updatedRoot).Project.Solution;
    }

    private static async Task<(Document Document, TypeDeclarationSyntax Declaration)?> GetSourceDeclarationAsync(
        INamedTypeSymbol sourceType,
        Solution solution,
        CancellationToken cancellationToken)
    {
        foreach (var syntaxReference in sourceType.DeclaringSyntaxReferences)
        {
            if (await syntaxReference.GetSyntaxAsync(cancellationToken).ConfigureAwait(false) is not TypeDeclarationSyntax declaration)
                continue;

            var document = solution.GetDocument(declaration.SyntaxTree);
            if (document is not null)
                return (document, declaration);
        }

        return null;
    }

    private static string GetIndent(SyntaxNode node)
    {
        var leading = node.GetLeadingTrivia().ToString();
        var lines = leading.Split('\n');
        return lines[lines.Length - 1];
    }
}
