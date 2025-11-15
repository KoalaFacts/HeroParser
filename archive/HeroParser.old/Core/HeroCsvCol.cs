using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace HeroParser.Core;

/// <summary>
/// A readonly ref struct that provides zero-allocation access to a single CSV column/field.
/// Inspired by Sep's memory optimization approach using delayed evaluation.
/// </summary>
[DebuggerDisplay("{ToString()}")]
public readonly ref struct HeroCsvCol
{
    private readonly ReadOnlySpan<char> _span;
    private readonly bool _isQuoted;
    private readonly bool _trimValues;

    internal HeroCsvCol(ReadOnlySpan<char> span, bool isQuoted = false, bool trimValues = false)
    {
        _span = span;
        _isQuoted = isQuoted;
        _trimValues = trimValues;
    }

    /// <summary>
    /// Gets the length of the column content.
    /// </summary>
    public int Length => _span.Length;

    /// <summary>
    /// Gets whether this column is empty.
    /// </summary>
    public bool IsEmpty => _span.IsEmpty;

    /// <summary>
    /// Gets the column content as a ReadOnlySpan of characters.
    /// This provides zero-allocation access to the raw data.
    /// </summary>
    public ReadOnlySpan<char> Span
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (_trimValues && !_span.IsEmpty)
            {
                return _span.Trim();
            }
            return _span;
        }
    }

    /// <summary>
    /// Converts the column to a string. This allocates a new string.
    /// Use Span property when possible to avoid allocation.
    /// </summary>
    /// <returns>The column content as a string.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override string ToString()
    {
        var span = Span;
        return span.IsEmpty ? string.Empty : span.ToString();
    }

#if NET7_0_OR_GREATER
    /// <summary>
    /// Tries to parse the column content as the specified type.
    /// Uses modern .NET ISpanParsable interface for zero-allocation parsing.
    /// </summary>
    /// <typeparam name="T">The type to parse to. Must implement ISpanParsable.</typeparam>
    /// <param name="value">The parsed value if successful.</param>
    /// <returns>True if parsing succeeded, false otherwise.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryParse<T>(out T? value) where T : ISpanParsable<T>
    {
        return T.TryParse(Span, null, out value);
    }

    /// <summary>
    /// Parses the column content as the specified type.
    /// Uses modern .NET ISpanParsable interface for zero-allocation parsing.
    /// </summary>
    /// <typeparam name="T">The type to parse to. Must implement ISpanParsable.</typeparam>
    /// <returns>The parsed value.</returns>
    /// <exception cref="FormatException">Thrown when parsing fails.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T Parse<T>() where T : ISpanParsable<T>
    {
        return T.Parse(Span, null);
    }
#endif

    /// <summary>
    /// Tries to parse the column content as an integer.
    /// </summary>
    /// <param name="value">The parsed integer if successful.</param>
    /// <returns>True if parsing succeeded, false otherwise.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryParseInt32(out int value)
    {
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
        return int.TryParse(Span, out value);
#else
        return int.TryParse(ToString(), out value);
#endif
    }

    /// <summary>
    /// Tries to parse the column content as a long.
    /// </summary>
    /// <param name="value">The parsed long if successful.</param>
    /// <returns>True if parsing succeeded, false otherwise.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryParseInt64(out long value)
    {
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
        return long.TryParse(Span, out value);
#else
        return long.TryParse(ToString(), out value);
#endif
    }

    /// <summary>
    /// Tries to parse the column content as a double.
    /// </summary>
    /// <param name="value">The parsed double if successful.</param>
    /// <returns>True if parsing succeeded, false otherwise.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryParseDouble(out double value)
    {
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
        return double.TryParse(Span, out value);
#else
        return double.TryParse(ToString(), out value);
#endif
    }

    /// <summary>
    /// Tries to parse the column content as a decimal.
    /// </summary>
    /// <param name="value">The parsed decimal if successful.</param>
    /// <returns>True if parsing succeeded, false otherwise.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryParseDecimal(out decimal value)
    {
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
        return decimal.TryParse(Span, out value);
#else
        return decimal.TryParse(ToString(), out value);
#endif
    }

    /// <summary>
    /// Tries to parse the column content as a DateTime.
    /// </summary>
    /// <param name="value">The parsed DateTime if successful.</param>
    /// <returns>True if parsing succeeded, false otherwise.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryParseDateTime(out DateTime value)
    {
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
        return DateTime.TryParse(Span, out value);
#else
        return DateTime.TryParse(ToString(), out value);
#endif
    }

    /// <summary>
    /// Tries to parse the column content as a boolean.
    /// Supports various representations: true/false, 1/0, yes/no, y/n.
    /// </summary>
    /// <param name="value">The parsed boolean if successful.</param>
    /// <returns>True if parsing succeeded, false otherwise.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryParseBoolean(out bool value)
    {
        var span = Span;

        // Handle empty/null
        if (span.IsEmpty)
        {
            value = false;
            return true;
        }

        // Try standard boolean parsing first
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
        if (bool.TryParse(span, out value))
        {
            return true;
        }
#else
        if (bool.TryParse(ToString(), out value))
        {
            return true;
        }
#endif

        // Try common representations
        if (span.Length == 1)
        {
            var ch = char.ToLowerInvariant(span[0]);
            switch (ch)
            {
                case '1':
                case 'y':
                case 't':
                    value = true;
                    return true;
                case '0':
                case 'n':
                case 'f':
                    value = false;
                    return true;
            }
        }
        else if (span.Equals("yes".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
                 span.Equals("on".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            value = true;
            return true;
        }
        else if (span.Equals("no".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
                 span.Equals("off".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            value = false;
            return true;
        }

        value = false;
        return false;
    }

    /// <summary>
    /// Checks if the column content equals the specified string.
    /// </summary>
    /// <param name="value">The string to compare with.</param>
    /// <param name="comparisonType">The string comparison type.</param>
    /// <returns>True if the content equals the string, false otherwise.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(string value, StringComparison comparisonType = StringComparison.Ordinal)
    {
        return Span.Equals(value.AsSpan(), comparisonType);
    }

    /// <summary>
    /// Checks if the column content equals the specified span.
    /// </summary>
    /// <param name="value">The span to compare with.</param>
    /// <param name="comparisonType">The string comparison type.</param>
    /// <returns>True if the content equals the span, false otherwise.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(ReadOnlySpan<char> value, StringComparison comparisonType = StringComparison.Ordinal)
    {
        return Span.Equals(value, comparisonType);
    }

    /// <summary>
    /// Copies the column content to the specified destination span.
    /// </summary>
    /// <param name="destination">The destination span.</param>
    /// <returns>True if the copy succeeded, false if the destination was too small.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryCopyTo(Span<char> destination)
    {
        return Span.TryCopyTo(destination);
    }

    /// <summary>
    /// Implicit conversion to ReadOnlySpan for convenient usage.
    /// </summary>
    /// <param name="col">The column to convert.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator ReadOnlySpan<char>(HeroCsvCol col) => col.Span;

    /// <summary>
    /// Implicit conversion to string for convenient usage.
    /// Note: This allocates a new string.
    /// </summary>
    /// <param name="col">The column to convert.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator string(HeroCsvCol col) => col.ToString();
}