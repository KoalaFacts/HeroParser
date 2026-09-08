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

    // Scan-ahead batch state (null when the per-row path is in use).
    private readonly PooledRowBatch? batch;
    private int batchRowCount;
    private int batchIndex;
    private int batchErrorRowStart;

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

        batchErrorRowStart = -1;
        if (CsvRowBatchScanner.IsSupported(options))
        {
            int endsCapacity = Math.Max(batchEndsCapacity, CsvRowBatchScanner.MinEndsCapacity(options.MaxColumnCount));
            batch = new PooledRowBatch(endsCapacity, trackLineNumbers);
        }
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
        return batch is not null ? MoveNextBatched() : MoveNextPerRow();
    }

    private bool MoveNextBatched()
    {
        while (true)
        {
            if (batchIndex < batchRowCount)
            {
                EmitBatchRow();
                return true;
            }

            if (batchErrorRowStart >= 0)
            {
                // The scanner flagged this row; the per-row parser reproduces its exception. Should it
                // parse cleanly after all, the row is emitted and batching resumes after it.
                position = batchErrorRowStart;
                batchErrorRowStart = -1;
                return MoveNextPerRow();
            }

            if (position >= data.Length)
                return false;

            int before = position;
            FillBatch();

            if (batchRowCount == 0 && batchErrorRowStart < 0)
            {
                if (position >= data.Length)
                    return false; // only blank lines remained

                if (position == before)
                    return MoveNextPerRow(); // no progress possible in batch form; parse one row directly
            }
        }
    }

    private void FillBatch()
    {
        Span<int> ends = batch!.Ends;
        Span<int> rowStarts = batch.RowStarts;
        Span<int> sourceLines = batch.SourceLines is { } lines ? lines : default;

        batchRowCount = !trackLineNumbers
            ? (enableQuotedFields
                ? CsvRowBatchScanner.Scan<T, NoTrackLineNumbers, QuotesEnabled>(data, position, sourceLineNumber, options, ends, rowStarts, sourceLines, out int nextPosition, out int nextSourceLine, out int errorRowStart)
                : CsvRowBatchScanner.Scan<T, NoTrackLineNumbers, QuotesDisabled>(data, position, sourceLineNumber, options, ends, rowStarts, sourceLines, out nextPosition, out nextSourceLine, out errorRowStart))
            : (enableQuotedFields
                ? CsvRowBatchScanner.Scan<T, TrackLineNumbers, QuotesEnabled>(data, position, sourceLineNumber, options, ends, rowStarts, sourceLines, out nextPosition, out nextSourceLine, out errorRowStart)
                : CsvRowBatchScanner.Scan<T, TrackLineNumbers, QuotesDisabled>(data, position, sourceLineNumber, options, ends, rowStarts, sourceLines, out nextPosition, out nextSourceLine, out errorRowStart));

        batchIndex = 0;
        batchErrorRowStart = errorRowStart;
        position = nextPosition;
        if (trackLineNumbers)
            sourceLineNumber = nextSourceLine;
    }

    private void EmitBatchRow()
    {
        int r = batchIndex++;
        int[] ends = batch!.Ends;
        int[] rowStarts = batch.RowStarts;
        int endsStart = rowStarts[r];
        int endsEnd = rowStarts[r + 1];
        int columnCount = endsEnd - endsStart - 1;
        int rowStart = ends[endsStart] + 1;
        int rowEnd = ends[endsEnd - 1];

        rowCount++;
        Current = new CsvRow<T>(
            data[rowStart..rowEnd],
            ends.AsSpan(endsStart, columnCount + 1),
            columnCount,
            rowCount,
            trackLineNumbers ? batch.SourceLines![r] : rowCount,
            options.TrimFields,
            baseOffset: rowStart);

        if (rowCount > options.MaxRowCount)
        {
            throw new CsvException(
                CsvErrorCode.TooManyRows,
                $"CSV exceeds maximum row limit of {options.MaxRowCount}");
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
        batch?.Dispose();
    }
}
