namespace HeroParser.SeparatedValues.Detection;

// Replays delimiter/BOM probe bytes and bounds source reads without owning the caller's stream.
internal sealed class CsvPrefixReadStream(
    Stream source, ReadOnlyMemory<byte> prefix, long maxScannedInputSize, long initiallyReadBytes) : Stream
{
    private int prefixOffset;
    private long sourceBytesRead = initiallyReadBytes;
    private bool probedAtLimit;
    private bool disposed;

    internal bool HasMoreThanBudget { get; private set; }

    internal async ValueTask<bool> HasMoreAfterBudgetAsync(CancellationToken cancellationToken)
    {
        if (sourceBytesRead < maxScannedInputSize)
            return false;
        if (probedAtLimit)
            return HasMoreThanBudget;

        probedAtLimit = true;
        byte[] probe = new byte[1];
        HasMoreThanBudget = await source.ReadAsync(probe, cancellationToken).ConfigureAwait(false) != 0;
        return HasMoreThanBudget;
    }

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

        if (prefixOffset < prefix.Length)
        {
            int count = Math.Min(buffer.Length, prefix.Length - prefixOffset);
            prefix.Span.Slice(prefixOffset, count).CopyTo(buffer);
            prefixOffset += count;
            return count;
        }

        if (sourceBytesRead == maxScannedInputSize)
        {
            if (probedAtLimit)
                return 0;
            probedAtLimit = true;
            // Let the parser finish a CR-terminated row; inference rejects unfinished rows after this probe.
            Span<byte> probe = stackalloc byte[1];
            HasMoreThanBudget = source.Read(probe) != 0;
            return 0;
        }

        int read = source.Read(buffer[..(int)Math.Min(buffer.Length, maxScannedInputSize - sourceBytesRead)]);
        sourceBytesRead += read;
        return read;
    }

    public override Task<int> ReadAsync(byte[] buffer, int bufferOffset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(bufferOffset, count), cancellationToken).AsTask();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (disposed)
            throw new ObjectDisposedException(nameof(CsvPrefixReadStream));
        cancellationToken.ThrowIfCancellationRequested();
        if (buffer.IsEmpty)
            return 0;

        if (prefixOffset < prefix.Length)
        {
            int count = Math.Min(buffer.Length, prefix.Length - prefixOffset);
            prefix.Span.Slice(prefixOffset, count).CopyTo(buffer.Span);
            prefixOffset += count;
            return count;
        }

        if (sourceBytesRead == maxScannedInputSize)
        {
            // The caller checks the result before accepting an unfinished final row.
            await HasMoreAfterBudgetAsync(cancellationToken).ConfigureAwait(false);
            return 0;
        }

        int read = await source.ReadAsync(buffer[..(int)Math.Min(buffer.Length, maxScannedInputSize - sourceBytesRead)],
            cancellationToken).ConfigureAwait(false);
        sourceBytesRead += read;
        return read;
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
