using System.Collections.Generic;
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
/// Code fix for AM012 — when [MapNamingConvention] finds multiple separator/case-insensitive
/// source candidates for one destination property, offer explicit [MapProperty("X")] actions so
/// the user can choose the exact source member and avoid ambiguous implicit binding.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AutoMapNamingConventionCodeFixProvider))]
[Shared]
public sealed class AutoMapNamingConventionCodeFixProvider : CodeFixProvider
{
    private const int MaxCandidates = 5;

    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create("AM012");

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
        var propDecl = node.AncestorsAndSelf().OfType<PropertyDeclarationSyntax>().FirstOrDefault();
        if (propDecl is null) return;

        var candidates = ParseCandidates(diagnostic.GetMessage())
            .Distinct(System.StringComparer.OrdinalIgnoreCase)
            .Take(MaxCandidates)
            .ToList();
        if (candidates.Count == 0) return;

        foreach (var candidate in candidates)
        {
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: $"Add [MapProperty(\"{candidate}\")]",
                    createChangedDocument: ct => AddMapPropertyAsync(context.Document, propDecl, candidate, ct),
                    equivalenceKey: $"AddMapPropertyAM012_{candidate}"),
                diagnostic);
        }
    }

    private static IReadOnlyList<string> ParseCandidates(string diagnosticMessage)
    {
        var segmentMatch = Regex.Match(
            diagnosticMessage,
            @"enabled:\s*(.+?)\.\s*Fix:",
            RegexOptions.Singleline | RegexOptions.CultureInvariant);
        if (!segmentMatch.Success) return System.Array.Empty<string>();

        var memberMatches = Regex.Matches(segmentMatch.Groups[1].Value, @"'([^']+)'");
        if (memberMatches.Count == 0) return System.Array.Empty<string>();

        var results = new List<string>(memberMatches.Count);
        foreach (Match match in memberMatches)
        {
            if (match.Groups.Count < 2) continue;
            var value = match.Groups[1].Value;
            if (!string.IsNullOrWhiteSpace(value))
                results.Add(value);
        }

        return results;
    }

    private static async Task<Document> AddMapPropertyAsync(
        Document document,
        PropertyDeclarationSyntax propDecl,
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
            SyntaxFactory.SingletonSeparatedList(attr))
            .WithTrailingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed);

        var leadingTrivia = propDecl.GetLeadingTrivia();
        var newAttrList = attrList.WithLeadingTrivia(leadingTrivia);

        var newPropDecl = propDecl
            .WithLeadingTrivia(SyntaxFactory.Whitespace(GetIndent(propDecl)))
            .AddAttributeLists(newAttrList);

        var newRoot = root.ReplaceNode(propDecl, newPropDecl);
        return document.WithSyntaxRoot(newRoot);
    }

    private static string GetIndent(SyntaxNode node)
    {
        var leading = node.GetLeadingTrivia().ToString();
        var lines = leading.Split('\n');
        return lines[lines.Length - 1];
    }
}
