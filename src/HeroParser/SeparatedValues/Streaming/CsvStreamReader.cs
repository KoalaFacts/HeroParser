using System.Buffers;
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
    private readonly bool leaveOpen;
    private bool disposed;
    private char[] buffer;
    private int offset;
    private int length;
    private int rowCount;
    private bool endOfStream;

    internal CsvStreamReader(Stream stream, CsvParserOptions options, Encoding encoding, bool leaveOpen, int initialBufferSize)
    {
        this.options = options;
        this.leaveOpen = leaveOpen;
        reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true, bufferSize: initialBufferSize, leaveOpen: leaveOpen);
        buffer = ArrayPool<char>.Shared.Rent(Math.Max(initialBufferSize, 4096));
        columnStartsBuffer = ArrayPool<int>.Shared.Rent(options.MaxColumns);
        columnLengthsBuffer = ArrayPool<int>.Shared.Rent(options.MaxColumns);
        offset = 0;
        length = 0;
        rowCount = 0;
        endOfStream = false;
        Current = default;
        disposed = false;
    }

    /// <summary>Gets the current UTF-16 backed row.</summary>
    public CsvCharSpanRow Current { get; private set; }

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
                columnStartsBuffer.AsSpan(0, options.MaxColumns),
                columnLengthsBuffer.AsSpan(0, options.MaxColumns));

            if (result.CharsConsumed > 0)
            {
                var rowChars = span[..result.RowLength];
                offset += result.CharsConsumed;

                if (rowChars.IsEmpty)
                    continue;

                Current = new CsvCharSpanRow(
                    rowChars,
                    columnStartsBuffer,
                    columnLengthsBuffer,
                    result.ColumnCount);

                rowCount++;
                if (rowCount > options.MaxRows)
                {
                    throw new CsvException(
                        CsvErrorCode.TooManyRows,
                        $"CSV exceeds maximum row limit of {options.MaxRows}");
                }
                return true;
            }

            if (endOfStream)
            {
                if (!leaveOpen && !disposed)
                {
                    reader.Dispose();
                    disposed = true;
                }

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
            var newBuffer = ArrayPool<char>.Shared.Rent(buffer.Length * 2);
            buffer.AsSpan(0, length).CopyTo(newBuffer);
            ArrayPool<char>.Shared.Return(buffer, clearArray: false);
            buffer = newBuffer;
        }

        var read = reader.Read(buffer, length, buffer.Length - length);
        if (read == 0)
        {
            endOfStream = true;
            return;
        }

        length += read;
    }

    /// <summary>Returns pooled buffers and optionally disposes the underlying stream.</summary>
    public void Dispose()
    {
        if (disposed)
            return;

        ArrayPool<int>.Shared.Return(columnStartsBuffer, clearArray: false);
        ArrayPool<int>.Shared.Return(columnLengthsBuffer, clearArray: false);
        ArrayPool<char>.Shared.Return(buffer, clearArray: false);

        reader.Dispose();
        disposed = true;
    }
}
