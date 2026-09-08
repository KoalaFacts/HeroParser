using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Reading.Rows;

namespace HeroParser.SeparatedValues.Reading.Shared;

/// <summary>
/// One scanned row inside a batch: offsets are relative to the span passed to
/// <see cref="CsvRowBatchCursor.Fill{T}"/>.
/// </summary>
internal readonly record struct CsvBatchRow(int RowStart, int RowEnd, int ColumnCount, int EndsStart, int SourceLine)
{
    public int Length => RowEnd - RowStart;
}

/// <summary>
/// Shared scan-ahead cursor for every reading path: owns the pooled batch buffers, runs
/// <see cref="CsvRowBatchScanner"/> over a span, and hands rows out one at a time. Readers keep only
/// their own buffer management (a whole span, or a refillable stream window) and row emission.
/// </summary>
/// <remarks>
/// <para>
/// Protocol per window: call <see cref="Fill{T}"/> with the data and whether it is the final block;
/// then <see cref="TryTake"/> until it returns false; then inspect <see cref="ErrorRowStart"/> (a row the
/// per-row parser must re-parse for its exception) and <see cref="NextPosition"/> (where the window's
/// unconsumed data begins: the partial trailing row for a streaming block, or the end).
/// </para>
/// <para>
/// This is a class so that ref-struct readers copied by <c>foreach</c> share one instance and
/// <see cref="Dispose"/> is idempotent (see <see cref="PooledRowBatch"/>).
/// </para>
/// </remarks>
internal sealed class CsvRowBatchCursor : IDisposable
{
    private readonly PooledRowBatch batch;
    private readonly CsvReadOptions options;
    private readonly bool trackLineNumbers;
    private readonly bool quotes;
    private int rowCount;
    private int index;

    private CsvRowBatchCursor(CsvReadOptions options, int endsCapacity)
    {
        this.options = options;
        trackLineNumbers = options.TrackSourceLineNumbers;
        quotes = options.EnableQuotedFields;
        batch = new PooledRowBatch(endsCapacity, trackLineNumbers);
        ErrorRowStart = -1;
    }

    /// <summary>
    /// Creates a cursor when the options allow scan-ahead (see <see cref="CsvRowBatchScanner.IsSupported"/>),
    /// otherwise <see langword="null"/> so the caller stays on the per-row parser.
    /// </summary>
    public static CsvRowBatchCursor? TryCreate(CsvReadOptions options, int endsCapacity = CsvRowBatchScanner.DEFAULT_ENDS_CAPACITY)
    {
        if (!CsvRowBatchScanner.IsSupported(options))
            return null;

        int capacity = Math.Max(endsCapacity, CsvRowBatchScanner.MinEndsCapacity(options.MaxColumnCount));
        return new CsvRowBatchCursor(options, capacity);
    }

    /// <summary>Rows recorded by the last <see cref="Fill{T}"/> that have not been taken yet.</summary>
    public bool HasPending => index < rowCount;

    /// <summary>Start (relative to the filled span) of a row the per-row parser must re-parse, or -1.</summary>
    public int ErrorRowStart { get; private set; }

    /// <summary>
    /// Position (relative to the filled span) where unconsumed data begins after the last
    /// <see cref="Fill{T}"/>: the error row, the partial trailing row of a streaming block, or the span end.
    /// </summary>
    public int NextPosition { get; private set; }

    /// <summary>Source line at <see cref="NextPosition"/> (meaningful when line tracking is on).</summary>
    public int NextSourceLine { get; private set; }

    /// <summary>
    /// Scans <paramref name="data"/> from <paramref name="start"/> and records complete rows.
    /// Returns the number of rows available to take.
    /// </summary>
    public int Fill<T>(ReadOnlySpan<T> data, int start, int sourceLine, bool isFinalBlock)
        where T : unmanaged, IEquatable<T>
    {
        Span<int> ends = batch.Ends;
        Span<int> rowStarts = batch.RowStarts;
        Span<int> sourceLines = batch.SourceLines is { } lines ? lines : default;

        rowCount = !trackLineNumbers
            ? (quotes
                ? CsvRowBatchScanner.Scan<T, NoTrackLineNumbers, QuotesEnabled>(data, start, sourceLine, isFinalBlock, options, ends, rowStarts, sourceLines, out int nextPosition, out int nextSourceLine, out int errorRowStart)
                : CsvRowBatchScanner.Scan<T, NoTrackLineNumbers, QuotesDisabled>(data, start, sourceLine, isFinalBlock, options, ends, rowStarts, sourceLines, out nextPosition, out nextSourceLine, out errorRowStart))
            : (quotes
                ? CsvRowBatchScanner.Scan<T, TrackLineNumbers, QuotesEnabled>(data, start, sourceLine, isFinalBlock, options, ends, rowStarts, sourceLines, out nextPosition, out nextSourceLine, out errorRowStart)
                : CsvRowBatchScanner.Scan<T, TrackLineNumbers, QuotesDisabled>(data, start, sourceLine, isFinalBlock, options, ends, rowStarts, sourceLines, out nextPosition, out nextSourceLine, out errorRowStart));

        index = 0;
        NextPosition = nextPosition;
        NextSourceLine = nextSourceLine;
        ErrorRowStart = errorRowStart;
        return rowCount;
    }

    /// <summary>Takes the next recorded row; false when the batch is exhausted.</summary>
    public bool TryTake(out CsvBatchRow row)
    {
        if (index >= rowCount)
        {
            row = default;
            return false;
        }

        int r = index++;
        int[] ends = batch.Ends;
        int[] rowStarts = batch.RowStarts;
        int endsStart = rowStarts[r];
        int endsEnd = rowStarts[r + 1];
        int columnCount = endsEnd - endsStart - 1;
        int rowStart = ends[endsStart] + 1;
        int rowEnd = ends[endsEnd - 1];
        int sourceLine = trackLineNumbers ? batch.SourceLines![r] : 0;
        row = new CsvBatchRow(rowStart, rowEnd, columnCount, endsStart, sourceLine);
        return true;
    }

    /// <summary>Clears the error marker once the caller has handed the row to the per-row parser.</summary>
    public void ClearError() => ErrorRowStart = -1;

    /// <summary>
    /// Builds the <see cref="CsvRow{T}"/> for a taken row. <paramref name="window"/> must be the same span
    /// (or a span starting at the same element) that was passed to <see cref="Fill{T}"/>.
    /// </summary>
    public CsvRow<T> CreateRow<T>(ReadOnlySpan<T> window, CsvBatchRow row, int rowNumber, int sourceLineFallback)
        where T : unmanaged, IEquatable<T>
    {
        return new CsvRow<T>(
            window.Slice(row.RowStart, row.Length),
            batch.Ends.AsSpan(row.EndsStart, row.ColumnCount + 1),
            row.ColumnCount,
            rowNumber,
            trackLineNumbers ? row.SourceLine : sourceLineFallback,
            options.TrimFields,
            baseOffset: row.RowStart);
    }

    public void Dispose() => batch.Dispose();
}
