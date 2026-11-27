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

    private const double DECIMAL_UPPER_EXCLUSIVE = 79228162514264337593543950336.0;
    private const double INT64_MIN_VAL = -9223372036854775808.0;
    private const double INT64_UPPER_EXCLUSIVE = 9223372036854775808.0;
    private const double UINT64_UPPER_EXCLUSIVE = 18446744073709551616.0;
    private const double INT128_MIN_VAL = -170141183460469231731687303715884105728.0;
    private const double INT128_UPPER_EXCLUSIVE = 170141183460469231731687303715884105728.0;
    private const double UINT128_UPPER_EXCLUSIVE = 340282366920938463463374607431768211456.0;

    internal string FormatRangeMinLiteral(string baseType) => FormatRangeLiteral(RangeMin, baseType);

    internal string FormatRangeMaxLiteral(string baseType) => FormatRangeLiteral(RangeMax, baseType);

    internal string FormatRangeMinViolationCondition(string valueExpr, string baseType)
        => FormatRangeBoundCondition(valueExpr, RangeMin, baseType, isMin: true);

    internal string FormatRangeMaxViolationCondition(string valueExpr, string baseType)
        => FormatRangeBoundCondition(valueExpr, RangeMax, baseType, isMin: false);

    internal static bool IsNumericType(string baseTypeName)
        => baseTypeName is "int" or "System.Int32" or "long" or "System.Int64"
            or "short" or "System.Int16" or "byte" or "System.Byte" or "uint" or "System.UInt32"
            or "ulong" or "System.UInt64" or "ushort" or "System.UInt16" or "sbyte" or "System.SByte"
            or "decimal" or "System.Decimal" or "double" or "System.Double" or "float" or "System.Single"
            or "System.Half" or "System.Int128" or "System.UInt128" or "System.Numerics.BigInteger";

    /// <summary>
    /// Builds a C# boolean expression that evaluates to <see langword="true"/> when <paramref name="valueExpr"/> violates
    /// <see cref="RangeMin"/> or <see cref="RangeMax"/>, or returns <see langword="null"/> when the range can never be violated.
    /// </summary>
    internal string? BuildRangeViolationCondition(string valueExpr, string baseType)
    {
        var conditions = new List<string>(2);
        if (HasRangeMin)
        {
            string minCond = FormatRangeBoundCondition(valueExpr, RangeMin, baseType, isMin: true);
            if (minCond == "true")
                return "true";
            if (minCond != "false")
                conditions.Add(minCond);
        }

        if (HasRangeMax)
        {
            string maxCond = FormatRangeBoundCondition(valueExpr, RangeMax, baseType, isMin: false);
            if (maxCond == "true")
                return "true";
            if (maxCond != "false")
                conditions.Add(maxCond);
        }

        return conditions.Count == 0 ? null : string.Join(" || ", conditions);
    }

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
        bool isNumericType = IsNumericType(baseTypeName);

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

    private static string FormatDoubleLiteral(double value)
    {
        if (double.IsPositiveInfinity(value))
            return "double.PositiveInfinity";
        if (double.IsNegativeInfinity(value))
            return "double.NegativeInfinity";
        return FormatInvariant(value) + "d";
    }

    private static string FormatFloatLiteral(double value)
    {
        if (double.IsPositiveInfinity(value))
            return "float.PositiveInfinity";
        if (double.IsNegativeInfinity(value))
            return "float.NegativeInfinity";
        return FormatInvariant(value) + "f";
    }

    private static string ParenthesizeIfNegative(string literal)
        => literal.StartsWith("-", StringComparison.Ordinal) ? $"({literal})" : literal;

    private static string FormatRangeBoundCondition(string valueExpr, double bound, string baseType, bool isMin)
    {
        var inv = CultureInfo.InvariantCulture;
        return baseType switch
        {
            "sbyte" or "System.SByte" => FormatExactBoundedIntCondition(
                valueExpr, bound, isMin, sbyte.MinValue, sbyte.MaxValue, v => "(sbyte)" + ParenthesizeIfNegative(((sbyte)v).ToString(inv))),
            "byte" or "System.Byte" => FormatExactBoundedIntCondition(
                valueExpr, bound, isMin, byte.MinValue, byte.MaxValue, v => "(byte)" + ((byte)v).ToString(inv)),
            "short" or "System.Int16" => FormatExactBoundedIntCondition(
                valueExpr, bound, isMin, short.MinValue, short.MaxValue, v => "(short)" + ParenthesizeIfNegative(((short)v).ToString(inv))),
            "ushort" or "System.UInt16" => FormatExactBoundedIntCondition(
                valueExpr, bound, isMin, ushort.MinValue, ushort.MaxValue, v => "(ushort)" + ((ushort)v).ToString(inv)),
            "int" or "System.Int32" => FormatExactBoundedIntCondition(
                valueExpr, bound, isMin, int.MinValue, int.MaxValue, v => ((int)v).ToString(inv)),
            "uint" or "System.UInt32" => FormatExactBoundedIntCondition(
                valueExpr, bound, isMin, uint.MinValue, uint.MaxValue, v => ((uint)v).ToString(inv) + "U"),
            "long" or "System.Int64" => FormatUpperExclusiveSignedIntCondition(
                valueExpr, bound, isMin, INT64_MIN_VAL, INT64_UPPER_EXCLUSIVE, "long.MinValue", v => ((long)v).ToString(inv) + "L"),
            "ulong" or "System.UInt64" => FormatUpperExclusiveUnsignedIntCondition(
                valueExpr, bound, isMin, UINT64_UPPER_EXCLUSIVE, v => ((ulong)v).ToString(inv) + "UL"),
            "System.Int128" => FormatUpperExclusiveSignedIntCondition(
                valueExpr, bound, isMin, INT128_MIN_VAL, INT128_UPPER_EXCLUSIVE,
                "global::System.Int128.MinValue", v => "(global::System.Int128)" + ParenthesizeIfNegative(FormatInvariant(v) + "d")),
            "System.UInt128" => FormatUpperExclusiveUnsignedIntCondition(
                valueExpr, bound, isMin, UINT128_UPPER_EXCLUSIVE,
                v => "(global::System.UInt128)" + ParenthesizeIfNegative(FormatInvariant(v) + "d")),
            "System.Numerics.BigInteger" => FormatBigIntegerCondition(valueExpr, bound, isMin),
            "decimal" or "System.Decimal" => FormatDecimalCondition(valueExpr, bound, isMin),
            "System.Half" => $"(double){valueExpr} {(isMin ? "<" : ">")} {FormatDoubleLiteral(bound)}",
            "float" or "System.Single" => $"(double){valueExpr} {(isMin ? "<" : ">")} {FormatDoubleLiteral(bound)}",
            "double" or "System.Double" => $"{valueExpr} {(isMin ? "<" : ">")} {FormatDoubleLiteral(bound)}",
            _ => $"{valueExpr} {(isMin ? "<" : ">")} {FormatRangeLiteral(bound, baseType)}"
        };
    }

    private static string FormatExactBoundedIntCondition(
        string valueExpr,
        double bound,
        bool isMin,
        double minVal,
        double maxVal,
        Func<double, string> formatLiteral)
    {
        double adjusted = isMin ? Math.Ceiling(bound) : Math.Floor(bound);
        if (adjusted == 0d)
            adjusted = 0d;

        if (isMin)
        {
            if (adjusted <= minVal)
                return "false";
            if (adjusted > maxVal)
                return "true";
            return $"{valueExpr} < {formatLiteral(adjusted)}";
        }

        if (adjusted >= maxVal)
            return "false";
        if (adjusted < minVal)
            return "true";
        return $"{valueExpr} > {formatLiteral(adjusted)}";
    }

    private static string FormatUpperExclusiveSignedIntCondition(
        string valueExpr,
        double bound,
        bool isMin,
        double minVal,
        double upperExclusive,
        string minValueExpr,
        Func<double, string> formatLiteral)
    {
        double adjusted = isMin ? Math.Ceiling(bound) : Math.Floor(bound);
        if (adjusted == 0d)
            adjusted = 0d;

        if (isMin)
        {
            if (adjusted <= minVal)
                return "false";
            if (adjusted >= upperExclusive)
                return "true";
            return $"{valueExpr} < {formatLiteral(adjusted)}";
        }

        if (adjusted >= upperExclusive)
            return "false";
        if (adjusted < minVal)
            return "true";
        if (adjusted == minVal)
            return $"{valueExpr} > {minValueExpr}";
        return $"{valueExpr} > {formatLiteral(adjusted)}";
    }

    private static string FormatUpperExclusiveUnsignedIntCondition(
        string valueExpr,
        double bound,
        bool isMin,
        double upperExclusive,
        Func<double, string> formatLiteral)
    {
        double adjusted = isMin ? Math.Ceiling(bound) : Math.Floor(bound);
        if (adjusted == 0d)
            adjusted = 0d;

        if (isMin)
        {
            if (adjusted <= 0d)
                return "false";
            if (adjusted >= upperExclusive)
                return "true";
            return $"{valueExpr} < {formatLiteral(adjusted)}";
        }

        if (adjusted >= upperExclusive)
            return "false";
        if (adjusted < 0d)
            return "true";
        return $"{valueExpr} > {formatLiteral(adjusted)}";
    }

    private static string FormatBigIntegerCondition(string valueExpr, double bound, bool isMin)
    {
        if (double.IsNegativeInfinity(bound))
            return isMin ? "false" : "true";
        if (double.IsPositiveInfinity(bound))
            return isMin ? "true" : "false";

        double adjusted = isMin ? Math.Ceiling(bound) : Math.Floor(bound);
        if (adjusted == 0d)
            adjusted = 0d;

        string op = isMin ? "<" : ">";
        return $"{valueExpr} {op} (global::System.Numerics.BigInteger){ParenthesizeIfNegative(FormatInvariant(adjusted) + "d")}";
    }

    private static readonly ulong[] pow10Table =
    [
        1UL,
        10UL,
        100UL,
        1_000UL,
        10_000UL,
        100_000UL,
        1_000_000UL,
        10_000_000UL,
        100_000_000UL,
        1_000_000_000UL,
        10_000_000_000UL,
        100_000_000_000UL,
        1_000_000_000_000UL,
        10_000_000_000_000UL,
        100_000_000_000_000UL,
        1_000_000_000_000_000UL,
        10_000_000_000_000_000UL,
        100_000_000_000_000_000UL,
        1_000_000_000_000_000_000UL,
        10_000_000_000_000_000_000UL
    ];

    private static string FormatDecimalCondition(string valueExpr, double bound, bool isMin)
    {
        if (bound <= -DECIMAL_UPPER_EXCLUSIVE)
            return isMin ? "false" : "true";
        if (bound >= DECIMAL_UPPER_EXCLUSIVE)
            return isMin ? "true" : "false";

        if (TryConvertDoubleBoundToDecimal(bound, isMin, out decimal decBound, out bool inclusiveViolation))
        {
            string op = isMin ? (inclusiveViolation ? "<=" : "<") : (inclusiveViolation ? ">=" : ">");
            string literal = decBound == 0m ? "0m" : decBound.ToString(CultureInfo.InvariantCulture) + "m";
            return $"{valueExpr} {op} {literal}";
        }

        string fallbackOp = isMin ? "<" : ">";
        return $"(double){valueExpr} {fallbackOp} {FormatDoubleLiteral(bound)}";
    }

    private static bool TryConvertDoubleBoundToDecimal(double bound, bool isMin, out decimal decBound, out bool inclusiveViolation)
    {
        decBound = 0m;
        inclusiveViolation = false;

        string text = FormatInvariant(bound);
        int i = 0;
        bool isNegative = false;
        if (i < text.Length && (text[i] == '-' || text[i] == '+'))
        {
            isNegative = text[i] == '-';
            i++;
        }

        ulong mantissa = 0;
        int sigDigits = 0;
        int fracDigits = 0;
        bool seenDot = false;
        bool seenNonZero = false;

        for (; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '.')
            {
                if (seenDot)
                    return false;
                seenDot = true;
                continue;
            }

            if (c == 'E' || c == 'e')
                break;

            if ((uint)(c - '0') > 9)
                return false;

            if (seenDot)
                fracDigits++;

            if (c != '0' || seenNonZero)
            {
                seenNonZero = true;
                sigDigits++;
                if (sigDigits > 19)
                    return false;
                mantissa = (mantissa * 10UL) + (uint)(c - '0');
            }
        }

        int exp = 0;
        if (i < text.Length && (text[i] == 'E' || text[i] == 'e'))
        {
            i++;
            if (!int.TryParse(text.Substring(i), NumberStyles.Integer, CultureInfo.InvariantCulture, out exp))
                return false;
        }

        if (mantissa == 0)
        {
            decBound = 0m;
            inclusiveViolation = false;
            return true;
        }

        int exp10 = exp - fracDigits;
        while (mantissa % 10UL == 0)
        {
            mantissa /= 10UL;
            exp10++;
        }

        if (exp10 >= -28)
            return decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out decBound);

        int shift = -28 - exp10;
        ulong quot = shift >= pow10Table.Length ? 0UL : mantissa / pow10Table[shift];
        decBound = quot == 0UL
            ? 0m
            : new decimal((int)(uint)quot, (int)(uint)(quot >> 32), 0, isNegative, 28);
        inclusiveViolation = isMin != isNegative;
        return true;
    }

    private static string FormatRangeLiteral(double value, string baseType)
    {
        var inv = CultureInfo.InvariantCulture;
        return baseType switch
        {
            "decimal" or "System.Decimal" => FormatInvariant(value) + "m",
            "float" or "System.Single" => FormatFloatLiteral(value),
            "double" or "System.Double" => FormatDoubleLiteral(value),
            "System.Half" => "(global::System.Half)" + ParenthesizeIfNegative(FormatDoubleLiteral(value)),
            "System.Int128" => "(global::System.Int128)" + ParenthesizeIfNegative(FormatDoubleLiteral(value)),
            "System.UInt128" => "(global::System.UInt128)" + ParenthesizeIfNegative(FormatDoubleLiteral(value)),
            "System.Numerics.BigInteger" => "(global::System.Numerics.BigInteger)" + ParenthesizeIfNegative(FormatDoubleLiteral(value)),
            "long" or "System.Int64" => ((long)value).ToString(inv) + "L",
            "ulong" or "System.UInt64" => ((ulong)value).ToString(inv) + "UL",
            "int" or "System.Int32" => ((int)value).ToString(inv),
            "uint" or "System.UInt32" => ((uint)value).ToString(inv) + "U",
            "short" or "System.Int16" => "(short)" + ParenthesizeIfNegative(((short)value).ToString(inv)),
            "ushort" or "System.UInt16" => "(ushort)" + ((ushort)value).ToString(inv),
            "byte" or "System.Byte" => "(byte)" + ((byte)value).ToString(inv),
            "sbyte" or "System.SByte" => "(sbyte)" + ParenthesizeIfNegative(((sbyte)value).ToString(inv)),
            _ => FormatInvariant(value)
        };
    }
}
