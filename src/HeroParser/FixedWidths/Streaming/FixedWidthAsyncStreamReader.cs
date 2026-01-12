using System.Buffers;
using System.Text;
using HeroParser.FixedWidths;

namespace HeroParser.FixedWidths.Streaming;

/// <summary>
/// Async fixed-width reader that streams from a file or stream without loading the entire payload into memory.
/// </summary>
public sealed class FixedWidthAsyncStreamReader : IAsyncDisposable
{
    // Absolute maximum buffer size (128 MB) to prevent unbounded memory growth
    private const int ABSOLUTE_MAX_BUFFER_SIZE = 128 * 1024 * 1024;

    private readonly ArrayPool<char> charPool;
    private readonly StreamReader reader;
    private readonly FixedWidthReadOptions options;
    private readonly bool trackLineNumbers;
    private char[] buffer;
    private int offset;
    private int length;
    private int recordCount;
    private int sourceLineNumber;
    private bool endOfStream;
    private bool disposed;
    private int currentRowStart;
    private int currentRowLength;
    private int currentLineNumber;
    private int currentSourceLineNumber;
#pragma warning disable IDE0032 // Use auto property - can't use auto property here as bytesRead is modified in FillBufferAsync
    private long bytesRead;
#pragma warning restore IDE0032

    /// <summary>The current row; valid until the next <see cref="MoveNextAsync"/> call.</summary>
    public FixedWidthCharSpanRow Current => new(
        buffer.AsSpan(currentRowStart, currentRowLength),
        currentLineNumber,
        currentSourceLineNumber,
        options);

    /// <summary>Gets the approximate number of bytes read from the underlying stream.</summary>
    /// <remarks>
    /// This value is estimated based on characters read assuming UTF-8 encoding (1 byte per ASCII character).
    /// For non-ASCII content or other encodings, this may not be precisely accurate.
    /// </remarks>
    public long BytesRead => bytesRead;

