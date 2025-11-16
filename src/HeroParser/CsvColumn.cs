using System.Globalization;
using System.Runtime.CompilerServices;

namespace HeroParser;

/// <summary>
/// Represents a single CSV column value with zero-allocation parsing.
/// Wraps ReadOnlySpan&lt;char&gt; for direct span access and type conversions.
/// </summary>
public readonly ref struct CsvColumn
{
    private readonly ReadOnlySpan<char> _span;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal CsvColumn(ReadOnlySpan<char> span)
    {
        _span = span;
    }

    /// <summary>
    /// Raw span access - zero allocations.
    /// </summary>
    public ReadOnlySpan<char> Span
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _span;
    }

    /// <summary>
    /// Length of the column value.
    /// </summary>
    public int Length
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _span.Length;
    }

    /// <summary>
    /// Check if column is empty.
    /// </summary>
    public bool IsEmpty
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _span.IsEmpty;
    }

    /// <summary>
    /// Parse column as a specific type using ISpanParsable&lt;T&gt;.
    /// Supported types: int, long, double, decimal, DateTime, Guid, etc.
    /// Zero allocations - parses directly from span.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T Parse<T>() where T : ISpanParsable<T>
    {
        return T.Parse(_span, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Try parse column as a specific type using ISpanParsable&lt;T&gt;.
    /// Zero allocations - parses directly from span.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryParse<T>(out T? result) where T : ISpanParsable<T>
    {
        return T.TryParse(_span, CultureInfo.InvariantCulture, out result);
    }

    // Type-specific parsing methods (work on all frameworks)

    /// <summary>
    /// Try parse as int. Zero allocations.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryParseInt32(out int result)
    {
        return int.TryParse(_span, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
    }

    /// <summary>
    /// Try parse as long. Zero allocations.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryParseInt64(out long result)
    {
        return long.TryParse(_span, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
    }

    /// <summary>
    /// Try parse as double. Zero allocations.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryParseDouble(out double result)
    {
        return double.TryParse(_span, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
    }

    /// <summary>
    /// Try parse as decimal. Zero allocations.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryParseDecimal(out decimal result)
    {
        return decimal.TryParse(_span, NumberStyles.Number, CultureInfo.InvariantCulture, out result);
    }

    /// <summary>
    /// Try parse as bool. Zero allocations.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryParseBoolean(out bool result)
    {
        return bool.TryParse(_span, out result);
    }

    /// <summary>
    /// Try parse as DateTime. Zero allocations.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryParseDateTime(out DateTime result)
    {
        return DateTime.TryParse(_span, CultureInfo.InvariantCulture, DateTimeStyles.None, out result);
    }

    /// <summary>
    /// Try parse as Guid. Zero allocations.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryParseGuid(out Guid result)
    {
        return Guid.TryParse(_span, out result);
    }

    /// <summary>
    /// Convert to string (allocates).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override string ToString()
    {
        return _span.ToString();
    }

    /// <summary>
    /// Check equality with another span.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(ReadOnlySpan<char> other)
    {
        return _span.SequenceEqual(other);
    }

    /// <summary>
    /// Check equality with a string.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(string? other)
    {
        if (other == null) return false;
        return _span.SequenceEqual(other.AsSpan());
    }

    /// <summary>
    /// Implicit conversion to ReadOnlySpan&lt;char&gt;.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator ReadOnlySpan<char>(CsvColumn col) => col._span;

    /// <summary>
    /// Remove RFC 4180 quotes and unescape doubled quotes.
    /// Returns the unquoted value as a ReadOnlySpan if no unescaping needed (zero allocation).
    /// If the value contains escaped quotes (""), allocates a new string with quotes unescaped.
    /// </summary>
    /// <param name="quote">Quote character (default: '"')</param>
    /// <returns>Unquoted span or original span if not quoted</returns>
    public ReadOnlySpan<char> Unquote(char quote = '"')
    {
        var span = _span;

        // Check if field is quoted
        if (span.Length >= 2 && span[0] == quote && span[^1] == quote)
        {
            // Remove surrounding quotes
            return span.Slice(1, span.Length - 2);
        }

        // Not quoted - return as-is
        return span;
    }

    /// <summary>
    /// Remove RFC 4180 quotes and fully unescape doubled quotes.
    /// This allocates a new string when escaped quotes ("") are present.
    /// Use Unquote() for zero-allocation when no escaped quotes exist.
    /// </summary>
    /// <param name="quote">Quote character (default: '"')</param>
    /// <returns>Unquoted and unescaped string</returns>
    public string UnquoteToString(char quote = '"')
    {
        var span = _span;

        // Check if field is quoted
        if (span.Length >= 2 && span[0] == quote && span[^1] == quote)
        {
            // Remove surrounding quotes
            var inner = span.Slice(1, span.Length - 2);

            // Check for escaped quotes
            if (inner.Contains(quote))
            {
                // Unescape doubled quotes: "" -> "
                return inner.ToString().Replace(new string(quote, 2), new string(quote, 1));
            }

            return inner.ToString();
        }

        // Not quoted - return as-is
        return span.ToString();
    }
}
