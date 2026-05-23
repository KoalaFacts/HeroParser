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
    /// Creates a <see cref="JsonlDataReader"/> from a JSONL file path.
    /// </summary>
    public static JsonlDataReader CreateDataReader(
        string path,
        JsonlReadOptions? options = null,
        JsonlDataReaderOptions? readerOptions = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return new JsonlDataReader(stream, options, readerOptions, leaveOpen: false);
    }
}