    internal FixedWidthAsyncStreamReader(Stream stream, FixedWidthReadOptions options, Encoding encoding, bool leaveOpen, int initialBufferSize)
    {
        this.options = options;
        trackLineNumbers = options.TrackSourceLineNumbers;
        // Use shared pool for better memory efficiency - arrays are always cleared on return
        charPool = ArrayPool<char>.Shared;
        reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true, bufferSize: initialBufferSize, leaveOpen: leaveOpen);
        buffer = RentBuffer(Math.Max(initialBufferSize, 4096));
        offset = 0;
        length = 0;
        recordCount = 0;
        sourceLineNumber = 1;
        endOfStream = false;
        disposed = false;
        currentRowStart = 0;
        currentRowLength = 0;
        currentLineNumber = 0;
        currentSourceLineNumber = 1;
    }

    /// <summary>
    /// Advances to the next record, reading from the underlying stream asynchronously as needed.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> when another record was parsed; otherwise, <see langword="false"/>.</returns>
    public async ValueTask<bool> MoveNextAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Handle initial row skipping
        if (recordCount == 0 && options.SkipRows > 0)
        {
            await SkipInitialRowsAsync(options.SkipRows, cancellationToken).ConfigureAwait(false);
        }

        if (options.RecordLength is { } fixedLength)
        {
            return await MoveNextFixedLengthAsync(fixedLength, cancellationToken).ConfigureAwait(false);
        }

        return await MoveNextLineBasedAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask SkipInitialRowsAsync(int rowsToSkip, CancellationToken cancellationToken)
    {
        int skipped = 0;
        while (skipped < rowsToSkip)
        {
            var span = buffer.AsSpan(offset, length - offset);
            if (!endOfStream && !FixedWidthLineScanner.ContainsLineBreak(span))
            {
                await FillBufferAsync(cancellationToken).ConfigureAwait(false);
                continue;
            }

            var lineEnd = FixedWidthLineScanner.FindLineEnd(span);
            if (lineEnd == -1)
            {
                if (endOfStream)
                {
                    offset = length;
                    return;
                }
                await FillBufferAsync(cancellationToken).ConfigureAwait(false);
                continue;
            }

            int consumed = lineEnd + 1;
            if (lineEnd < span.Length - 1 && span[lineEnd] == '\r' && span[lineEnd + 1] == '\n')
            {
                consumed++;
            }

            offset += consumed;

            if (trackLineNumbers)
                sourceLineNumber++;

            skipped++;
        }
    }

    private async ValueTask<bool> MoveNextFixedLengthAsync(int recordLength, CancellationToken cancellationToken)
    {
        while (true)
        {
            var available = length - offset;

            // Check if we have enough data for a record
            if (available >= recordLength)
            {
                recordCount++;
                if (recordCount > options.MaxRecordCount)
                {
                    throw new FixedWidthException(
                        FixedWidthErrorCode.TooManyRecords,
                        $"Maximum record count of {options.MaxRecordCount} exceeded.");
                }

                int rowStartLine = trackLineNumbers ? sourceLineNumber : 0;
                currentRowStart = offset;
                currentRowLength = recordLength;
                currentLineNumber = recordCount;
                currentSourceLineNumber = trackLineNumbers ? rowStartLine : recordCount;

                // Count newlines in the record for source line tracking
                if (trackLineNumbers)
                {
                    sourceLineNumber += FixedWidthLineScanner.CountNewlines(buffer.AsSpan(offset, recordLength));
                }

                offset += recordLength;
                return true;
            }

            if (endOfStream)
            {
                // Handle partial record at end
                if (available > 0)
                {
                    throw new FixedWidthException(
                        FixedWidthErrorCode.InvalidRecordLength,
                        $"Expected record length of {recordLength}, but only {available} characters remaining.",
                        recordCount + 1);
                }
                return false;
            }

            await FillBufferAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask<bool> MoveNextLineBasedAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var span = buffer.AsSpan(offset, length - offset);

            // Ensure we have a complete line or reached end of stream
            if (!endOfStream && !FixedWidthLineScanner.ContainsLineBreak(span))
            {
                await FillBufferAsync(cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (span.IsEmpty && endOfStream)
            {
                return false;
            }

            var lineEnd = FixedWidthLineScanner.FindLineEnd(span);
            ReadOnlySpan<char> line;
            int consumed;

            if (lineEnd == -1)
            {
                if (!endOfStream)
                {
                    await FillBufferAsync(cancellationToken).ConfigureAwait(false);
                    continue;
                }
                // Last line without newline
                line = span;
                consumed = span.Length;
            }
            else
            {
                line = span[..lineEnd];
                consumed = lineEnd + 1;
                if (lineEnd < span.Length - 1 && span[lineEnd] == '\r' && span[lineEnd + 1] == '\n')
                {
                    consumed++;
                }
            }

            int rowStartLine = trackLineNumbers ? sourceLineNumber : 0;
            offset += consumed;

            // Handle empty lines
            if (line.IsEmpty && options.SkipEmptyLines)
            {
                if (trackLineNumbers)
                    sourceLineNumber++;
                continue;
            }

            // Handle comment lines
            if (options.CommentCharacter is { } commentChar && line.Length > 0 && line[0] == commentChar)
            {
                if (trackLineNumbers)
                    sourceLineNumber++;
                continue;
            }

            recordCount++;
            if (recordCount > options.MaxRecordCount)
            {
                throw new FixedWidthException(
                    FixedWidthErrorCode.TooManyRecords,
                    $"Maximum record count of {options.MaxRecordCount} exceeded.");
            }

            // Store the row data (need to ensure it's before the offset adjustment)
            currentRowStart = offset - consumed;
            currentRowLength = line.Length;
            currentLineNumber = recordCount;
            currentSourceLineNumber = trackLineNumbers ? rowStartLine : recordCount;

            if (trackLineNumbers)
                sourceLineNumber++;

            return true;
        }
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
            // Check to prevent unbounded buffer growth (DoS protection)
            if (buffer.Length >= ABSOLUTE_MAX_BUFFER_SIZE)
            {
                throw new FixedWidthException(
                    FixedWidthErrorCode.ParseError,
                    $"Record exceeds maximum size of {ABSOLUTE_MAX_BUFFER_SIZE:N0} characters. " +
                    "Ensure records have proper line endings.");
            }

            var newBuffer = RentBuffer(buffer.Length * 2);
            buffer.AsSpan(0, length).CopyTo(newBuffer);
            ReturnBuffer(buffer);
            buffer = newBuffer;
        }

        var read = await reader.ReadAsync(buffer.AsMemory(length, buffer.Length - length), cancellationToken).ConfigureAwait(false);
        if (read == 0)
        {
            endOfStream = true;
            return;
        }

        length += read;
        bytesRead += read;
    }

    /// <summary>
    /// Asynchronously releases resources used by the reader.
    /// </summary>
    /// <returns>A <see cref="ValueTask"/> representing the asynchronous dispose operation.</returns>
    /// <remarks>
    /// The underlying stream is only closed if <c>leaveOpen</c> was <see langword="false"/> when the reader was created.
    /// </remarks>
    public ValueTask DisposeAsync()
    {
        if (disposed)
            return ValueTask.CompletedTask;

        disposed = true;
        ReturnBuffer(buffer);
        buffer = null!; // Prevent use-after-free
        reader.Dispose();
        return ValueTask.CompletedTask;
    }

    private char[] RentBuffer(int minimumLength)
    {
        var rented = charPool.Rent(minimumLength);
        Array.Clear(rented);
        return rented;
    }

    private void ReturnBuffer(char[] toReturn)
    {
        charPool.Return(toReturn, clearArray: true);
    }
}

