using System.Globalization;
using System.Runtime.CompilerServices;

namespace HeroParser;

/// <summary>
/// Represents a single CSV column value with zero-allocation parsing.
/// Wraps ReadOnlySpan&lt;char&gt; for direct span access and type conversions.
/// </summary>
public readonly ref struct CsvCol
{
    private readonly ReadOnlySpan<char> _span;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal CsvCol(ReadOnlySpan<char> span)
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
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T Parse<T>() where T : ISpanParsable<T>
    {
        return T.Parse(_span, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Try parse column as a specific type.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryParse<T>(out T result) where T : ISpanParsable<T>
    {
        return T.TryParse(_span, CultureInfo.InvariantCulture, out result);
    }

    // Optimized type-specific parsing methods (faster than generic Parse<T>)

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryParseInt32(out int result)
    {
        return int.TryParse(_span, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryParseInt64(out long result)
    {
        return long.TryParse(_span, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryParseDouble(out double result)
    {
        return double.TryParse(_span, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryParseDecimal(out decimal result)
    {
        return decimal.TryParse(_span, NumberStyles.Number, CultureInfo.InvariantCulture, out result);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryParseBoolean(out bool result)
    {
        return bool.TryParse(_span, out result);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryParseDateTime(out DateTime result)
    {
        return DateTime.TryParse(_span, CultureInfo.InvariantCulture, DateTimeStyles.None, out result);
    }

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
    public bool Equals(string other)
    {
        return _span.SequenceEqual(other.AsSpan());
    }

    /// <summary>
    /// Implicit conversion to ReadOnlySpan&lt;char&gt;.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator ReadOnlySpan<char>(CsvCol col) => col._span;
}
