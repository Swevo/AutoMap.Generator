using System;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AutoMap;

internal static class AutoMapperMigrationHelpers
{
    internal static bool TryGetCreateMapInvocationInfo(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SimpleNameSyntax createMapName,
        out INamedTypeSymbol sourceType,
        out INamedTypeSymbol destinationType)
    {
        createMapName = null!;
        sourceType = null!;
        destinationType = null!;

        createMapName = invocation.Expression switch
        {
            GenericNameSyntax genericName => genericName,
            MemberAccessExpressionSyntax { Name: GenericNameSyntax genericName } => genericName,
            MemberBindingExpressionSyntax { Name: GenericNameSyntax genericName } => genericName,
            _ => null!
        };

        if (createMapName is not GenericNameSyntax genericCreateMap
            || genericCreateMap.Identifier.ValueText != "CreateMap"
            || genericCreateMap.TypeArgumentList.Arguments.Count != 2)
        {
            return false;
        }

        var symbolInfo = semanticModel.GetSymbolInfo(invocation, cancellationToken);
        var methodSymbol = symbolInfo.Symbol as IMethodSymbol
            ?? symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();

        if (methodSymbol is null
            || methodSymbol.Name != "CreateMap"
            || methodSymbol.TypeArguments.Length != 2
            || !IsAutoMapperCreateMap(methodSymbol))
        {
            return false;
        }

        if (methodSymbol.TypeArguments[0] is not INamedTypeSymbol resolvedSourceType
            || methodSymbol.TypeArguments[1] is not INamedTypeSymbol resolvedDestinationType)
        {
            return false;
        }

        sourceType = resolvedSourceType;
        destinationType = resolvedDestinationType;
        return true;
    }

    private static bool IsAutoMapperCreateMap(IMethodSymbol methodSymbol)
    {
        var ns = methodSymbol.ContainingNamespace?.ToDisplayString();
        return ns is not null
            && (ns.Equals("AutoMapper", StringComparison.Ordinal)
                || ns.StartsWith("AutoMapper.", StringComparison.Ordinal));
    }
}
