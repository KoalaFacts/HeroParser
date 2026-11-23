using Microsoft.CodeAnalysis;

namespace HeroParser.Generators;

/// <summary>
/// Wraps a record <see cref="INamedTypeSymbol"/> and exposes accessibility, naming, and accessor expression generation.
/// </summary>
internal readonly struct RecordTypeTarget
{
    private static readonly SymbolDisplayFormat fullyQualifiedFormatWithNullable =
        SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    private readonly INamedTypeSymbol symbol;

    internal RecordTypeTarget(INamedTypeSymbol symbol)
    {
        this.symbol = symbol;
    }

    internal INamedTypeSymbol Symbol => symbol;

    internal bool IsAccessible
    {
        get
        {
            var current = symbol;
            while (current is not null)
            {
                if (current.DeclaredAccessibility is Accessibility.Private
                    or Accessibility.Protected
                    or Accessibility.ProtectedAndInternal)
                {
                    return false;
                }
                current = current.ContainingType;
            }
            return true;
        }
    }

    internal string FullyQualifiedName => symbol.ToDisplayString(fullyQualifiedFormatWithNullable);

    internal string SafeClassName
        => symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            .Replace("global::", "")
            .Replace(".", "_")
            .Replace("<", "_")
            .Replace(">", "_")
            .Replace(",", "_")
            .Replace(" ", "");

    internal string CreateSetterExpression(string propertyName, string propertyTypeName)
        => $"({symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)} target, object? val) => target.{propertyName} = ({propertyTypeName})val!";

    internal string CreateGetterExpression(string propertyName)
        => $"({symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)} target) => target.{propertyName}";
}
