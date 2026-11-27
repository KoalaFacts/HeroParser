using System.Buffers;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Numerics;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using HeroParser.Internal;
using HeroParser.JsonLines;
using HeroParser.JsonLines.Writing;

namespace HeroParser;

public static partial class Jsonl
{
    /// <summary>
    /// Streams rows from an <see cref="IDataReader"/> into a <see cref="Stream"/> as UTF-8 JSONL objects without reflection.
    /// </summary>
    /// <param name="reader">The data reader providing columns and rows.</param>
    /// <param name="stream">The destination writable stream.</param>
    /// <param name="options">Optional JSONL writer options; defaults to <see cref="JsonlWriteOptions.Default"/>.</param>
    /// <param name="leaveOpen">When <see langword="true"/>, leaves <paramref name="stream"/> open after writing completes.</param>
    public static void WriteFromDataReader(
        IDataReader reader,
        Stream stream,
        JsonlWriteOptions? options = null,
        bool leaveOpen = true)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(stream);

        try
        {
            options ??= JsonlWriteOptions.Default;
            options.Validate();

            int fieldCount = reader.FieldCount;
            if (fieldCount <= 0)
            {
                stream.Flush();
                return;
            }

            JsonEncodedText[] columnNames = EncodeColumnNames(reader, fieldCount, options);
            byte[] newlineBytes = GetNewlineBytes(options);
            var bufferWriter = new ArrayBufferWriter<byte>();
            using var writer = new Utf8JsonWriter(bufferWriter, new JsonWriterOptions
            {
                Indented = false,
                SkipValidation = false,
                Encoder = options.SerializerOptions?.Encoder
            });

            DataReaderColumnPlan plan = default;
            long recordsWritten = 0;
            long recordIndex = 0;
            long bytesWritten = 0;
            bool needsTrailingNewline = false;

            while (reader.Read())
            {
                if (!plan.IsInitialized)
                    plan = new DataReaderColumnPlan(reader);

                EnforceMaxRows(options, recordsWritten);

                bool serialized = false;
                try
                {
                    WriteDataReaderRecord(reader, fieldCount, columnNames, plan, writer, bufferWriter);
                    serialized = true;
                }
                catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
                {
                    HandleDataReaderSerializeFailure(options, recordIndex, reader.GetType(), ex, writer, bufferWriter);
                }

                recordIndex++;
                if (!serialized)
                    continue;

                int sepLen = needsTrailingNewline ? newlineBytes.Length : 0;
                ReadOnlySpan<byte> payload = bufferWriter.WrittenSpan;
                long projectedBytes = bytesWritten + sepLen + payload.Length;
                EnforceMaxOutputSize(options, projectedBytes);

                if (needsTrailingNewline)
                    stream.Write(newlineBytes, 0, newlineBytes.Length);
                stream.Write(payload);

                bytesWritten = projectedBytes;
                recordsWritten++;
                needsTrailingNewline = true;
            }

            if (options.WriteFinalNewline && needsTrailingNewline)
            {
                EnforceMaxOutputSize(options, bytesWritten + newlineBytes.Length);
                stream.Write(newlineBytes, 0, newlineBytes.Length);
            }

            stream.Flush();
        }
        finally
        {
            if (!leaveOpen)
                stream.Dispose();
        }
    }

    /// <summary>
    /// Streams rows from an <see cref="IDataReader"/> directly to a JSONL file without reflection.
    /// </summary>
    /// <param name="path">Destination file path.</param>
    /// <param name="reader">The data reader providing columns and rows.</param>
    /// <param name="options">Optional JSONL writer options; defaults to <see cref="JsonlWriteOptions.Default"/>.</param>
    public static void WriteFromDataReaderToFile(
        string path,
        IDataReader reader,
        JsonlWriteOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(reader);

        using var stream = new CompressedFile(path).OpenWrite();
        WriteFromDataReader(reader, stream, options, leaveOpen: false);
    }

    /// <summary>
    /// Serializes rows from an <see cref="IDataReader"/> into a JSONL string without reflection.
    /// </summary>
    /// <param name="reader">The data reader providing columns and rows.</param>
    /// <param name="options">Optional JSONL writer options; defaults to <see cref="JsonlWriteOptions.Default"/>.</param>
    /// <returns>The serialized JSONL text.</returns>
    public static string WriteFromDataReaderToText(
        IDataReader reader,
        JsonlWriteOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(reader);

        using var stream = new MemoryStream();
        WriteFromDataReader(reader, stream, options, leaveOpen: true);
        return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
    }

    /// <summary>
    /// Asynchronously streams rows from a <see cref="DbDataReader"/> into a <see cref="Stream"/> as UTF-8 JSONL objects without reflection.
    /// </summary>
    /// <param name="reader">The async-capable database data reader providing columns and rows.</param>
    /// <param name="stream">The destination writable stream.</param>
    /// <param name="options">Optional JSONL writer options; defaults to <see cref="JsonlWriteOptions.Default"/>.</param>
    /// <param name="leaveOpen">When <see langword="true"/>, leaves <paramref name="stream"/> open after writing completes.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public static async ValueTask WriteFromDataReaderAsync(
        DbDataReader reader,
        Stream stream,
        JsonlWriteOptions? options = null,
        bool leaveOpen = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(stream);

        try
        {
            options ??= JsonlWriteOptions.Default;
            options.Validate();

            bool hasRow = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);

            int fieldCount = reader.FieldCount;
            if (fieldCount <= 0)
            {
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            JsonEncodedText[] columnNames = EncodeColumnNames(reader, fieldCount, options);
            byte[] newlineBytes = GetNewlineBytes(options);
            var bufferWriter = new ArrayBufferWriter<byte>();
            var writer = new Utf8JsonWriter(bufferWriter, new JsonWriterOptions
            {
                Indented = false,
                SkipValidation = false,
                Encoder = options.SerializerOptions?.Encoder
            });
            await using (writer.ConfigureAwait(false))
            {
                bool useAsyncFieldValues = DataReaderColumnPlan.HasAsyncFieldValueOverride(reader);
                DataReaderColumnPlan plan = default;
                long recordsWritten = 0;
                long recordIndex = 0;
                long bytesWritten = 0;
                bool needsTrailingNewline = false;

                if (hasRow)
                {
                    do
                    {
                        if (!plan.IsInitialized)
                            plan = new DataReaderColumnPlan(reader);

                        EnforceMaxRows(options, recordsWritten);

                        bool serialized = false;
                        try
                        {
                            if (useAsyncFieldValues)
                                await WriteDataReaderRecordAsync(reader, fieldCount, columnNames, plan, writer, bufferWriter, cancellationToken).ConfigureAwait(false);
                            else
                                WriteDataReaderRecord(reader, fieldCount, columnNames, plan, writer, bufferWriter);
                            serialized = true;
                        }
                        catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
                        {
                            HandleDataReaderSerializeFailure(options, recordIndex, reader.GetType(), ex, writer, bufferWriter);
                        }

                        recordIndex++;
                        if (serialized)
                        {
                            int sepLen = needsTrailingNewline ? newlineBytes.Length : 0;
                            ReadOnlyMemory<byte> payload = bufferWriter.WrittenMemory;
                            long projectedBytes = bytesWritten + sepLen + payload.Length;
                            EnforceMaxOutputSize(options, projectedBytes);

                            if (needsTrailingNewline)
                                await stream.WriteAsync(newlineBytes.AsMemory(), cancellationToken).ConfigureAwait(false);
                            await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);

                            bytesWritten = projectedBytes;
                            recordsWritten++;
                            needsTrailingNewline = true;
                        }
                    }
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false));
                }

                if (options.WriteFinalNewline && needsTrailingNewline)
                {
                    EnforceMaxOutputSize(options, bytesWritten + newlineBytes.Length);
                    await stream.WriteAsync(newlineBytes.AsMemory(), cancellationToken).ConfigureAwait(false);
                }

                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            if (!leaveOpen)
                await stream.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Asynchronously streams rows from a <see cref="DbDataReader"/> directly to a JSONL file without reflection.
    /// </summary>
    /// <param name="path">Destination file path.</param>
    /// <param name="reader">The async-capable database data reader providing columns and rows.</param>
    /// <param name="options">Optional JSONL writer options; defaults to <see cref="JsonlWriteOptions.Default"/>.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public static async ValueTask WriteFromDataReaderToFileAsync(
        string path,
        DbDataReader reader,
        JsonlWriteOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(reader);
        cancellationToken.ThrowIfCancellationRequested();

        var stream = new CompressedFile(path).OpenWrite(
            bufferSize: 4096,
            FileOptions.Asynchronous);
        await using (stream.ConfigureAwait(false))
        {
            await WriteFromDataReaderAsync(reader, stream, options, leaveOpen: false, cancellationToken).ConfigureAwait(false);
        }
    }

    private static JsonEncodedText[] EncodeColumnNames(IDataRecord reader, int fieldCount, JsonlWriteOptions options)
    {
        var encoder = options.SerializerOptions?.Encoder;
        var seen = new HashSet<string>(fieldCount, StringComparer.Ordinal);
        var names = new JsonEncodedText[fieldCount];
        for (int i = 0; i < fieldCount; i++)
        {
            string name = reader.GetName(i) ?? string.Empty;
            if (!seen.Add(name))
            {
                throw new JsonlException(
                    JsonlErrorCode.SerializeError,
                    $"Duplicate column name '{name}' at ordinal {i}. JSONL object property names must be unique.");
            }
            names[i] = JsonEncodedText.Encode(name, encoder);
        }
        return names;
    }

    private static byte[] GetNewlineBytes(JsonlWriteOptions options)
        => Encoding.UTF8.GetBytes(options.NewLine);

    private static void WriteDataReaderRecord(
        IDataRecord reader,
        int fieldCount,
        JsonEncodedText[] columnNames,
        DataReaderColumnPlan plan,
        Utf8JsonWriter writer,
        ArrayBufferWriter<byte> bufferWriter)
    {
        bufferWriter.Clear();
        writer.Reset(bufferWriter);

        writer.WriteStartObject();
        for (int i = 0; i < fieldCount; i++)
        {
            writer.WritePropertyName(columnNames[i]);
            DataReaderColumnKind kind = plan[i];
            if (kind is not (DataReaderColumnKind.String or DataReaderColumnKind.Object) && reader.IsDBNull(i))
            {
                writer.WriteNullValue();
                continue;
            }

            switch (kind)
            {
                case DataReaderColumnKind.String:
                    {
                        object? raw = reader.GetValue(i);
                        if (raw is null or DBNull)
                            writer.WriteNullValue();
                        else if (raw is string s)
                            writer.WriteStringValue(s);
                        else
                            WriteJsonValue(writer, raw);
                    }
                    break;
                case DataReaderColumnKind.Object:
                    WriteJsonValue(writer, reader.GetValue(i));
                    break;
                case DataReaderColumnKind.Int32:
                    writer.WriteNumberValue(reader.GetInt32(i));
                    break;
                case DataReaderColumnKind.Int64:
                    writer.WriteNumberValue(reader.GetInt64(i));
                    break;
                case DataReaderColumnKind.Int16:
                    writer.WriteNumberValue(reader.GetInt16(i));
                    break;
                case DataReaderColumnKind.Byte:
                    writer.WriteNumberValue(reader.GetByte(i));
                    break;
                case DataReaderColumnKind.Single:
                    {
                        float f = reader.GetFloat(i);
                        if (float.IsFinite(f))
                            writer.WriteNumberValue(f);
                        else
                            writer.WriteNullValue();
                    }
                    break;
                case DataReaderColumnKind.Double:
                    {
                        double d = reader.GetDouble(i);
                        if (double.IsFinite(d))
                            writer.WriteNumberValue(d);
                        else
                            writer.WriteNullValue();
                    }
                    break;
                case DataReaderColumnKind.Decimal:
                    writer.WriteNumberValue(reader.GetDecimal(i));
                    break;
                case DataReaderColumnKind.Boolean:
                    writer.WriteBooleanValue(reader.GetBoolean(i));
                    break;
                case DataReaderColumnKind.DateTime:
                    writer.WriteStringValue(reader.GetDateTime(i));
                    break;
                case DataReaderColumnKind.Guid:
                    writer.WriteStringValue(reader.GetGuid(i));
                    break;
                case DataReaderColumnKind.Char:
                    {
                        Span<char> chars = [reader.GetChar(i)];
                        writer.WriteStringValue(chars);
                    }
                    break;
                default:
                    break;
            }
        }
        writer.WriteEndObject();
        writer.Flush();
    }

    private static async ValueTask WriteDataReaderRecordAsync(
        DbDataReader reader,
        int fieldCount,
        JsonEncodedText[] columnNames,
        DataReaderColumnPlan plan,
        Utf8JsonWriter writer,
        ArrayBufferWriter<byte> bufferWriter,
        CancellationToken cancellationToken)
    {
        bufferWriter.Clear();
        writer.Reset(bufferWriter);

        writer.WriteStartObject();
        for (int i = 0; i < fieldCount; i++)
        {
            writer.WritePropertyName(columnNames[i]);
            if (await reader.IsDBNullAsync(i, cancellationToken).ConfigureAwait(false))
            {
                writer.WriteNullValue();
                continue;
            }

            switch (plan[i])
            {
                case DataReaderColumnKind.String:
                    {
                        string? str = await reader.GetFieldValueAsync<string?>(i, cancellationToken).ConfigureAwait(false);
                        if (str is null)
                            writer.WriteNullValue();
                        else
                            writer.WriteStringValue(str);
                    }
                    break;
                case DataReaderColumnKind.Object:
                    WriteJsonValue(writer, await reader.GetFieldValueAsync<object?>(i, cancellationToken).ConfigureAwait(false));
                    break;
                case DataReaderColumnKind.Int32:
                    writer.WriteNumberValue(await reader.GetFieldValueAsync<int>(i, cancellationToken).ConfigureAwait(false));
                    break;
                case DataReaderColumnKind.Int64:
                    writer.WriteNumberValue(await reader.GetFieldValueAsync<long>(i, cancellationToken).ConfigureAwait(false));
                    break;
                case DataReaderColumnKind.Int16:
                    writer.WriteNumberValue(await reader.GetFieldValueAsync<short>(i, cancellationToken).ConfigureAwait(false));
                    break;
                case DataReaderColumnKind.Byte:
                    writer.WriteNumberValue(await reader.GetFieldValueAsync<byte>(i, cancellationToken).ConfigureAwait(false));
                    break;
                case DataReaderColumnKind.Single:
                    {
                        float f = await reader.GetFieldValueAsync<float>(i, cancellationToken).ConfigureAwait(false);
                        if (float.IsFinite(f))
                            writer.WriteNumberValue(f);
                        else
                            writer.WriteNullValue();
                    }
                    break;
                case DataReaderColumnKind.Double:
                    {
                        double d = await reader.GetFieldValueAsync<double>(i, cancellationToken).ConfigureAwait(false);
                        if (double.IsFinite(d))
                            writer.WriteNumberValue(d);
                        else
                            writer.WriteNullValue();
                    }
                    break;
                case DataReaderColumnKind.Decimal:
                    writer.WriteNumberValue(await reader.GetFieldValueAsync<decimal>(i, cancellationToken).ConfigureAwait(false));
                    break;
                case DataReaderColumnKind.Boolean:
                    writer.WriteBooleanValue(await reader.GetFieldValueAsync<bool>(i, cancellationToken).ConfigureAwait(false));
                    break;
                case DataReaderColumnKind.DateTime:
                    writer.WriteStringValue(await reader.GetFieldValueAsync<DateTime>(i, cancellationToken).ConfigureAwait(false));
                    break;
                case DataReaderColumnKind.Guid:
                    writer.WriteStringValue(await reader.GetFieldValueAsync<Guid>(i, cancellationToken).ConfigureAwait(false));
                    break;
                case DataReaderColumnKind.Char:
                    WriteJsonChar(writer, await reader.GetFieldValueAsync<char>(i, cancellationToken).ConfigureAwait(false));
                    break;
                default:
                    break;
            }
        }
        writer.WriteEndObject();
        writer.Flush();
    }

    private static void WriteJsonChar(Utf8JsonWriter writer, char value)
    {
        Span<char> chars = [value];
        writer.WriteStringValue(chars);
    }

    private static void WriteSpanFormattableString<TValue>(Utf8JsonWriter writer, TValue value, string? format)
        where TValue : ISpanFormattable
    {
        Span<char> tmp = stackalloc char[128];
        if (value.TryFormat(tmp, out int written, format, CultureInfo.InvariantCulture))
            writer.WriteStringValue(tmp[..written]);
        else
            writer.WriteStringValue(((IFormattable)value).ToString(format, CultureInfo.InvariantCulture));
    }

    private static void WriteSpanFormattableRaw<TValue>(Utf8JsonWriter writer, TValue value)
        where TValue : ISpanFormattable
    {
        Span<char> tmp = stackalloc char[128];
        if (value.TryFormat(tmp, out int written, format: null, CultureInfo.InvariantCulture))
            writer.WriteRawValue(tmp[..written], skipInputValidation: true);
        else
            writer.WriteRawValue(((IFormattable)value).ToString(null, CultureInfo.InvariantCulture), skipInputValidation: true);
    }

    private static void WriteJsonValue(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null or DBNull:
                writer.WriteNullValue();
                break;

            case string s:
                writer.WriteStringValue(s);
                break;

            case bool b:
                writer.WriteBooleanValue(b);
                break;

            case byte by:
                writer.WriteNumberValue(by);
                break;

            case sbyte sb:
                writer.WriteNumberValue(sb);
                break;

            case short sh:
                writer.WriteNumberValue(sh);
                break;

            case ushort ush:
                writer.WriteNumberValue(ush);
                break;

            case int i:
                writer.WriteNumberValue(i);
                break;

            case uint ui:
                writer.WriteNumberValue(ui);
                break;

            case long l:
                writer.WriteNumberValue(l);
                break;

            case ulong ul:
                writer.WriteNumberValue(ul);
                break;

            case float f:
                if (float.IsFinite(f))
                    writer.WriteNumberValue(f);
                else
                    writer.WriteNullValue();
                break;

            case double d:
                if (double.IsFinite(d))
                    writer.WriteNumberValue(d);
                else
                    writer.WriteNullValue();
                break;

            case decimal dec:
                writer.WriteNumberValue(dec);
                break;

            case DateTime dt:
                writer.WriteStringValue(dt);
                break;

            case DateTimeOffset dto:
                writer.WriteStringValue(dto);
                break;

            case DateOnly dOnly:
                WriteSpanFormattableString(writer, dOnly, "yyyy-MM-dd");
                break;

            case TimeOnly tOnly:
                WriteSpanFormattableString(writer, tOnly, "HH:mm:ss.FFFFFFF");
                break;

            case Guid guid:
                writer.WriteStringValue(guid);
                break;

            case TimeSpan ts:
                WriteSpanFormattableString(writer, ts, "c");
                break;

            case char ch:
                {
                    Span<char> chars = [ch];
                    writer.WriteStringValue(chars);
                }
                break;

            case Half half:
                if (Half.IsFinite(half))
                    writer.WriteNumberValue((double)half);
                else
                    writer.WriteNullValue();
                break;

            case Int128 i128:
                WriteSpanFormattableRaw(writer, i128);
                break;

            case UInt128 u128:
                WriteSpanFormattableRaw(writer, u128);
                break;

            case BigInteger bi:
                WriteSpanFormattableRaw(writer, bi);
                break;

            case Uri uri:
                writer.WriteStringValue(uri.OriginalString);
                break;

            case byte[] bytes:
                writer.WriteBase64StringValue(bytes);
                break;

            case float[] floats:
                writer.WriteStartArray();
                for (int i = 0; i < floats.Length; i++)
                {
                    if (float.IsFinite(floats[i]))
                        writer.WriteNumberValue(floats[i]);
                    else
                        writer.WriteNullValue();
                }
                writer.WriteEndArray();
                break;

            case ISpanFormattable spanFormattable:
                WriteSpanFormattableString(writer, spanFormattable, format: null);
                break;

            case IFormattable formattable:
                writer.WriteStringValue(formattable.ToString(null, CultureInfo.InvariantCulture));
                break;

            default:
                writer.WriteStringValue(value.ToString());
                break;
        }
    }

    private static void EnforceMaxRows(JsonlWriteOptions options, long recordsWritten)
    {
        if (options.MaxRowCount is { } cap && recordsWritten >= cap)
        {
            throw new JsonlException(
                JsonlErrorCode.OutputSizeExceeded,
                string.Create(CultureInfo.InvariantCulture, $"MaxRowCount of {cap:N0} exceeded."));
        }
    }

    private static void HandleDataReaderSerializeFailure(
        JsonlWriteOptions options,
        long recordIndex,
        Type sourceType,
        Exception ex,
        Utf8JsonWriter writer,
        ArrayBufferWriter<byte> bufferWriter)
    {
        bufferWriter.Clear();
        writer.Reset(bufferWriter);

        if (options.OnError is not null)
        {
            var context = new JsonlSerializeErrorContext
            {
                RecordIndex = recordIndex,
                SourceType = sourceType
            };
            if (options.OnError(context, ex) == JsonlSerializeErrorAction.SkipRecord)
                return;
        }

        if (ex is JsonlException)
            ExceptionDispatchInfo.Throw(ex);

        throw new JsonlException(
            JsonlErrorCode.SerializeError,
            $"Failed to serialize record at index {recordIndex}: {ex.Message}",
            recordIndex + 1,
            ex);
    }

    private static void EnforceMaxOutputSize(JsonlWriteOptions options, long bytesWritten)
    {
        if (options.MaxOutputSize is { } cap && bytesWritten > cap)
        {
            throw new JsonlException(
                JsonlErrorCode.OutputSizeExceeded,
                string.Create(CultureInfo.InvariantCulture, $"Output size of {bytesWritten:N0} bytes exceeds MaxOutputSize of {cap:N0} bytes."));
        }
    }
}
