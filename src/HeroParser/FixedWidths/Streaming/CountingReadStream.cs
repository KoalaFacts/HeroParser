using HeroParser.FixedWidths;

namespace HeroParser.FixedWidths.Streaming;

/// <summary>
/// Wraps a stream and enforces the configured fixed-width input-size budget on raw bytes read.
/// </summary>
internal sealed class CountingReadStream(Stream inner, FixedWidthReadOptions options, bool leaveOpen) : Stream
{

    public long BytesRead { get; private set; }

    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => inner.CanSeek;
    public override bool CanWrite => inner.CanWrite;
    public override long Length => inner.Length;

    public override long Position
    {
        get => inner.Position;
        set => inner.Position = value;
    }

    public override void Flush() => inner.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);

    public override int Read(byte[] buffer, int offset, int count)
    {
        int read = inner.Read(buffer, offset, count);
        Track(read);
        return read;
    }

    public override int Read(Span<byte> buffer)
    {
        int read = inner.Read(buffer);
        Track(read);
        return read;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        int read = await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        Track(read);
        return read;
    }

    public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);

    public override void SetLength(long value) => inner.SetLength(value);

    public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);

    public override void Write(ReadOnlySpan<byte> buffer) => inner.Write(buffer);

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => inner.WriteAsync(buffer, offset, count, cancellationToken);

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        => inner.WriteAsync(buffer, cancellationToken);

    public override void WriteByte(byte value) => inner.WriteByte(value);

    public override int ReadByte()
    {
        int value = inner.ReadByte();
        if (value >= 0)
            Track(1);
        return value;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !leaveOpen)
            inner.Dispose();

        base.Dispose(disposing);
    }

    public override ValueTask DisposeAsync()
    {
        if (leaveOpen)
            return ValueTask.CompletedTask;

        return inner.DisposeAsync();
    }

    private void Track(int read)
    {
        if (read <= 0)
            return;

        BytesRead += read;
        options.ValidateInputSize(BytesRead);
    }
}
