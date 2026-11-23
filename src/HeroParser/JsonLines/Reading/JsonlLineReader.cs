using System.Buffers;
using System.Globalization;

namespace HeroParser.JsonLines.Reading;

/// <summary>
/// Line reader that splits a stream of UTF-8 JSONL on <c>\n</c> boundaries,
/// trimming <c>\r</c> terminators and stripping a leading UTF-8 BOM. The returned spans alias
/// an internal pooled buffer and remain valid only until the next <see cref="TryReadLine"/> or <see cref="TryReadLineAsync"/> call.
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

    private bool eof;
    private bool disposed;

    internal JsonlLineReader(Stream stream, JsonlReadOptions options, bool leaveOpen = true)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        this.stream = stream;
        this.leaveOpen = leaveOpen;
        maxLineSizeBytes = options.MaxLineSizeBytes;
        buffer = ArrayPool<byte>.Shared.Rent(INITIAL_BUFFER_SIZE);
    }

    internal long BytesRead { get; private set; }

    /// <summary>
    /// Reads the next non-EOF line. <paramref name="line"/> aliases an internal buffer.
    /// </summary>
    /// <returns><see langword="true"/> when a line was produced; <see langword="false"/> at end of stream.</returns>
    internal bool TryReadLine(out ReadOnlyMemory<byte> line, out long lineNumberOut)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        while (true)
        {
            if (TryExtractBufferedLine(out line, out lineNumberOut))
                return true;

            if (eof)
            {
                line = default;
                lineNumberOut = lineNumber;
                return false;
            }

            FillBuffer();
        }
    }

    /// <summary>
    /// Asynchronously reads the next non-EOF line. The returned memory aliases an internal buffer
    /// and remains valid until the next read call.
    /// </summary>
    internal async ValueTask<(bool HasLine, ReadOnlyMemory<byte> Line, long LineNumber)> TryReadLineAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        while (true)
        {
            if (TryExtractBufferedLine(out ReadOnlyMemory<byte> line, out long lineNumberOut))
                return (true, line, lineNumberOut);

            if (eof)
                return (false, default, lineNumber);

            await FillBufferAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private bool TryExtractBufferedLine(out ReadOnlyMemory<byte> line, out long lineNumberOut)
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
                    string.Create(CultureInfo.InvariantCulture, $"A single line exceeds the configured MaxLineSizeBytes of {maxLineSizeBytes:N0} bytes."),
                    lineNumber + 1);
            }

            line = (lineNumber == 0 && lineEnd >= 3 &&
                    buffer[bufferStart] == 0xEF && buffer[bufferStart + 1] == 0xBB && buffer[bufferStart + 2] == 0xBF)
                ? buffer.AsMemory(bufferStart + 3, lineEnd - 3)
                : buffer.AsMemory(bufferStart, lineEnd);

            lineNumber++;
            lineNumberOut = lineNumber;
            bufferStart += newlineIdx + 1;
            bufferLen -= newlineIdx + 1;
            return true;
        }

        if (eof && bufferLen > 0)
        {
            if (bufferLen > maxLineSizeBytes)
            {
                throw new JsonlException(
                    JsonlErrorCode.LineTooLong,
                    string.Create(CultureInfo.InvariantCulture, $"A single line exceeds the configured MaxLineSizeBytes of {maxLineSizeBytes:N0} bytes."),
                    lineNumber + 1);
            }

            line = (lineNumber == 0 && bufferLen >= 3 &&
                    buffer[bufferStart] == 0xEF && buffer[bufferStart + 1] == 0xBB && buffer[bufferStart + 2] == 0xBF)
                ? buffer.AsMemory(bufferStart + 3, bufferLen - 3)
                : buffer.AsMemory(bufferStart, bufferLen);

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

    private (int Offset, int Count) PrepareBufferForRead()
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
                    string.Create(CultureInfo.InvariantCulture, $"A single line exceeds the configured MaxLineSizeBytes of {maxLineSizeBytes:N0} bytes."),
                    lineNumber + 1);
            }
            if (newSize > maxLineSizeBytes)
                newSize = maxLineSizeBytes;
            byte[] newBuffer = ArrayPool<byte>.Shared.Rent(newSize);
            Buffer.BlockCopy(buffer, 0, newBuffer, 0, bufferLen);
            ArrayPool<byte>.Shared.Return(buffer);
            buffer = newBuffer;
        }

        return (bufferLen, buffer.Length - bufferLen);
    }

    private void FillBuffer()
    {
        var (offset, count) = PrepareBufferForRead();
        int read = stream.Read(buffer, offset, count);
        if (read == 0)
        {
            eof = true;
            return;
        }

        bufferLen += read;
        BytesRead += read;
    }

    private async ValueTask FillBufferAsync(CancellationToken cancellationToken)
    {
        var (offset, count) = PrepareBufferForRead();
        int read = await stream.ReadAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
        if (read == 0)
        {
            eof = true;
            return;
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
