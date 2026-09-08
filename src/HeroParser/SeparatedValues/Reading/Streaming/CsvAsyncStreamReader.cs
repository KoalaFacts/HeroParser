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

    // Scan-ahead: the cursor scans the buffered window [offset, length) in batches; null when the
    // options keep this reader on the per-row parser. batchBase is the buffer offset the current batch
    // was scanned from (row offsets in the batch are relative to it). The buffer is only compacted or
    // grown once a batch is fully consumed, so batch offsets stay valid while rows are handed out.
    private readonly CsvRowBatchCursor? cursor;
    private int batchBase;
    private bool currentFromBatch;
    private CsvBatchRow currentBatchRow;
    private bool parseErrorRowPerRow;

    /// <summary>The current row; valid until the next <see cref="MoveNextAsync"/> call.</summary>
    public CsvRow<byte> Current
    {
        get
        {
            ThrowIfDisposed();
            if (currentFromBatch)
            {
                return cursor!.CreateRow(buffer.AsSpan(batchBase, length - batchBase), currentBatchRow, currentRowNumber, currentSourceLineNumber);
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
        columnEndsBuffer = new PooledColumnEnds(options.MaxColumnCount + 1);
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
            // Rows already scanned into the batch are handed out before the buffer is touched again.
            if (cursor is not null && cursor.TryTake(out var batchRow))
            {
                if (EmitBatchRow(batchRow))
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

            var span = buffer.AsSpan(offset, length - offset);
            if (span.IsEmpty && endOfStream)
            {
                return false;
            }

            if (cursor is not null && !parseErrorRowPerRow)
            {
                int rows = cursor.Fill(span, 0, sourceLineNumber, isFinalBlock: endOfStream);
                batchBase = offset;
                int consumed = cursor.NextPosition;

                if (rows > 0)
                {
                    // The batch owns [offset, offset + consumed); a flagged row, if any, is re-parsed
                    // by the per-row path once the batch is drained.
                    offset += consumed;
                    if (trackLineNumbers)
                        sourceLineNumber = cursor.NextSourceLine;
                    parseErrorRowPerRow = cursor.ErrorRowStart >= 0;
                    cursor.ClearError();
                    continue;
                }

                if (cursor.ErrorRowStart >= 0)
                {
                    // Nothing before the flagged row: parse it now for its exception (or its row).
                    offset += cursor.ErrorRowStart;
                    cursor.ClearError();
                    parseErrorRowPerRow = true;
                    continue;
                }

                if (consumed > 0)
                {
                    // Only blank lines were consumed.
                    offset += consumed;
                    if (trackLineNumbers)
                        sourceLineNumber = cursor.NextSourceLine;
                    continue;
                }

                if (!endOfStream)
                {
                    // A partial row: more data is needed before it can be closed.
                    await FillBufferAsync(cancellationToken).ConfigureAwait(false);
                    continue;
                }

                // End of stream with a trailing row the scanner could not close: the per-row parser
                // either emits it (no line ending) or throws (unterminated quote), exactly as before.
                parseErrorRowPerRow = true;
                continue;
            }

            parseErrorRowPerRow = false;
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
            catch (CsvException ex) when (!endOfStream && ex.QuoteStartPosition.HasValue)
            {
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

