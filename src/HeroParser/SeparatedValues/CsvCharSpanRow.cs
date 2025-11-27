using System.Buffers;
using System.Runtime.CompilerServices;

namespace HeroParser.SeparatedValues;

/// <summary>
/// Represents a single CSV row backed by the original UTF-16 characters.
/// </summary>
/// <remarks>
/// Thread-Safety: This is a ref struct that wraps stack-allocated or pooled memory and cannot be
/// shared across threads. Each reader should be used on a single thread. Use <see cref="Clone"/>
/// or <see cref="ToImmutable"/> to create owned copies if you need to store rows beyond the enumeration scope.
/// </remarks>
public readonly ref struct CsvCharSpanRow
{
    private readonly ReadOnlySpan<char> line;
    private readonly int columnCount;
    private readonly ReadOnlySpan<int> columnStarts;
    private readonly ReadOnlySpan<int> columnLengths;
    private readonly int lineNumber;

    internal CsvCharSpanRow(
        ReadOnlySpan<char> line,
        Span<int> columnStartsBuffer,
        Span<int> columnLengthsBuffer,
        int columnCount,
        int lineNumber)
    {
        this.line = line;
        this.columnCount = columnCount;
        this.lineNumber = lineNumber;
        columnStarts = columnStartsBuffer[..columnCount];
        columnLengths = columnLengthsBuffer[..columnCount];
    }

    /// <summary>Gets the number of parsed columns in the row.</summary>
    public int ColumnCount => columnCount;

    /// <summary>Gets the 1-based line number of this row in the CSV data.</summary>
    public int LineNumber => lineNumber;

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

    /// <summary>
    /// Creates an owned copy of the row data, solving the buffer ownership issue.
    /// </summary>
    /// <returns>A new <see cref="CsvCharSpanRow"/> with its own copy of the data.</returns>
    /// <remarks>
    /// This method allocates new memory and copies the row data, allowing the returned row
    /// to be used after the original buffer has been modified or disposed.
    /// </remarks>
    public CsvCharSpanRow Clone()
    {
        var newLine = line.ToArray();
        var newStarts = columnStarts.ToArray();
        var newLengths = columnLengths.ToArray();
        return new CsvCharSpanRow(newLine, newStarts, newLengths, columnCount, lineNumber);
    }

    /// <summary>
    /// Creates an immutable copy of the row data, solving the buffer ownership issue.
    /// </summary>
    /// <returns>A new <see cref="CsvCharSpanRow"/> with its own copy of the data.</returns>
    /// <remarks>
    /// This is an alias for <see cref="Clone"/> that creates an owned copy of the row data.
    /// </remarks>
    public CsvCharSpanRow ToImmutable() => Clone();

    // All potentially dangerous characters for SIMD pre-scan
    private static readonly SearchValues<char> allDangerousChars = SearchValues.Create("=@\t\r-+");

    /// <summary>
    /// Checks if any column in the row starts with a potentially dangerous character
    /// that could trigger CSV injection (formula injection) in spreadsheet applications.
    /// </summary>
    /// <returns>True if any column starts with a dangerous character pattern.</returns>
    /// <remarks>
    /// This method uses SIMD-accelerated pre-scanning to quickly determine if any
    /// dangerous characters exist in the row, making the common case (safe data) very fast.
    /// Dangerous patterns: =, @, \t, \r, and -/+ followed by non-numeric characters.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool HasDangerousFields()
    {
        // Fast path: SIMD pre-scan for any dangerous characters in the entire line
        // If no dangerous characters exist anywhere, we can return immediately
        if (!line.ContainsAny(allDangerousChars))
            return false;

        // Slow path: Check each column's first character
        for (int i = 0; i < columnCount; i++)
        {
            if (IsDangerousColumnCore(i))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Checks if a specific column starts with a potentially dangerous character.
    /// </summary>
    /// <param name="index">Zero-based column index.</param>
    /// <returns>True if the column starts with a dangerous character pattern.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsDangerousColumn(int index)
    {
        return IsDangerousColumnCore(index);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsDangerousColumnCore(int index)
    {
        var length = columnLengths[index];
        if (length == 0) return false;

        var start = columnStarts[index];
        char first = line[start];

        // Always dangerous characters
        if (first == '=' || first == '@' || first == '\t' || first == '\r')
            return true;

        // Smart detection for - and +
        if (first == '-' || first == '+')
        {
            if (length == 1) return false;
            char second = line[start + 1];
            // Safe if followed by digit or decimal point (numbers, phone numbers)
            return !((uint)(second - '0') <= 9 || second == '.');
        }

        return false;
    }
}
