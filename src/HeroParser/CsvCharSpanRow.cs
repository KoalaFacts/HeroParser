using System.Runtime.CompilerServices;

namespace HeroParser;

/// <summary>
/// Represents a UTF-16 row parsed by the streaming reader.
/// </summary>
public readonly ref struct CsvRow
{
    private readonly ReadOnlySpan<char> _line;
    private readonly ReadOnlySpan<int> _columnStarts;
    private readonly ReadOnlySpan<int> _columnLengths;
    private readonly int _columnCount;

    internal CsvRow(
        ReadOnlySpan<char> line,
        Span<int> columnStartsBuffer,
        Span<int> columnLengthsBuffer,
        int columnCount)
    {
        _line = line;
        _columnStarts = columnStartsBuffer[..columnCount];
        _columnLengths = columnLengthsBuffer[..columnCount];
        _columnCount = columnCount;
    }

    /// <summary>Number of columns in this row.</summary>
    public int ColumnCount => _columnCount;

    /// <summary>Access a column by index.</summary>
    public CsvColumn this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            var start = _columnStarts[index];
            var length = _columnLengths[index];
            return new CsvColumn(_line.Slice(start, length));
        }
    }

    /// <summary>Materialize the row as a string array.</summary>
    public string[] ToStringArray()
    {
        var result = new string[_columnCount];
        for (int i = 0; i < _columnCount; i++)
        {
            result[i] = new string(_line.Slice(_columnStarts[i], _columnLengths[i]));
        }
        return result;
    }
}
