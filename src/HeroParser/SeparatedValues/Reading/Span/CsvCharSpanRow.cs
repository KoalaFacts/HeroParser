using System.Buffers;
using System.Runtime.CompilerServices;

namespace HeroParser.SeparatedValues.Reading.Span;

/// <summary>
/// Represents a single CSV row backed by the original UTF-16 characters.
/// </summary>
/// <remarks>
/// <para>
/// Thread-Safety: This is a ref struct that wraps stack-allocated or pooled memory and cannot be
/// shared across threads. Each reader should be used on a single thread. Use <see cref="Clone"/>
/// or <see cref="ToImmutable"/> to create owned copies if you need to store rows beyond the enumeration scope.
/// </para>
/// <para>
/// Uses Ends-only storage: columnEnds[0] = -1, columnEnds[1..N] = column end positions.
/// Column start = columnEnds[index] + 1, length = columnEnds[index+1] - columnEnds[index] - 1.
/// </para>
/// </remarks>
public readonly ref struct CsvCharSpanRow
{
    private readonly ReadOnlySpan<char> line;
    private readonly int columnCount;
    private readonly ReadOnlySpan<int> columnEnds;
    private readonly int lineNumber;
    private readonly int sourceLineNumber;
    private readonly bool trimFields;

    internal CsvCharSpanRow(
        ReadOnlySpan<char> line,
        ReadOnlySpan<int> columnEndsBuffer,
        int columnCount,
        int lineNumber,
        int sourceLineNumber,
        bool trimFields = false)
    {
        this.line = line;
        this.columnCount = columnCount;
        this.lineNumber = lineNumber;
        this.sourceLineNumber = sourceLineNumber;
        this.trimFields = trimFields;
        // columnEnds has columnCount + 1 entries (including the -1 sentinel)
        columnEnds = columnEndsBuffer[..(columnCount + 1)];
    }

    /// <summary>Gets the number of parsed columns in the row.</summary>
    public int ColumnCount => columnCount;

    /// <summary>
    /// Gets the 1-based logical row number in the CSV data.
    /// </summary>
    /// <remarks>
    /// This represents the ordinal position of the row in the data (1st row, 2nd row, etc.).
    /// For multi-line quoted fields, this counts the entire field as one row.
    /// Use <see cref="SourceLineNumber"/> for the physical line number in the source file.
    /// </remarks>
    public int LineNumber => lineNumber;

    /// <summary>
    /// Gets the 1-based source line number where this row starts in the original CSV file.
    /// </summary>
    /// <remarks>
    /// This is the physical line number in the source file where the row begins.
    /// For rows with multi-line quoted fields, this points to the line where the row starts,
    /// not where it ends. This is useful for debugging, error reporting, and logging.
    /// </remarks>
    public int SourceLineNumber => sourceLineNumber;

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

            // compute start and length from ends
            var start = columnEnds[index] + 1;
            var end = columnEnds[index + 1];

            if (trimFields)
            {
                // Apply trimming at read time
                (start, end) = TrimBounds(start, end);
            }

            var length = end - start;
            return new CsvCharSpanColumn(line.Slice(start, length));
        }
    }

    /// <summary>Materializes the row into a string array by copying the underlying characters.</summary>
    public string[] ToStringArray()
    {
        var result = new string[columnCount];
        for (int i = 0; i < columnCount; i++)
        {
            // compute start and length from ends
            var start = columnEnds[i] + 1;
            var end = columnEnds[i + 1];

            if (trimFields)
            {
                (start, end) = TrimBounds(start, end);
            }

            var length = end - start;
            result[i] = new string(line.Slice(start, length));
        }
        return result;
    }

    /// <summary>
    /// Trims leading and trailing whitespace from the specified bounds.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private (int start, int end) TrimBounds(int start, int end)
    {
        // Skip quoted fields (they start and end with quotes)
        if (end - start >= 2 && line[start] == '"' && line[end - 1] == '"')
        {
            return (start, end);
        }

        // Trim leading whitespace
        while (start < end && (line[start] == ' ' || line[start] == '\t'))
        {
            start++;
        }

        // Trim trailing whitespace
        while (end > start && (line[end - 1] == ' ' || line[end - 1] == '\t'))
        {
            end--;
        }

        return (start, end);
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
        var newEnds = columnEnds.ToArray();
        return new CsvCharSpanRow(newLine, newEnds, columnCount, lineNumber, sourceLineNumber, trimFields);
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
        // For small column counts, direct iteration is faster than SIMD overhead
        if (columnCount <= 4)
        {
            for (int i = 0; i < columnCount; i++)
            {
                if (IsDangerousColumn(i))
                    return true;
            }
            return false;
        }

        // Fast path: SIMD pre-scan for any dangerous characters in the entire line
        // If no dangerous characters exist anywhere, we can return immediately
        if (!line.ContainsAny(allDangerousChars))
            return false;

        // Slow path: Check each column's first character
        for (int i = 0; i < columnCount; i++)
        {
            if (IsDangerousColumn(i))
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
        // compute start and length from ends
        var start = columnEnds[index] + 1;
        var end = columnEnds[index + 1];

        if (trimFields)
        {
            (start, end) = TrimBounds(start, end);
        }

        var length = end - start;

        if (length == 0) return false;

        char first = line[start];

        // Switch enables jump table optimization for O(1) character dispatch
        switch (first)
        {
            case '=':
            case '@':
            case '\t':
            case '\r':
                return true;

            case '-':
            case '+':
                if (length == 1) return false;
                char second = line[start + 1];
                // Safe if followed by digit or decimal point (numbers, phone numbers)
                return !((uint)(second - '0') <= 9 || second == '.');

            default:
                return false;
        }
    }
}
