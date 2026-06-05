using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json.Serialization.Metadata;
using HeroParser.JsonLines.Writing;

namespace HeroParser;

public static partial class Jsonl
{
    /// <summary>
    /// Creates a fluent writer builder for the specified record type.
    /// </summary>
    public static JsonlWriterBuilder<T> Write<T>() => new();

    /// <summary>
    /// Serializes records to a JSONL string using reflection.
    /// </summary>
    [RequiresUnreferencedCode("JSONL serialization without a JsonTypeInfo<T> uses reflection. Use the JsonTypeInfo<T> overload for AOT.")]
    [RequiresDynamicCode("JSONL serialization without a JsonTypeInfo<T> uses runtime code generation.")]
    public static string WriteToText<T>(IEnumerable<T> records, JsonlWriteOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(records);

        int capacity = 4096;
        if (records is System.Collections.ICollection collection)
        {
            capacity = collection.Count * 128;
        }
        else if (records is IReadOnlyCollection<T> readOnlyCollection)
        {
            capacity = readOnlyCollection.Count * 128;
        }

        using var stream = new MemoryStream(capacity);
        using (var writer = new JsonlStreamWriter(stream, options, leaveOpen: true))
        {
            foreach (T record in records)
                writer.WriteRecord(record);
            writer.Flush();
        }
        return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
    }

    /// <summary>
    /// Serializes records to a JSONL string using a <see cref="JsonTypeInfo{T}"/>.
    /// </summary>
    public static string WriteToText<T>(IEnumerable<T> records, JsonTypeInfo<T> typeInfo, JsonlWriteOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(typeInfo);

        int capacity = 4096;
        if (records is System.Collections.ICollection collection)
        {
            capacity = collection.Count * 128;
        }
        else if (records is IReadOnlyCollection<T> readOnlyCollection)
        {
            capacity = readOnlyCollection.Count * 128;
        }

        using var stream = new MemoryStream(capacity);
        using (var writer = new JsonlStreamWriter(stream, options, leaveOpen: true))
        {
            foreach (T record in records)
                writer.WriteRecord(record, typeInfo);
            writer.Flush();
        }
        return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
    }

    /// <summary>
    /// Alias for <see cref="WriteToText{T}(IEnumerable{T}, JsonlWriteOptions?)"/>.
    /// </summary>
    [RequiresUnreferencedCode("JSONL serialization without a JsonTypeInfo<T> uses reflection. Use the JsonTypeInfo<T> overload for AOT.")]
    [RequiresDynamicCode("JSONL serialization without a JsonTypeInfo<T> uses runtime code generation.")]
    public static string SerializeRecords<T>(IEnumerable<T> records, JsonlWriteOptions? options = null)
        => WriteToText(records, options);

    /// <summary>
    /// Serializes records to a file using reflection.
    /// </summary>
    [RequiresUnreferencedCode("JSONL serialization without a JsonTypeInfo<T> uses reflection. Use the JsonTypeInfo<T> overload for AOT.")]
    [RequiresDynamicCode("JSONL serialization without a JsonTypeInfo<T> uses runtime code generation.")]
    public static void WriteToFile<T>(string path, IEnumerable<T> records, JsonlWriteOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(records);
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new JsonlStreamWriter(stream, options, leaveOpen: false);
        foreach (T record in records)
            writer.WriteRecord(record);
        writer.Flush();
    }

    /// <summary>
    /// Serializes records to a stream using reflection.
    /// </summary>
    [RequiresUnreferencedCode("JSONL serialization without a JsonTypeInfo<T> uses reflection. Use the JsonTypeInfo<T> overload for AOT.")]
    [RequiresDynamicCode("JSONL serialization without a JsonTypeInfo<T> uses runtime code generation.")]
    public static void WriteToStream<T>(Stream stream, IEnumerable<T> records, JsonlWriteOptions? options = null, bool leaveOpen = true)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(records);
        using var writer = new JsonlStreamWriter(stream, options, leaveOpen);
        foreach (T record in records)
            writer.WriteRecord(record);
        writer.Flush();
    }

    /// <summary>
    /// Asynchronously serializes records to a file using reflection.
    /// </summary>
    [RequiresUnreferencedCode("JSONL serialization without a JsonTypeInfo<T> uses reflection. Use the JsonTypeInfo<T> overload for AOT.")]
    [RequiresDynamicCode("JSONL serialization without a JsonTypeInfo<T> uses runtime code generation.")]
    public static async ValueTask WriteToFileAsync<T>(
        string path,
        IAsyncEnumerable<T> records,
        JsonlWriteOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(records);
        var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 4096, FileOptions.Asynchronous);
        await using var streamDisposal = stream.ConfigureAwait(false);
        var writer = new JsonlStreamWriter(stream, options, leaveOpen: false);
        await using var writerDisposal = writer.ConfigureAwait(false);
        await foreach (T record in records.WithCancellation(cancellationToken).ConfigureAwait(false))
            await writer.WriteRecordAsync(record, cancellationToken).ConfigureAwait(false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Asynchronously serializes records to a stream using reflection.
    /// </summary>
    [RequiresUnreferencedCode("JSONL serialization without a JsonTypeInfo<T> uses reflection. Use the JsonTypeInfo<T> overload for AOT.")]
    [RequiresDynamicCode("JSONL serialization without a JsonTypeInfo<T> uses runtime code generation.")]
    public static async ValueTask WriteToStreamAsync<T>(
        Stream stream,
        IAsyncEnumerable<T> records,
        JsonlWriteOptions? options = null,
        bool leaveOpen = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(records);
        var writer = new JsonlStreamWriter(stream, options, leaveOpen);
        await using var writerDisposal = writer.ConfigureAwait(false);
        await foreach (T record in records.WithCancellation(cancellationToken).ConfigureAwait(false))
            await writer.WriteRecordAsync(record, cancellationToken).ConfigureAwait(false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
