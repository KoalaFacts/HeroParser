using Microsoft.CodeAnalysis;
using System;

namespace HeroParser.Generators;

/// <summary>
/// Inspects a Roslyn <see cref="ITypeSymbol"/> and exposes normalized type classification and interface capabilities.
/// </summary>
internal readonly struct TypeMetadata
{
    private static readonly SymbolDisplayFormat fullyQualifiedFormatWithNullable =
        SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    internal TypeMetadata(ITypeSymbol symbol)
    {
        Symbol = symbol;
        UnderlyingSymbol = UnwrapNullable(symbol);
    }

    internal ITypeSymbol Symbol { get; }

    internal ITypeSymbol UnderlyingSymbol { get; }

    internal string FullyQualifiedName => Symbol.ToDisplayString(fullyQualifiedFormatWithNullable);

    internal string UnderlyingFullyQualifiedName => UnderlyingSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    internal bool IsNullableValueType => Symbol is INamedTypeSymbol { IsGenericType: true, Name: "Nullable" };

    internal bool IsNullable => IsNullableValueType
        || (Symbol.NullableAnnotation == NullableAnnotation.Annotated && Symbol.OriginalDefinition.SpecialType == SpecialType.None);

    internal bool IsEnum => UnderlyingSymbol.TypeKind == TypeKind.Enum;

    internal string? EnumUnderlyingReaderMethod => UnderlyingSymbol is INamedTypeSymbol { EnumUnderlyingType.SpecialType: var st }
        ? st switch
        {
            SpecialType.System_Byte => "GetByte",
            SpecialType.System_SByte => "GetSByte",
            SpecialType.System_Int16 => "GetInt16",
            SpecialType.System_UInt16 => "GetUInt16",
            SpecialType.System_Int32 => "GetInt32",
            SpecialType.System_UInt32 => "GetUInt32",
            SpecialType.System_Int64 => "GetInt64",
            SpecialType.System_UInt64 => "GetUInt64",
            _ => null
        }
        : null;

    internal bool IsReferenceType => UnderlyingSymbol.IsReferenceType;

    internal bool IsByteArray => UnderlyingSymbol is IArrayTypeSymbol { Rank: 1, ElementType.SpecialType: SpecialType.System_Byte };

    internal bool IsReadOnlyMemoryChar => Symbol.ToDisplayString() == "System.ReadOnlyMemory<char>";

    internal string BaseTypeName
    {
        get
        {
            var name = UnderlyingSymbol.ToDisplayString();
            return name.EndsWith("?", StringComparison.Ordinal)
                ? name.Substring(0, name.Length - 1)
                : name;
        }
    }

    internal bool IsSpanParsable => ImplementsSelfGenericInterface("System", "ISpanParsable");

    internal bool IsUtf8SpanParsable => ImplementsSelfGenericInterface("System", "IUtf8SpanParsable");

    internal bool IsSpanFormattable => ImplementsNonGenericInterface("System", "ISpanFormattable");

    internal bool IsFormattable => ImplementsNonGenericInterface("System", "IFormattable");

    internal bool IsSupported => UnderlyingSymbol.SpecialType switch
    {
        SpecialType.System_String => true,
        SpecialType.System_Char => true,
        SpecialType.System_Int32 => true,
        SpecialType.System_Int64 => true,
        SpecialType.System_Int16 => true,
        SpecialType.System_Byte => true,
        SpecialType.System_UInt32 => true,
        SpecialType.System_UInt64 => true,
        SpecialType.System_UInt16 => true,
        SpecialType.System_SByte => true,
        SpecialType.System_Double => true,
        SpecialType.System_Single => true,
        SpecialType.System_Decimal => true,
        SpecialType.System_Boolean => true,
        _ when IsByteArray => true,
        _ => IsSupportedComplexType
    };

    private bool IsSupportedComplexType => UnderlyingSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", "") switch
    {
        "System.DateTime" => true,
        "System.DateTimeOffset" => true,
        "System.DateOnly" => true,
        "System.TimeOnly" => true,
        "System.TimeSpan" => true,
        "System.Guid" => true,
        "System.Half" => true,
        "System.Int128" => true,
        "System.UInt128" => true,
        "System.Numerics.BigInteger" => true,
        "System.Uri" => true,
        _ when IsEnum => true,
        _ when IsSpanParsable || IsUtf8SpanParsable => true,
        _ => false
    };

    private static ITypeSymbol UnwrapNullable(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol { IsGenericType: true, Name: "Nullable" } named)
            return named.TypeArguments[0];

        return type.WithNullableAnnotation(NullableAnnotation.NotAnnotated);
    }

    private bool ImplementsSelfGenericInterface(string ns, string interfaceName)
    {
        if (UnderlyingSymbol.IsAbstract ||
            UnderlyingSymbol.TypeKind is TypeKind.Interface or TypeKind.TypeParameter or TypeKind.Array or TypeKind.Dynamic)
        {
            return false;
        }

        foreach (var iface in UnderlyingSymbol.AllInterfaces)
        {
            if (iface.Name == interfaceName &&
                iface.ContainingNamespace?.ToDisplayString() == ns &&
                iface.TypeArguments.Length == 1 &&
                SymbolEqualityComparer.Default.Equals(iface.TypeArguments[0], UnderlyingSymbol))
            {
                return true;
            }
        }

        return false;
    }

    private bool ImplementsNonGenericInterface(string ns, string interfaceName)
    {
        foreach (var iface in UnderlyingSymbol.AllInterfaces)
        {
            if (iface.Name == interfaceName &&
                iface.TypeArguments.Length == 0 &&
                iface.ContainingNamespace?.ToDisplayString() == ns)
            {
                return true;
            }
        }

        return false;
    }
}
