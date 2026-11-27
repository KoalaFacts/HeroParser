using HeroParser.Htbs;
using HeroParser.Htbs.Reading.Data;
using HeroParser.Internal;

namespace HeroParser;

public static partial class Htb
{
    /// <summary>
    /// Creates an <see cref="HtbDataReader"/> over an HTB stream without reflection.
    /// </summary>
    /// <param name="stream">Readable stream containing HTB data.</param>
    /// <param name="options">Optional HTB read options; defaults to <see cref="HtbReadOptions.Default"/>.</param>
    /// <param name="leaveOpen">When <see langword="true"/> (default), leaves <paramref name="stream"/> open when the reader is disposed.</param>
    /// <returns>An <see cref="HtbDataReader"/> instance.</returns>
    public static HtbDataReader CreateDataReader(
        Stream stream,
        HtbReadOptions? options = null,
        bool leaveOpen = true)
        => new(stream, options, leaveOpen);

    /// <summary>
    /// Creates an <see cref="HtbDataReader"/> over an HTB file (including transparent <c>.gz</c>, <c>.br</c>, and <c>.zst</c>/<c>.zstd</c> decompression) without reflection.
    /// </summary>
    /// <param name="path">Filesystem path to the HTB file.</param>
    /// <param name="options">Optional HTB read options; defaults to <see cref="HtbReadOptions.Default"/>.</param>
    /// <returns>An <see cref="HtbDataReader"/> that owns the underlying file stream.</returns>
    public static HtbDataReader CreateDataReader(
        string path,
        HtbReadOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        var stream = new CompressedFile(path).OpenRead(bufferSize: 4096, FileOptions.SequentialScan);
        try
        {
            return new HtbDataReader(stream, options, leaveOpen: false);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }
}
