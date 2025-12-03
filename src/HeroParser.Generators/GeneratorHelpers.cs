using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Text;

namespace HeroParser.Generators;

/// <summary>
/// Shared utilities for source generators.
/// </summary>
internal static class GeneratorHelpers
{
    /// <summary>
    /// Fully qualified format with nullable reference type modifiers.
    /// </summary>
    public static readonly SymbolDisplayFormat FullyQualifiedFormatWithNullable =
        SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    /// <summary>
    /// Checks if an attribute matches any of the given names.
    /// </summary>
    public static bool IsNamed(AttributeData attribute, IReadOnlyList<string> names)
    {
        var name = attribute.AttributeClass?.ToDisplayString();
        if (name is null)
            return false;

        foreach (var candidate in names)
        {
            if (string.Equals(name, candidate, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Gets the first attribute matching any of the given names.
    /// </summary>
    public static AttributeData? GetFirstMatchingAttribute(ISymbol symbol, IReadOnlyList<string> names)
    {
        foreach (var attr in symbol.GetAttributes())
        {
            if (IsNamed(attr, names))
                return attr;
        }
        return null;
    }

    /// <summary>
    /// Checks if the type is accessible (at least internal).
    /// </summary>
    public static bool IsTypeAccessible(INamedTypeSymbol type)
    {
        var current = type;
        while (current is not null)
        {
            if (current.DeclaredAccessibility == Accessibility.Private ||
                current.DeclaredAccessibility == Accessibility.Protected ||
                current.DeclaredAccessibility == Accessibility.ProtectedAndInternal)
            {
                return false;
            }
            current = current.ContainingType;
        }
        return true;
    }

    /// <summary>
    /// Checks if a type is supported for binding.
    /// </summary>
    public static bool IsSupportedType(ITypeSymbol type)
    {
        var underlying = type is INamedTypeSymbol named && named.IsGenericType && named.Name == "Nullable"
            ? named.TypeArguments[0]
            : type;

        return underlying.SpecialType switch
        {
            SpecialType.System_String => true,
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
            _ => IsSupportedComplexType(underlying)
        };
    }

    /// <summary>
    /// Checks if a complex type is supported.
    /// </summary>
    public static bool IsSupportedComplexType(ITypeSymbol type)
    {
        return type.ToDisplayString() switch
        {
            "System.DateTime" => true,
            "System.DateTimeOffset" => true,
            "System.DateOnly" => true,
            "System.TimeOnly" => true,
            "System.Guid" => true,
            "System.TimeZoneInfo" => true,
            "System.ReadOnlyMemory<char>" => true,
            _ when type.TypeKind == TypeKind.Enum => true,
            _ => false
        };
    }

    /// <summary>
    /// Checks if a type is ReadOnlyMemory&lt;char&gt;.
    /// </summary>
    public static bool IsReadOnlyMemoryChar(ITypeSymbol type)
    {
        return type.ToDisplayString() == "System.ReadOnlyMemory<char>";
    }

    /// <summary>
    /// Escapes a string for use in C# string literals.
    /// </summary>
    public static string EscapeString(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    /// <summary>
    /// Escapes a character for use in C# char literals.
    /// </summary>
    public static string EscapeChar(char c)
    {
        return c switch
        {
            '\'' => "\\'",
            '\\' => "\\\\",
            '\0' => "\\0",
            '\n' => "\\n",
            '\r' => "\\r",
            '\t' => "\\t",
            _ => c.ToString()
        };
    }

    /// <summary>
    /// Creates a safe class name from a type symbol.
    /// </summary>
    public static string CreateSafeClassName(INamedTypeSymbol type)
    {
        var name = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            .Replace("global::", "")
            .Replace(".", "_")
            .Replace("<", "_")
            .Replace(">", "_")
            .Replace(",", "_")
            .Replace(" ", "");
        return name;
    }
}

/// <summary>
/// Helper for building generated source code with proper indentation.
/// </summary>
internal sealed class SourceBuilder
{
    private const string INDENT_STRING = "    ";

    private readonly StringBuilder builder;
    private int indent;

    /// <summary>
    /// Initializes a new instance with default capacity.
    /// </summary>
    public SourceBuilder()
    {
        builder = new StringBuilder(4096);
    }

    /// <summary>
    /// Initializes a new instance with specified capacity.
    /// </summary>
    public SourceBuilder(int capacity)
    {
        builder = new StringBuilder(capacity);
    }

    /// <summary>
    /// Increases indentation level.
    /// </summary>
    public void Indent() => indent++;

    /// <summary>
    /// Decreases indentation level.
    /// </summary>
    public void Unindent() => indent = Math.Max(0, indent - 1);

    /// <summary>
    /// Appends a line with current indentation.
    /// </summary>
    public void AppendLine(string line = "")
    {
        for (int i = 0; i < indent; i++)
            builder.Append(INDENT_STRING);
        builder.AppendLine(line);
    }

    /// <summary>
    /// Appends text without indentation or newline.
    /// </summary>
    public void Append(string text)
    {
        builder.Append(text);
    }

    /// <inheritdoc/>
    public override string ToString() => builder.ToString();

    /// <summary>
    /// Clears the builder and resets indentation.
    /// </summary>
    public void Clear()
    {
        builder.Clear();
        indent = 0;
    }
}
