using System.Globalization;
using System.Numerics;
using HeroParser.Internal;

namespace HeroParser.Validation;

/// <summary>
/// Evaluates <c>[Validate]</c> / fluent <c>Range(min, max)</c> bounds without losing integer precision by casting wide integers to <see cref="double"/>.
/// </summary>
internal readonly struct NumericRangeCheck(double? min, double? max)
{
    private const double DECIMAL_UPPER_EXCLUSIVE = 79228162514264337593543950336.0;
    private const double INT64_MIN_VAL = -9223372036854775808.0;
    private const double INT64_UPPER_EXCLUSIVE = 9223372036854775808.0;
    private const double UINT64_UPPER_EXCLUSIVE = 18446744073709551616.0;
    private const double INT128_MIN_VAL = -170141183460469231731687303715884105728.0;
    private const double INT128_UPPER_EXCLUSIVE = 170141183460469231731687303715884105728.0;
    private const double UINT128_UPPER_EXCLUSIVE = 340282366920938463463374607431768211456.0;

    /// <summary>Gets whether a finite or infinite (non-NaN) lower bound is configured.</summary>
    public bool HasMin => min is { } minVal && !double.IsNaN(minVal);

    /// <summary>Gets whether a finite or infinite (non-NaN) upper bound is configured.</summary>
    public bool HasMax => max is { } maxVal && !double.IsNaN(maxVal);

    /// <summary>Gets whether either range bound is configured.</summary>
    public bool HasRange => HasMin || HasMax;

    /// <summary>
    /// Evaluates range bounds against a boxed runtime property value.
    /// Returns <see langword="true"/> when <paramref name="value"/> was recognized as a numeric value.
    /// </summary>
    public bool TryEvaluate(
        object value,
        CultureInfo culture,
        out bool belowMin,
        out bool aboveMax,
        out string formattedValue)
    {
        switch (value)
        {
            case sbyte i8:
                EvaluateInt64(i8, out belowMin, out aboveMax, out formattedValue);
                return true;
            case byte u8:
                EvaluateInt64(u8, out belowMin, out aboveMax, out formattedValue);
                return true;
            case short i16:
                EvaluateInt64(i16, out belowMin, out aboveMax, out formattedValue);
                return true;
            case ushort u16:
                EvaluateInt64(u16, out belowMin, out aboveMax, out formattedValue);
                return true;
            case int i32:
                EvaluateInt64(i32, out belowMin, out aboveMax, out formattedValue);
                return true;
            case uint u32:
                EvaluateInt64(u32, out belowMin, out aboveMax, out formattedValue);
                return true;
            case long i64:
                EvaluateInt64(i64, out belowMin, out aboveMax, out formattedValue);
                return true;
            case ulong u64:
                EvaluateUInt64(u64, out belowMin, out aboveMax, out formattedValue);
                return true;
            case Int128 i128:
                EvaluateInt128(i128, out belowMin, out aboveMax, out formattedValue);
                return true;
            case UInt128 u128:
                EvaluateUInt128(u128, out belowMin, out aboveMax, out formattedValue);
                return true;
            case BigInteger bi:
                EvaluateBigInteger(bi, out belowMin, out aboveMax, out formattedValue);
                return true;
            case decimal dec:
                EvaluateDecimal(dec, out belowMin, out aboveMax, out formattedValue);
                return true;
            case Half h:
                EvaluateDouble((double)h, out belowMin, out aboveMax, out formattedValue);
                return true;
            case float f:
                EvaluateDouble(f, out belowMin, out aboveMax, out formattedValue);
                return true;
            case double d:
                EvaluateDouble(d, out belowMin, out aboveMax, out formattedValue);
                return true;
            case string text:
                return TryEvaluateSpan(text, culture, out belowMin, out aboveMax, out formattedValue, NumberStyles.Float | NumberStyles.AllowThousands);
            default:
                try
                {
                    double converted = Convert.ToDouble(value, culture);
                    EvaluateDouble(converted, out belowMin, out aboveMax, out formattedValue);
                    return true;
                }
                catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
                {
                    belowMin = false;
                    aboveMax = false;
                    formattedValue = string.Empty;
                    return false;
                }
        }
    }

    /// <summary>
    /// Evaluates range bounds against a character span representing a formatted numeric field.
    /// Returns <see langword="true"/> when <paramref name="value"/> parses as a number.
    /// </summary>
    public bool TryEvaluateSpan(
        ReadOnlySpan<char> value,
        CultureInfo culture,
        out bool belowMin,
        out bool aboveMax,
        out string formattedValue,
        NumberStyles floatStyles = NumberStyles.Any)
    {
        var parser = new CultureNumberParser(culture);
        if (parser.TryParse(value, NumberStyles.Integer, out long parsedInt64))
        {
            EvaluateInt64(parsedInt64, out belowMin, out aboveMax, out formattedValue);
            return true;
        }

        if (CouldBeOverflowingInteger(value, culture))
        {
            if (parser.TryParse(value, NumberStyles.Integer, out Int128 parsedInt128))
            {
                EvaluateInt128(parsedInt128, out belowMin, out aboveMax, out formattedValue);
                return true;
            }

            if (parser.TryParse(value, NumberStyles.Integer, out BigInteger parsedBi))
            {
                EvaluateBigInteger(parsedBi, out belowMin, out aboveMax, out formattedValue);
                return true;
            }
        }

        if (parser.TryParse(value, floatStyles, out double parsedDouble))
        {
            EvaluateDouble(parsedDouble, out belowMin, out aboveMax, out formattedValue);
            return true;
        }

        belowMin = false;
        aboveMax = false;
        formattedValue = string.Empty;
        return false;
    }

    private void EvaluateInt64(long value, out bool belowMin, out bool aboveMax, out string formattedValue)
    {
        belowMin = false;
        aboveMax = false;

        if (min is { } minVal && !double.IsNaN(minVal))
        {
            double adj = CeilingNormalized(minVal);
            if (adj >= INT64_UPPER_EXCLUSIVE)
                belowMin = true;
            else if (adj > INT64_MIN_VAL)
                belowMin = value < (long)adj;
        }

        if (max is { } maxVal && !double.IsNaN(maxVal))
        {
            double adj = FloorNormalized(maxVal);
            if (adj < INT64_MIN_VAL)
                aboveMax = true;
            else if (adj == INT64_MIN_VAL)
                aboveMax = value > long.MinValue;
            else if (adj < INT64_UPPER_EXCLUSIVE)
                aboveMax = value > (long)adj;
        }

        formattedValue = belowMin || aboveMax ? value.ToString(CultureInfo.InvariantCulture) : string.Empty;
    }

    private void EvaluateUInt64(ulong value, out bool belowMin, out bool aboveMax, out string formattedValue)
    {
        belowMin = false;
        aboveMax = false;

        if (min is { } minVal && !double.IsNaN(minVal))
        {
            double adj = CeilingNormalized(minVal);
            if (adj >= UINT64_UPPER_EXCLUSIVE)
                belowMin = true;
            else if (adj > 0d)
                belowMin = value < (ulong)adj;
        }

        if (max is { } maxVal && !double.IsNaN(maxVal))
        {
            double adj = FloorNormalized(maxVal);
            if (adj < 0d)
                aboveMax = true;
            else if (adj < UINT64_UPPER_EXCLUSIVE)
                aboveMax = value > (ulong)adj;
        }

        formattedValue = belowMin || aboveMax ? value.ToString(CultureInfo.InvariantCulture) : string.Empty;
    }

    private void EvaluateInt128(Int128 value, out bool belowMin, out bool aboveMax, out string formattedValue)
    {
        belowMin = false;
        aboveMax = false;

        if (min is { } minVal && !double.IsNaN(minVal))
        {
            double adj = CeilingNormalized(minVal);
            if (adj >= INT128_UPPER_EXCLUSIVE)
                belowMin = true;
            else if (adj > INT128_MIN_VAL)
                belowMin = value < (Int128)adj;
        }

        if (max is { } maxVal && !double.IsNaN(maxVal))
        {
            double adj = FloorNormalized(maxVal);
            if (adj < INT128_MIN_VAL)
                aboveMax = true;
            else if (adj == INT128_MIN_VAL)
                aboveMax = value > Int128.MinValue;
            else if (adj < INT128_UPPER_EXCLUSIVE)
                aboveMax = value > (Int128)adj;
        }

        formattedValue = belowMin || aboveMax ? value.ToString(CultureInfo.InvariantCulture) : string.Empty;
    }

    private void EvaluateUInt128(UInt128 value, out bool belowMin, out bool aboveMax, out string formattedValue)
    {
        belowMin = false;
        aboveMax = false;

        if (min is { } minVal && !double.IsNaN(minVal))
        {
            double adj = CeilingNormalized(minVal);
            if (adj >= UINT128_UPPER_EXCLUSIVE)
                belowMin = true;
            else if (adj > 0d)
                belowMin = value < (UInt128)adj;
        }

        if (max is { } maxVal && !double.IsNaN(maxVal))
        {
            double adj = FloorNormalized(maxVal);
            if (adj < 0d)
                aboveMax = true;
            else if (adj < UINT128_UPPER_EXCLUSIVE)
                aboveMax = value > (UInt128)adj;
        }

        formattedValue = belowMin || aboveMax ? value.ToString(CultureInfo.InvariantCulture) : string.Empty;
    }

    private void EvaluateBigInteger(BigInteger value, out bool belowMin, out bool aboveMax, out string formattedValue)
    {
        belowMin = false;
        aboveMax = false;

        if (min is { } minVal && !double.IsNaN(minVal))
        {
            if (double.IsPositiveInfinity(minVal))
                belowMin = true;
            else if (!double.IsNegativeInfinity(minVal))
                belowMin = value < new BigInteger(CeilingNormalized(minVal));
        }

        if (max is { } maxVal && !double.IsNaN(maxVal))
        {
            if (double.IsNegativeInfinity(maxVal))
                aboveMax = true;
            else if (!double.IsPositiveInfinity(maxVal))
                aboveMax = value > new BigInteger(FloorNormalized(maxVal));
        }

        formattedValue = belowMin || aboveMax ? value.ToString(CultureInfo.InvariantCulture) : string.Empty;
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

    private void EvaluateDecimal(decimal value, out bool belowMin, out bool aboveMax, out string formattedValue)
    {
        belowMin = false;
        aboveMax = false;

        if (min is { } minVal && !double.IsNaN(minVal))
        {
            if (minVal >= DECIMAL_UPPER_EXCLUSIVE)
                belowMin = true;
            else if (minVal > -DECIMAL_UPPER_EXCLUSIVE)
            {
                belowMin = TryConvertDoubleBoundToDecimal(minVal, isMin: true, out decimal decMin, out bool inclusiveMin)
                    ? (inclusiveMin ? value <= decMin : value < decMin)
                    : (double)value < minVal;
            }
        }

        if (max is { } maxVal && !double.IsNaN(maxVal))
        {
            if (maxVal <= -DECIMAL_UPPER_EXCLUSIVE)
                aboveMax = true;
            else if (maxVal < DECIMAL_UPPER_EXCLUSIVE)
            {
                aboveMax = TryConvertDoubleBoundToDecimal(maxVal, isMin: false, out decimal decMax, out bool inclusiveMax)
                    ? (inclusiveMax ? value >= decMax : value > decMax)
                    : (double)value > maxVal;
            }
        }

        formattedValue = belowMin || aboveMax ? value.ToString(CultureInfo.InvariantCulture) : string.Empty;
    }

    private void EvaluateDouble(double value, out bool belowMin, out bool aboveMax, out string formattedValue)
    {
        belowMin = min is { } minVal && !double.IsNaN(minVal) && value < minVal;
        aboveMax = max is { } maxVal && !double.IsNaN(maxVal) && value > maxVal;
        formattedValue = belowMin || aboveMax ? value.ToString(CultureInfo.InvariantCulture) : string.Empty;
    }

    private static bool TryConvertDoubleBoundToDecimal(double bound, bool isMin, out decimal decBound, out bool inclusiveViolation)
    {
        decBound = 0m;
        inclusiveViolation = false;

        Span<char> buffer = stackalloc char[64];
        if (!bound.TryFormat(buffer, out int written, "R", CultureInfo.InvariantCulture))
            return false;

        ReadOnlySpan<char> text = buffer[..written];
        int i = 0;
        bool isNegative = false;
        if (i < text.Length && text[i] is '-' or '+')
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

            if (c is 'E' or 'e')
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
        if (i < text.Length && text[i] is 'E' or 'e')
        {
            i++;
            if (!int.TryParse(text[i..], NumberStyles.Integer, CultureInfo.InvariantCulture, out exp))
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

    private static double CeilingNormalized(double value)
    {
        double result = Math.Ceiling(value);
        return result == 0d ? 0d : result;
    }

    private static double FloorNormalized(double value)
    {
        double result = Math.Floor(value);
        return result == 0d ? 0d : result;
    }

    private static bool CouldBeOverflowingInteger(ReadOnlySpan<char> value, CultureInfo culture)
    {
        if (value.Length < 19)
            return false;

        ReadOnlySpan<char> trimmed = value.Trim();
        if (trimmed.Length < 19)
            return false;

        var nfi = culture.NumberFormat;
        if (!string.IsNullOrEmpty(nfi.NegativeSign) && trimmed.StartsWith(nfi.NegativeSign, StringComparison.Ordinal))
            trimmed = trimmed[nfi.NegativeSign.Length..];
        else if (!string.IsNullOrEmpty(nfi.PositiveSign) && trimmed.StartsWith(nfi.PositiveSign, StringComparison.Ordinal))
            trimmed = trimmed[nfi.PositiveSign.Length..];
        else if (trimmed[0] is '-' or '+')
            trimmed = trimmed[1..];

        if (trimmed.Length < 19)
            return false;

        foreach (char c in trimmed)
        {
            if (!char.IsAsciiDigit(c))
                return false;
        }

        return true;
    }
}
