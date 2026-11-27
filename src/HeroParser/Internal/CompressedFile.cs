using System.Buffers;
using System.IO.Compression;
using System.Text;

namespace HeroParser.Internal;

internal enum FileCompressionFormat
{
    None,
    GZip,
    Brotli,
    Zstandard
}

/// <summary>
/// Wraps a file path and handles transparent compression/decompression for <c>.gz</c>, <c>.br</c>, and <c>.zst</c>/<c>.zstd</c> files.
/// </summary>
internal readonly struct CompressedFile
{
    private const int COPY_BUFFER_SIZE = 16 * 1024;
    private readonly string path;

    internal CompressedFile(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        this.path = path;
        Format = DetectFormat(path);
    }

    internal string Path => path;

    internal FileCompressionFormat Format { get; }

    internal bool IsCompressed => Format != FileCompressionFormat.None;

    internal Stream OpenRead(
        int bufferSize = 4096,
        FileOptions options = FileOptions.SequentialScan)
    {
        EnsureSupported();
        var fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, options);
        if (Format == FileCompressionFormat.None)
            return fileStream;

        try
        {
            return Format switch
            {
                FileCompressionFormat.GZip => new GZipStream(fileStream, CompressionMode.Decompress, leaveOpen: false),
                FileCompressionFormat.Brotli => new BrotliStream(fileStream, CompressionMode.Decompress, leaveOpen: false),
#if NET11_0_OR_GREATER
                FileCompressionFormat.Zstandard => (OperatingSystem.IsBrowser() || OperatingSystem.IsWasi())
                    ? throw new PlatformNotSupportedException("Transparent Zstandard (.zst) compression is not supported on browser or WASI platforms.")
                    : new ZstandardStream(fileStream, CompressionMode.Decompress, leaveOpen: false),
#endif
                _ => throw new NotSupportedException($"Compression format '{Format}' is not supported.")
            };
        }
        catch
        {
            fileStream.Dispose();
            throw;
        }
    }

    internal Stream OpenWrite(
        int bufferSize = 4096,
        FileOptions options = FileOptions.None,
        CompressionLevel level = CompressionLevel.Fastest)
    {
        EnsureSupported();
        var fileStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, options);
        if (Format == FileCompressionFormat.None)
            return fileStream;

        try
        {
            return Format switch
            {
                FileCompressionFormat.GZip => new NonEmptyGZipWriteStream(fileStream, level),
                FileCompressionFormat.Brotli => new BrotliStream(fileStream, level, leaveOpen: false),
#if NET11_0_OR_GREATER
                FileCompressionFormat.Zstandard => (OperatingSystem.IsBrowser() || OperatingSystem.IsWasi())
                    ? throw new PlatformNotSupportedException("Transparent Zstandard (.zst) compression is not supported on browser or WASI platforms.")
                    : new ZstandardStream(fileStream, level, leaveOpen: false),
#endif
                _ => throw new NotSupportedException($"Compression format '{Format}' is not supported.")
            };
        }
        catch
        {
            fileStream.Dispose();
            throw;
        }
    }

    internal byte[] ReadAllBytes(
        long? maxBytes = null,
        Func<long, Exception>? onSizeExceeded = null)
    {
        if (!IsCompressed)
        {
            if (maxBytes is { } limit)
            {
                long length = new FileInfo(path).Length;
                if (length > limit)
                {
                    throw onSizeExceeded?.Invoke(length)
                        ?? new InvalidOperationException($"File length ({length:N0} bytes) exceeds maximum allowed ({limit:N0} bytes).");
                }
            }

            return File.ReadAllBytes(path);
        }

        using var compressedStream = OpenRead(COPY_BUFFER_SIZE, FileOptions.SequentialScan);
        using var output = new MemoryStream();
        byte[] rented = ArrayPool<byte>.Shared.Rent(COPY_BUFFER_SIZE);
        try
        {
            long totalRead = 0;
            int read;
            while ((read = compressedStream.Read(rented, 0, rented.Length)) > 0)
            {
                totalRead += read;
                if (maxBytes is { } limit && totalRead > limit)
                {
                    throw onSizeExceeded?.Invoke(totalRead)
                        ?? new InvalidOperationException($"Decompressed file size ({totalRead:N0} bytes) exceeds maximum allowed ({limit:N0} bytes).");
                }

                output.Write(rented, 0, read);
            }

            return output.ToArray();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    internal string ReadAllText(Encoding? encoding = null)
    {
        if (!IsCompressed)
            return encoding is null ? File.ReadAllText(path) : File.ReadAllText(path, encoding);

        using var stream = OpenRead(COPY_BUFFER_SIZE, FileOptions.SequentialScan);
        using var reader = encoding is null
            ? new StreamReader(stream, detectEncodingFromByteOrderMarks: true)
            : new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    internal async Task<string> ReadAllTextAsync(
        Encoding? encoding = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsCompressed)
        {
            return encoding is null
                ? await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false)
                : await File.ReadAllTextAsync(path, encoding, cancellationToken).ConfigureAwait(false);
        }

        var stream = OpenRead(COPY_BUFFER_SIZE, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using (stream.ConfigureAwait(false))
        {
            using var reader = encoding is null
                ? new StreamReader(stream, detectEncodingFromByteOrderMarks: true)
                : new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true);
            return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private void EnsureSupported()
    {
        if (Format != FileCompressionFormat.Zstandard)
            return;

#if NET11_0_OR_GREATER
        if (OperatingSystem.IsBrowser() || OperatingSystem.IsWasi())
            throw new PlatformNotSupportedException("Transparent Zstandard (.zst) compression is not supported on browser or WASI platforms.");
#else
        throw new PlatformNotSupportedException("Transparent Zstandard (.zst) compression requires .NET 11 or later.");
#endif
    }

    private static FileCompressionFormat DetectFormat(string path)
    {
        if (path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
            return FileCompressionFormat.GZip;
        if (path.EndsWith(".br", StringComparison.OrdinalIgnoreCase))
            return FileCompressionFormat.Brotli;
        if (path.EndsWith(".zst", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".zstd", StringComparison.OrdinalIgnoreCase))
            return FileCompressionFormat.Zstandard;
        return FileCompressionFormat.None;
    }
}

/// <summary>
/// Wraps <see cref="GZipStream"/> so disposing a zero-byte stream still writes a valid empty RFC 1952 member on .NET 8-10.
/// </summary>
internal sealed class NonEmptyGZipWriteStream(FileStream inner, CompressionLevel level) : Stream
{
    private static readonly byte[] emptyGzipMember =
        [0x1F, 0x8B, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xFF, 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];

    private GZipStream? gzip;
    private bool disposed;

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => !disposed && inner.CanWrite;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    private GZipStream EnsureGzip() => gzip ??= new GZipStream(inner, level, leaveOpen: true);

    public override void Flush()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        gzip?.Flush();
    }

    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled(cancellationToken);

        return gzip is not null ? gzip.FlushAsync(cancellationToken) : Task.CompletedTask;
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count)
    {
        ValidateBufferArguments(buffer, offset, count);
        ObjectDisposedException.ThrowIf(disposed, this);
        if (count == 0)
            return;

        EnsureGzip().Write(buffer, offset, count);
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (buffer.IsEmpty)
            return;

        EnsureGzip().Write(buffer);
    }

    public override void WriteByte(byte value)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        EnsureGzip().WriteByte(value);
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ValidateBufferArguments(buffer, offset, count);
        ObjectDisposedException.ThrowIf(disposed, this);
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled(cancellationToken);
        if (count == 0)
            return Task.CompletedTask;

        return EnsureGzip().WriteAsync(buffer, offset, count, cancellationToken);
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (cancellationToken.IsCancellationRequested)
            return ValueTask.FromCanceled(cancellationToken);
        if (buffer.IsEmpty)
            return ValueTask.CompletedTask;

        return EnsureGzip().WriteAsync(buffer, cancellationToken);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !disposed)
        {
            disposed = true;
            try
            {
                if (gzip is not null)
                    gzip.Dispose();
                else
                    inner.Write(emptyGzipMember);
            }
            finally
            {
                inner.Dispose();
            }
        }

        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        if (disposed)
            return;

        disposed = true;
        try
        {
            if (gzip is not null)
                await gzip.DisposeAsync().ConfigureAwait(false);
            else
                await inner.WriteAsync(emptyGzipMember).ConfigureAwait(false);
        }
        finally
        {
            await inner.DisposeAsync().ConfigureAwait(false);
        }

        GC.SuppressFinalize(this);
    }
}
