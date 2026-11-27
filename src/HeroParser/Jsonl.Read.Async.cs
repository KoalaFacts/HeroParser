using System.Diagnostics.CodeAnalysis;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using HeroParser.JsonLines;
using HeroParser.JsonLines.Reading;
using System.Globalization;

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
        return new JsonlPipeLineReader(reader, options ?? JsonlReadOptions.Default).ReadLinesAsync(cancellationToken);
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
        JsonSerializerOptions serializerOptions = JsonLines.BigIntegerJsonConverter.ResolveOptions(effective.SerializerOptions);
        JsonLines.Reading.Binders.IJsonlSourceBinder<byte, T>? binder = null;
        if (effective.SerializerOptions is null)
            JsonLines.Reading.Binders.JsonlRecordBinderFactory.TryGetByteBinder<T>(out binder);
        JsonLines.Reading.Binders.JsonlRecordBinderFactory.TryGetValidator<T>(out var validator);
        long recordsRead = 0;
        long recordIndex = 0;
        int skipped = 0;

        try
        {
            await foreach (JsonlLine line in new JsonlPipeLineReader(reader, effective).ReadLinesAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!TryAdvance(in line, effective, ref skipped, ref recordsRead, out bool yieldNow))
                    continue;
                if (!yieldNow) continue;

                T? value;
                try
                {
                    ReadOnlySpan<byte> span = line.Utf8.Span;
                    if (binder is not null && default(T) is null && new JsonLines.Reading.JsonlValidationHelper().IsJsonNullLiteral(span))
                    {
                        value = default;
                    }
                    else if (binder is JsonLines.Reading.Binders.IJsonlTryByteBinder<T> tryBinder && tryBinder.TryBind(span, out var bound))
                    {
                        value = bound;
                    }
                    else if (binder is not null && binder is not JsonLines.Reading.Binders.IJsonlTryByteBinder<T>)
                    {
                        value = binder.Bind(span);
                    }
                    else
                    {
                        value = JsonSerializer.Deserialize<T>(span, serializerOptions);
                        if (value is not null)
                        {
                            JsonlReadContractValidator.Instance.ValidateDeserialized(
                                value,
                                span,
                                JsonlReadContractValidator.TryGetTypeInfo<T>(serializerOptions),
                                validator,
                                (int)Math.Min(line.LineNumber, int.MaxValue),
                                effective.ValidationMode);
                        }
                    }
                }
                catch (Exception ex) when (effective.OnError is not null && ex is not (OutOfMemoryException or OperationCanceledException))
                {
                    Exception reported = ex is Validation.ValidationException vex
                        ? new JsonlValidationHelper().AttachLineNumber(vex, line.LineNumber)
                        : ex;
                    var ctx = BuildErrorContext<T>(in line, recordIndex);
                    if (effective.OnError(ctx, reported) == JsonlDeserializeErrorAction.SkipRecord)
                    {
                        recordIndex++;
                        continue;
                    }
                    throw new JsonlException(JsonlErrorCode.DeserializeError, $"Failed to deserialize line: {reported.Message}", line.LineNumber, reported);
                }
                catch (Validation.ValidationException vex)
                {
                    if (effective.ValidationMode == Validation.ValidationMode.Lenient)
                    {
                        recordIndex++;
                        continue;
                    }
                    throw new JsonlValidationHelper().AttachLineNumber(vex, line.LineNumber);
                }
                catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException or JsonlException))
                {
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
        JsonLines.Reading.Binders.JsonlRecordBinderFactory.TryGetValidator<T>(out var validator);
        long recordsRead = 0;
        long recordIndex = 0;
        int skipped = 0;

        try
        {
            await foreach (JsonlLine line in new JsonlPipeLineReader(reader, effective).ReadLinesAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!TryAdvance(in line, effective, ref skipped, ref recordsRead, out bool yieldNow))
                    continue;
                if (!yieldNow) continue;

                T? value;
                try
                {
                    ReadOnlySpan<byte> span = line.Utf8.Span;
                    value = JsonSerializer.Deserialize(span, typeInfo);
                    if (value is not null)
                    {
                        JsonlReadContractValidator.Instance.ValidateDeserialized(
                            value,
                            span,
                            typeInfo,
                            validator,
                            (int)Math.Min(line.LineNumber, int.MaxValue),
                            effective.ValidationMode);
                    }
                }
                catch (Exception ex) when (effective.OnError is not null && ex is not (OutOfMemoryException or OperationCanceledException))
                {
                    Exception reported = ex is Validation.ValidationException vex
                        ? new JsonlValidationHelper().AttachLineNumber(vex, line.LineNumber)
                        : ex;
                    var ctx = BuildErrorContext<T>(in line, recordIndex);
                    if (effective.OnError(ctx, reported) == JsonlDeserializeErrorAction.SkipRecord)
                    {
                        recordIndex++;
                        continue;
                    }
                    throw new JsonlException(JsonlErrorCode.DeserializeError, $"Failed to deserialize line: {reported.Message}", line.LineNumber, reported);
                }
                catch (Validation.ValidationException vex)
                {
                    if (effective.ValidationMode == Validation.ValidationMode.Lenient)
                    {
                        recordIndex++;
                        continue;
                    }
                    throw new JsonlValidationHelper().AttachLineNumber(vex, line.LineNumber);
                }
                catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException or JsonlException))
                {
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
                string.Create(CultureInfo.InvariantCulture, $"Number of records exceeds the configured MaxRowCount of {options.MaxRowCount:N0}."),
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
