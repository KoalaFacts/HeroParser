namespace HeroParser.SeparatedValues.Detection;

// Replays delimiter/BOM probe bytes without taking ownership of the caller's stream.
internal sealed class CsvPrefixReadStream(Stream source, ReadOnlyMemory<byte> prefix) : Stream
{
    private int offset;
    private bool disposed;

    public override bool CanRead => !disposed && source.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int bufferOffset, int count) =>
        Read(buffer.AsSpan(bufferOffset, count));

    public override int Read(Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (buffer.IsEmpty)
            return 0;

        if (offset < prefix.Length)
        {
            int count = Math.Min(buffer.Length, prefix.Length - offset);
            prefix.Span.Slice(offset, count).CopyTo(buffer);
            offset += count;
            return count;
        }

        return source.Read(buffer);
    }

    public override Task<int> ReadAsync(byte[] buffer, int bufferOffset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(bufferOffset, count), cancellationToken).AsTask();

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (disposed)
            return ValueTask.FromException<int>(new ObjectDisposedException(nameof(CsvPrefixReadStream)));
        if (cancellationToken.IsCancellationRequested)
            return ValueTask.FromCanceled<int>(cancellationToken);
        if (buffer.IsEmpty)
            return ValueTask.FromResult(0);

        if (offset < prefix.Length)
        {
            int count = Math.Min(buffer.Length, prefix.Length - offset);
            prefix.Span.Slice(offset, count).CopyTo(buffer.Span);
            offset += count;
            return ValueTask.FromResult(count);
        }

        return source.ReadAsync(buffer, cancellationToken);
    }

    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int bufferOffset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        disposed = true;
        base.Dispose(disposing);
    }

    public override ValueTask DisposeAsync()
    {
        disposed = true;
        return ValueTask.CompletedTask;
    }
}
