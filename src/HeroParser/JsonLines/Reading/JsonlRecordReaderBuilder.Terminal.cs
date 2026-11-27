using System.Diagnostics.CodeAnalysis;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Globalization;
using HeroParser.Internal;

namespace HeroParser.JsonLines.Reading;

public sealed partial class JsonlRecordReaderBuilder<T>
{
    /// <summary>
    /// Reads records from a JSONL string using a <see cref="JsonTypeInfo{T}"/> for AOT-safe deserialization.
    /// </summary>
    public JsonlRecordReader<T> FromText(string jsonl, JsonTypeInfo<T> typeInfo)
    {
        ArgumentNullException.ThrowIfNull(jsonl);
        ArgumentNullException.ThrowIfNull(typeInfo);
        this.typeInfo = typeInfo;
        byte[] utf8 = Encoding.UTF8.GetBytes(jsonl);
        return CreateReader(new MemoryStream(utf8, writable: false), leaveOpen: false);
    }

    /// <summary>
    /// Reads records from a JSONL file using a <see cref="JsonTypeInfo{T}"/> for AOT-safe deserialization.
    /// </summary>
    public JsonlRecordReader<T> FromFile(string path, JsonTypeInfo<T> typeInfo)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(typeInfo);
        this.typeInfo = typeInfo;
        return CreateFileReader(path);
    }

    /// <summary>
    /// Reads records from a UTF-8 JSONL stream using a <see cref="JsonTypeInfo{T}"/> for AOT-safe deserialization.
    /// </summary>
    public JsonlRecordReader<T> FromStream(Stream stream, JsonTypeInfo<T> typeInfo, bool leaveOpen = true)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(typeInfo);
        this.typeInfo = typeInfo;
        return CreateReader(stream, leaveOpen);
    }

    /// <summary>
    /// Asynchronously reads records from a JSONL file without loading the entire file into memory using a <see cref="JsonTypeInfo{T}"/> for AOT-safe deserialization.
    /// </summary>
    public async IAsyncEnumerable<T> FromFileAsync(
        string path,
        JsonTypeInfo<T> typeInfo,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(typeInfo);
        this.typeInfo = typeInfo;

        Stream stream = new CompressedFile(path).OpenRead(
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var streamDisposal = stream.ConfigureAwait(false);

        PipeReader pipe = PipeReader.Create(stream);
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
    /// Asynchronously reads records from a stream without loading the entire stream into memory using a <see cref="JsonTypeInfo{T}"/> for AOT-safe deserialization.
    /// </summary>
    public async IAsyncEnumerable<T> FromStreamAsync(
        Stream stream,
        JsonTypeInfo<T> typeInfo,
        bool leaveOpen = true,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(typeInfo);
        this.typeInfo = typeInfo;

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
    /// Asynchronously reads records from a <see cref="PipeReader"/> using a <see cref="JsonTypeInfo{T}"/> for AOT-safe deserialization.
    /// </summary>
    public IAsyncEnumerable<T> FromPipeReaderAsync(PipeReader reader, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(typeInfo);
        this.typeInfo = typeInfo;
        return EnumeratePipeAsync(reader, cancellationToken);
    }

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
        return CreateFileReader(path);
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

        Stream stream = new CompressedFile(path).OpenRead(
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var streamDisposal = stream.ConfigureAwait(false);

        PipeReader pipe = PipeReader.Create(stream);
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

        Binders.IJsonlSourceBinder<byte, T>? binder = null;
        if (typeInfo is null && options.SerializerOptions is null)
            Binders.JsonlRecordBinderFactory.TryGetByteBinder<T>(out binder);
        Binders.JsonlRecordBinderFactory.TryGetValidator<T>(out var validator);
        JsonSerializerOptions? effectiveSerializerOptions = typeInfo is null && (binder is null || binder is Binders.IJsonlTryByteBinder<T>)
            ? BigIntegerJsonConverter.ResolveOptions(options.SerializerOptions)
            : null;

        await foreach (JsonlLine line in new JsonlPipeLineReader(reader, options).ReadLinesAsync(cancellationToken).ConfigureAwait(false))
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
                    string.Create(CultureInfo.InvariantCulture, $"Number of records exceeds the configured MaxRowCount of {options.MaxRowCount:N0}."),
                    line.LineNumber);
            }

            T? value;
            try
            {
                if (binder is not null && default(T) is null && new JsonlValidationHelper().IsJsonNullLiteral(bytes))
                {
                    value = default;
                }
                else if (binder is Binders.IJsonlTryByteBinder<T> tryBinder && tryBinder.TryBind(bytes, out var bound))
                {
                    value = bound;
                }
                else if (binder is not null && binder is not Binders.IJsonlTryByteBinder<T>)
                {
                    value = binder.Bind(bytes);
                }
                else
                {
                    value = typeInfo is not null
                        ? JsonSerializer.Deserialize(bytes, typeInfo)
                        : DeserializeReflectionInstance(bytes, effectiveSerializerOptions);
                    if (value is not null)
                    {
                        JsonTypeInfo? activeTypeInfo = typeInfo ?? TryGetReflectionTypeInfo(effectiveSerializerOptions);
                        JsonlReadContractValidator.Instance.ValidateDeserialized(
                            value,
                            bytes,
                            activeTypeInfo,
                            validator,
                            (int)Math.Min(line.LineNumber, int.MaxValue),
                            options.ValidationMode);
                    }
                }
            }
            catch (Exception ex) when (options.OnError is not null && ex is not (OutOfMemoryException or OperationCanceledException))
            {
                Exception reportedException = ex is Validation.ValidationException vex
                    ? new JsonlValidationHelper().AttachLineNumber(vex, line.LineNumber)
                    : ex;

                var ctx = new JsonlDeserializeErrorContext
                {
                    LineNumber = line.LineNumber,
                    RecordIndex = recordIndex,
                    RawLine = Encoding.UTF8.GetString(bytes),
                    TargetType = typeof(T)
                };

                JsonlDeserializeErrorAction action = options.OnError(ctx, reportedException);
                if (action == JsonlDeserializeErrorAction.Throw)
                {
                    throw new JsonlException(
                        JsonlErrorCode.DeserializeError,
                        $"Failed to deserialize line: {reportedException.Message}",
                        line.LineNumber,
                        reportedException);
                }
                recordIndex++;
                continue;
            }
            catch (Validation.ValidationException vex)
            {
                if (options.ValidationMode == Validation.ValidationMode.Lenient)
                {
                    recordIndex++;
                    continue;
                }

                throw new JsonlValidationHelper().AttachLineNumber(vex, line.LineNumber);
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException or JsonlException))
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

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Reflection path is gated by [RequiresUnreferencedCode] on the public async terminals; callers using WithTypeInfo never reach this branch.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Reflection path is gated by [RequiresDynamicCode] on the public async terminals; callers using WithTypeInfo never reach this branch.")]
    private static JsonTypeInfo? TryGetReflectionTypeInfo(JsonSerializerOptions? options)
        => options is null ? null : JsonlReadContractValidator.TryGetTypeInfo<T>(options);

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
