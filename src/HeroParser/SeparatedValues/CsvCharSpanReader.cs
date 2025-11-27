using System.Runtime.CompilerServices;

#pragma warning disable IDE0060 // Remove unused parameter - Dispose kept for API compatibility

namespace HeroParser.SeparatedValues;

/// <summary>
/// Enumerates CSV rows from a UTF-16 span without allocating intermediate objects.
/// </summary>
/// <remarks>
/// Rows are parsed lazily as <see cref="MoveNext"/> advances.
/// This reader uses dedicated arrays (not pooled) to ensure thread-safety when multiple
/// readers are used concurrently or when <see cref="GetEnumerator"/> creates a copy.
/// </remarks>
public ref struct CsvCharSpanReader
{
    private readonly ReadOnlySpan<char> chars;
    private readonly CsvParserOptions options;
    private readonly int[] columnStartsBuffer;
    private readonly int[] columnLengthsBuffer;
    private int position;
    private int rowCount;

    internal CsvCharSpanReader(ReadOnlySpan<char> chars, CsvParserOptions options)
    {
        this.chars = chars;
        this.options = options;
        position = 0;
        rowCount = 0;
        Current = default;
        // Use dedicated arrays instead of ArrayPool to avoid sharing issues when
        // GetEnumerator() creates a copy that shares the same array references
        columnStartsBuffer = new int[options.MaxColumnCount];
        columnLengthsBuffer = new int[options.MaxColumnCount];
    }

    /// <summary>Gets the current UTF-16 backed row.</summary>
    /// <remarks>The value is only valid after <see cref="MoveNext"/> returns <see langword="true"/>.</remarks>
    public CsvCharSpanRow Current { get; private set; }

    /// <summary>Returns this instance so it can be consumed by <c>foreach</c>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly CsvCharSpanReader GetEnumerator() => this;

    /// <summary>
    /// Advances to the next row in the input span.
    /// </summary>
    /// <returns><see langword="true"/> when another row was parsed; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="CsvException">Thrown when the input violates <see cref="CsvParserOptions"/>.</exception>
    public bool MoveNext()
    {
        while (true)
        {
            if (position >= chars.Length)
                return false;

            var remaining = chars[position..];
            var result = CsvStreamingParser.ParseRow(
                remaining,
                options,
                columnStartsBuffer.AsSpan(0, options.MaxColumnCount),
                columnLengthsBuffer.AsSpan(0, options.MaxColumnCount));

            if (result.CharsConsumed == 0)
                return false;

            var rowChars = remaining[..result.RowLength];
            if (rowChars.IsEmpty)
            {
                position += result.CharsConsumed;
                continue;
            }

            rowCount++;
            Current = new CsvCharSpanRow(
                rowChars,
                columnStartsBuffer,
                columnLengthsBuffer,
                result.ColumnCount,
                rowCount);

            position += result.CharsConsumed;
            if (rowCount > options.MaxRowCount)
            {
                throw new CsvException(
                    CsvErrorCode.TooManyRows,
                    $"CSV exceeds maximum row limit of {options.MaxRowCount}");
            }
            return true;
        }
    }

    /// <summary>
    /// No-op for compatibility; dedicated arrays are used instead of pooled buffers.
    /// </summary>
    public readonly void Dispose()
    {
        // No-op: dedicated arrays don't need to be returned to a pool
    }
}
