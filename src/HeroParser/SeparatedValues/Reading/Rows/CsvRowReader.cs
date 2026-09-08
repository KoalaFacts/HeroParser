using System.Runtime.CompilerServices;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Reading.Shared;

namespace HeroParser.SeparatedValues.Reading.Rows;

/// <summary>
/// Enumerates CSV rows from a span without allocating intermediate objects.
/// </summary>
/// <typeparam name="T">The element type: <see cref="char"/> for UTF-16 or <see cref="byte"/> for UTF-8.</typeparam>
/// <remarks>
/// <para>Rows are parsed lazily as <see cref="MoveNext"/> advances.</para>
/// <para>
/// On SIMD-capable hardware the reader scans ahead: one pass records the column ends of a batch of
/// rows into a pooled buffer and <see cref="MoveNext"/> then only advances an index (see
/// <see cref="CsvRowBatchScanner"/>). Configurations the scanner does not cover (comment or escape
/// character, SIMD disabled) parse one row per call.
/// </para>
/// <para>
/// This reader uses pooled buffers per instance to reduce allocations. They are returned to the
/// pool when <see cref="Dispose"/> is called (including by <c>foreach</c>).
/// </para>
/// <para>Uses Ends-only column indexing to minimize memory writes during parsing.</para>
/// </remarks>
public ref struct CsvRowReader<T> where T : unmanaged, IEquatable<T>
{
    private readonly ReadOnlySpan<T> data;
    private readonly CsvReadOptions options;
    private readonly PooledColumnEnds columnEndsBuffer;
    private readonly int[] columnEnds;
    private readonly bool trackLineNumbers;
    private readonly bool enableQuotedFields;
    private int position;
    private int rowCount;
    private int sourceLineNumber; // Track source line number (1-based), only when TrackSourceLineNumbers enabled

    // Shared scan-ahead cursor (null when the per-row path is in use).
    private readonly CsvRowBatchCursor? cursor;

    internal CsvRowReader(ReadOnlySpan<T> data, CsvReadOptions options)
        : this(data, options, CsvRowBatchScanner.DEFAULT_ENDS_CAPACITY)
    {
    }

    /// <summary>
    /// Creates a reader with an explicit scan-ahead buffer size. Tests use small capacities to force
    /// batch boundaries; the capacity is raised to the minimum a single row needs.
    /// </summary>
    internal CsvRowReader(ReadOnlySpan<T> data, CsvReadOptions options, int batchEndsCapacity)
    {
        this.data = data;
        this.options = options;
        trackLineNumbers = options.TrackSourceLineNumbers;
        enableQuotedFields = options.EnableQuotedFields;
        position = 0;
        rowCount = 0;
        sourceLineNumber = 1; // Start at line 1
        Current = default;
        // Ends-only storage: need maxColumns + 1 entries
        columnEndsBuffer = new PooledColumnEnds(options.MaxColumnCount + 1);
        columnEnds = columnEndsBuffer.Buffer;
        cursor = CsvRowBatchCursor.TryCreate(options, batchEndsCapacity);
    }

    /// <summary>Gets the current row.</summary>
    /// <remarks>The value is only valid after <see cref="MoveNext"/> returns <see langword="true"/>.</remarks>
    public CsvRow<T> Current { get; private set; }

    /// <summary>Returns this instance so it can be consumed by <c>foreach</c>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly CsvRowReader<T> GetEnumerator() => this;

    /// <summary>
    /// Advances to the next row in the input span.
    /// </summary>
    /// <returns><see langword="true"/> when another row was parsed; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="CsvException">Thrown when the input violates <see cref="CsvReadOptions"/>.</exception>
    public bool MoveNext()
    {
        return cursor is not null ? MoveNextBatched(cursor) : MoveNextPerRow();
    }

    private bool MoveNextBatched(CsvRowBatchCursor batchCursor)
    {
        while (true)
        {
            // Per-row hot path: hand out an already scanned row without entering the protocol.
            if (batchCursor.TryTake(out var row))
            {
                rowCount++;
                Current = batchCursor.CreateRow(data, row, rowCount, rowCount);
                if (rowCount > options.MaxRowCount)
                {
                    throw new CsvException(
                        CsvErrorCode.TooManyRows,
                        $"CSV exceeds maximum row limit of {options.MaxRowCount}");
                }
                return true;
            }

            switch (batchCursor.Advance(data, ref position, data.Length, endOfStream: true, ref sourceLineNumber))
            {
                case CsvBatchStep.ParsePerRow:
                    // A flagged row (the per-row parser reproduces its exception) or the final row
                    // without a line ending; either way one per-row step, then batching resumes.
                    return MoveNextPerRow();

                case CsvBatchStep.Continue:
                    continue;

                case CsvBatchStep.EndOfInput:
                case CsvBatchStep.RefillNeeded: // cannot occur for a final block
                default:
                    return false;
            }
        }
    }

    private bool MoveNextPerRow()
    {
        while (true)
        {
            if (position >= data.Length)
                return false;

            var remaining = data[position..];
            int rowStartLine = trackLineNumbers ? sourceLineNumber : 0; // Only capture when tracking enabled
            var columnEndsSpan = columnEnds.AsSpan(0, options.MaxColumnCount + 1);

            CsvRowParseResult result = !trackLineNumbers
                ? (enableQuotedFields
                    ? CsvRowParser.ParseRow<T, NoTrackLineNumbers, QuotesEnabled>(remaining, options, columnEndsSpan)
                    : CsvRowParser.ParseRow<T, NoTrackLineNumbers, QuotesDisabled>(remaining, options, columnEndsSpan))
                : (enableQuotedFields
                    ? CsvRowParser.ParseRow<T, TrackLineNumbers, QuotesEnabled>(remaining, options, columnEndsSpan)
                    : CsvRowParser.ParseRow<T, TrackLineNumbers, QuotesDisabled>(remaining, options, columnEndsSpan));

            if (result.CharsConsumed == 0)
                return false;

            // Update source line number based on newlines encountered (only when tracking enabled)
            if (trackLineNumbers)
                sourceLineNumber += result.NewlineCount;

            var rowData = remaining[..result.RowLength];
            if (rowData.IsEmpty)
            {
                position += result.CharsConsumed;
                continue;
            }

            rowCount++;
            Current = new CsvRow<T>(
                rowData,
                columnEnds,
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
    /// Returns pooled buffers for column tracking.
    /// </summary>
    public readonly void Dispose()
    {
        columnEndsBuffer.Dispose();
        cursor?.Dispose();
    }
}
