using Microsoft.CodeAnalysis;
using System.Collections.Generic;

namespace HeroParser.Generators;

/// <summary>
/// Extracted read/write format metadata from <c>[Parse]</c> and <c>[Format]</c> attributes.
/// </summary>
internal readonly record struct ParsedPropertyAttributes(
    string? ParseFormat,
    string? WriteFormat,
    bool ExcludeIfAllEmpty)
{
    private static readonly IReadOnlyList<string> parseAttributeNames = ["HeroParser.ParseAttribute"];
    private static readonly IReadOnlyList<string> formatAttributeNames = ["HeroParser.FormatAttribute"];

    /// <summary>Extracts <c>[Parse]</c> and <c>[Format]</c> metadata from a symbol.</summary>
    internal static ParsedPropertyAttributes Extract(ISymbol symbol)
    {
        var attributes = new SymbolAttributes(symbol);

        string? parseFormat = null;
        var parseAttribute = attributes.FindFirst(parseAttributeNames);
        if (parseAttribute is not null)
        {
            foreach (var arg in parseAttribute.NamedArguments)
            {
                if (arg.Key == "Format" && arg.Value.Value is string f && !string.IsNullOrWhiteSpace(f))
                {
                    parseFormat = f;
                    break;
                }
            }
        }

        string? writeFormat = null;
        bool excludeIfAllEmpty = false;
        var formatAttribute = attributes.FindFirst(formatAttributeNames);
        if (formatAttribute is not null)
        {
            foreach (var arg in formatAttribute.NamedArguments)
            {
                if (arg.Key == "WriteFormat" && arg.Value.Value is string wf && !string.IsNullOrWhiteSpace(wf))
                    writeFormat = wf;
                else if (arg.Key == "ExcludeIfAllEmpty" && arg.Value.Value is bool e)
                    excludeIfAllEmpty = e;
            }
        }

        return new ParsedPropertyAttributes(parseFormat, writeFormat, excludeIfAllEmpty);
    }
}
