using System.Buffers;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Reading.Rows;
using HeroParser.SeparatedValues.Reading.Shared;

namespace HeroParser.SeparatedValues.Reading.Streaming;

/// <summary>
/// Async CSV reader that streams from a <see cref="Stream"/> without loading the entire payload into memory.
/// </summary>
public sealed class CsvAsyncStreamReader : IAsyncDisposable
{
    // Absolute maximum buffer size (128 MB) to prevent unbounded memory growth.
    private const int ABSOLUTE_MAX_BUFFER_SIZE = 128 * 1024 * 1024;
    private const int MAX_LINE_ENDING_LENGTH = 2;

    private readonly ArrayPool<byte> bytePool;
    private readonly Stream stream;
    private readonly CsvReadOptions options;
    private readonly bool leaveOpen;
    private readonly bool trackLineNumbers;
    private readonly int maxRowSize;
    private readonly int maxBufferSize;
    private readonly PooledColumnEnds columnEndsBuffer;
    private readonly int skipRows;

    private byte[] buffer;
    private int offset;
    private int length;
    private int rowCount;
    private int skippedCount;
    private int sourceLineNumber;
    private bool endOfStream;
    private bool disposed;
    private bool bomProcessed;

    private int currentRowStart;
    private int currentRowLength;
    private int currentColumnCount;
    private int currentRowNumber;
    private int currentSourceLineNumber;

    // Shared scan-ahead cursor (null when the options keep this reader on the per-row parser).
    // It drives the batch / refill / fallback protocol over the buffered window; see CsvRowBatchCursor.
    private readonly CsvRowBatchCursor? cursor;
    private bool currentFromBatch;
    private CsvBatchRow currentBatchRow;

    /// <summary>The current row; valid until the next <see cref="MoveNextAsync"/> call.</summary>
    public CsvRow<byte> Current
    {
        get
        {
            ThrowIfDisposed();
            if (currentFromBatch)
            {
                return cursor!.CreateRow(buffer, currentBatchRow, currentRowNumber, currentSourceLineNumber);
            }

            return new CsvRow<byte>(
                buffer.AsSpan(currentRowStart, currentRowLength),
                columnEndsBuffer.Buffer,
                currentColumnCount,
                currentRowNumber,
                currentSourceLineNumber,
                options.TrimFields);
        }
    }

    /// <summary>Gets the approximate number of bytes read from the underlying stream.</summary>
    public long BytesRead { get; private set; }

    internal CsvAsyncStreamReader(Stream stream, CsvReadOptions options, bool leaveOpen, int initialBufferSize, int skipRows = 0)
    {
        this.stream = stream;
        this.options = options;
        this.leaveOpen = leaveOpen;
        trackLineNumbers = options.TrackSourceLineNumbers;
        maxRowSize = options.MaxRowSize ?? ABSOLUTE_MAX_BUFFER_SIZE;
        maxBufferSize = CalculateMaxBufferSize(maxRowSize);
        this.skipRows = skipRows;

        bytePool = ArrayPool<byte>.Shared;
        buffer = RentBuffer(Math.Max(initialBufferSize, 4096));
        columnEndsBuffer = new PooledColumnEnds(CsvRowBatchScanner.MinEndsCapacity(options.MaxColumnCount));
        cursor = CsvRowBatchCursor.TryCreate(options);

        offset = 0;
        length = 0;
        rowCount = 0;
        skippedCount = 0;
        sourceLineNumber = 1;
        endOfStream = false;
        disposed = false;
        bomProcessed = false;
        BytesRead = 0;

        currentRowStart = 0;
        currentRowLength = 0;
        currentColumnCount = 0;
        currentRowNumber = 0;
        currentSourceLineNumber = 1;
    }

