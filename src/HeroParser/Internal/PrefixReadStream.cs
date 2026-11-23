namespace HeroParser.Internal;

/// <summary>
/// Replays a small lookahead byte prefix before reading the remaining bytes from an inner non-seekable stream.
/// </summary>
internal sealed class PrefixReadStream(Stream inner, byte[] prefix, bool leaveOpen = false) : Stream
{
    private readonly Stream inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly byte[] prefix = prefix ?? throw new ArgumentNullException(nameof(prefix));
    private int prefixOffset;

    public override bool CanRead => inner.CanRead;
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
        ValidateBufferArguments(buffer, offset, count);
        return Read(buffer.AsSpan(offset, count));
    }

    public override int Read(Span<byte> buffer)
    {
        if (buffer.IsEmpty)
            return 0;

        int remaining = prefix.Length - prefixOffset;
        if (remaining > 0)
        {
            int copy = Math.Min(remaining, buffer.Length);
            prefix.AsSpan(prefixOffset, copy).CopyTo(buffer);
            prefixOffset += copy;
            return copy;
        }

        return inner.Read(buffer);
    }

    public override int ReadByte()
    {
        if (prefixOffset < prefix.Length)
            return prefix[prefixOffset++];

        return inner.ReadByte();
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ValidateBufferArguments(buffer, offset, count);
        return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (buffer.IsEmpty)
            return ValueTask.FromResult(0);

        int remaining = prefix.Length - prefixOffset;
        if (remaining > 0)
        {
            int copy = Math.Min(remaining, buffer.Length);
            prefix.AsSpan(prefixOffset, copy).CopyTo(buffer.Span);
            prefixOffset += copy;
            return ValueTask.FromResult(copy);
        }

        return inner.ReadAsync(buffer, cancellationToken);
    }

    public override void Flush() { }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

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
}
