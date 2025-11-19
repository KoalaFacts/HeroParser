using System.Text;

namespace HeroParser.SeparatedValues;

/// <summary>
/// Represents a UTF-8 row returned by <see cref="CsvByteSpanReader"/>.
/// </summary>
public readonly ref struct CsvByteSpanRow
{
    private readonly ReadOnlySpan<byte> _line;
    private readonly ReadOnlySpan<int> _columnStarts;
    private readonly ReadOnlySpan<int> _columnLengths;
    private readonly int _columnCount;

    internal CsvByteSpanRow(
        ReadOnlySpan<byte> line,
        Span<int> columnStartsBuffer,
        Span<int> columnLengthsBuffer,
        int columnCount)
    {
        _line = line;
        _columnStarts = columnStartsBuffer[..columnCount];
        _columnLengths = columnLengthsBuffer[..columnCount];
        _columnCount = columnCount;
    }

    /// <summary>Number of columns.</summary>
    public int ColumnCount => _columnCount;

    /// <summary>Access a column by index.</summary>
    public CsvByteSpanColumn this[int index]
    {
        get
        {
            var start = _columnStarts[index];
            var length = _columnLengths[index];
            return new CsvByteSpanColumn(_line.Slice(start, length));
        }
    }

    /// <summary>Materialize the row as strings (allocates).</summary>
    public string[] ToStringArray()
    {
        var result = new string[_columnCount];
        for (int i = 0; i < _columnCount; i++)
        {
            result[i] = Encoding.UTF8.GetString(
                _line.Slice(_columnStarts[i], _columnLengths[i]));
        }
        return result;
    }
}
