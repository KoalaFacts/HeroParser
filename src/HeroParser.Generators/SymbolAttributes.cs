using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;

namespace HeroParser.Generators;

/// <summary>
/// Provides attribute lookup operations over a Roslyn <see cref="ISymbol"/>.
/// </summary>
internal readonly struct SymbolAttributes
{
    private readonly ISymbol symbol;

    internal SymbolAttributes(ISymbol symbol)
    {
        this.symbol = symbol;
    }

    /// <summary>Gets the first attribute matching <paramref name="attributeName"/>, or <see langword="null"/>.</summary>
    internal AttributeData? FindFirst(string attributeName)
    {
        foreach (var attr in symbol.GetAttributes())
        {
            if (string.Equals(attr.AttributeClass?.ToDisplayString(), attributeName, StringComparison.Ordinal))
                return attr;
        }
        return null;
    }

    /// <summary>Gets the first attribute matching any name in <paramref name="attributeNames"/>, or <see langword="null"/>.</summary>
    internal AttributeData? FindFirst(IReadOnlyList<string> attributeNames)
    {
        foreach (var attr in symbol.GetAttributes())
        {
            var name = attr.AttributeClass?.ToDisplayString();
            if (name is null)
                continue;

            foreach (var candidate in attributeNames)
            {
                if (string.Equals(name, candidate, StringComparison.Ordinal))
                    return attr;
            }
        }
        return null;
    }
}
