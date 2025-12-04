using System.Buffers;
using System.Runtime.CompilerServices;

namespace HeroParser.SeparatedValues;

/// <summary>
/// Represents a single CSV row backed by <see cref="ReadOnlyMemory{T}"/> for zero-allocation access.
/// </summary>
/// <remarks>
/// <para>
/// Unlike <see cref="CsvCharSpanRow"/>, this struct can be stored in fields and collections
/// because it uses <see cref="ReadOnlyMemory{T}"/> instead of <see cref="ReadOnlySpan{T}"/>.
/// This enables zero-allocation binding to <see cref="ReadOnlyMemory{T}"/> properties.
/// </para>
/// <para>
/// <b>Thread Safety:</b> This struct is immutable after construction and is safe to share
/// across multiple threads. Each row owns its column index data independently with no shared state.
/// </para>
/// <para>
/// Uses Ends-only storage: columnEnds[0] = -1, columnEnds[1..N] = column end positions.
/// Column start = columnEnds[index] + 1, length = columnEnds[index+1] - columnEnds[index] - 1.
/// </para>
/// </remarks>
public readonly struct CsvMemoryRow
{
    private readonly ReadOnlyMemory<char> line;
    // Ends-only storage: [columnEnds[0], columnEnds[1], ..., columnEnds[columnCount]]
    // where columnEnds[0] = -1 (sentinel), columnEnds[1..N] = column end positions
    private readonly int[] columnEnds;
    private readonly int columnCount;
    private readonly int lineNumber;
    private readonly int sourceLineNumber;
    private readonly bool trimFields;

    internal CsvMemoryRow(
        ReadOnlyMemory<char> line,
        int[] columnEnds,
        int columnCount,
        int lineNumber,
        int sourceLineNumber,
        bool trimFields = false)
    {
        this.line = line;
        this.columnEnds = columnEnds;
        this.columnCount = columnCount;
        this.lineNumber = lineNumber;
        this.sourceLineNumber = sourceLineNumber;
        this.trimFields = trimFields;
    }

    /// <summary>Gets the number of parsed columns in the row.</summary>
    public int ColumnCount => columnCount;

    /// <summary>Gets the 1-based logical row number in the CSV data.</summary>
    public int LineNumber => lineNumber;

    /// <summary>Gets the 1-based source line number where this row starts.</summary>
    public int SourceLineNumber => sourceLineNumber;

    /// <summary>Gets a column by zero-based index.</summary>
    public CsvMemoryColumn this[int index]
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
                (start, end) = TrimBounds(start, end);
            }

            var length = end - start;
            return new CsvMemoryColumn(line.Slice(start, length));
        }
    }

    /// <summary>Gets a column's memory directly by zero-based index.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlyMemory<char> GetColumnMemory(int index)
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
            (start, end) = TrimBounds(start, end);
        }

        var length = end - start;
        return line.Slice(start, length);
    }

    /// <summary>Gets a column's span directly by zero-based index for parsing.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<char> GetColumnSpan(int index)
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
            (start, end) = TrimBounds(start, end);
        }

        var length = end - start;
        return line.Slice(start, length).Span;
    }

    /// <summary>Materializes the row into a string array.</summary>
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
            result[i] = new string(line.Slice(start, length).Span);
        }
        return result;
    }

    /// <summary>
    /// Trims leading and trailing whitespace from the specified bounds.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private (int start, int end) TrimBounds(int start, int end)
    {
        var span = line.Span;

        // Skip quoted fields (they start and end with quotes)
        if (end - start >= 2 && span[start] == '"' && span[end - 1] == '"')
        {
            return (start, end);
        }

        // Trim leading whitespace
        while (start < end && (span[start] == ' ' || span[start] == '\t'))
        {
            start++;
        }

        // Trim trailing whitespace
        while (end > start && (span[end - 1] == ' ' || span[end - 1] == '\t'))
        {
            end--;
        }

        return (start, end);
    }

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
        var span = line.Span;

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
        if (!span.ContainsAny(allDangerousChars))
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
        var span = line.Span;

        // compute start and length from ends
        var start = columnEnds[index] + 1;
        var end = columnEnds[index + 1];

        if (trimFields)
        {
            (start, end) = TrimBounds(start, end);
        }

        var length = end - start;

        if (length == 0) return false;

        char first = span[start];

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
                char second = span[start + 1];
                // Safe if followed by digit or decimal point (numbers, phone numbers)
                return !((uint)(second - '0') <= 9 || second == '.');

            default:
                return false;
        }
    }
}
