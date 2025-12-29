using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace HeroParser.SeparatedValues.Reading.Rows;

/// <summary>
/// Represents a single CSV row backed by the original span data.
/// </summary>
/// <typeparam name="T">The element type: <see cref="char"/> for UTF-16 or <see cref="byte"/> for UTF-8.</typeparam>
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
public readonly ref struct CsvRow<T> where T : unmanaged, IEquatable<T>
{
    private readonly ReadOnlySpan<T> line;
    private readonly int columnCount;
    private readonly ReadOnlySpan<int> columnEnds;
    private readonly int lineNumber;
    private readonly int sourceLineNumber;
    private readonly bool trimFields;

    internal CsvRow(
        ReadOnlySpan<T> line,
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
    /// <returns>A <see cref="CsvColumn{T}"/> pointing at the requested column.</returns>
    /// <exception cref="IndexOutOfRangeException">Thrown when <paramref name="index"/> falls outside <see cref="ColumnCount"/>.</exception>
    public CsvColumn<T> this[int index]
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
            return new CsvColumn<T>(line.Slice(start, length));
        }
    }

    /// <summary>
    /// Fast path for single-char discriminator lookup. Gets the first character and length of a column
    /// without creating a CsvColumn struct. Returns false if index is out of bounds.
    /// </summary>
    /// <remarks>
    /// Internal method optimized for multi-schema parsing where we need to quickly check
    /// single-character discriminators (like H/D/T in banking formats).
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryGetColumnFirstChar(int index, out int firstChar, out int length)
    {
        if ((uint)index >= (uint)columnCount)
        {
            firstChar = 0;
            length = 0;
            return false;
        }

        var start = columnEnds[index] + 1;
        var end = columnEnds[index + 1];

        if (trimFields)
        {
            (start, end) = TrimBounds(start, end);
        }

        length = end - start;
        if (length == 0)
        {
            firstChar = 0;
            return true;
        }

        // Get first character directly - JIT eliminates dead branch
        firstChar = typeof(T) == typeof(char)
            ? Unsafe.As<T, char>(ref Unsafe.AsRef(in line[start]))
            : Unsafe.As<T, byte>(ref Unsafe.AsRef(in line[start]));
        return true;
    }

    /// <summary>Materializes the row into a string array by copying the underlying data.</summary>
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
            var slice = line.Slice(start, length);

            if (typeof(T) == typeof(char))
            {
                var chars = MemoryMarshal.CreateReadOnlySpan(
                    ref Unsafe.As<T, char>(ref MemoryMarshal.GetReference(slice)), length);
                result[i] = new string(chars);
            }
            else if (typeof(T) == typeof(byte))
            {
                var bytes = MemoryMarshal.CreateReadOnlySpan(
                    ref Unsafe.As<T, byte>(ref MemoryMarshal.GetReference(slice)), length);
                result[i] = Encoding.UTF8.GetString(bytes);
            }
        }
        return result;
    }

    /// <summary>
    /// Trims leading and trailing whitespace from the specified bounds.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private (int start, int end) TrimBounds(int start, int end)
    {
        if (typeof(T) == typeof(char))
        {
            return TrimBoundsChar(start, end);
        }
        if (typeof(T) == typeof(byte))
        {
            return TrimBoundsByte(start, end);
        }
        return (start, end);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private (int start, int end) TrimBoundsChar(int start, int end)
    {
        var charLine = MemoryMarshal.CreateReadOnlySpan(
            ref Unsafe.As<T, char>(ref MemoryMarshal.GetReference(line)), line.Length);

        const char quote = '"';
        const char space = ' ';
        const char tab = '\t';

        // Skip quoted fields (they start and end with quotes)
        if (end - start >= 2 && charLine[start] == quote && charLine[end - 1] == quote)
        {
            return (start, end);
        }

        // Trim leading whitespace
        while (start < end && (charLine[start] == space || charLine[start] == tab))
        {
            start++;
        }

        // Trim trailing whitespace
        while (end > start && (charLine[end - 1] == space || charLine[end - 1] == tab))
        {
            end--;
        }

        return (start, end);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private (int start, int end) TrimBoundsByte(int start, int end)
    {
        var byteLine = MemoryMarshal.CreateReadOnlySpan(
            ref Unsafe.As<T, byte>(ref MemoryMarshal.GetReference(line)), line.Length);

        const byte quote = (byte)'"';
        const byte space = (byte)' ';
        const byte tab = (byte)'\t';

        // Skip quoted fields (they start and end with quotes)
        if (end - start >= 2 && byteLine[start] == quote && byteLine[end - 1] == quote)
        {
            return (start, end);
        }

        // Trim leading whitespace
        while (start < end && (byteLine[start] == space || byteLine[start] == tab))
        {
            start++;
        }

        // Trim trailing whitespace
        while (end > start && (byteLine[end - 1] == space || byteLine[end - 1] == tab))
        {
            end--;
        }

        return (start, end);
    }

    /// <summary>
    /// Creates an owned copy of the row data, solving the buffer ownership issue.
    /// </summary>
    /// <returns>A new <see cref="CsvRow{T}"/> with its own copy of the data.</returns>
    /// <remarks>
    /// This method allocates new memory and copies the row data, allowing the returned row
    /// to be used after the original buffer has been modified or disposed.
    /// </remarks>
    public CsvRow<T> Clone()
    {
        var newLine = line.ToArray();
        var newEnds = columnEnds.ToArray();
        return new CsvRow<T>(newLine, newEnds, columnCount, lineNumber, sourceLineNumber, trimFields);
    }

    /// <summary>
    /// Creates an immutable copy of the row data, solving the buffer ownership issue.
    /// </summary>
    /// <returns>A new <see cref="CsvRow{T}"/> with its own copy of the data.</returns>
    /// <remarks>
    /// This is an alias for <see cref="Clone"/> that creates an owned copy of the row data.
    /// </remarks>
    public CsvRow<T> ToImmutable() => Clone();

    // All potentially dangerous characters for SIMD pre-scan
    private static readonly SearchValues<char> allDangerousChars = SearchValues.Create("=@\t\r-+");
    private static readonly SearchValues<byte> allDangerousBytes = SearchValues.Create("=@\t\r-+"u8);

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
        bool hasDangerous;
        if (typeof(T) == typeof(char))
        {
            var charLine = MemoryMarshal.CreateReadOnlySpan(
                ref Unsafe.As<T, char>(ref MemoryMarshal.GetReference(line)), line.Length);
            hasDangerous = charLine.ContainsAny(allDangerousChars);
        }
        else if (typeof(T) == typeof(byte))
        {
            var byteLine = MemoryMarshal.CreateReadOnlySpan(
                ref Unsafe.As<T, byte>(ref MemoryMarshal.GetReference(line)), line.Length);
            hasDangerous = byteLine.ContainsAny(allDangerousBytes);
        }
        else
        {
            hasDangerous = true; // Unknown type, check each column
        }

        if (!hasDangerous)
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

        if (typeof(T) == typeof(char))
        {
            return IsDangerousColumnChar(start, length);
        }
        if (typeof(T) == typeof(byte))
        {
            return IsDangerousColumnByte(start, length);
        }
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsDangerousColumnChar(int start, int length)
    {
        var charLine = MemoryMarshal.CreateReadOnlySpan(
            ref Unsafe.As<T, char>(ref MemoryMarshal.GetReference(line)), line.Length);

        char first = charLine[start];

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
                char second = charLine[start + 1];
                // Safe if followed by digit or decimal point (numbers, phone numbers)
                return !((uint)(second - '0') <= 9 || second == '.');

            default:
                return false;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsDangerousColumnByte(int start, int length)
    {
        var byteLine = MemoryMarshal.CreateReadOnlySpan(
            ref Unsafe.As<T, byte>(ref MemoryMarshal.GetReference(line)), line.Length);

        byte first = byteLine[start];

        // Switch enables jump table optimization for O(1) character dispatch
        switch (first)
        {
            case (byte)'=':
            case (byte)'@':
            case (byte)'\t':
            case (byte)'\r':
                return true;

            case (byte)'-':
            case (byte)'+':
                if (length == 1) return false;
                byte second = byteLine[start + 1];
                // Safe if followed by digit or decimal point (numbers, phone numbers)
                return !((uint)(second - '0') <= 9 || second == '.');

            default:
                return false;
        }
    }
}
