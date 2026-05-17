using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace HeroParser.JsonLines.Writing;

/// <summary>
/// Fluent builder for configuring and executing JSONL writing operations.
/// </summary>
/// <typeparam name="T">The record type to serialize.</typeparam>
public sealed class JsonlWriterBuilder<T>
{
    private JsonSerializerOptions? serializerOptions;
    private JsonTypeInfo<T>? typeInfo;
    private string newLine = "\n";
    private Encoding? encoding;
    private int? maxRowCount;
    private long? maxOutputSize;
    private bool writeFinalNewline;
    private JsonlSerializeErrorHandler? onError;

    internal JsonlWriterBuilder() { }

    /// <summary>Sets serializer options used when no <see cref="JsonTypeInfo{T}"/> is configured.</summary>
    public JsonlWriterBuilder<T> WithJsonOptions(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        serializerOptions = options;
        return this;
    }

    /// <summary>Sets the <see cref="JsonTypeInfo{T}"/> used for AOT-safe serialization.</summary>
    public JsonlWriterBuilder<T> WithTypeInfo(JsonTypeInfo<T> typeInfo)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        this.typeInfo = typeInfo;
        return this;
    }

    /// <summary>Sets the line separator (default <c>"\n"</c>).</summary>
    public JsonlWriterBuilder<T> WithNewLine(string newLine)
    {
        if (string.IsNullOrEmpty(newLine))
            throw new ArgumentException("NewLine must not be empty.", nameof(newLine));
        this.newLine = newLine;
        return this;
    }

    /// <summary>Sets the output encoding (default UTF-8 without BOM).</summary>
    public JsonlWriterBuilder<T> WithEncoding(Encoding encoding)
    {
        ArgumentNullException.ThrowIfNull(encoding);
        this.encoding = encoding;
        return this;
    }

    /// <summary>Caps the number of records that may be written.</summary>
    public JsonlWriterBuilder<T> WithMaxRowCount(int? rows)
    {
        maxRowCount = rows;
        return this;
    }

    /// <summary>Caps the maximum output byte count.</summary>
    public JsonlWriterBuilder<T> WithMaxOutputSize(long? bytes)
    {
        maxOutputSize = bytes;
        return this;
    }

    /// <summary>Whether to emit a final newline after the last record (default <see langword="false"/>).</summary>
    public JsonlWriterBuilder<T> WithFinalNewline(bool value = true)
    {
        writeFinalNewline = value;
        return this;
    }

    /// <summary>Registers a per-record serialization error handler.</summary>
    public JsonlWriterBuilder<T> OnError(JsonlSerializeErrorHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        onError = handler;
        return this;
    }

    private JsonlWriteOptions BuildOptions() => new()
    {
        SerializerOptions = serializerOptions,
        NewLine = newLine,
        Encoding = encoding,
        MaxRowCount = maxRowCount,
        MaxOutputSize = maxOutputSize,
        WriteFinalNewline = writeFinalNewline,
        OnError = onError
    };

    /// <summary>Serializes records to an in-memory UTF-8 string.</summary>
    [RequiresUnreferencedCode("JSONL serialization without WithTypeInfo uses reflection. Call WithTypeInfo for AOT/trimming support.")]
    [RequiresDynamicCode("JSONL serialization without WithTypeInfo uses runtime code generation.")]
    public string ToText(IEnumerable<T> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        using var stream = new MemoryStream();
        WriteToStreamInternal(stream, records, leaveOpen: true);
        return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
    }

    /// <summary>Serializes records to a file.</summary>
    [RequiresUnreferencedCode("JSONL serialization without WithTypeInfo uses reflection. Call WithTypeInfo for AOT/trimming support.")]
    [RequiresDynamicCode("JSONL serialization without WithTypeInfo uses runtime code generation.")]
    public void ToFile(string path, IEnumerable<T> records)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(records);
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        WriteToStreamInternal(stream, records, leaveOpen: false);
    }

    /// <summary>Serializes records to an existing stream.</summary>
    [RequiresUnreferencedCode("JSONL serialization without WithTypeInfo uses reflection. Call WithTypeInfo for AOT/trimming support.")]
    [RequiresDynamicCode("JSONL serialization without WithTypeInfo uses runtime code generation.")]
    public void ToStream(Stream stream, IEnumerable<T> records, bool leaveOpen = true)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(records);
        WriteToStreamInternal(stream, records, leaveOpen);
    }

    /// <summary>Asynchronously serializes records to a file.</summary>
    [RequiresUnreferencedCode("JSONL serialization without WithTypeInfo uses reflection. Call WithTypeInfo for AOT/trimming support.")]
    [RequiresDynamicCode("JSONL serialization without WithTypeInfo uses runtime code generation.")]
    public async ValueTask ToFileAsync(string path, IAsyncEnumerable<T> records, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(records);
        var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 4096, FileOptions.Asynchronous);
        await using var streamDisposal = stream.ConfigureAwait(false);
        await WriteToStreamInternalAsync(stream, records, leaveOpen: false, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Asynchronously serializes records to an existing stream.</summary>
    [RequiresUnreferencedCode("JSONL serialization without WithTypeInfo uses reflection. Call WithTypeInfo for AOT/trimming support.")]
    [RequiresDynamicCode("JSONL serialization without WithTypeInfo uses runtime code generation.")]
    public ValueTask ToStreamAsync(Stream stream, IAsyncEnumerable<T> records, bool leaveOpen = true, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(records);
        return WriteToStreamInternalAsync(stream, records, leaveOpen, cancellationToken);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Reflection path is gated by [RequiresUnreferencedCode] on the public terminals; callers using WithTypeInfo never reach this branch.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Reflection path is gated by [RequiresDynamicCode] on the public terminals; callers using WithTypeInfo never reach this branch.")]
    private void WriteToStreamInternal(Stream stream, IEnumerable<T> records, bool leaveOpen)
    {
        using var writer = new JsonlStreamWriter(stream, BuildOptions(), leaveOpen);
        foreach (T record in records)
        {
            if (typeInfo is not null)
                writer.WriteRecord(record, typeInfo);
            else
                writer.WriteRecord(record);
        }
        writer.Flush();
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Reflection path is gated by [RequiresUnreferencedCode] on the public terminals; callers using WithTypeInfo never reach this branch.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Reflection path is gated by [RequiresDynamicCode] on the public terminals; callers using WithTypeInfo never reach this branch.")]
    private async ValueTask WriteToStreamInternalAsync(Stream stream, IAsyncEnumerable<T> records, bool leaveOpen, CancellationToken cancellationToken)
    {
        var writer = new JsonlStreamWriter(stream, BuildOptions(), leaveOpen);
        await using var writerDisposal = writer.ConfigureAwait(false);
        await foreach (T record in records.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (typeInfo is not null)
                await writer.WriteRecordAsync(record, typeInfo, cancellationToken).ConfigureAwait(false);
            else
                await writer.WriteRecordAsync(record, cancellationToken).ConfigureAwait(false);
        }
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
