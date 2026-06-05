using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;

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
    private long jsonBytesCommitted;
    private long newlineBytesWritten;
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

        Encoding encoding = this.options.Encoding ?? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        newlineBytes = encoding.GetBytes(this.options.NewLine);

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
        EmitPendingNewline();

        bufferWriter.Clear();
        writer.Reset(bufferWriter);

        try
        {
            JsonSerializer.Serialize(writer, value, typeInfo);
            writer.Flush();
        }
        catch (Exception ex)
        {
            HandleSerializeFailure(ex);
            return;
        }

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
        EmitPendingNewline();

        bufferWriter.Clear();
        writer.Reset(bufferWriter);

        try
        {
            JsonSerializer.Serialize(writer, value, options.SerializerOptions);
            writer.Flush();
        }
        catch (Exception ex)
        {
            HandleSerializeFailure(ex);
            return;
        }

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
        await EmitPendingNewlineAsync(cancellationToken).ConfigureAwait(false);

        bufferWriter.Clear();
        writer.Reset(bufferWriter);

        try
        {
            JsonSerializer.Serialize(writer, value, typeInfo);
            writer.Flush();
        }
        catch (Exception ex)
        {
            HandleSerializeFailure(ex);
            return;
        }

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
        await EmitPendingNewlineAsync(cancellationToken).ConfigureAwait(false);

        bufferWriter.Clear();
        writer.Reset(bufferWriter);

        try
        {
            JsonSerializer.Serialize(writer, value, options.SerializerOptions);
            writer.Flush();
        }
        catch (Exception ex)
        {
            HandleSerializeFailure(ex);
            return;
        }

        await CommitRecordAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Flushes any pending bytes (and emits a trailing newline if <see cref="JsonlWriteOptions.WriteFinalNewline"/> is set).
    /// </summary>
    public void Flush()
    {
        if (disposed) return;
        if (options.WriteFinalNewline && needsTrailingNewline)
        {
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
                $"MaxRowCount of {cap:N0} exceeded.");
        }
    }

    private void EmitPendingNewline()
    {
        if (needsTrailingNewline)
        {
            stream.Write(newlineBytes, 0, newlineBytes.Length);
            newlineBytesWritten += newlineBytes.Length;
            needsTrailingNewline = false;
            EnforceMaxOutputSize();
        }
    }

    private async ValueTask EmitPendingNewlineAsync(CancellationToken cancellationToken)
    {
        if (needsTrailingNewline)
        {
            await stream.WriteAsync(newlineBytes.AsMemory(), cancellationToken).ConfigureAwait(false);
            newlineBytesWritten += newlineBytes.Length;
            needsTrailingNewline = false;
            EnforceMaxOutputSize();
        }
    }

    private void CommitRecord()
    {
        ReadOnlySpan<byte> data = bufferWriter.WrittenSpan;
        stream.Write(data);
        jsonBytesCommitted += data.Length;
        RecordsWritten++;
        needsTrailingNewline = true;
        EnforceMaxOutputSize();
    }

    private async ValueTask CommitRecordAsync(CancellationToken cancellationToken)
    {
        ReadOnlyMemory<byte> data = bufferWriter.WrittenMemory;
        await stream.WriteAsync(data, cancellationToken).ConfigureAwait(false);
        jsonBytesCommitted += data.Length;
        RecordsWritten++;
        needsTrailingNewline = true;
        EnforceMaxOutputSize();
    }

    private void HandleSerializeFailure(Exception ex)
    {
        bufferWriter.Clear();
        writer.Reset(bufferWriter);
        if (options.OnError is not null)
        {
            var ctx = new JsonlSerializeErrorContext
            {
                RecordIndex = RecordsWritten,
                SourceType = ex.GetType()
            };
            if (options.OnError(ctx, ex) == JsonlSerializeErrorAction.SkipRecord)
                return;
        }
        throw new JsonlException(JsonlErrorCode.SerializeError, $"Failed to serialize record at index {RecordsWritten}: {ex.Message}", RecordsWritten + 1, ex);
    }

    private void EnforceMaxOutputSize()
    {
        if (options.MaxOutputSize is { } cap && BytesWritten > cap)
        {
            throw new JsonlException(
                JsonlErrorCode.OutputSizeExceeded,
                $"Output size of {BytesWritten:N0} bytes exceeds MaxOutputSize of {cap:N0} bytes.");
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
