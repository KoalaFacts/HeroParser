using System.Buffers;
using System.Text;

namespace HeroParser.SeparatedValues.Reading.Streaming;

/// <summary>Transcodes UTF-16 file input using buffers reused for the lifetime of the reader.</summary>
internal sealed class Utf16ToUtf8ReadStream : Stream
{
    private const int CHAR_BUFFER_SIZE = 32 * 1024;
    private const int SOURCE_BUFFER_SIZE = 64 * 1024;

    private readonly StreamReader reader;
    private readonly Encoder encoder = new UTF8Encoding(false).GetEncoder();
    private readonly char[] charBuffer;
    private readonly byte[] byteBuffer;
    private int byteOffset;
    private int byteCount;
    private bool endOfStream;
    private bool disposed;

    public Utf16ToUtf8ReadStream(Stream stream, Encoding encoding)
    {
        reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: false,
            bufferSize: SOURCE_BUFFER_SIZE, leaveOpen: false);
        char[]? rentedChars = null;
        try
        {
            rentedChars = ArrayPool<char>.Shared.Rent(CHAR_BUFFER_SIZE);
            byteBuffer = ArrayPool<byte>.Shared.Rent(Encoding.UTF8.GetMaxByteCount(CHAR_BUFFER_SIZE));
            charBuffer = rentedChars;
        }
        catch
        {
            if (rentedChars is not null)
                ArrayPool<char>.Shared.Return(rentedChars);
            reader.Dispose();
            throw;
        }
    }

    public override bool CanRead => !disposed;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        return Read(buffer.AsSpan(offset, count));
    }

    public override int Read(Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (buffer.IsEmpty)
            return 0;

        while (byteCount == 0 && !endOfStream)
        {
            int charsRead = reader.Read(charBuffer.AsSpan(0, CHAR_BUFFER_SIZE));
            endOfStream = charsRead == 0;
            byteCount = encoder.GetBytes(charBuffer.AsSpan(0, charsRead), byteBuffer, flush: endOfStream);
            byteOffset = 0;
        }

        return CopyPending(buffer);
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (buffer.IsEmpty)
            return 0;

        while (byteCount == 0 && !endOfStream)
        {
            int charsRead = await reader.ReadAsync(charBuffer.AsMemory(0, CHAR_BUFFER_SIZE), cancellationToken)
                .ConfigureAwait(false);
            endOfStream = charsRead == 0;
            byteCount = encoder.GetBytes(charBuffer.AsSpan(0, charsRead), byteBuffer, flush: endOfStream);
            byteOffset = 0;
        }

        return CopyPending(buffer.Span);
    }

    private int CopyPending(Span<byte> destination)
    {
        int count = Math.Min(destination.Length, byteCount);
        byteBuffer.AsSpan(byteOffset, count).CopyTo(destination);
        byteOffset += count;
        byteCount -= count;
        return count;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !disposed)
        {
            disposed = true;
            ArrayPool<char>.Shared.Return(charBuffer);
            ArrayPool<byte>.Shared.Return(byteBuffer);
            reader.Dispose();
        }
        base.Dispose(disposing);
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
