using System.Globalization;
using System.Runtime.CompilerServices;

namespace HeroParser.SeparatedValues;

/// <summary>
/// Represents a UTF-16 CSV column backed by the original character span.
/// </summary>
/// <remarks>Operations avoid allocations unless explicitly noted (e.g., <see cref="ToString"/>).</remarks>
public readonly ref struct CsvCharSpanColumn
{
    private readonly ReadOnlySpan<char> chars;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal CsvCharSpanColumn(ReadOnlySpan<char> chars)
    {
        this.chars = chars;
    }

    /// <summary>Gets the raw UTF-16 span that composes the column.</summary>
    public ReadOnlySpan<char> CharSpan => chars;

    /// <summary>Gets the column length in characters.</summary>
    public int Length => chars.Length;

    /// <summary>Gets a value indicating whether the column is empty.</summary>
    public bool IsEmpty => chars.IsEmpty;

    /// <summary>Creates a <see cref="string"/> representation of the column.</summary>
    public override string ToString() => new(chars);

    /// <summary>Parses the column using <see cref="ISpanParsable{T}"/> and invariant culture.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T Parse<T>() where T : ISpanParsable<T>
        => T.Parse(chars, CultureInfo.InvariantCulture);

    /// <summary>Attempts to parse the column using <see cref="ISpanParsable{T}"/> and invariant culture.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryParse<T>(out T? result) where T : ISpanParsable<T>
        => T.TryParse(chars, CultureInfo.InvariantCulture, out result);

    /// <summary>Attempts to parse the column as an <see cref="int"/> using invariant culture.</summary>
    public bool TryParseInt32(out int result)
        => int.TryParse(chars, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);

    /// <summary>Attempts to parse the column as a <see cref="long"/> using invariant culture.</summary>
    public bool TryParseInt64(out long result)
        => long.TryParse(chars, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);

    /// <summary>Attempts to parse the column as a <see cref="double"/> using invariant culture.</summary>
    public bool TryParseDouble(out double result)
        => double.TryParse(chars, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out result);

    /// <summary>Attempts to parse the column as a <see cref="decimal"/> using invariant culture.</summary>
    public bool TryParseDecimal(out decimal result)
        => decimal.TryParse(chars, NumberStyles.Number, CultureInfo.InvariantCulture, out result);

    /// <summary>Attempts to parse the column as a <see cref="bool"/>.</summary>
    public bool TryParseBoolean(out bool result)
        => bool.TryParse(chars, out result);

    /// <summary>Attempts to parse the column as a <see cref="DateTime"/> using invariant culture.</summary>
    public bool TryParseDateTime(out DateTime result)
        => DateTime.TryParse(chars, CultureInfo.InvariantCulture, DateTimeStyles.None, out result);

    /// <summary>Attempts to parse the column as a <see cref="Guid"/>.</summary>
    public bool TryParseGuid(out Guid result)
        => Guid.TryParse(chars, out result);

    /// <summary>Compares the column with a string using ordinal semantics.</summary>
    public bool Equals(string? other)
        => other is not null && other.AsSpan().SequenceEqual(chars);

    /// <summary>Returns the underlying span with surrounding quotes removed, if present.</summary>
    /// <param name="quote">Quote character (defaults to double quote).</param>
    public ReadOnlySpan<char> Unquote(char quote = '"')
    {
        var span = chars;
        if (span.Length >= 2 && span[0] == quote && span[^1] == quote)
        {
            return span[1..^1];
        }
        return span;
    }

    /// <summary>Unquotes the column (if needed) and returns it as a <see cref="string"/>, collapsing doubled quotes.</summary>
    /// <param name="quote">Quote character (defaults to double quote).</param>
    public string UnquoteToString(char quote = '"')
    {
        var span = chars;
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
