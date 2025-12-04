using System.Buffers;
using System.Text;

namespace HeroParser.SeparatedValues.Streaming;

/// <summary>
/// Streams CSV rows from a <see cref="Stream"/> without loading the entire payload into memory.
/// </summary>
public ref struct CsvStreamReader
{
    // Absolute maximum buffer size (128 MB) to prevent unbounded memory growth even when MaxRowSize is null
    private const int ABSOLUTE_MAX_BUFFER_SIZE = 128 * 1024 * 1024;

    private readonly ArrayPool<char> charPool;
    private readonly StreamReader reader;
    private readonly CsvParserOptions options;
    private readonly int[] columnStartsBuffer;
    private readonly int[] columnLengthsBuffer;
    private readonly bool trackLineNumbers;
    private bool disposed;
    private char[] buffer;
    private int offset;
    private int length;
    private int rowCount;
    private int sourceLineNumber; // Track source line number (1-based), only when TrackSourceLineNumbers enabled
    private bool endOfStream;
#pragma warning disable IDE0032 // Use auto property - can't use auto property here as bytesRead is modified in FillBuffer
    private long bytesRead;
#pragma warning restore IDE0032

    internal CsvStreamReader(Stream stream, CsvParserOptions options, Encoding encoding, bool leaveOpen, int initialBufferSize)
    {
        this.options = options;
        trackLineNumbers = options.TrackSourceLineNumbers;
        // Use shared pool for better memory efficiency - arrays are always cleared on return
        charPool = ArrayPool<char>.Shared;
        reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true, bufferSize: initialBufferSize, leaveOpen: leaveOpen);
        buffer = RentBuffer(Math.Max(initialBufferSize, 4096));
        columnStartsBuffer = new int[options.MaxColumnCount];
        columnLengthsBuffer = new int[options.MaxColumnCount];
        offset = 0;
        length = 0;
        rowCount = 0;
        sourceLineNumber = 1; // Start at line 1
        endOfStream = false;
        Current = default;
        disposed = false;
        bytesRead = 0;
    }

    /// <summary>Gets the current UTF-16 backed row.</summary>
    public CsvCharSpanRow Current { get; private set; }

    /// <summary>Gets the approximate number of bytes read from the underlying stream.</summary>
    /// <remarks>
    /// This value is estimated based on characters read assuming UTF-8 encoding (1 byte per ASCII character).
    /// For non-ASCII content or other encodings, this may not be precisely accurate.
    /// </remarks>
    public readonly long BytesRead => bytesRead;

    /// <summary>Returns this instance so it can be consumed by <c>foreach</c>.</summary>
    public readonly CsvStreamReader GetEnumerator() => this;

    /// <summary>
    /// Advances to the next row, reading from the underlying stream as needed.
    /// </summary>
    /// <returns><see langword="true"/> when another row was parsed; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="CsvException">Thrown when the input violates <see cref="CsvParserOptions"/>.</exception>
    public bool MoveNext()
    {
        while (true)
        {
            var span = buffer.AsSpan(offset, length - offset);
            int rowStartLine = trackLineNumbers ? sourceLineNumber : 0; // Only capture when tracking enabled
            var result = CsvStreamingParser.ParseRow(
                span,
                options,
                columnStartsBuffer.AsSpan(0, options.MaxColumnCount),
                columnLengthsBuffer.AsSpan(0, options.MaxColumnCount),
                trackLineNumbers);

            if (result.CharsConsumed > 0)
            {
                var rowChars = span[..result.RowLength];
                offset += result.CharsConsumed;

                // Update source line number based on newlines encountered (only when tracking enabled)
                if (trackLineNumbers)
                    sourceLineNumber += result.NewlineCount;

                if (rowChars.IsEmpty)
                    continue;

                rowCount++;
                Current = new CsvCharSpanRow(
                    rowChars,
                    columnStartsBuffer,
                    columnLengthsBuffer,
                    result.ColumnCount,
                    rowCount,
                    trackLineNumbers ? rowStartLine : rowCount); // Use rowCount as fallback when tracking disabled
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
                Dispose();
                return false;
            }

            FillBuffer();
        }
    }

    private void FillBuffer()
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

        var read = reader.Read(buffer, length, buffer.Length - length);
        if (read == 0)
        {
            endOfStream = true;
            return;
        }

        length += read;
        bytesRead += read; // Approximate bytes read (1 char ≈ 1 byte for ASCII/UTF-8)
    }

    /// <summary>Disposes the underlying stream if not opened with leaveOpen.</summary>
    /// <remarks>
    /// The underlying stream is only closed if <c>leaveOpen</c> was <see langword="false"/> when the reader was created.
    /// </remarks>
    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        ReturnBuffer(buffer);
        buffer = null!; // Prevent use-after-free

        // StreamReader was created with leaveOpen flag, so it handles stream disposal correctly
        reader.Dispose();
    }

    private readonly char[] RentBuffer(int minimumLength)
    {
        var rented = charPool.Rent(minimumLength);
        Array.Clear(rented);
        return rented;
    }

    private readonly void ReturnBuffer(char[] toReturn)
    {
        charPool.Return(toReturn, clearArray: true);
    }
}
