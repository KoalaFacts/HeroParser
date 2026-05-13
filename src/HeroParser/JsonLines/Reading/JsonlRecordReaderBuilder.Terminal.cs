using System.Diagnostics.CodeAnalysis;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace HeroParser.JsonLines.Reading;

public sealed partial class JsonlRecordReaderBuilder<T>
{
    /// <summary>
    /// Reads records from a JSONL string.
    /// </summary>
    [RequiresUnreferencedCode("JSONL deserialization without a JsonTypeInfo<T> uses reflection. Call WithTypeInfo for AOT/trimming support.")]
    [RequiresDynamicCode("JSONL deserialization without a JsonTypeInfo<T> uses runtime code generation.")]
    public JsonlRecordReader<T> FromText(string jsonl)
    {
        ArgumentNullException.ThrowIfNull(jsonl);
        byte[] utf8 = Encoding.UTF8.GetBytes(jsonl);
        return CreateReader(new MemoryStream(utf8, writable: false), leaveOpen: false);
    }

    /// <summary>
    /// Reads records from a JSONL file.
    /// </summary>
    [RequiresUnreferencedCode("JSONL deserialization without a JsonTypeInfo<T> uses reflection. Call WithTypeInfo for AOT/trimming support.")]
    [RequiresDynamicCode("JSONL deserialization without a JsonTypeInfo<T> uses runtime code generation.")]
    public JsonlRecordReader<T> FromFile(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return CreateReader(stream, leaveOpen: false);
    }

    /// <summary>
    /// Reads records from a UTF-8 JSONL stream.
    /// </summary>
    [RequiresUnreferencedCode("JSONL deserialization without a JsonTypeInfo<T> uses reflection. Call WithTypeInfo for AOT/trimming support.")]
    [RequiresDynamicCode("JSONL deserialization without a JsonTypeInfo<T> uses runtime code generation.")]
    public JsonlRecordReader<T> FromStream(Stream stream, bool leaveOpen = true)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return CreateReader(stream, leaveOpen);
    }

    /// <summary>
    /// Asynchronously reads records from a JSONL file without loading the entire file into memory.
    /// </summary>
    [RequiresUnreferencedCode("JSONL deserialization without a JsonTypeInfo<T> uses reflection. Call WithTypeInfo for AOT/trimming support.")]
    [RequiresDynamicCode("JSONL deserialization without a JsonTypeInfo<T> uses runtime code generation.")]
    public async IAsyncEnumerable<T> FromFileAsync(
        string path,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(path);

        FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        PipeReader pipe = PipeReader.Create(stream);
        try
        {
            await foreach (T record in EnumeratePipeAsync(pipe, cancellationToken).ConfigureAwait(false))
                yield return record;
        }
        finally
        {
            await pipe.CompleteAsync().ConfigureAwait(false);
            await stream.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Asynchronously reads records from a stream without loading the entire stream into memory.
    /// </summary>
    [RequiresUnreferencedCode("JSONL deserialization without a JsonTypeInfo<T> uses reflection. Call WithTypeInfo for AOT/trimming support.")]
    [RequiresDynamicCode("JSONL deserialization without a JsonTypeInfo<T> uses runtime code generation.")]
    public async IAsyncEnumerable<T> FromStreamAsync(
        Stream stream,
        bool leaveOpen = true,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        PipeReader pipe = PipeReader.Create(stream, new StreamPipeReaderOptions(leaveOpen: leaveOpen));
        try
        {
            await foreach (T record in EnumeratePipeAsync(pipe, cancellationToken).ConfigureAwait(false))
                yield return record;
        }
        finally
        {
            await pipe.CompleteAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Asynchronously reads records from a <see cref="PipeReader"/>.
    /// </summary>
    [RequiresUnreferencedCode("JSONL deserialization without a JsonTypeInfo<T> uses reflection. Call WithTypeInfo for AOT/trimming support.")]
    [RequiresDynamicCode("JSONL deserialization without a JsonTypeInfo<T> uses runtime code generation.")]
    public IAsyncEnumerable<T> FromPipeReaderAsync(PipeReader reader, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return EnumeratePipeAsync(reader, cancellationToken);
    }

    private async IAsyncEnumerable<T> EnumeratePipeAsync(
        PipeReader reader,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        JsonlReadOptions options = BuildOptions();
        long recordsRead = 0;
        long recordIndex = 0;
        int skipped = 0;

        await foreach (JsonlLine line in JsonlPipeLineReader.ReadLinesAsync(reader, options, cancellationToken).ConfigureAwait(false))
        {
            ReadOnlySpan<byte> bytes = line.Utf8.Span;

            if (options.SkipEmptyLines && IsAllWhitespace(bytes))
                continue;

            if (skipped < options.SkipRows)
            {
                skipped++;
                continue;
            }

            if (recordsRead >= options.MaxRowCount)
            {
                throw new JsonlException(
                    JsonlErrorCode.TooManyRows,
                    $"Number of records exceeds the configured MaxRowCount of {options.MaxRowCount:N0}.",
                    line.LineNumber);
            }

            T? value;
            try
            {
                value = typeInfo is not null
                    ? JsonSerializer.Deserialize(bytes, typeInfo)
                    : DeserializeReflectionInstance(bytes, options.SerializerOptions);
            }
            catch (Exception ex) when (options.OnError is not null)
            {
                var ctx = new JsonlDeserializeErrorContext
                {
                    LineNumber = line.LineNumber,
                    RecordIndex = recordIndex,
                    RawLine = Encoding.UTF8.GetString(bytes),
                    TargetType = typeof(T)
                };

                JsonlDeserializeErrorAction action = options.OnError(ctx, ex);
                if (action == JsonlDeserializeErrorAction.Throw)
                {
                    throw new JsonlException(
                        JsonlErrorCode.DeserializeError,
                        $"Failed to deserialize line: {ex.Message}",
                        line.LineNumber,
                        ex);
                }
                recordIndex++;
                continue;
            }
            catch (Exception ex)
            {
                throw new JsonlException(
                    JsonlErrorCode.DeserializeError,
                    $"Failed to deserialize line: {ex.Message}",
                    line.LineNumber,
                    ex);
            }

            recordIndex++;
            recordsRead++;
            yield return value!;
        }
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Reflection path is gated by [RequiresUnreferencedCode] on the public async terminals; callers using WithTypeInfo never reach this branch.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Reflection path is gated by [RequiresDynamicCode] on the public async terminals; callers using WithTypeInfo never reach this branch.")]
    private static T? DeserializeReflectionInstance(ReadOnlySpan<byte> utf8, JsonSerializerOptions? options)
        => JsonSerializer.Deserialize<T>(utf8, options);

    private static bool IsAllWhitespace(ReadOnlySpan<byte> span)
    {
        for (int i = 0; i < span.Length; i++)
        {
            byte b = span[i];
            if (b is not ((byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n'))
                return false;
        }
        return true;
    }
}
