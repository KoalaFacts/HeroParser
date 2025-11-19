using System.Buffers.Text;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace HeroParser.SeparatedValues;

/// <summary>
/// Represents a UTF-8 encoded CSV column backed by the original input buffer.
/// </summary>
/// <remarks>
/// All operations avoid allocations unless explicitly stated (e.g., <see cref="ToString"/> or <see cref="UnquoteToString(byte)"/>).
/// </remarks>
public readonly ref struct CsvByteSpanColumn
{
    private readonly ReadOnlySpan<byte> utf8;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal CsvByteSpanColumn(ReadOnlySpan<byte> utf8)
    {
        this.utf8 = utf8;
    }

    /// <summary>Gets the raw UTF-8 bytes that compose the column.</summary>
    public ReadOnlySpan<byte> Utf8Span => utf8;
    /// <summary>Gets the column length in bytes.</summary>
    public int Length => utf8.Length;
    /// <summary>Gets a value indicating whether the column is empty.</summary>
    public bool IsEmpty => utf8.IsEmpty;

    /// <summary>Decodes the column into a UTF-16 <see cref="string"/>.</summary>
    public override string ToString() => Encoding.UTF8.GetString(utf8);

    /// <summary>Parses the column using <see cref="ISpanParsable{T}"/> and invariant culture.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T Parse<T>() where T : ISpanParsable<T>
        => T.Parse(ToString(), CultureInfo.InvariantCulture);

    /// <summary>Attempts to parse the column using <see cref="ISpanParsable{T}"/> and invariant culture.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryParse<T>(out T? result) where T : ISpanParsable<T>
        => T.TryParse(ToString(), CultureInfo.InvariantCulture, out result);

    /// <summary>Attempts to parse the column as an <see cref="int"/> using invariant culture.</summary>
    public bool TryParseInt32(out int result)
        => Utf8Parser.TryParse(utf8, out result, out int consumed) && consumed == utf8.Length;

    /// <summary>Attempts to parse the column as a <see cref="long"/> using invariant culture.</summary>
    public bool TryParseInt64(out long result)
        => Utf8Parser.TryParse(utf8, out result, out int consumed) && consumed == utf8.Length;

    /// <summary>Attempts to parse the column as a <see cref="double"/> using invariant culture.</summary>
    public bool TryParseDouble(out double result)
        => Utf8Parser.TryParse(utf8, out result, out int consumed) && consumed == utf8.Length;

    /// <summary>Attempts to parse the column as a <see cref="decimal"/> using invariant culture.</summary>
    public bool TryParseDecimal(out decimal result)
        => Utf8Parser.TryParse(utf8, out result, out int consumed) && consumed == utf8.Length;

    /// <summary>Attempts to parse the column as a <see cref="bool"/> using invariant culture.</summary>
    public bool TryParseBoolean(out bool result)
        => Utf8Parser.TryParse(utf8, out result, out int consumed) && consumed == utf8.Length;

    /// <summary>Attempts to parse the column as a <see cref="DateTime"/> using invariant culture.</summary>
    public bool TryParseDateTime(out DateTime result)
        => Utf8Parser.TryParse(utf8, out result, out int consumed) && consumed == utf8.Length;

    /// <summary>Attempts to parse the column as a <see cref="Guid"/>.</summary>
    public bool TryParseGuid(out Guid result)
        => Utf8Parser.TryParse(utf8, out result, out int consumed) && consumed == utf8.Length;

    /// <summary>Compares the column with a string using ordinal semantics.</summary>
    public bool Equals(string? other)
        => other is not null && other.AsSpan().SequenceEqual(ToString());

    /// <summary>Returns the underlying span with surrounding quotes removed, if present.</summary>
    /// <param name="quote">Quote character (defaults to double quote).</param>
    public ReadOnlySpan<byte> Unquote(byte quote = (byte)'"')
    {
        var span = utf8;
        if (span.Length >= 2 && span[0] == quote && span[^1] == quote)
        {
            return span[1..^1];
        }
        return span;
    }

    /// <summary>Unquotes and decodes the column into a <see cref="string"/>, collapsing doubled quotes.</summary>
    /// <param name="quote">Quote character (defaults to double quote).</param>
    public string UnquoteToString(byte quote = (byte)'"')
    {
        var span = utf8;

        if (span.Length >= 2 && span[0] == quote && span[^1] == quote)
        {
            var inner = span[1..^1];

            if (inner.Contains(quote))
            {
                return Encoding.UTF8.GetString(inner).Replace(new string((char)quote, 2), new string((char)quote, 1));
            }

            return Encoding.UTF8.GetString(inner);
        }

        return Encoding.UTF8.GetString(span);
    }
}
