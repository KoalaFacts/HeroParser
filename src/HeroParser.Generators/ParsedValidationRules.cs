using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace HeroParser.Generators;

/// <summary>
/// Extracted validation constraints from <c>[Validate]</c> with formatting and write-emitter helpers.
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
    private static readonly IReadOnlyList<string> validateAttributeNames = ["HeroParser.ValidateAttribute"];

#pragma warning disable RS2008 // Enable analyzer release tracking - not needed for internal generator
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

    /// <summary>Gets whether any validation constraint is active.</summary>
    internal bool HasAnyRule => NotNull || NotEmpty
        || MaxLength >= 0 || MinLength >= 0
        || !double.IsNaN(RangeMin) || !double.IsNaN(RangeMax)
        || Pattern != null;

    internal bool HasRangeMin => !double.IsNaN(RangeMin);

    internal bool HasRangeMax => !double.IsNaN(RangeMax);

    internal bool HasRange => HasRangeMin || HasRangeMax;

    internal string FormatRangeMinLiteral(string baseType) => FormatRangeLiteral(RangeMin, baseType);

    internal string FormatRangeMaxLiteral(string baseType) => FormatRangeLiteral(RangeMax, baseType);

    internal string FormatRangeMinInvariant() => FormatInvariant(RangeMin);

    internal string FormatRangeMaxInvariant() => FormatInvariant(RangeMax);

    /// <summary>Builds the range validation message emitted into generated binders with invariant numbers.</summary>
    internal string FormatRangeMessage()
    {
        if (HasRangeMin && HasRangeMax)
            return $"Value must be between {FormatInvariant(RangeMin)} and {FormatInvariant(RangeMax)}";
        if (HasRangeMin)
            return $"Value must be >= {FormatInvariant(RangeMin)}";
        return $"Value must be <= {FormatInvariant(RangeMax)}";
    }

    /// <summary>Emits a <c>WritePropertyValidation</c> argument (or <c>null</c>) closing a <c>WriterTemplate(...)</c> constructor call.</summary>
    internal void EmitWriteValidationArgument(SourceBuilder builder)
    {
        if (HasAnyRule)
        {
            builder.AppendLine("new global::HeroParser.Validation.WritePropertyValidation(");
            builder.Indent();
            builder.AppendLine($"{(NotNull ? "true" : "false")},");
            builder.AppendLine($"{(NotEmpty ? "true" : "false")},");
            builder.AppendLine(MaxLength >= 0 ? $"{MaxLength}," : "null,");
            builder.AppendLine(MinLength >= 0 ? $"{MinLength}," : "null,");
            builder.AppendLine(HasRangeMin ? FormattableString.Invariant($"{RangeMin},") : "null,");
            builder.AppendLine(HasRangeMax ? FormattableString.Invariant($"{RangeMax},") : "null,");
            builder.AppendLine(Pattern != null ? $"{new CSharpStringLiteral(Pattern).Quoted}," : "null,");
            builder.AppendLine($"{PatternTimeoutMs})),");
            builder.Unindent();
        }
        else
        {
            builder.AppendLine("null),");
        }
    }

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

        var validateAttribute = new SymbolAttributes(property).FindFirst(validateAttributeNames);
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
            or "decimal" or "System.Decimal" or "double" or "System.Double" or "float" or "System.Single"
            or "System.Half" or "System.Int128" or "System.UInt128" or "System.Numerics.BigInteger";

        bool notEmpty = rules.NotEmpty;
        int maxLength = rules.MaxLength;
        int minLength = rules.MinLength;
        double rangeMin = rules.RangeMin;
        double rangeMax = rules.RangeMax;
        string? pattern = rules.Pattern;

        if (notEmpty && !isStringType)
        {
            diagnostics.Add(Diagnostic.Create(NotEmptyOnNonStringDiagnostic, location, property.Name, baseTypeName));
            notEmpty = false;
        }
        if ((maxLength >= 0 || minLength >= 0) && !isStringType)
        {
            diagnostics.Add(Diagnostic.Create(LengthOnNonStringDiagnostic, location, property.Name, baseTypeName));
            maxLength = -1;
            minLength = -1;
        }
        if ((!double.IsNaN(rangeMin) || !double.IsNaN(rangeMax)) && !isNumericType)
        {
            diagnostics.Add(Diagnostic.Create(RangeOnNonNumericDiagnostic, location, property.Name, baseTypeName));
            rangeMin = double.NaN;
            rangeMax = double.NaN;
        }
        if (pattern != null && !isStringType)
        {
            diagnostics.Add(Diagnostic.Create(PatternOnNonStringDiagnostic, location, property.Name, baseTypeName));
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

    private static string FormatInvariant(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static string FormatRangeLiteral(double value, string baseType)
    {
        var inv = CultureInfo.InvariantCulture;
        return baseType switch
        {
            "decimal" or "System.Decimal" => FormatInvariant(value) + "m",
            "float" or "System.Single" => FormatInvariant(value) + "f",
            "double" or "System.Double" => FormatInvariant(value) + "d",
            "System.Half" => "(global::System.Half)" + FormatInvariant(value) + "d",
            "System.Int128" => "(global::System.Int128)" + FormatInvariant(value) + "d",
            "System.UInt128" => "(global::System.UInt128)" + FormatInvariant(value) + "d",
            "System.Numerics.BigInteger" => "(global::System.Numerics.BigInteger)" + FormatInvariant(value) + "d",
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
}
