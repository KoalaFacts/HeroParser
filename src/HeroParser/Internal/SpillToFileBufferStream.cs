namespace HeroParser.Internal;

/// <summary>
/// Seekable, writable staging stream that keeps small payloads in memory and spills
/// to an auto-deleted private temporary file once writes exceed a threshold.
/// </summary>
internal sealed class SpillToFileBufferStream : Stream
{
    internal const int DEFAULT_MEMORY_THRESHOLD = 1024 * 1024;

    private readonly int memoryThreshold;
    private Stream inner;
    private bool disposed;

    internal SpillToFileBufferStream(int memoryThreshold = DEFAULT_MEMORY_THRESHOLD)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(memoryThreshold);
        this.memoryThreshold = memoryThreshold;
        inner = new MemoryStream();
    }

    internal bool HasSpilledToDisk { get; private set; }

    public override bool CanRead
    {
        get
        {
            EnsureNotDisposed();
            return inner.CanRead;
        }
    }

    public override bool CanSeek
    {
        get
        {
            EnsureNotDisposed();
            return inner.CanSeek;
        }
    }

    public override bool CanWrite
    {
        get
        {
            EnsureNotDisposed();
            return inner.CanWrite;
        }
    }

    public override long Length
    {
        get
        {
            EnsureNotDisposed();
            return inner.Length;
        }
    }

    public override long Position
    {
        get
        {
            EnsureNotDisposed();
            return inner.Position;
        }
        set
        {
            EnsureNotDisposed();
            inner.Position = value;
        }
    }

    public override void Flush()
    {
        EnsureNotDisposed();
        inner.Flush();
    }

    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        EnsureNotDisposed();
        return inner.FlushAsync(cancellationToken);
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        EnsureNotDisposed();
        return inner.Read(buffer, offset, count);
    }

    public override int Read(Span<byte> buffer)
    {
        EnsureNotDisposed();
        return inner.Read(buffer);
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        EnsureNotDisposed();
        return inner.ReadAsync(buffer, offset, count, cancellationToken);
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        EnsureNotDisposed();
        return inner.ReadAsync(buffer, cancellationToken);
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        EnsureNotDisposed();
        return inner.Seek(offset, origin);
    }

    public override void SetLength(long value)
    {
        EnsureNotDisposed();
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        if (!HasSpilledToDisk && value > memoryThreshold)
            SpillToTempFile();
        inner.SetLength(value);
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        EnsureNotDisposed();
        ValidateBufferArguments(buffer, offset, count);
        EnsureCapacityForWrite(count);
        inner.Write(buffer, offset, count);
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        EnsureNotDisposed();
        EnsureCapacityForWrite(buffer.Length);
        inner.Write(buffer);
    }

    public override void WriteByte(byte value)
    {
        EnsureNotDisposed();
        EnsureCapacityForWrite(1);
        inner.WriteByte(value);
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        EnsureNotDisposed();
        ValidateBufferArguments(buffer, offset, count);
        EnsureCapacityForWrite(count);
        return inner.WriteAsync(buffer, offset, count, cancellationToken);
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        EnsureNotDisposed();
        EnsureCapacityForWrite(buffer.Length);
        return inner.WriteAsync(buffer, cancellationToken);
    }

    public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
    {
        EnsureNotDisposed();
        return inner.CopyToAsync(destination, bufferSize, cancellationToken);
    }

    protected override void Dispose(bool disposing)
    {
        if (!disposed && disposing)
        {
            disposed = true;
            inner.Dispose();
        }
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        if (!disposed)
        {
            disposed = true;
            await inner.DisposeAsync().ConfigureAwait(false);
        }
        await base.DisposeAsync().ConfigureAwait(false);
    }

    private void EnsureNotDisposed()
        => ObjectDisposedException.ThrowIf(disposed, this);

    private void EnsureCapacityForWrite(int count)
    {
        if (!HasSpilledToDisk && count > 0 && inner.Position + count > memoryThreshold)
            SpillToTempFile();
    }

    private void SpillToTempFile()
    {
        var memory = (MemoryStream)inner;
        long savedPosition = memory.Position;
        string tempPath = Path.Combine(Path.GetTempPath(), $"heroparser-stage-{Guid.NewGuid():N}.tmp");
        var streamOptions = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.ReadWrite,
            Share = FileShare.None,
            BufferSize = 65_536,
            Options = FileOptions.DeleteOnClose | FileOptions.SequentialScan
        };
        if (!OperatingSystem.IsWindows())
        {
            streamOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        FileStream fileStream = new(tempPath, streamOptions);
        try
        {
            if (memory.Length > 0 && memory.TryGetBuffer(out ArraySegment<byte> buffer))
            {
                fileStream.Write(buffer.Array!, buffer.Offset, buffer.Count);
            }
            if (savedPosition != fileStream.Position)
            {
                fileStream.Position = savedPosition;
            }
        }
        catch
        {
            fileStream.Dispose();
            throw;
        }

        memory.Dispose();
        inner = fileStream;
        HasSpilledToDisk = true;
    }
}
