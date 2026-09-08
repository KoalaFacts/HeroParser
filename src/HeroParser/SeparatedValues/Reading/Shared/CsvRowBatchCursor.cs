using System.Buffers;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Reading.Rows;

namespace HeroParser.SeparatedValues.Reading.Shared;

/// <summary>
/// One scanned row inside a batch: offsets are relative to the window the batch was scanned from.
/// </summary>
internal readonly record struct CsvBatchRow(int RowStart, int RowEnd, int ColumnCount, int EndsStart, int SourceLine)
{
    public int Length => RowEnd - RowStart;
}

/// <summary>What a reader should do next after <see cref="CsvRowBatchCursor.Advance{T}"/>.</summary>
internal enum CsvBatchStep
{
    /// <summary>A scanned row is available in the <c>row</c> output.</summary>
    Row,
    /// <summary>Progress was made (a batch was scanned, or blank lines consumed); call again.</summary>
    Continue,
    /// <summary>The window holds only a partial row and more data may follow: refill, then call again.</summary>
    RefillNeeded,
    /// <summary>The row at the current offset must go through the per-row parser (error, or final row without a line ending).</summary>
    ParsePerRow,
    /// <summary>No data remains.</summary>
    EndOfInput
}

/// <summary>
/// Shared scan-ahead cursor for every reading path: owns the pooled batch buffers, runs
/// <see cref="CsvRowBatchScanner"/> over a window, hands rows out one at a time, and drives the
/// batch / refill / fallback protocol so readers keep only their buffer management and row emission.
/// </summary>
/// <remarks>
/// <para>
/// Readers call <see cref="Advance{T}"/> in a loop with their buffer, current offset, buffered length
/// and end-of-stream flag, and act on the returned <see cref="CsvBatchStep"/>. A whole-span reader
/// passes the span, its position, the span length and <c>endOfStream: true</c>; a streaming reader
/// passes its window and refills on <see cref="CsvBatchStep.RefillNeeded"/>. Rows are handed out before
/// the offset moves past them, and a reader must not compact or grow its buffer while rows are pending,
/// which the protocol guarantees by only returning <see cref="CsvBatchStep.RefillNeeded"/> when the
/// batch is drained.
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
    private int windowBase;         // buffer offset the current batch was scanned from
    private bool pendingPerRow;     // a flagged row follows the batch and must be parsed per-row once it drains

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

    /// <summary>
    /// Rows scanned into the current batch that have not been handed out yet. While this is true the
    /// reader must not compact or grow its buffer, because the batch's offsets point into it.
    /// </summary>
    public bool HasPending => index < rowCount;

    /// <summary>Start (relative to the filled window) of a row the per-row parser must re-parse, or -1.</summary>
    public int ErrorRowStart { get; private set; }

    /// <summary>
    /// Position (relative to the filled window) where unconsumed data begins after the last
    /// <see cref="Fill{T}"/>: the error row, the partial trailing row of a streaming block, or the window end.
    /// </summary>
    public int NextPosition { get; private set; }

    /// <summary>Source line at <see cref="NextPosition"/> (meaningful when line tracking is on).</summary>
    public int NextSourceLine { get; private set; }

    private int[] Ends => ends ?? throw new ObjectDisposedException(nameof(CsvRowBatchCursor));

    private int[] RowStarts => rowStarts ?? throw new ObjectDisposedException(nameof(CsvRowBatchCursor));

    /// <summary>
    /// Drives one step of the protocol over <paramref name="buffer"/>'s window <c>[offset, length)</c>.
    /// Moves <paramref name="offset"/> past consumed data and, with line tracking, keeps
    /// <paramref name="sourceLine"/> at the line of the new offset.
    /// </summary>
    public CsvBatchStep Advance<T>(ReadOnlySpan<T> buffer, ref int offset, int length, bool endOfStream, ref int sourceLine, out CsvBatchRow row)
        where T : unmanaged, IEquatable<T>
    {
        if (TryTake(out row))
            return CsvBatchStep.Row;

        if (pendingPerRow)
        {
            // The batch just drained up to a flagged row; the per-row parser reproduces its exception
            // (or, should it parse cleanly after all, yields it) and batching resumes after it.
            pendingPerRow = false;
            return CsvBatchStep.ParsePerRow;
        }

        var window = buffer[offset..length];
        if (window.IsEmpty)
            return endOfStream ? CsvBatchStep.EndOfInput : CsvBatchStep.RefillNeeded;

        int rows = Fill(window, 0, sourceLine, isFinalBlock: endOfStream);
        windowBase = offset;
        int consumed = NextPosition;

        if (rows > 0)
        {
            // The batch owns [offset, offset + consumed); NextPosition already stops at a flagged row.
            offset += consumed;
            if (trackLineNumbers)
                sourceLine = NextSourceLine;
            pendingPerRow = ErrorRowStart >= 0;
            ErrorRowStart = -1;
            return CsvBatchStep.Continue;
        }

        if (ErrorRowStart >= 0)
        {
            offset += ErrorRowStart;
            ErrorRowStart = -1;
            return CsvBatchStep.ParsePerRow;
        }

        if (consumed > 0)
        {
            // Only blank lines were consumed.
            offset += consumed;
            if (trackLineNumbers)
                sourceLine = NextSourceLine;
            return CsvBatchStep.Continue;
        }

        // A trailing row the scanner could not close: partial if more data may follow, otherwise the
        // per-row parser emits it (no line ending) or throws (unterminated quote), exactly as before.
        return endOfStream ? CsvBatchStep.ParsePerRow : CsvBatchStep.RefillNeeded;
    }

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

    /// <summary>
    /// Builds the <see cref="CsvRow{T}"/> for a row taken from the current batch. <paramref name="buffer"/>
    /// and <paramref name="length"/> must describe the same buffer that was passed to <see cref="Advance{T}"/>.
    /// </summary>
    public CsvRow<T> CreateRow<T>(ReadOnlySpan<T> buffer, int length, CsvBatchRow row, int rowNumber, int sourceLineFallback)
        where T : unmanaged, IEquatable<T>
    {
        var window = buffer[windowBase..length];
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
        ArrayPool<int>.Shared.Return(e);

        if (rowStarts is { } starts)
        {
            rowStarts = null;
            ArrayPool<int>.Shared.Return(starts);
        }

        if (sourceLines is { } lines)
        {
            sourceLines = null;
            ArrayPool<int>.Shared.Return(lines);
        }
    }
}
