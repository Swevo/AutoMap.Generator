using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AutoMap;

/// <summary>
/// Code fix for AM006 — offers to add <c>[MapEnum("DestValueName")]</c> to the unmatched source
/// enum member, one registered code action per candidate destination enum value (up to 5), so
/// the user can pick the right one from a small list rather than AutoMap.Generator guessing.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AutoMapEnumCodeFixProvider))]
[Shared]
public sealed class AutoMapEnumCodeFixProvider : CodeFixProvider
{
    private const int MaxCandidates = 5;

    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create("AM006");

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
        var memberDecl = node.AncestorsAndSelf().OfType<EnumMemberDeclarationSyntax>().FirstOrDefault();
        if (memberDecl is null) return;

        // The diagnostic message embeds the destination enum's simple name — parse it out rather
        // than re-deriving the source→destination mapping graph from scratch.
        var message = diagnostic.GetMessage();
        var match = Regex.Match(message, "destination enum '([^']+)'");
        if (!match.Success) return;
        var destEnumName = match.Groups[1].Value;

        var semanticModel = await context.Document
            .GetSemanticModelAsync(context.CancellationToken)
            .ConfigureAwait(false);
        if (semanticModel is null) return;

        var compilation = semanticModel.Compilation;
        INamedTypeSymbol? destEnum = null;
        foreach (var candidate in compilation.GetSymbolsWithName(n => n == destEnumName, SymbolFilter.Type, context.CancellationToken))
        {
            if (candidate is INamedTypeSymbol nts && nts.TypeKind == TypeKind.Enum)
            {
                destEnum = nts;
                break;
            }
        }
        if (destEnum is null) return;

        var destMemberNames = destEnum.GetMembers()
            .OfType<IFieldSymbol>()
            .Where(f => f.HasConstantValue)
            .Select(f => f.Name)
            .Take(MaxCandidates)
            .ToList();

        foreach (var destName in destMemberNames)
        {
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: $"Add [MapEnum(\"{destName}\")]",
                    createChangedDocument: ct => AddMapEnumAsync(context.Document, memberDecl, destName, ct),
                    equivalenceKey: $"AddMapEnumAM006_{destName}"),
                diagnostic);
        }
    }

    private static async Task<Document> AddMapEnumAsync(
        Document document,
        EnumMemberDeclarationSyntax memberDecl,
        string destValueName,
        CancellationToken cancellationToken)
    {
        var root = await document
            .GetSyntaxRootAsync(cancellationToken)
            .ConfigureAwait(false);
        if (root is null) return document;

        var arg = SyntaxFactory.AttributeArgument(
            SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(destValueName)));
        var attr = SyntaxFactory.Attribute(
            SyntaxFactory.ParseName("MapEnum"),
            SyntaxFactory.AttributeArgumentList(SyntaxFactory.SingletonSeparatedList(arg)));
        var attrList = SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(attr))
            .WithTrailingTrivia(SyntaxFactory.Space);

        var newMemberDecl = memberDecl.AddAttributeLists(attrList);
        var newRoot = root.ReplaceNode(memberDecl, newMemberDecl);
        return document.WithSyntaxRoot(newRoot);
    }
}
