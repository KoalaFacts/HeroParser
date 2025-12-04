using System.Runtime.CompilerServices;

#pragma warning disable IDE0060 // Remove unused parameter - Dispose kept for API compatibility

namespace HeroParser.SeparatedValues;

/// <summary>
/// Enumerates CSV rows directly from a UTF-8 span without allocating intermediate strings.
/// </summary>
/// <remarks>
/// <para>Rows are parsed lazily as <see cref="MoveNext"/> advances; call <see cref="Dispose"/> to return pooled buffers.</para>
/// <para>Uses Ends-only column indexing to minimize memory writes during parsing.</para>
/// </remarks>
public ref struct CsvByteSpanReader
{
    private readonly ReadOnlySpan<byte> utf8;
    private readonly CsvParserOptions options;
    private readonly int[] columnEndsBuffer;
    private readonly bool trackLineNumbers;
    private int position;
    private int rowCount;
    private int sourceLineNumber; // Track source line number (1-based), only when TrackSourceLineNumbers enabled

    internal CsvByteSpanReader(ReadOnlySpan<byte> utf8, CsvParserOptions options)
    {
        this.utf8 = utf8;
        this.options = options;
        trackLineNumbers = options.TrackSourceLineNumbers;
        position = 0;
        rowCount = 0;
        sourceLineNumber = 1; // Start at line 1
        Current = default;
        // Ends-only storage: need maxColumns + 1 entries
        // columnEnds[0] = -1 (sentinel), columnEnds[1..N] = column end positions
        columnEndsBuffer = new int[options.MaxColumnCount + 1];
    }

    /// <summary>Gets the current UTF-8 backed row.</summary>
    /// <remarks>The value is only valid after <see cref="MoveNext"/> returns <see langword="true"/>.</remarks>
    public CsvByteSpanRow Current { get; private set; }

    /// <summary>Returns this instance so it can be consumed by <c>foreach</c>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly CsvByteSpanReader GetEnumerator() => this;

    /// <summary>
    /// Advances to the next row in the input span.
    /// </summary>
    /// <returns><see langword="true"/> when another row was parsed; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="CsvException">Thrown when the input violates <see cref="CsvParserOptions"/>.</exception>
    public bool MoveNext()
    {
        while (true)
        {
            if (position >= utf8.Length)
                return false;

            var remaining = utf8[position..];
            int rowStartLine = trackLineNumbers ? sourceLineNumber : 0; // Only capture when tracking enabled
            var result = CsvStreamingParser.ParseRow(
                remaining,
                options,
                columnEndsBuffer.AsSpan(0, options.MaxColumnCount + 1),
                trackLineNumbers);

            if (result.CharsConsumed == 0)
                return false;

            // Update source line number based on newlines encountered (only when tracking enabled)
            if (trackLineNumbers)
                sourceLineNumber += result.NewlineCount;

            var rowBytes = remaining[..result.RowLength];
            if (rowBytes.IsEmpty)
            {
                position += result.CharsConsumed;
                continue;
            }

            rowCount++;
            Current = new CsvByteSpanRow(
                rowBytes,
                columnEndsBuffer,
                result.ColumnCount,
                rowCount,
                trackLineNumbers ? rowStartLine : rowCount, // Use rowCount as fallback when tracking disabled
                options.TrimFields);

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
