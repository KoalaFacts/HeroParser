using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace HeroParser.JsonLines.Reading;

/// <summary>
/// Enumerates strongly-typed records deserialized from a JSONL stream. Each line is parsed independently.
/// </summary>
/// <typeparam name="T">The record type to deserialize.</typeparam>
public sealed class JsonlRecordReader<T> : IEnumerable<T>, IDisposable
{
    private readonly JsonlLineReader lineReader;
    private readonly JsonlReadOptions options;
    private readonly JsonTypeInfo<T>? typeInfo;
    private readonly Binders.IJsonlBinder<T>? binder;
    private bool enumerated;
    private bool disposed;

    /// <summary>
    /// Initializes a new reader using a <see cref="JsonTypeInfo{T}"/> for AOT-safe deserialization.
    /// </summary>
    public JsonlRecordReader(Stream stream, JsonTypeInfo<T> typeInfo, JsonlReadOptions? options = null, bool leaveOpen = true)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(typeInfo);
        this.options = options ?? JsonlReadOptions.Default;
        this.options.Validate();
        this.typeInfo = typeInfo;
        Binders.JsonlRecordBinderFactory.TryGetBinder<T>(out binder);
        lineReader = new JsonlLineReader(stream, this.options, leaveOpen);
    }

    /// <summary>
    /// Initializes a new reader using reflection-based <see cref="System.Text.Json"/> deserialization.
    /// Pass a <see cref="JsonTypeInfo{T}"/> for AOT/trimming support.
    /// </summary>
    [RequiresUnreferencedCode("JSON deserialization without JsonTypeInfo<T> uses reflection. Pass a JsonTypeInfo<T> from a JsonSerializerContext for AOT/trimming support.")]
    [RequiresDynamicCode("JSON deserialization without JsonTypeInfo<T> uses runtime code generation.")]
    public JsonlRecordReader(Stream stream, JsonlReadOptions? options = null, bool leaveOpen = true)
    {
        ArgumentNullException.ThrowIfNull(stream);
        this.options = options ?? JsonlReadOptions.Default;
        this.options.Validate();
        typeInfo = null;
        Binders.JsonlRecordBinderFactory.TryGetBinder<T>(out binder);
        lineReader = new JsonlLineReader(stream, this.options, leaveOpen);
    }

    /// <inheritdoc/>
    public IEnumerator<T> GetEnumerator()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (enumerated)
        {
            throw new InvalidOperationException(
                "A JsonlRecordReader<T> can only be enumerated once. Materialize results to a list if multiple passes are needed.");
        }
        enumerated = true;
        return EnumerateCore();
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private IEnumerator<T> EnumerateCore()
    {
        long recordsRead = 0;
        long recordIndex = 0;
        int skipped = 0;

        while (lineReader.TryReadLine(out ReadOnlySpan<byte> line, out long lineNumber))
        {
            if (options.SkipEmptyLines && IsWhitespace(line))
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
                    lineNumber);
            }

            T? value;
            try
            {
                value = Deserialize(line);
            }
            catch (Exception ex) when (options.OnError is not null)
            {
                var context = new JsonlDeserializeErrorContext
                {
                    LineNumber = lineNumber,
                    RecordIndex = recordIndex,
                    RawLine = System.Text.Encoding.UTF8.GetString(line),
                    TargetType = typeof(T)
                };

                JsonlDeserializeErrorAction action = options.OnError(context, ex);
                if (action == JsonlDeserializeErrorAction.Throw)
                {
                    throw new JsonlException(
                        JsonlErrorCode.DeserializeError,
                        $"Failed to deserialize line: {ex.Message}",
                        lineNumber,
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
                    lineNumber,
                    ex);
            }

            recordIndex++;
            recordsRead++;

            if (options.Progress is not null && (recordsRead % options.ProgressIntervalRows) == 0)
            {
                options.Progress.Report(new JsonlProgress
                {
                    LineNumber = lineNumber,
                    BytesRead = lineReader.BytesRead,
                    RecordsRead = recordsRead
                });
            }

            yield return value!;
        }
    }

    private T? Deserialize(ReadOnlySpan<byte> utf8)
    {
        if (binder is not null)
            return binder.Bind(utf8);

        if (typeInfo is not null)
            return JsonSerializer.Deserialize(utf8, typeInfo);

        return DeserializeReflection(utf8, options.SerializerOptions);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Reflection path is gated by the [RequiresUnreferencedCode] reflection constructor; callers using JsonTypeInfo<T> never reach this branch.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Reflection path is gated by the [RequiresDynamicCode] reflection constructor; callers using JsonTypeInfo<T> never reach this branch.")]
    private static T? DeserializeReflection(ReadOnlySpan<byte> utf8, JsonSerializerOptions? options)
        => JsonSerializer.Deserialize<T>(utf8, options);

    private static bool IsWhitespace(ReadOnlySpan<byte> span)
    {
        for (int i = 0; i < span.Length; i++)
        {
            byte b = span[i];
            if (b is not ((byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n'))
                return false;
        }
        return true;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        lineReader.Dispose();
    }
}
