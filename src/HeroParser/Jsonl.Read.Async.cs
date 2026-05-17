using System.Diagnostics.CodeAnalysis;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using HeroParser.JsonLines;
using HeroParser.JsonLines.Reading;

namespace HeroParser;

public static partial class Jsonl
{
    /// <summary>
    /// Asynchronously deserializes records from a JSONL stream using reflection.
    /// </summary>
    [RequiresUnreferencedCode("JSONL deserialization without a JsonTypeInfo<T> uses reflection. Use the JsonTypeInfo<T> overload for AOT.")]
    [RequiresDynamicCode("JSONL deserialization without a JsonTypeInfo<T> uses runtime code generation.")]
    public static IAsyncEnumerable<T> DeserializeRecordsAsync<T>(
        Stream stream,
        JsonlReadOptions? options = null,
        bool leaveOpen = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        PipeReader pipe = PipeReader.Create(stream, new StreamPipeReaderOptions(leaveOpen: leaveOpen));
        return EnumeratePipeReflectionAsync<T>(pipe, options, completePipe: true, cancellationToken);
    }

    /// <summary>
    /// Asynchronously deserializes records from a JSONL stream using a <see cref="JsonTypeInfo{T}"/>.
    /// </summary>
    public static IAsyncEnumerable<T> DeserializeRecordsAsync<T>(
        Stream stream,
        JsonTypeInfo<T> typeInfo,
        JsonlReadOptions? options = null,
        bool leaveOpen = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(typeInfo);
        PipeReader pipe = PipeReader.Create(stream, new StreamPipeReaderOptions(leaveOpen: leaveOpen));
        return EnumeratePipeTypedAsync(pipe, typeInfo, options, completePipe: true, cancellationToken);
    }

    /// <summary>
    /// Asynchronously deserializes records from a <see cref="PipeReader"/> using reflection.
    /// </summary>
    [RequiresUnreferencedCode("JSONL deserialization without a JsonTypeInfo<T> uses reflection. Use the JsonTypeInfo<T> overload for AOT.")]
    [RequiresDynamicCode("JSONL deserialization without a JsonTypeInfo<T> uses runtime code generation.")]
    public static IAsyncEnumerable<T> DeserializeRecordsAsync<T>(
        PipeReader reader,
        JsonlReadOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return EnumeratePipeReflectionAsync<T>(reader, options, completePipe: false, cancellationToken);
    }

    /// <summary>
    /// Asynchronously deserializes records from a <see cref="PipeReader"/> using a <see cref="JsonTypeInfo{T}"/>.
    /// </summary>
    public static IAsyncEnumerable<T> DeserializeRecordsAsync<T>(
        PipeReader reader,
        JsonTypeInfo<T> typeInfo,
        JsonlReadOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(typeInfo);
        return EnumeratePipeTypedAsync(reader, typeInfo, options, completePipe: false, cancellationToken);
    }

    /// <summary>
    /// Asynchronously enumerates raw JSONL lines (with line numbers) from a <see cref="PipeReader"/>.
    /// </summary>
    public static IAsyncEnumerable<JsonlLine> ReadLinesAsync(
        PipeReader reader,
        JsonlReadOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return JsonlPipeLineReader.ReadLinesAsync(reader, options ?? JsonlReadOptions.Default, cancellationToken);
    }

