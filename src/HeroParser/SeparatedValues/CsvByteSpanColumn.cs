using System.Buffers.Text;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace HeroParser.SeparatedValues;

/// <summary>
/// Represents a UTF-8 column returned by the byte reader.
/// </summary>
public readonly ref struct CsvByteSpanColumn
{
    private readonly ReadOnlySpan<byte> _utf8;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal CsvByteSpanColumn(ReadOnlySpan<byte> utf8)
    {
        _utf8 = utf8;
    }

    /// <summary>Raw UTF-8 bytes.</summary>
    public ReadOnlySpan<byte> Utf8Span => _utf8;
    /// <summary>Length in bytes.</summary>
    public int Length => _utf8.Length;
    /// <summary>Whether the column is empty.</summary>
    public bool IsEmpty => _utf8.IsEmpty;

    /// <summary>Return the column as a string.</summary>
    public override string ToString() => Encoding.UTF8.GetString(_utf8);

    /// <summary>Parse via <see cref="ISpanParsable{T}"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T Parse<T>() where T : ISpanParsable<T>
        => T.Parse(ToString(), CultureInfo.InvariantCulture);

    /// <summary>Try parse via <see cref="ISpanParsable{T}"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryParse<T>(out T? result) where T : ISpanParsable<T>
        => T.TryParse(ToString(), CultureInfo.InvariantCulture, out result);

    /// <summary>Try parse as <see cref="int"/>.</summary>
    public bool TryParseInt32(out int result)
        => Utf8Parser.TryParse(_utf8, out result, out int consumed) && consumed == _utf8.Length;

    /// <summary>Try parse as <see cref="long"/>.</summary>
    public bool TryParseInt64(out long result)
        => Utf8Parser.TryParse(_utf8, out result, out int consumed) && consumed == _utf8.Length;

    /// <summary>Try parse as <see cref="double"/>.</summary>
    public bool TryParseDouble(out double result)
        => Utf8Parser.TryParse(_utf8, out result, out int consumed) && consumed == _utf8.Length;

    /// <summary>Try parse as <see cref="decimal"/>.</summary>
    public bool TryParseDecimal(out decimal result)
        => Utf8Parser.TryParse(_utf8, out result, out int consumed) && consumed == _utf8.Length;

    /// <summary>Try parse as <see cref="bool"/>.</summary>
    public bool TryParseBoolean(out bool result)
        => Utf8Parser.TryParse(_utf8, out result, out int consumed) && consumed == _utf8.Length;

    /// <summary>Try parse as <see cref="DateTime"/>.</summary>
    public bool TryParseDateTime(out DateTime result)
        => Utf8Parser.TryParse(_utf8, out result, out int consumed) && consumed == _utf8.Length;

    /// <summary>Try parse as <see cref="Guid"/>.</summary>
    public bool TryParseGuid(out Guid result)
        => Utf8Parser.TryParse(_utf8, out result, out int consumed) && consumed == _utf8.Length;

    /// <summary>Compare with a string.</summary>
    public bool Equals(string? other)
        => other is not null && other.AsSpan().SequenceEqual(ToString());

    /// <summary>Return the inner span without surrounding quotes.</summary>
    public ReadOnlySpan<byte> Unquote(byte quote = (byte)'"')
    {
        var span = _utf8;
        if (span.Length >= 2 && span[0] == quote && span[^1] == quote)
        {
            return span[1..^1];
        }
        return span;
    }

    /// <summary>Unquote and decode to string.</summary>
    public string UnquoteToString(byte quote = (byte)'"')
    {
        var span = _utf8;

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
