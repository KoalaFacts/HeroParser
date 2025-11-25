using System.Text;

namespace HeroParser.SeparatedValues;

/// <summary>
/// Represents a single CSV row backed by the original UTF-8 bytes.
/// </summary>
public readonly ref struct CsvByteSpanRow
{
    private readonly ReadOnlySpan<byte> line;
    private readonly int columnCount;
    private readonly ReadOnlySpan<int> columnStarts;
    private readonly ReadOnlySpan<int> columnLengths;

    internal CsvByteSpanRow(
        ReadOnlySpan<byte> line,
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
    /// <returns>A <see cref="CsvByteSpanColumn"/> pointing at the requested column.</returns>
    /// <exception cref="IndexOutOfRangeException">Thrown when <paramref name="index"/> falls outside <see cref="ColumnCount"/>.</exception>
    public CsvByteSpanColumn this[int index]
    {
        get
        {
            if ((uint)index >= (uint)columnCount)
            {
                throw new IndexOutOfRangeException(
                    $"Column index {index} is out of range. Column count is {columnCount}.");
            }

            var start = columnStarts[index];
            var length = columnLengths[index];
            return new CsvByteSpanColumn(line.Slice(start, length));
        }
    }

    /// <summary>Materializes the row into a UTF-8 decoded string array by copying the column data.</summary>
    public string[] ToStringArray()
    {
        var result = new string[columnCount];
        for (int i = 0; i < columnCount; i++)
        {
            result[i] = Encoding.UTF8.GetString(
                line.Slice(columnStarts[i], columnLengths[i]));
        }
        return result;
    }

    /// <summary>
    /// Creates an owned copy of the row data, solving the buffer ownership issue.
    /// </summary>
    /// <returns>A new <see cref="CsvByteSpanRow"/> with its own copy of the data.</returns>
    /// <remarks>
    /// This method allocates new memory and copies the row data, allowing the returned row
    /// to be used after the original buffer has been modified or disposed.
    /// </remarks>
    public CsvByteSpanRow Clone()
    {
        var newLine = line.ToArray();
        var newStarts = columnStarts.ToArray();
        var newLengths = columnLengths.ToArray();
        return new CsvByteSpanRow(newLine, newStarts, newLengths, columnCount);
    }

    /// <summary>
    /// Creates an immutable copy of the row data, solving the buffer ownership issue.
    /// </summary>
    /// <returns>A new <see cref="CsvByteSpanRow"/> with its own copy of the data.</returns>
    /// <remarks>
    /// This is an alias for <see cref="Clone"/> that creates an owned copy of the row data.
    /// </remarks>
    public CsvByteSpanRow ToImmutable() => Clone();
}
