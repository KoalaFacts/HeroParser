namespace HeroParser;

public static partial class Excel
{
    /// <summary>
    /// Asynchronously reads rows from the first sheet of an Excel file and deserializes them as
    /// <typeparamref name="T"/> records, yielding each record as it is parsed.
    /// </summary>
    /// <typeparam name="T">The record type to deserialize rows into.</typeparam>
    /// <param name="path">The path to the .xlsx file.</param>
    /// <param name="cancellationToken">Token to cancel enumeration between rows.</param>
    /// <returns>An async sequence of deserialized records.</returns>
    public static IAsyncEnumerable<T> DeserializeRecordsAsync<T>(
        string path,
        CancellationToken cancellationToken = default) where T : new()
        => Read<T>().FromFileAsync(path, cancellationToken);

    /// <summary>
    /// Asynchronously reads rows from the first sheet of a stream containing .xlsx data and
    /// deserializes them as <typeparamref name="T"/> records, yielding each record as it is parsed.
    /// </summary>
    /// <typeparam name="T">The record type to deserialize rows into.</typeparam>
    /// <param name="stream">A seekable stream containing .xlsx data.</param>
    /// <param name="cancellationToken">Token to cancel enumeration between rows.</param>
    /// <returns>An async sequence of deserialized records.</returns>
    public static IAsyncEnumerable<T> DeserializeRecordsAsync<T>(
        Stream stream,
        CancellationToken cancellationToken = default) where T : new()
        => Read<T>().FromStreamAsync(stream, cancellationToken);
}
