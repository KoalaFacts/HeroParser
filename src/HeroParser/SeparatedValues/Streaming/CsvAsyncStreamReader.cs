using System.Buffers;
using System.Text;

namespace HeroParser.SeparatedValues.Streaming;

/// <summary>
/// Async CSV reader that streams from a file or stream without loading the entire payload into memory.
/// </summary>
/// <remarks>
/// <para>Uses Ends-only column indexing to minimize memory writes during parsing.</para>
/// </remarks>
public sealed class CsvAsyncStreamReader : IAsyncDisposable
{
    // Absolute maximum buffer size (128 MB) to prevent unbounded memory growth even when MaxRowSize is null
    private const int ABSOLUTE_MAX_BUFFER_SIZE = 128 * 1024 * 1024;

    private readonly ArrayPool<char> charPool;
    private readonly StreamReader reader;
    private readonly CsvParserOptions options;
    private readonly int[] columnEndsBuffer;
    private readonly bool trackLineNumbers;
    private char[] buffer;
    private int offset;
    private int length;
    private int rowCount;
    private int sourceLineNumber; // Track source line number (1-based), only when TrackSourceLineNumbers enabled
    private bool endOfStream;
    private bool disposed;
    private int currentRowStart;
    private int currentRowLength;
    private int currentColumnCount;
    private int currentLineNumber;
    private int currentSourceLineNumber; // Source line number for current row
#pragma warning disable IDE0032 // Use auto property - can't use auto property here as bytesRead is modified in FillBufferAsync
    private long bytesRead;
#pragma warning restore IDE0032

    /// <summary>The current row; valid until the next <see cref="MoveNextAsync"/> call.</summary>
    public CsvCharSpanRow Current => new(
        buffer.AsSpan(currentRowStart, currentRowLength),
        columnEndsBuffer,
        currentColumnCount,
        currentLineNumber,
        currentSourceLineNumber,
        options.TrimFields);

    /// <summary>Gets the approximate number of bytes read from the underlying stream.</summary>
    /// <remarks>
    /// This value is estimated based on characters read assuming UTF-8 encoding (1 byte per ASCII character).
    /// For non-ASCII content or other encodings, this may not be precisely accurate.
    /// </remarks>
    public long BytesRead => bytesRead;

    internal CsvAsyncStreamReader(Stream stream, CsvParserOptions options, Encoding encoding, bool leaveOpen, int initialBufferSize)
    {
        this.options = options;
        trackLineNumbers = options.TrackSourceLineNumbers;
        // Use shared pool for better memory efficiency - arrays are always cleared on return
        charPool = ArrayPool<char>.Shared;
        reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true, bufferSize: initialBufferSize, leaveOpen: leaveOpen);
        buffer = RentBuffer(Math.Max(initialBufferSize, 4096));
        // Ends-only storage: need maxColumns + 1 entries
        columnEndsBuffer = new int[options.MaxColumnCount + 1];
        offset = 0;
        length = 0;
        rowCount = 0;
        sourceLineNumber = 1; // Start at line 1
        endOfStream = false;
        disposed = false;
        currentRowStart = 0;
        currentRowLength = 0;
        currentColumnCount = 0;
        currentLineNumber = 0;
        currentSourceLineNumber = 1;
    }

    /// <summary>
    /// Advances to the next row, reading from the underlying stream asynchronously as needed.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> when another row was parsed; otherwise, <see langword="false"/>.</returns>
    public async ValueTask<bool> MoveNextAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        while (true)
        {
            var span = buffer.AsSpan(offset, length - offset);
            if (!endOfStream && !ContainsLineBreak(span))
            {
                await FillBufferAsync(cancellationToken).ConfigureAwait(false);
                continue;
            }

            var result = CsvStreamingParser.ParseRow(
                span,
                options,
                columnEndsBuffer.AsSpan(0, options.MaxColumnCount + 1),
                trackLineNumbers);

            if (result.CharsConsumed > 0)
            {
                var rowStart = offset;
                var rowLength = result.RowLength;
                int rowStartLine = trackLineNumbers ? sourceLineNumber : 0; // Only capture when tracking enabled
                offset += result.CharsConsumed;

                // Update source line number based on newlines encountered (only when tracking enabled)
                if (trackLineNumbers)
                    sourceLineNumber += result.NewlineCount;

                if (rowLength == 0)
                    continue;

                rowCount++;
                currentRowStart = rowStart;
                currentRowLength = rowLength;
                currentColumnCount = result.ColumnCount;
                currentLineNumber = rowCount;
                currentSourceLineNumber = trackLineNumbers ? rowStartLine : rowCount; // Use rowCount as fallback
                if (rowCount > options.MaxRowCount)
                {
                    throw new CsvException(
                        CsvErrorCode.TooManyRows,
                        $"CSV exceeds maximum row limit of {options.MaxRowCount}");
                }
                return true;
            }

            if (endOfStream)
            {
                return false;
            }

            await FillBufferAsync(cancellationToken).ConfigureAwait(false);
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
            // Check MaxRowSize to prevent unbounded buffer growth (DoS protection)
            int effectiveMaxSize = options.MaxRowSize ?? ABSOLUTE_MAX_BUFFER_SIZE;
            if (buffer.Length >= effectiveMaxSize)
            {
                throw new CsvException(
                    CsvErrorCode.ParseError,
                    $"Row exceeds maximum size of {effectiveMaxSize:N0} characters. " +
                    "Increase MaxRowSize or ensure rows have proper line endings.");
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
        bytesRead += read; // Approximate bytes read (1 char ≈ 1 byte for ASCII/UTF-8)
    }

    private static bool ContainsLineBreak(ReadOnlySpan<char> span)
    {
        return span.IndexOfAny('\r', '\n') >= 0;
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

        // StreamReader was created with leaveOpen flag, so it handles stream disposal correctly
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
