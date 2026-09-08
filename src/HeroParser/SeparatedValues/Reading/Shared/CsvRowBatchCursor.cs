using System.Buffers;
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
/// Buffer layout: <c>ends</c> holds absolute column-end offsets, <c>rowStarts</c> the index of each
/// row's sentinel in <c>ends</c> terminated by one extra entry, and <c>sourceLines</c> (line tracking
/// only) the source line each row starts on. See <see cref="CsvRowBatchScanner"/>.
/// </para>
/// <para>
/// This is a class, holding the arrays itself, for the same reason <see cref="PooledColumnEnds"/> is:
/// ref-struct readers are copied by <c>foreach</c> and dispose once per copy. One shared instance with
/// an idempotent <see cref="Dispose"/> keeps the arrays from being returned to the pool twice, which
/// would hand the same array to two owners.
/// </para>
/// </remarks>
internal sealed class CsvRowBatchCursor : IDisposable
{
    private readonly CsvReadOptions options;
    private readonly bool trackLineNumbers;
    private readonly bool quotes;
    private int[]? ends;
    private int[]? rowStarts;
    private int[]? sourceLines;
    private int rowCount;
    private int index;

    private CsvRowBatchCursor(CsvReadOptions options, int endsCapacity)
    {
        this.options = options;
        trackLineNumbers = options.TrackSourceLineNumbers;
        quotes = options.EnableQuotedFields;
        ends = ArrayPool<int>.Shared.Rent(endsCapacity);
        rowStarts = ArrayPool<int>.Shared.Rent(CsvRowBatchScanner.RowStartsCapacity(endsCapacity));
        sourceLines = trackLineNumbers ? ArrayPool<int>.Shared.Rent(rowStarts.Length) : null;
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

    /// <summary>Start (relative to the filled span) of a row the per-row parser must re-parse, or -1.</summary>
    public int ErrorRowStart { get; private set; }

    /// <summary>
    /// Position (relative to the filled span) where unconsumed data begins after the last
    /// <see cref="Fill{T}"/>: the error row, the partial trailing row of a streaming block, or the span end.
    /// </summary>
    public int NextPosition { get; private set; }

    /// <summary>Source line at <see cref="NextPosition"/> (meaningful when line tracking is on).</summary>
    public int NextSourceLine { get; private set; }

    private int[] Ends => ends ?? throw new ObjectDisposedException(nameof(CsvRowBatchCursor));

    private int[] RowStarts => rowStarts ?? throw new ObjectDisposedException(nameof(CsvRowBatchCursor));

    /// <summary>
    /// Scans <paramref name="data"/> from <paramref name="start"/> and records complete rows.
    /// Returns the number of rows available to take.
    /// </summary>
    public int Fill<T>(ReadOnlySpan<T> data, int start, int sourceLine, bool isFinalBlock)
        where T : unmanaged, IEquatable<T>
    {
        Span<int> endsSpan = Ends;
        Span<int> rowStartsSpan = RowStarts;
        Span<int> sourceLinesSpan = sourceLines is { } lines ? lines : default;

        rowCount = !trackLineNumbers
            ? (quotes
                ? CsvRowBatchScanner.Scan<T, NoTrackLineNumbers, QuotesEnabled>(data, start, sourceLine, isFinalBlock, options, endsSpan, rowStartsSpan, sourceLinesSpan, out int nextPosition, out int nextSourceLine, out int errorRowStart)
                : CsvRowBatchScanner.Scan<T, NoTrackLineNumbers, QuotesDisabled>(data, start, sourceLine, isFinalBlock, options, endsSpan, rowStartsSpan, sourceLinesSpan, out nextPosition, out nextSourceLine, out errorRowStart))
            : (quotes
                ? CsvRowBatchScanner.Scan<T, TrackLineNumbers, QuotesEnabled>(data, start, sourceLine, isFinalBlock, options, endsSpan, rowStartsSpan, sourceLinesSpan, out nextPosition, out nextSourceLine, out errorRowStart)
                : CsvRowBatchScanner.Scan<T, TrackLineNumbers, QuotesDisabled>(data, start, sourceLine, isFinalBlock, options, endsSpan, rowStartsSpan, sourceLinesSpan, out nextPosition, out nextSourceLine, out errorRowStart));

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
        int[] e = Ends;
        int[] starts = RowStarts;
        int endsStart = starts[r];
        int endsEnd = starts[r + 1];
        int columnCount = endsEnd - endsStart - 1;
        int rowStart = e[endsStart] + 1;
        int rowEnd = e[endsEnd - 1];
        int line = trackLineNumbers ? sourceLines![r] : 0;
        row = new CsvBatchRow(rowStart, rowEnd, columnCount, endsStart, line);
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
            Ends.AsSpan(row.EndsStart, row.ColumnCount + 1),
            row.ColumnCount,
            rowNumber,
            trackLineNumbers ? row.SourceLine : sourceLineFallback,
            options.TrimFields,
            baseOffset: row.RowStart);
    }

    /// <summary>Returns the pooled arrays; safe to call more than once.</summary>
    public void Dispose()
    {
        var e = ends;
        if (e is null)
            return;

        ends = null;
        ArrayPool<int>.Shared.Return(e, clearArray: false);

        var starts = rowStarts;
        rowStarts = null;
        if (starts is not null)
            ArrayPool<int>.Shared.Return(starts, clearArray: false);

        var lines = sourceLines;
        sourceLines = null;
        if (lines is not null)
            ArrayPool<int>.Shared.Return(lines, clearArray: false);
    }
}
