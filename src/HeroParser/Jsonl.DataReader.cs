using HeroParser.Internal;
using HeroParser.JsonLines.Reading;
using HeroParser.JsonLines.Reading.Data;

namespace HeroParser;

public static partial class Jsonl
{
    /// <summary>
    /// Creates a <see cref="JsonlDataReader"/> over a JSONL stream — useful for bulk loading via <c>SqlBulkCopy</c>.
    /// </summary>
    public static JsonlDataReader CreateDataReader(
        Stream stream,
        JsonlReadOptions? options = null,
        JsonlDataReaderOptions? readerOptions = null,
        bool leaveOpen = true)
        => new(stream, options, readerOptions, leaveOpen);

    /// <summary>
    /// Creates a <see cref="JsonlDataReader"/> from a JSONL file path (supports transparent <c>.gz</c> decompression).
    /// </summary>
    public static JsonlDataReader CreateDataReader(
        string path,
        JsonlReadOptions? options = null,
        JsonlDataReaderOptions? readerOptions = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        Stream stream = new CompressedFile(path).OpenRead(bufferSize: 4096, FileOptions.SequentialScan);
        try
        {
            return new JsonlDataReader(stream, options, readerOptions, leaveOpen: false);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }
}