    /// <summary>
    /// Advances to the next row, reading from the underlying stream asynchronously as needed.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> when another row was parsed; otherwise, <see langword="false"/>.</returns>
    public async ValueTask<bool> MoveNextAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        while (true)
        {
            // Per-row hot path: rows already scanned are handed out before the buffer is touched again
            // (a refill compacts the buffer, and pending batch rows point into the current window).
            if (cursor is not null && cursor.TryTake(out var pendingRow))
            {
                if (EmitBatchRow(pendingRow))
                    return true;
                continue;
            }

            if (!endOfStream && offset >= length)
            {
                await FillBufferAsync(cancellationToken).ConfigureAwait(false);
            }

            if (!bomProcessed)
            {
                if (!TryProcessBom())
                {
                    if (!endOfStream)
                    {
                        await FillBufferAsync(cancellationToken).ConfigureAwait(false);
                        continue;
                    }
                }
            }

            if (cursor is not null)
            {
                switch (cursor.Advance<byte>(buffer, ref offset, length, endOfStream, ref sourceLineNumber))
                {
                    case CsvBatchStep.Continue:
                        continue;

                    case CsvBatchStep.RefillNeeded:
                        await FillBufferAsync(cancellationToken).ConfigureAwait(false);
                        continue;

                    case CsvBatchStep.EndOfInput:
                        return false;

                    case CsvBatchStep.ParsePerRow:
                    default:
                        break; // fall through to the per-row parser at the current offset
                }
            }

            var span = buffer.AsSpan(offset, length - offset);

            currentFromBatch = false;

            int rowStartOffset = offset;
            int rowStartLine = trackLineNumbers ? sourceLineNumber : 0;

            CsvRowParseResult result;
            try
            {
                result = trackLineNumbers
                    ? CsvRowParser.ParseRow<byte, TrackLineNumbers>(span, options, columnEndsBuffer.Span)
                    : CsvRowParser.ParseRow<byte, NoTrackLineNumbers>(span, options, columnEndsBuffer.Span);
            }
            catch (CsvException ex) when (!endOfStream && (ex.QuoteStartPosition.HasValue || span.IndexOfAny((byte)'\n', (byte)'\r') < 0))
            {
                // An open quote, or a row with no line ending anywhere in the buffered data yet, is a
                // partial row: a limit it seems to break (field length, column count) must be judged on
                // the complete row, so read more first.
                await FillBufferAsync(cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (result.CharsConsumed == 0)
                return false;

            if (result.RowLength > maxRowSize)
            {
                throw new CsvException(
                    CsvErrorCode.ParseError,
                    $"Row exceeds maximum size of {maxRowSize:N0} bytes. Ensure rows have proper line endings.");
            }

            if (result.RowLength == span.Length && !endOfStream)
            {
                await FillBufferAsync(cancellationToken).ConfigureAwait(false);
                continue;
            }

            // The row closed on a CR that is the buffer's last byte: its LF may arrive with the next
            // read, and closing now would turn that LF into a phantom blank line. Refill first.
            if (!endOfStream && result.CharsConsumed == span.Length && result.CharsConsumed > result.RowLength && span[^1] == (byte)'\r')
            {
                await FillBufferAsync(cancellationToken).ConfigureAwait(false);
                continue;
            }

            offset = rowStartOffset + result.CharsConsumed;
            if (trackLineNumbers)
                sourceLineNumber += result.NewlineCount;

            if (result.RowLength == 0)
                continue;

            rowCount++;
            if (rowCount > options.MaxRowCount)
            {
                throw new CsvException(
                    CsvErrorCode.TooManyRows,
                    $"CSV exceeds maximum row limit of {options.MaxRowCount}");
            }

            if (skippedCount < skipRows)
            {
                skippedCount++;
                continue;
            }

            currentRowStart = rowStartOffset;
            currentRowLength = result.RowLength;
            currentColumnCount = result.ColumnCount;
            currentRowNumber = rowCount;
            currentSourceLineNumber = trackLineNumbers ? rowStartLine : rowCount;
            return true;
        }
    }

    /// <summary>
    /// Applies the per-row bookkeeping (size limit, row count, skip rows) to a scanned row and makes it
    /// current. Returns false when the row is skipped.
    /// </summary>
    private bool EmitBatchRow(CsvBatchRow row)
    {
        if (row.Length > maxRowSize)
        {
            throw new CsvException(
                CsvErrorCode.ParseError,
                $"Row exceeds maximum size of {maxRowSize:N0} bytes. Ensure rows have proper line endings.");
        }

        rowCount++;
        if (rowCount > options.MaxRowCount)
        {
            throw new CsvException(
                CsvErrorCode.TooManyRows,
                $"CSV exceeds maximum row limit of {options.MaxRowCount}");
        }

        if (skippedCount < skipRows)
        {
            skippedCount++;
            return false;
        }

        currentFromBatch = true;
        currentBatchRow = row;
        currentRowNumber = rowCount;
        currentSourceLineNumber = trackLineNumbers ? row.SourceLine : rowCount;
        return true;
    }

    private bool TryProcessBom()
    {
        if (bomProcessed)
            return true;

        int available = length - offset;
        if (available < 3 && !endOfStream)
            return false;

        if (available >= 2)
        {
            if (buffer[offset] == 0xFF && buffer[offset + 1] == 0xFE)
                throw new CsvException(CsvErrorCode.InvalidOptions, "UTF-16 LE encoding detected. HeroParser only supports UTF-8.");
            if (buffer[offset] == 0xFE && buffer[offset + 1] == 0xFF)
                throw new CsvException(CsvErrorCode.InvalidOptions, "UTF-16 BE encoding detected. HeroParser only supports UTF-8.");
        }

        if (available >= 3 && buffer[offset] == 0xEF && buffer[offset + 1] == 0xBB && buffer[offset + 2] == 0xBF)
        {
            offset += 3;
        }

        bomProcessed = true;
        return true;
    }

    private async ValueTask FillBufferAsync(CancellationToken cancellationToken)
    {
        if (offset > 0)
        {
            var remaining = buffer.AsSpan(offset, length - offset);
            remaining.CopyTo(buffer);
            length = remaining.Length;
            offset = 0;
        }

        if (length == buffer.Length)
        {
            if (buffer.Length >= maxBufferSize)
            {
                throw new CsvException(
                    CsvErrorCode.ParseError,
                    $"Row exceeds maximum size of {maxRowSize:N0} bytes. Ensure rows have proper line endings.");
            }

            int newSize = Math.Min(buffer.Length * 2, maxBufferSize);
            var newBuffer = RentBuffer(newSize);
            buffer.AsSpan(0, length).CopyTo(newBuffer);
            ReturnBuffer(buffer);
            buffer = newBuffer;
        }

        int read = await stream.ReadAsync(buffer.AsMemory(length, buffer.Length - length), cancellationToken).ConfigureAwait(false);
        if (read == 0)
        {
            endOfStream = true;
            return;
        }

        length += read;
        BytesRead += read;
    }

    private void ThrowIfDisposed()
    {
        if (disposed)
            throw new ObjectDisposedException(nameof(CsvAsyncStreamReader));
    }

    /// <summary>
    /// Asynchronously releases resources used by the reader.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        if (disposed)
            return ValueTask.CompletedTask;

        disposed = true;
        columnEndsBuffer.Return();
        cursor?.Dispose();
        ReturnBuffer(buffer);
        buffer = null!;

        if (!leaveOpen)
        {
            return stream.DisposeAsync();
        }

        return ValueTask.CompletedTask;
    }

    private byte[] RentBuffer(int minimumLength)
    {
        int bufferSize = minimumLength;
        if (bufferSize > maxBufferSize)
            bufferSize = maxBufferSize;
        return bytePool.Rent(bufferSize);
    }

    private void ReturnBuffer(byte[] toReturn)
    {
        bytePool.Return(toReturn, clearArray: false);
    }

    private static int CalculateMaxBufferSize(int maxRowSize)
    {
        if (maxRowSize >= int.MaxValue - MAX_LINE_ENDING_LENGTH)
            return int.MaxValue;
        return maxRowSize + MAX_LINE_ENDING_LENGTH;
    }
}

