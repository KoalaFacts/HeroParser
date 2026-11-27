using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Globalization;

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
    private readonly Binders.IJsonlSourceBinder<byte, T>? binder;
    private readonly Binders.IJsonlRecordValidator<T>? validator;
    private JsonSerializerOptions? effectiveSerializerOptions;
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
        binder = null;
        Binders.JsonlRecordBinderFactory.TryGetValidator<T>(out validator);
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
        if (this.options.SerializerOptions is null)
            Binders.JsonlRecordBinderFactory.TryGetByteBinder<T>(out binder);
        Binders.JsonlRecordBinderFactory.TryGetValidator<T>(out validator);
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

        while (lineReader.TryReadLine(out ReadOnlyMemory<byte> lineMemory, out long lineNumber))
        {
            var line = lineMemory.Span;
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
                    string.Create(CultureInfo.InvariantCulture, $"Number of records exceeds the configured MaxRowCount of {options.MaxRowCount:N0}."),
                    lineNumber);
            }

            T? value;
            try
            {
                value = Deserialize(line, lineNumber);
            }
            catch (Exception ex) when (options.OnError is not null && ex is not (OutOfMemoryException or OperationCanceledException))
            {
                Exception reportedException = ex is Validation.ValidationException vex
                    ? new JsonlValidationHelper().AttachLineNumber(vex, lineNumber)
                    : ex;

                var context = new JsonlDeserializeErrorContext
                {
                    LineNumber = lineNumber,
                    RecordIndex = recordIndex,
                    RawLine = System.Text.Encoding.UTF8.GetString(line),
                    TargetType = typeof(T)
                };

                JsonlDeserializeErrorAction action = options.OnError(context, reportedException);
                if (action == JsonlDeserializeErrorAction.Throw)
                {
                    throw new JsonlException(
                        JsonlErrorCode.DeserializeError,
                        $"Failed to deserialize line: {reportedException.Message}",
                        lineNumber,
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

                throw new JsonlValidationHelper().AttachLineNumber(vex, lineNumber);
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException or JsonlException))
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

    private T? Deserialize(ReadOnlySpan<byte> utf8, long lineNumber)
    {
        if (binder is Binders.IJsonlTryByteBinder<T> tryBinder)
        {
            if (default(T) is null && new JsonlValidationHelper().IsJsonNullLiteral(utf8))
                return default;
            if (tryBinder.TryBind(utf8, out var bound))
                return bound;
        }
        else if (binder is not null)
        {
            if (default(T) is null && new JsonlValidationHelper().IsJsonNullLiteral(utf8))
                return default;
            return binder.Bind(utf8);
        }

        T? value = typeInfo is not null
            ? JsonSerializer.Deserialize(utf8, typeInfo)
            : DeserializeReflection(utf8);
        if (value is not null)
        {
            JsonTypeInfo? activeTypeInfo = typeInfo ?? TryGetReflectionTypeInfo();
            JsonlReadContractValidator.Instance.ValidateDeserialized(
                value,
                utf8,
                activeTypeInfo,
                validator,
                (int)Math.Min(lineNumber, int.MaxValue),
                options.ValidationMode);
        }
        return value;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Reflection path is gated by the [RequiresUnreferencedCode] reflection constructor; callers using JsonTypeInfo<T> never reach this branch.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Reflection path is gated by the [RequiresDynamicCode] reflection constructor; callers using JsonTypeInfo<T> never reach this branch.")]
    private T? DeserializeReflection(ReadOnlySpan<byte> utf8)
        => JsonSerializer.Deserialize<T>(utf8, effectiveSerializerOptions ??= BigIntegerJsonConverter.ResolveOptions(options.SerializerOptions));

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Reflection path is gated by the [RequiresUnreferencedCode] reflection constructor; callers using JsonTypeInfo<T> never reach this branch.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Reflection path is gated by the [RequiresDynamicCode] reflection constructor; callers using JsonTypeInfo<T> never reach this branch.")]
    private JsonTypeInfo? TryGetReflectionTypeInfo()
        => effectiveSerializerOptions is null ? null : JsonlReadContractValidator.TryGetTypeInfo<T>(effectiveSerializerOptions);

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

internal readonly struct JsonlValidationHelper
{
    public bool IsJsonNullLiteral(ReadOnlySpan<byte> utf8)
        => utf8.Trim(" \t\r\n"u8).SequenceEqual("null"u8);

    public Validation.ValidationException AttachLineNumber(Validation.ValidationException exception, long lineNumber)
    {
        int clampedLine = (int)Math.Min(lineNumber, int.MaxValue);
        if (clampedLine <= 0)
            return exception;

        IReadOnlyList<Validation.ValidationError> existing = exception.Errors;
        bool needsUpdate = false;
        for (int i = 0; i < existing.Count; i++)
        {
            if (existing[i].RowNumber == 0)
            {
                needsUpdate = true;
                break;
            }
        }

        if (!needsUpdate)
            return exception;

        var updated = new Validation.ValidationError[existing.Count];
        for (int i = 0; i < existing.Count; i++)
        {
            var err = existing[i];
            updated[i] = err.RowNumber == 0
                ? err with { RowNumber = clampedLine }
                : err;
        }

        return new Validation.ValidationException(updated);
    }
}
