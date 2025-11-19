using System.Runtime.CompilerServices;

namespace HeroParser.SeparatedValues;

/// <summary>
/// Represents a single CSV row backed by the original UTF-16 characters.
/// </summary>
public readonly ref struct CsvCharSpanRow
{
    private readonly ReadOnlySpan<char> line;
    private readonly int columnCount;
    private readonly ReadOnlySpan<int> columnStarts;
    private readonly ReadOnlySpan<int> columnLengths;

    internal CsvCharSpanRow(
        ReadOnlySpan<char> line,
        Span<int> columnStartsBuffer,
        Span<int> columnLengthsBuffer,
        int columnCount)
    {
        this.line = line;
        this.columnCount = columnCount;
        columnStarts = columnStartsBuffer[..columnCount];
        columnLengths = columnLengthsBuffer[..columnCount];
    }

    /// <summary>Gets the number of parsed columns in the row.</summary>
    public int ColumnCount => columnCount;

    /// <summary>Gets a column by zero-based index.</summary>
    /// <param name="index">Zero-based column index.</param>
    /// <returns>A <see cref="CsvCharSpanColumn"/> pointing at the requested column.</returns>
    /// <exception cref="IndexOutOfRangeException">Thrown when <paramref name="index"/> falls outside <see cref="ColumnCount"/>.</exception>
    public CsvCharSpanColumn this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if ((uint)index >= (uint)columnCount)
            {
                throw new IndexOutOfRangeException(
                    $"Column index {index} is out of range. Column count is {columnCount}.");
            }

            var start = columnStarts[index];
            var length = columnLengths[index];
            return new CsvCharSpanColumn(line.Slice(start, length));
        }
    }

    /// <summary>Materializes the row into a string array by copying the underlying characters.</summary>
    public string[] ToStringArray()
    {
        var result = new string[columnCount];
        for (int i = 0; i < columnCount; i++)
        {
            result[i] = new string(line.Slice(columnStarts[i], columnLengths[i]));
        }
        return result;
    }
}
