using System.Text;

namespace HeroParser.SeparatedValues.Streaming;

/// <summary>
/// Streams CSV rows from a <see cref="Stream"/> without loading the entire payload into memory.
/// </summary>
public ref struct CsvStreamReader
{
    private readonly StreamReader reader;
    private readonly CsvParserOptions options;
    private readonly int[] columnStartsBuffer;
    private readonly int[] columnLengthsBuffer;
    private bool disposed;
    private char[] buffer;
    private int offset;
    private int length;
    private int rowCount;
    private bool endOfStream;
#pragma warning disable IDE0032 // Use auto property - can't use auto property here as bytesRead is modified in FillBuffer
    private long bytesRead;
#pragma warning restore IDE0032

    internal CsvStreamReader(Stream stream, CsvParserOptions options, Encoding encoding, bool leaveOpen, int initialBufferSize)
    {
        this.options = options;
        reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true, bufferSize: initialBufferSize, leaveOpen: leaveOpen);
        // Use dedicated arrays instead of ArrayPool to avoid sharing issues when
        // GetEnumerator() creates a copy that shares the same array references
        buffer = new char[Math.Max(initialBufferSize, 4096)];
        columnStartsBuffer = new int[options.MaxColumnCount];
        columnLengthsBuffer = new int[options.MaxColumnCount];
        offset = 0;
        length = 0;
        rowCount = 0;
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
            var result = CsvStreamingParser.ParseRow(
                span,
                options,
                columnStartsBuffer.AsSpan(0, options.MaxColumnCount),
                columnLengthsBuffer.AsSpan(0, options.MaxColumnCount));

            if (result.CharsConsumed > 0)
            {
                var rowChars = span[..result.RowLength];
                offset += result.CharsConsumed;

                if (rowChars.IsEmpty)
                    continue;

                rowCount++;
                Current = new CsvCharSpanRow(
                    rowChars,
                    columnStartsBuffer,
                    columnLengthsBuffer,
                    result.ColumnCount,
                    rowCount);
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
            if (options.MaxRowSize.HasValue && buffer.Length >= options.MaxRowSize.Value)
            {
                throw new CsvException(
                    CsvErrorCode.ParseError,
                    $"Row exceeds maximum size of {options.MaxRowSize.Value:N0} characters. " +
                    "Increase MaxRowSize or ensure rows have proper line endings.");
            }

            var newBuffer = new char[buffer.Length * 2];
            buffer.AsSpan(0, length).CopyTo(newBuffer);
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

        // StreamReader was created with leaveOpen flag, so it handles stream disposal correctly
        reader.Dispose();
        disposed = true;
    }
}
