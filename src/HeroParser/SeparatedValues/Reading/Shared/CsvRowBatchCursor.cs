using System.Buffers;
using System.Runtime.CompilerServices;
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
    /// <summary>Progress was made (a batch was scanned, or blank lines consumed); take rows, then call again.</summary>
    Continue,
    /// <summary>The window holds only a partial row and more data may follow: refill, then call again.</summary>
    RefillNeeded,
    /// <summary>The row at the current offset must go through the per-row parser (a limit violation, or the final row without a line ending).</summary>
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
/// Protocol: a reader first drains pending rows with <see cref="TryTake"/> (the per-row hot path), and
/// only when none are pending calls <see cref="Advance{T}"/> with its buffer, current offset, buffered
/// length and end-of-stream flag, then acts on the returned <see cref="CsvBatchStep"/>. A whole-span
/// reader passes the span, its position, the span length and <c>endOfStream: true</c>; a streaming
/// reader passes its window and refills on <see cref="CsvBatchStep.RefillNeeded"/>. Because rows are
/// drained before <see cref="Advance{T}"/> is entered, a reader never compacts or grows its buffer while
/// batch offsets still point into it.
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
    /// Takes the next recorded row; false when the batch is exhausted. This is the per-row hot path, so it
    /// is kept small enough to inline; the one null check reports use after <see cref="Dispose"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryTake(out CsvBatchRow row)
    {
        if (index >= rowCount)
        {
            row = default;
            return false;
        }

        int[]? e = ends;
        if (e is null)
            ThrowDisposed();

        int r = index++;
        int[] starts = rowStarts!;
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
    /// Drives one step of the protocol over <paramref name="buffer"/>'s window <c>[offset, length)</c>.
    /// Call only when <see cref="TryTake"/> has returned false. Moves <paramref name="offset"/> past
    /// consumed data and, with line tracking, keeps <paramref name="sourceLine"/> at the line of the new
    /// offset.
    /// </summary>
    public CsvBatchStep Advance<T>(ReadOnlySpan<T> buffer, ref int offset, int length, bool endOfStream, ref int sourceLine)
        where T : unmanaged, IEquatable<T>
    {
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

        int rows = Fill(window, sourceLine, isFinalBlock: endOfStream, out int consumed, out int nextSourceLine, out bool rowFlagged);
        windowBase = offset;

        // Consume once, whatever was scanned: rows, blank lines, or the prefix before a flagged row.
        // The scanner already stops the consumed prefix at a flagged row's start.
        offset += consumed;
        if (trackLineNumbers)
            sourceLine = nextSourceLine;

        if (rows > 0)
        {
            pendingPerRow = rowFlagged;
            return CsvBatchStep.Continue;
        }

        if (rowFlagged)
            return CsvBatchStep.ParsePerRow;

        if (consumed > 0)
            return CsvBatchStep.Continue; // only blank lines were consumed

        // A trailing row the scanner could not close: partial if more data may follow, otherwise the
        // per-row parser emits it (no line ending) or throws (unterminated quote), exactly as before.
        return endOfStream ? CsvBatchStep.ParsePerRow : CsvBatchStep.RefillNeeded;
    }

    private int Fill<T>(ReadOnlySpan<T> window, int sourceLine, bool isFinalBlock, out int consumed, out int nextSourceLine, out bool rowFlagged)
        where T : unmanaged, IEquatable<T>
    {
        Span<int> endsSpan = ends ?? throw new ObjectDisposedException(nameof(CsvRowBatchCursor));
        Span<int> rowStartsSpan = rowStarts;
        Span<int> sourceLinesSpan = sourceLines is { } lines ? lines : default;

        rowCount = !trackLineNumbers
            ? (quotes
                ? CsvRowBatchScanner.Scan<T, NoTrackLineNumbers, QuotesEnabled>(window, 0, sourceLine, isFinalBlock, singleRow: false, options, endsSpan, rowStartsSpan, sourceLinesSpan, out consumed, out nextSourceLine, out int errorRowStart)
                : CsvRowBatchScanner.Scan<T, NoTrackLineNumbers, QuotesDisabled>(window, 0, sourceLine, isFinalBlock, singleRow: false, options, endsSpan, rowStartsSpan, sourceLinesSpan, out consumed, out nextSourceLine, out errorRowStart))
            : (quotes
                ? CsvRowBatchScanner.Scan<T, TrackLineNumbers, QuotesEnabled>(window, 0, sourceLine, isFinalBlock, singleRow: false, options, endsSpan, rowStartsSpan, sourceLinesSpan, out consumed, out nextSourceLine, out errorRowStart)
                : CsvRowBatchScanner.Scan<T, TrackLineNumbers, QuotesDisabled>(window, 0, sourceLine, isFinalBlock, singleRow: false, options, endsSpan, rowStartsSpan, sourceLinesSpan, out consumed, out nextSourceLine, out errorRowStart));

        index = 0;
        rowFlagged = errorRowStart >= 0;
        return rowCount;
    }

    /// <summary>
    /// Builds the <see cref="CsvRow{T}"/> for a row taken from the current batch. <paramref name="buffer"/>
    /// must be the buffer that was passed to <see cref="Advance{T}"/>.
    /// </summary>
    public CsvRow<T> CreateRow<T>(ReadOnlySpan<T> buffer, CsvBatchRow row, int rowNumber, int sourceLineFallback)
        where T : unmanaged, IEquatable<T>
    {
        return new CsvRow<T>(
            buffer.Slice(windowBase + row.RowStart, row.Length),
            ends.AsSpan(row.EndsStart, row.ColumnCount + 1),
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

    [MethodImpl(MethodImplOptions.NoInlining)]
    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void ThrowDisposed() => throw new ObjectDisposedException(nameof(CsvRowBatchCursor));
}
