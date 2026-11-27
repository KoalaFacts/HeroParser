using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using System.Globalization;
using HeroParser.Validation;

namespace HeroParser.JsonLines.Writing;

/// <summary>
/// Low-level JSONL stream writer. Serializes each record via <see cref="Utf8JsonWriter"/> and emits a configurable newline.
/// </summary>
public sealed class JsonlStreamWriter : IDisposable, IAsyncDisposable
{
    private readonly Stream stream;
    private readonly bool leaveOpen;
    private readonly ArrayBufferWriter<byte> bufferWriter = new();
    private readonly Utf8JsonWriter writer;
    private readonly byte[] newlineBytes;
    private readonly JsonlWriteOptions options;
    private JsonSerializerOptions? effectiveSerializerOptions;
    private long jsonBytesCommitted;
    private long newlineBytesWritten;
    private long recordIndex;
    private bool needsTrailingNewline;
    private bool disposed;

    /// <summary>
    /// Initializes a new <see cref="JsonlStreamWriter"/>.
    /// </summary>
    /// <param name="stream">The destination stream.</param>
    /// <param name="options">Writer options.</param>
    /// <param name="leaveOpen">When <see langword="true"/>, the underlying stream is left open on dispose.</param>
    public JsonlStreamWriter(Stream stream, JsonlWriteOptions? options = null, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(stream);
        this.stream = stream;
        this.leaveOpen = leaveOpen;
        this.options = options ?? JsonlWriteOptions.Default;
        this.options.Validate();

        newlineBytes = Encoding.UTF8.GetBytes(this.options.NewLine);

        writer = new Utf8JsonWriter(bufferWriter, new JsonWriterOptions
        {
            Indented = false,
            SkipValidation = false,
            Encoder = this.options.SerializerOptions?.Encoder
        });
    }

    /// <summary>Gets the number of records successfully written.</summary>
    public long RecordsWritten { get; private set; }

    /// <summary>Gets the total byte count written to the underlying stream.</summary>
    public long BytesWritten => jsonBytesCommitted + newlineBytesWritten;

    /// <summary>
    /// Writes a record using a <see cref="JsonTypeInfo{T}"/> (AOT-safe).
    /// </summary>
    public void WriteRecord<T>(T value, JsonTypeInfo<T> typeInfo)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(typeInfo);
        EnforcePreWriteLimits();
        long currentIndex = recordIndex++;

        bufferWriter.Clear();
        writer.Reset(bufferWriter);

        try
        {
            JsonSerializer.Serialize(writer, value, typeInfo);
            writer.Flush();
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
        {
            HandleSerializeFailure(ex, currentIndex);
            return;
        }

        bool requiresEmptyObjectCheck = ValidateAotRecord(value, typeInfo, currentIndex);
        if (requiresEmptyObjectCheck && !JsonlTypeInfoRecordValidator.IsEmptyJsonObject(bufferWriter.WrittenSpan))
            throw JsonlTypeInfoRecordValidator.CreateUnsupportedStrictContractException(value?.GetType() ?? typeInfo.Type);

        CommitRecord();
    }

    /// <summary>
    /// Writes a record using reflection-based <see cref="JsonSerializer"/>.
    /// </summary>
    [RequiresUnreferencedCode("JSONL serialization without a JsonTypeInfo<T> uses reflection. Use the JsonTypeInfo<T> overload for AOT.")]
    [RequiresDynamicCode("JSONL serialization without a JsonTypeInfo<T> uses runtime code generation.")]
    public void WriteRecord<T>(T value)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        EnforcePreWriteLimits();
        long currentIndex = recordIndex++;

        bufferWriter.Clear();
        writer.Reset(bufferWriter);

        try
        {
            JsonSerializer.Serialize(writer, value, GetEffectiveSerializerOptions());
            writer.Flush();
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
        {
            HandleSerializeFailure(ex, currentIndex);
            return;
        }

        bool requiresEmptyObjectCheck = ValidateReflectionRecord(value, currentIndex);
        if (requiresEmptyObjectCheck && !JsonlTypeInfoRecordValidator.IsEmptyJsonObject(bufferWriter.WrittenSpan))
            throw JsonlTypeInfoRecordValidator.CreateUnsupportedStrictContractException(value?.GetType() ?? typeof(T));

        CommitRecord();
    }

