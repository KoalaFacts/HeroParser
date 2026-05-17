using System.Buffers;

namespace HeroParser.JsonLines.Reading;

/// <summary>
/// Synchronous line reader that splits a stream of UTF-8 JSONL on <c>\n</c> boundaries,
/// trimming <c>\r</c> terminators and stripping a leading UTF-8 BOM. The returned spans alias
/// an internal pooled buffer and remain valid only until the next <see cref="TryReadLine"/> call.
/// </summary>
internal sealed class JsonlLineReader : IDisposable
{
    private const int INITIAL_BUFFER_SIZE = 8 * 1024;
    private const byte LF = (byte)'\n';
    private const byte CR = (byte)'\r';

    private readonly Stream stream;
    private readonly bool leaveOpen;
    private readonly int maxLineSizeBytes;
    private byte[] buffer;
    private int bufferStart;
    private int bufferLen;
    private long lineNumber;
    private bool firstChunk = true;
    private bool eof;
    private bool disposed;

    public JsonlLineReader(Stream stream, JsonlReadOptions options, bool leaveOpen = true)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        this.stream = stream;
        this.leaveOpen = leaveOpen;
        maxLineSizeBytes = options.MaxLineSizeBytes;
        buffer = ArrayPool<byte>.Shared.Rent(INITIAL_BUFFER_SIZE);
    }

    public long BytesRead { get; private set; }

    /// <summary>
    /// Reads the next non-EOF line. <paramref name="line"/> aliases an internal buffer.
    /// </summary>
    /// <returns><see langword="true"/> when a line was produced; <see langword="false"/> at end of stream.</returns>
    public bool TryReadLine(out ReadOnlySpan<byte> line, out long lineNumberOut)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        while (true)
        {
            ReadOnlySpan<byte> span = buffer.AsSpan(bufferStart, bufferLen);
            int newlineIdx = span.IndexOf(LF);
            if (newlineIdx >= 0)
            {
                int lineEnd = newlineIdx;
                if (lineEnd > 0 && span[lineEnd - 1] == CR)
                    lineEnd--;
                if (lineEnd > maxLineSizeBytes)
                {
                    throw new JsonlException(
                        JsonlErrorCode.LineTooLong,
                        $"A single line exceeds the configured MaxLineSizeBytes of {maxLineSizeBytes:N0} bytes.",
                        lineNumber + 1);
                }
                line = buffer.AsSpan(bufferStart, lineEnd);
                lineNumber++;
                lineNumberOut = lineNumber;
                bufferStart += newlineIdx + 1;
                bufferLen -= newlineIdx + 1;
                return true;
            }

            if (eof)
            {
                if (bufferLen > 0)
                {
                    if (bufferLen > maxLineSizeBytes)
                    {
                        throw new JsonlException(
                            JsonlErrorCode.LineTooLong,
                            $"A single line exceeds the configured MaxLineSizeBytes of {maxLineSizeBytes:N0} bytes.",
                            lineNumber + 1);
                    }
                    line = buffer.AsSpan(bufferStart, bufferLen);
                    lineNumber++;
                    lineNumberOut = lineNumber;
                    bufferStart += bufferLen;
                    bufferLen = 0;
                    return true;
                }
                line = default;
                lineNumberOut = lineNumber;
                return false;
            }

            FillBuffer();
        }
    }

    private void FillBuffer()
    {
        if (bufferStart > 0)
        {
            Buffer.BlockCopy(buffer, bufferStart, buffer, 0, bufferLen);
            bufferStart = 0;
        }

        if (bufferLen == buffer.Length)
        {
            int newSize = buffer.Length * 2;
            if (buffer.Length >= maxLineSizeBytes)
            {
                throw new JsonlException(
                    JsonlErrorCode.LineTooLong,
                    $"A single line exceeds the configured MaxLineSizeBytes of {maxLineSizeBytes:N0} bytes.",
                    lineNumber + 1);
            }
            if (newSize > maxLineSizeBytes)
                newSize = maxLineSizeBytes;
            byte[] newBuffer = ArrayPool<byte>.Shared.Rent(newSize);
            Buffer.BlockCopy(buffer, 0, newBuffer, 0, bufferLen);
            ArrayPool<byte>.Shared.Return(buffer);
            buffer = newBuffer;
        }

        int read = stream.Read(buffer, bufferLen, buffer.Length - bufferLen);
        if (read == 0)
        {
            eof = true;
            return;
        }

        if (firstChunk)
        {
            firstChunk = false;
            if (bufferLen == 0 && read >= 3 &&
                buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF)
            {
                Buffer.BlockCopy(buffer, 3, buffer, 0, read - 3);
                read -= 3;
                BytesRead += 3;
            }
        }

        bufferLen += read;
        BytesRead += read;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        ArrayPool<byte>.Shared.Return(buffer);
        if (!leaveOpen) stream.Dispose();
    }
}