    [RequiresUnreferencedCode("JSONL deserialization without a JsonTypeInfo<T> uses reflection. Use the JsonTypeInfo<T> overload for AOT.")]
    [RequiresDynamicCode("JSONL deserialization without a JsonTypeInfo<T> uses runtime code generation.")]
    private static async IAsyncEnumerable<T> EnumeratePipeReflectionAsync<T>(
        PipeReader reader,
        JsonlReadOptions? options,
        bool completePipe,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        JsonlReadOptions effective = options ?? JsonlReadOptions.Default;
        effective.Validate();
        long recordsRead = 0;
        long recordIndex = 0;
        int skipped = 0;

        try
        {
            await foreach (JsonlLine line in JsonlPipeLineReader.ReadLinesAsync(reader, effective, cancellationToken).ConfigureAwait(false))
            {
                if (!TryAdvance(in line, effective, ref skipped, ref recordsRead, out bool yieldNow))
                    continue;
                if (!yieldNow) continue;

                T? value;
                try
                {
                    value = JsonSerializer.Deserialize<T>(line.Utf8.Span, effective.SerializerOptions);
                }
                catch (Exception ex)
                {
                    if (effective.OnError is not null)
                    {
                        var ctx = BuildErrorContext<T>(in line, recordIndex);
                        if (effective.OnError(ctx, ex) == JsonlDeserializeErrorAction.SkipRecord)
                        {
                            recordIndex++;
                            continue;
                        }
                    }
                    throw new JsonlException(JsonlErrorCode.DeserializeError, $"Failed to deserialize line: {ex.Message}", line.LineNumber, ex);
                }

                recordIndex++;
                recordsRead++;
                yield return value!;
            }
        }
        finally
        {
            if (completePipe)
                await reader.CompleteAsync().ConfigureAwait(false);
        }
    }

    private static async IAsyncEnumerable<T> EnumeratePipeTypedAsync<T>(
        PipeReader reader,
        JsonTypeInfo<T> typeInfo,
        JsonlReadOptions? options,
        bool completePipe,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        JsonlReadOptions effective = options ?? JsonlReadOptions.Default;
        effective.Validate();
        long recordsRead = 0;
        long recordIndex = 0;
        int skipped = 0;

        try
        {
            await foreach (JsonlLine line in JsonlPipeLineReader.ReadLinesAsync(reader, effective, cancellationToken).ConfigureAwait(false))
            {
                if (!TryAdvance(in line, effective, ref skipped, ref recordsRead, out bool yieldNow))
                    continue;
                if (!yieldNow) continue;

                T? value;
                try
                {
                    value = JsonSerializer.Deserialize(line.Utf8.Span, typeInfo);
                }
                catch (Exception ex)
                {
                    if (effective.OnError is not null)
                    {
                        var ctx = BuildErrorContext<T>(in line, recordIndex);
                        if (effective.OnError(ctx, ex) == JsonlDeserializeErrorAction.SkipRecord)
                        {
                            recordIndex++;
                            continue;
                        }
                    }
                    throw new JsonlException(JsonlErrorCode.DeserializeError, $"Failed to deserialize line: {ex.Message}", line.LineNumber, ex);
                }

                recordIndex++;
                recordsRead++;
                yield return value!;
            }
        }
        finally
        {
            if (completePipe)
                await reader.CompleteAsync().ConfigureAwait(false);
        }
    }

    private static bool TryAdvance(in JsonlLine line, JsonlReadOptions options, ref int skipped, ref long recordsRead, out bool yieldNow)
    {
        yieldNow = false;
        ReadOnlySpan<byte> span = line.Utf8.Span;

        if (options.SkipEmptyLines && IsAllWhitespace(span))
            return false;

        if (skipped < options.SkipRows)
        {
            skipped++;
            return false;
        }

        if (recordsRead >= options.MaxRowCount)
        {
            throw new JsonlException(
                JsonlErrorCode.TooManyRows,
                $"Number of records exceeds the configured MaxRowCount of {options.MaxRowCount:N0}.",
                line.LineNumber);
        }

        yieldNow = true;
        return true;
    }

    private static JsonlDeserializeErrorContext BuildErrorContext<T>(in JsonlLine line, long recordIndex)
        => new()
        {
            LineNumber = line.LineNumber,
            RecordIndex = recordIndex,
            RawLine = System.Text.Encoding.UTF8.GetString(line.Utf8.Span),
            TargetType = typeof(T)
        };

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