    /// <summary>
    /// Asynchronously writes a record using a <see cref="JsonTypeInfo{T}"/>.
    /// </summary>
    public async ValueTask WriteRecordAsync<T>(T value, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(typeInfo);
        EnforcePreWriteLimits();
        long currentIndex = recordIndex++;

        bufferWriter.Clear();
        writer.Reset(bufferWriter);

        try
        {
            JsonSerializer.Serialize(writer, value, typeInfo);
            writer.Flush();
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
        {
            HandleSerializeFailure(ex, currentIndex);
            return;
        }

        bool requiresEmptyObjectCheck = ValidateAotRecord(value, typeInfo, currentIndex);
        if (requiresEmptyObjectCheck && !JsonlTypeInfoRecordValidator.IsEmptyJsonObject(bufferWriter.WrittenSpan))
            throw JsonlTypeInfoRecordValidator.CreateUnsupportedStrictContractException(value?.GetType() ?? typeInfo.Type);

        await CommitRecordAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Asynchronously writes a record using reflection-based <see cref="JsonSerializer"/>.
    /// </summary>
    [RequiresUnreferencedCode("JSONL serialization without a JsonTypeInfo<T> uses reflection. Use the JsonTypeInfo<T> overload for AOT.")]
    [RequiresDynamicCode("JSONL serialization without a JsonTypeInfo<T> uses runtime code generation.")]
    public async ValueTask WriteRecordAsync<T>(T value, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        EnforcePreWriteLimits();
        long currentIndex = recordIndex++;

        bufferWriter.Clear();
        writer.Reset(bufferWriter);

        try
        {
            JsonSerializer.Serialize(writer, value, GetEffectiveSerializerOptions());
            writer.Flush();
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
        {
            HandleSerializeFailure(ex, currentIndex);
            return;
        }

        bool requiresEmptyObjectCheck = ValidateReflectionRecord(value, currentIndex);
        if (requiresEmptyObjectCheck && !JsonlTypeInfoRecordValidator.IsEmptyJsonObject(bufferWriter.WrittenSpan))
            throw JsonlTypeInfoRecordValidator.CreateUnsupportedStrictContractException(value?.GetType() ?? typeof(T));

        await CommitRecordAsync(cancellationToken).ConfigureAwait(false);
    }

    private JsonSerializerOptions GetEffectiveSerializerOptions()
        => effectiveSerializerOptions ??= BigIntegerJsonConverter.ResolveOptions(options.SerializerOptions);

    /// <summary>
    /// Flushes any pending bytes (and emits a trailing newline if <see cref="JsonlWriteOptions.WriteFinalNewline"/> is set).
    /// </summary>
    public void Flush()
    {
        if (disposed) return;
        if (options.WriteFinalNewline && needsTrailingNewline)
        {
            EnforceMaxOutputSize(BytesWritten + newlineBytes.Length);
            stream.Write(newlineBytes, 0, newlineBytes.Length);
            newlineBytesWritten += newlineBytes.Length;
            needsTrailingNewline = false;
        }
        writer.Flush();
        stream.Flush();
    }

    /// <summary>
    /// Asynchronously flushes any pending bytes.
    /// </summary>
    public async ValueTask FlushAsync(CancellationToken cancellationToken = default)
    {
        if (disposed) return;
        if (options.WriteFinalNewline && needsTrailingNewline)
        {
            EnforceMaxOutputSize(BytesWritten + newlineBytes.Length);
            await stream.WriteAsync(newlineBytes.AsMemory(), cancellationToken).ConfigureAwait(false);
            newlineBytesWritten += newlineBytes.Length;
            needsTrailingNewline = false;
        }
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private void EnforcePreWriteLimits()
    {
        if (options.MaxRowCount is { } cap && RecordsWritten >= cap)
        {
            throw new JsonlException(
                JsonlErrorCode.OutputSizeExceeded,
                string.Create(CultureInfo.InvariantCulture, $"MaxRowCount of {cap:N0} exceeded."));
        }
    }

    private static bool CanUseGeneratedWriteValidator(JsonTypeInfo typeInfo)
        => typeInfo.Kind == JsonTypeInfoKind.Object
            && typeInfo.Properties.Count == 0
            && typeInfo.OriginatingResolver is JsonSerializerContext
            && typeInfo.Options.TypeInfoResolver is JsonSerializerContext
            && typeInfo.Options.TypeInfoResolverChain.Count <= 1
            && typeInfo.OnSerialized is null
            && !typeInfo.Options.IncludeFields
            && !typeInfo.Options.IgnoreReadOnlyProperties
            && !typeInfo.Options.IgnoreReadOnlyFields
            && typeInfo.Options.DefaultIgnoreCondition == JsonIgnoreCondition.Never
            && typeInfo.Options.Converters.Count == 0;

    private bool ValidateAotRecord<T>(T value, JsonTypeInfo<T> typeInfo, long currentIndex)
    {
        if (options.ValidationMode != ValidationMode.Strict || value is null)
            return false;

        int rowNumber = (int)Math.Min(currentIndex + 1, int.MaxValue);
        JsonTypeInfo effectiveTypeInfo = JsonlTypeInfoRecordValidator.ResolveEffectiveWriteTypeInfo(value, typeInfo);
        Reading.Binders.JsonlRecordBinderFactory.TryGetValidator<T>(out var validator);
        if (effectiveTypeInfo.Type == typeof(T)
            && value is not IJsonOnSerialized
            && CanUseGeneratedWriteValidator(effectiveTypeInfo)
            && validator is not null)
        {
            validator.Validate(value, rowNumber);
            return false;
        }

        return JsonlTypeInfoRecordValidator.Instance.Validate(value, effectiveTypeInfo, bufferWriter.WrittenSpan, rowNumber, validator);
    }

    [RequiresUnreferencedCode("JSONL write validation without [GenerateBinder] uses reflection to inspect [Validate] attributes.")]
    [RequiresDynamicCode("JSONL serialization without a JsonTypeInfo<T> uses runtime code generation.")]
    private bool ValidateReflectionRecord<T>(T value, long currentIndex)
    {
        if (options.ValidationMode != ValidationMode.Strict || value is null)
            return false;

        int rowNumber = (int)Math.Min(currentIndex + 1, int.MaxValue);
        JsonSerializerOptions effectiveOptions = GetEffectiveSerializerOptions();
        if (!effectiveOptions.IsReadOnly)
            effectiveOptions.MakeReadOnly(populateMissingResolver: true);
        JsonTypeInfo declaredTypeInfo = effectiveOptions.GetTypeInfo(typeof(T));
        JsonTypeInfo effectiveTypeInfo = JsonlTypeInfoRecordValidator.ResolveEffectiveWriteTypeInfo(value, declaredTypeInfo);
        return JsonlTypeInfoRecordValidator.Instance.Validate(value, effectiveTypeInfo, bufferWriter.WrittenSpan, rowNumber);
    }

    private void CommitRecord()
    {
        ReadOnlySpan<byte> data = bufferWriter.WrittenSpan;
        long pendingNewline = needsTrailingNewline ? newlineBytes.Length : 0;
        long finalNewline = options.WriteFinalNewline ? newlineBytes.Length : 0;
        EnforceMaxOutputSize(BytesWritten + pendingNewline + data.Length + finalNewline);

        if (needsTrailingNewline)
        {
            stream.Write(newlineBytes, 0, newlineBytes.Length);
            newlineBytesWritten += newlineBytes.Length;
            needsTrailingNewline = false;
        }

        stream.Write(data);
        jsonBytesCommitted += data.Length;
        RecordsWritten++;
        needsTrailingNewline = true;
    }

    private async ValueTask CommitRecordAsync(CancellationToken cancellationToken)
    {
        ReadOnlyMemory<byte> data = bufferWriter.WrittenMemory;
        long pendingNewline = needsTrailingNewline ? newlineBytes.Length : 0;
        long finalNewline = options.WriteFinalNewline ? newlineBytes.Length : 0;
        EnforceMaxOutputSize(BytesWritten + pendingNewline + data.Length + finalNewline);

        if (needsTrailingNewline)
        {
            await stream.WriteAsync(newlineBytes.AsMemory(), cancellationToken).ConfigureAwait(false);
            newlineBytesWritten += newlineBytes.Length;
            needsTrailingNewline = false;
        }

        await stream.WriteAsync(data, cancellationToken).ConfigureAwait(false);
        jsonBytesCommitted += data.Length;
        RecordsWritten++;
        needsTrailingNewline = true;
    }

    private void HandleSerializeFailure(Exception ex, long failedIndex)
    {
        bufferWriter.Clear();
        writer.Reset(bufferWriter);
        if (options.OnError is not null)
        {
            var ctx = new JsonlSerializeErrorContext
            {
                RecordIndex = failedIndex,
                SourceType = ex.GetType()
            };
            if (options.OnError(ctx, ex) == JsonlSerializeErrorAction.SkipRecord)
                return;
        }
        throw new JsonlException(JsonlErrorCode.SerializeError, $"Failed to serialize record at index {failedIndex}: {ex.Message}", failedIndex + 1, ex);
    }

    private void EnforceMaxOutputSize(long projectedBytes)
    {
        if (options.MaxOutputSize is { } cap && projectedBytes > cap)
        {
            throw new JsonlException(
                JsonlErrorCode.OutputSizeExceeded,
                string.Create(CultureInfo.InvariantCulture, $"Output size of {projectedBytes:N0} bytes exceeds MaxOutputSize of {cap:N0} bytes."));
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (disposed) return;
        try { Flush(); } catch { /* swallow to avoid masking earlier exception */ }
        writer.Dispose();
        if (!leaveOpen) stream.Dispose();
        disposed = true;
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        try { await FlushAsync().ConfigureAwait(false); } catch { /* swallow to avoid masking earlier exception */ }
        await writer.DisposeAsync().ConfigureAwait(false);
        if (!leaveOpen) await stream.DisposeAsync().ConfigureAwait(false);
        disposed = true;
    }
}
