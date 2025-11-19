using System.Globalization;
using System.Runtime.CompilerServices;

namespace HeroParser;

/// <summary>
/// Represents a UTF-16 column.
/// </summary>
public readonly ref struct CsvCharSpanColumn
{
    private readonly ReadOnlySpan<char> _chars;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal CsvCharSpanColumn(ReadOnlySpan<char> chars)
    {
        _chars = chars;
    }

    /// <summary>Raw UTF-16 span.</summary>
    public ReadOnlySpan<char> CharSpan => _chars;

    /// <summary>Length in characters.</summary>
    public int Length => _chars.Length;

    /// <summary>Whether the column is empty.</summary>
    public bool IsEmpty => _chars.IsEmpty;

    /// <summary>Return the column as a string.</summary>
    public override string ToString() => new string(_chars);

    /// <summary>Parse via <see cref="ISpanParsable{T}"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T Parse<T>() where T : ISpanParsable<T>
        => T.Parse(_chars, CultureInfo.InvariantCulture);

    /// <summary>Try parse via <see cref="ISpanParsable{T}"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryParse<T>(out T? result) where T : ISpanParsable<T>
        => T.TryParse(_chars, CultureInfo.InvariantCulture, out result);

    /// <summary>Try parse as <see cref="int"/>.</summary>
    public bool TryParseInt32(out int result)
        => int.TryParse(_chars, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);

    /// <summary>Try parse as <see cref="long"/>.</summary>
    public bool TryParseInt64(out long result)
        => long.TryParse(_chars, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);

    /// <summary>Try parse as <see cref="double"/>.</summary>
    public bool TryParseDouble(out double result)
        => double.TryParse(_chars, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out result);

    /// <summary>Try parse as <see cref="decimal"/>.</summary>
    public bool TryParseDecimal(out decimal result)
        => decimal.TryParse(_chars, NumberStyles.Number, CultureInfo.InvariantCulture, out result);

    /// <summary>Try parse as <see cref="bool"/>.</summary>
    public bool TryParseBoolean(out bool result)
        => bool.TryParse(_chars, out result);

    /// <summary>Try parse as <see cref="DateTime"/>.</summary>
    public bool TryParseDateTime(out DateTime result)
        => DateTime.TryParse(_chars, CultureInfo.InvariantCulture, DateTimeStyles.None, out result);

    /// <summary>Try parse as <see cref="Guid"/>.</summary>
    public bool TryParseGuid(out Guid result)
        => Guid.TryParse(_chars, out result);

    /// <summary>Compare with a string.</summary>
    public bool Equals(string? other)
        => other is not null && other.AsSpan().SequenceEqual(_chars);

    /// <summary>Return the inner span without surrounding quotes.</summary>
    public ReadOnlySpan<char> Unquote(char quote = '"')
    {
        var span = _chars;
        if (span.Length >= 2 && span[0] == quote && span[^1] == quote)
        {
            return span[1..^1];
        }
        return span;
    }

    /// <summary>Unquote and return as string.</summary>
    public string UnquoteToString(char quote = '"')
    {
        var span = _chars;
        if (span.Length >= 2 && span[0] == quote && span[^1] == quote)
        {
            var inner = span[1..^1];
            if (inner.Contains(quote))
            {
                return inner.ToString().Replace(new string(quote, 2), new string(quote, 1));
            }
            return inner.ToString();
        }

        return span.ToString();
    }
}
