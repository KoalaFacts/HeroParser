using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace HeroParser.Generators;

/// <summary>
/// Extracted read/write format metadata from <c>[Parse]</c> and <c>[Format]</c> attributes.
/// </summary>
internal readonly record struct ParsedPropertyAttributes(
    string? ParseFormat,
    string? WriteFormat,
    bool ExcludeIfAllEmpty)
{
    /// <summary>Extracts <c>[Parse]</c> and <c>[Format]</c> metadata from a symbol.</summary>
    internal static ParsedPropertyAttributes Extract(ISymbol symbol)
    {
        string? parseFormat = null;
        var parseAttribute = GeneratorHelpers.GetFirstMatchingAttribute(symbol, GeneratorHelpers.ParseAttributeNames);
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
        var formatAttribute = GeneratorHelpers.GetFirstMatchingAttribute(symbol, GeneratorHelpers.FormatAttributeNames);
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

/// <summary>
/// Extracted validation constraints from <c>[Validate]</c> with optional diagnostic checks.
/// </summary>
internal readonly record struct ParsedValidationRules(
    bool NotNull,
    bool NotEmpty,
    int MaxLength,
    int MinLength,
    double RangeMin,
    double RangeMax,
    string? Pattern,
    int PatternTimeoutMs)
{
    /// <summary>Gets whether any validation constraint is active.</summary>
    internal bool HasAnyRule => NotNull || NotEmpty
        || MaxLength >= 0 || MinLength >= 0
        || !double.IsNaN(RangeMin) || !double.IsNaN(RangeMax)
        || Pattern != null;

    /// <summary>Extracts <c>[Validate]</c> arguments without running type compatibility diagnostics.</summary>
    internal static ParsedValidationRules ExtractWithoutDiagnostics(ISymbol property)
    {
        bool notNull = false;
        bool notEmpty = false;
        int maxLength = -1;
        int minLength = -1;
        double rangeMin = double.NaN;
        double rangeMax = double.NaN;
        string? pattern = null;
        int patternTimeoutMs = 1000;

        var validateAttribute = GeneratorHelpers.GetFirstMatchingAttribute(property, GeneratorHelpers.ValidateAttributeNames);
        if (validateAttribute is not null)
        {
#pragma warning disable IDE0010 // Populate switch - intentionally not exhaustive
            foreach (var arg in validateAttribute.NamedArguments)
            {
                switch (arg.Key)
                {
                    case "NotNull" when arg.Value.Value is bool r:
                        notNull = r; break;
                    case "NotEmpty" when arg.Value.Value is bool ne:
                        notEmpty = ne; break;
                    case "MaxLength" when arg.Value.Value is int ml && ml >= 0:
                        maxLength = ml; break;
                    case "MinLength" when arg.Value.Value is int mnl && mnl >= 0:
                        minLength = mnl; break;
                    case "RangeMin" when arg.Value.Value is double rmin && !double.IsNaN(rmin):
                        rangeMin = rmin; break;
                    case "RangeMax" when arg.Value.Value is double rmax && !double.IsNaN(rmax):
                        rangeMax = rmax; break;
                    case "Pattern" when arg.Value.Value is string pat && !string.IsNullOrWhiteSpace(pat):
                        pattern = pat; break;
                    case "PatternTimeoutMs" when arg.Value.Value is int pt && pt > 0:
                        patternTimeoutMs = pt; break;
                }
            }
#pragma warning restore IDE0010
        }

        return new ParsedValidationRules(
            notNull,
            notEmpty,
            maxLength,
            minLength,
            rangeMin,
            rangeMax,
            pattern,
            patternTimeoutMs);
    }

    /// <summary>Extracts <c>[Validate]</c> arguments and emits HERO004–HERO007 diagnostics for mismatched property types.</summary>
    internal static ParsedValidationRules ExtractAndValidate(
        IPropertySymbol property,
        string baseTypeName,
        List<Diagnostic> diagnostics)
    {
        var rules = ExtractWithoutDiagnostics(property);
        var location = property.Locations.FirstOrDefault() ?? Location.None;

        bool isStringType = baseTypeName is "string" or "string?";
        bool isNumericType = baseTypeName is "int" or "System.Int32" or "long" or "System.Int64"
            or "short" or "System.Int16" or "byte" or "System.Byte" or "uint" or "System.UInt32"
            or "ulong" or "System.UInt64" or "ushort" or "System.UInt16" or "sbyte" or "System.SByte"
            or "decimal" or "System.Decimal" or "double" or "System.Double" or "float" or "System.Single";

        bool notEmpty = rules.NotEmpty;
        int maxLength = rules.MaxLength;
        int minLength = rules.MinLength;
        double rangeMin = rules.RangeMin;
        double rangeMax = rules.RangeMax;
        string? pattern = rules.Pattern;

        if (notEmpty && !isStringType)
        {
            diagnostics.Add(Diagnostic.Create(GeneratorHelpers.NotEmptyOnNonStringDiagnostic, location, property.Name, baseTypeName));
            notEmpty = false;
        }
        if ((maxLength >= 0 || minLength >= 0) && !isStringType)
        {
            diagnostics.Add(Diagnostic.Create(GeneratorHelpers.LengthOnNonStringDiagnostic, location, property.Name, baseTypeName));
            maxLength = -1;
            minLength = -1;
        }
        if ((!double.IsNaN(rangeMin) || !double.IsNaN(rangeMax)) && !isNumericType)
        {
            diagnostics.Add(Diagnostic.Create(GeneratorHelpers.RangeOnNonNumericDiagnostic, location, property.Name, baseTypeName));
            rangeMin = double.NaN;
            rangeMax = double.NaN;
        }
        if (pattern != null && !isStringType)
        {
            diagnostics.Add(Diagnostic.Create(GeneratorHelpers.PatternOnNonStringDiagnostic, location, property.Name, baseTypeName));
            pattern = null;
        }

        return rules with
        {
            NotEmpty = notEmpty,
            MaxLength = maxLength,
            MinLength = minLength,
            RangeMin = rangeMin,
            RangeMax = rangeMax,
            Pattern = pattern
        };
    }
}

/// <summary>
/// Shared utilities for source generators.
/// </summary>
internal static class GeneratorHelpers
{
#pragma warning disable RS2008 // Enable analyzer release tracking - not needed for internal generator
    // Shared validation diagnostics used by both CSV and FixedWidth generators
    internal static readonly DiagnosticDescriptor NotEmptyOnNonStringDiagnostic = new(
        "HERO004", "NotEmpty only applies to string properties",
        "Property '{0}' has NotEmpty = true but is type '{1}'. NotEmpty only applies to string properties.",
        "HeroParser.Generators", DiagnosticSeverity.Error, isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor LengthOnNonStringDiagnostic = new(
        "HERO005", "MaxLength/MinLength only apply to string properties",
        "Property '{0}' has MaxLength or MinLength but is type '{1}'. These only apply to string properties.",
        "HeroParser.Generators", DiagnosticSeverity.Error, isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor RangeOnNonNumericDiagnostic = new(
        "HERO006", "RangeMin/RangeMax only apply to numeric properties",
        "Property '{0}' has RangeMin or RangeMax but is type '{1}'. These only apply to numeric properties.",
        "HeroParser.Generators", DiagnosticSeverity.Error, isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor PatternOnNonStringDiagnostic = new(
        "HERO007", "Pattern only applies to string properties",
        "Property '{0}' has Pattern but is type '{1}'. Pattern only applies to string properties.",
        "HeroParser.Generators", DiagnosticSeverity.Error, isEnabledByDefault: true);
#pragma warning restore RS2008

    // Shared attribute name lists used by both CSV and FixedWidth generators
    internal static readonly IReadOnlyList<string> ParseAttributeNames = ["HeroParser.ParseAttribute"];
    internal static readonly IReadOnlyList<string> ValidateAttributeNames = ["HeroParser.ValidateAttribute"];
    internal static readonly IReadOnlyList<string> FormatAttributeNames = ["HeroParser.FormatAttribute"];

    /// <summary>
    /// Fully qualified format with nullable reference type modifiers.
    /// </summary>
    internal static readonly SymbolDisplayFormat FullyQualifiedFormatWithNullable =
        SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    /// <summary>
    /// Checks if an attribute matches any of the given names.
    /// </summary>
    internal static bool IsNamed(AttributeData attribute, IReadOnlyList<string> names)
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
    internal static AttributeData? GetFirstMatchingAttribute(ISymbol symbol, IReadOnlyList<string> names)
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
    internal static bool IsTypeAccessible(INamedTypeSymbol type)
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
    internal static bool IsSupportedType(ITypeSymbol type)
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
    internal static bool IsSupportedComplexType(ITypeSymbol type)
    {
        return type.ToDisplayString() switch
        {
            "System.DateTime" => true,
            "System.DateTimeOffset" => true,
            "System.DateOnly" => true,
            "System.TimeOnly" => true,
            "System.Guid" => true,
            _ when type.TypeKind == TypeKind.Enum => true,
            _ => false
        };
    }

    /// <summary>
    /// Checks if a type is ReadOnlyMemory&lt;char&gt;.
    /// </summary>
    internal static bool IsReadOnlyMemoryChar(ITypeSymbol type)
    {
        return type.ToDisplayString() == "System.ReadOnlyMemory<char>";
    }

    /// <summary>
    /// Extracts the underlying non-nullable type display name, nullability flag, and enum flag.
    /// </summary>
    internal static (string BaseTypeName, bool IsNullable, bool IsEnum) GetBaseTypeInfo(ITypeSymbol type)
    {
        var isNullable = false;
        var actualType = type;

        if (type is INamedTypeSymbol { IsGenericType: true, Name: "Nullable" } nullable)
        {
            isNullable = true;
            actualType = nullable.TypeArguments[0];
        }

        if (type.NullableAnnotation == NullableAnnotation.Annotated && type.OriginalDefinition.SpecialType == SpecialType.None)
        {
            isNullable = true;
        }

        var isEnum = actualType.TypeKind == TypeKind.Enum;
        var baseTypeName = actualType.ToDisplayString();
        if (baseTypeName.EndsWith("?", StringComparison.Ordinal))
        {
            baseTypeName = baseTypeName.Substring(0, baseTypeName.Length - 1);
        }

        return (baseTypeName, isNullable, isEnum);
    }

    /// <summary>Creates a lambda expression string setting <paramref name="propertyName"/> on <paramref name="type"/>.</summary>
    internal static string CreateSetter(string typeName, INamedTypeSymbol type, string propertyName)
        => $"({type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)} target, object? val) => target.{propertyName} = ({typeName})val!";

    /// <summary>Creates a lambda expression string reading <paramref name="propertyName"/> from <paramref name="type"/>.</summary>
    internal static string CreateGetter(INamedTypeSymbol type, string propertyName)
        => $"({type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)} target) => target.{propertyName}";

    /// <summary>
    /// Emits a <c>WritePropertyValidation</c> argument (or <c>null</c>) closing a <c>WriterTemplate(...)</c> constructor call.
    /// </summary>
    internal static void EmitWriteValidationArgument(
        SourceBuilder builder,
        bool notNull,
        bool notEmpty,
        int maxLength,
        int minLength,
        double rangeMin,
        double rangeMax,
        string? pattern,
        int patternTimeoutMs)
    {
        if (notNull || notEmpty
            || maxLength >= 0 || minLength >= 0
            || !double.IsNaN(rangeMin) || !double.IsNaN(rangeMax)
            || pattern != null)
        {
            builder.AppendLine("new global::HeroParser.Validation.WritePropertyValidation(");
            builder.Indent();
            builder.AppendLine($"{(notNull ? "true" : "false")},");
            builder.AppendLine($"{(notEmpty ? "true" : "false")},");
            builder.AppendLine(maxLength >= 0 ? $"{maxLength}," : "null,");
            builder.AppendLine(minLength >= 0 ? $"{minLength}," : "null,");
            builder.AppendLine(!double.IsNaN(rangeMin) ? FormattableString.Invariant($"{rangeMin},") : "null,");
            builder.AppendLine(!double.IsNaN(rangeMax) ? FormattableString.Invariant($"{rangeMax},") : "null,");
            builder.AppendLine(pattern != null ? $"\"{EscapeString(pattern)}\"," : "null,");
            builder.AppendLine($"{patternTimeoutMs})),");
            builder.Unindent();
        }
        else
        {
            builder.AppendLine("null),");
        }
    }

    /// <summary>
    /// Returns the enum type to use as a generic argument for <c>Enum.TryParse&lt;TEnum&gt;</c>-style
    /// calls. A nullable enum property's fully qualified name ends in <c>?</c>
    /// (<c>Nullable&lt;TEnum&gt;</c>), which does not satisfy the <c>struct, Enum</c> constraint
    /// (CS0453), so the suffix is removed to name the underlying enum.
    /// </summary>
    internal static string GetEnumTypeArgument(string fullyQualifiedTypeName)
    {
        return fullyQualifiedTypeName.EndsWith("?", StringComparison.Ordinal)
            ? fullyQualifiedTypeName.Substring(0, fullyQualifiedTypeName.Length - 1)
            : fullyQualifiedTypeName;
    }

    /// <summary>
    /// Escapes a string for use in C# string literals.
    /// </summary>
    internal static string EscapeString(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t")
            .Replace("\0", "\\0");
    }

    /// <summary>
    /// Escapes a character for use in C# char literals.
    /// </summary>
    internal static string EscapeChar(char c)
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
    internal static string CreateSafeClassName(INamedTypeSymbol type)
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

    /// <summary>
    /// Formats a number for generated source or JSON with the invariant culture. Generators run in the
    /// compiler under the build machine's culture, which may use a decimal comma or a U+2212 minus sign.
    /// </summary>
    internal static string FormatInvariant(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    /// <summary>
    /// Formats a <c>[Validate(RangeMin/RangeMax)]</c> bound as a C# literal of the member's type,
    /// independent of the build machine's culture.
    /// </summary>
    internal static string FormatRangeLiteral(double value, string baseType)
    {
        var inv = CultureInfo.InvariantCulture;
        return baseType switch
        {
            "decimal" or "System.Decimal" => FormatInvariant(value) + "m",
            "float" or "System.Single" => FormatInvariant(value) + "f",
            "double" or "System.Double" => FormatInvariant(value) + "d",
            "long" or "System.Int64" => ((long)value).ToString(inv) + "L",
            "ulong" or "System.UInt64" => ((ulong)value).ToString(inv) + "UL",
            "int" or "System.Int32" => ((int)value).ToString(inv),
            "uint" or "System.UInt32" => ((uint)value).ToString(inv) + "U",
            "short" or "System.Int16" => "(short)" + ((short)value).ToString(inv),
            "ushort" or "System.UInt16" => "(ushort)" + ((ushort)value).ToString(inv),
            "byte" or "System.Byte" => "(byte)" + ((byte)value).ToString(inv),
            "sbyte" or "System.SByte" => "(sbyte)" + ((sbyte)value).ToString(inv),
            _ => FormatInvariant(value)
        };
    }

    /// <summary>
    /// Builds the range validation message emitted into generated binders, with invariant numbers.
    /// </summary>
    internal static string FormatRangeMessage(double min, double max)
    {
        if (!double.IsNaN(min) && !double.IsNaN(max))
            return $"Value must be between {FormatInvariant(min)} and {FormatInvariant(max)}";
        if (!double.IsNaN(min))
            return $"Value must be >= {FormatInvariant(min)}";
        return $"Value must be <= {FormatInvariant(max)}";
    }
}

/// <summary>
/// Helper for building generated source code with proper indentation.
/// </summary>
/// <remarks>
/// This class is designed for single-use generation within a source generator context.
/// While the <see cref="Clear"/> method is available for reuse scenarios, typical usage
/// involves creating a new instance for each generated file.
/// </remarks>
internal sealed class SourceBuilder
{
    private const string INDENT_STRING = "    ";

    private readonly StringBuilder builder;
    private int indent;

    /// <summary>
    /// Initializes a new instance with default capacity.
    /// </summary>
    internal SourceBuilder()
    {
        builder = new StringBuilder(4096);
    }

    /// <summary>
    /// Initializes a new instance with specified capacity.
    /// </summary>
    internal SourceBuilder(int capacity)
    {
        builder = new StringBuilder(capacity);
    }

    /// <summary>
    /// Increases indentation level.
    /// </summary>
    internal void Indent() => indent++;

    /// <summary>
    /// Decreases indentation level.
    /// </summary>
    internal void Unindent() => indent = Math.Max(0, indent - 1);

    /// <summary>
    /// Appends a line with current indentation.
    /// </summary>
    internal void AppendLine(string line = "")
    {
        for (int i = 0; i < indent; i++)
            builder.Append(INDENT_STRING);
        builder.AppendLine(line);
    }

    /// <summary>
    /// Appends text without indentation or newline.
    /// </summary>
    internal void Append(string text)
    {
        builder.Append(text);
    }

    /// <inheritdoc/>
    public override string ToString() => builder.ToString();

    /// <summary>
    /// Clears the builder content and resets indentation to zero, allowing the instance to be reused.
    /// </summary>
    /// <remarks>
    /// Call this method to reset the builder for generating a new file in the same source generator run.
    /// </remarks>
    internal void Clear()
    {
        builder.Clear();
        indent = 0;
    }
}
