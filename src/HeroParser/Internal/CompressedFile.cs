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
                FileCompressionFormat.GZip => new GZipStream(fileStream, level, leaveOpen: false),
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
