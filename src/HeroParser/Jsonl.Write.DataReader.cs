using System.Buffers;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Numerics;
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

        options ??= JsonlWriteOptions.Default;
        options.Validate();

        try
        {
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
            long bytesWritten = 0;
            bool needsTrailingNewline = false;

            while (reader.Read())
            {
                if (!plan.IsInitialized)
                    plan = new DataReaderColumnPlan(reader);

                EnforceMaxRows(options, recordsWritten);
                if (needsTrailingNewline)
                {
                    stream.Write(newlineBytes, 0, newlineBytes.Length);
                    bytesWritten += newlineBytes.Length;
                    EnforceMaxOutputSize(options, bytesWritten);
                    needsTrailingNewline = false;
                }

                WriteDataReaderRecord(reader, fieldCount, columnNames, plan, writer, bufferWriter);
                ReadOnlySpan<byte> payload = bufferWriter.WrittenSpan;
                stream.Write(payload);
                bytesWritten += payload.Length;
                recordsWritten++;
                needsTrailingNewline = true;
                EnforceMaxOutputSize(options, bytesWritten);
            }

            if (options.WriteFinalNewline && needsTrailingNewline)
            {
                stream.Write(newlineBytes, 0, newlineBytes.Length);
                bytesWritten += newlineBytes.Length;
                EnforceMaxOutputSize(options, bytesWritten);
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

        options ??= JsonlWriteOptions.Default;
        options.Validate();

        try
        {
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
                DataReaderColumnPlan plan = default;
                long recordsWritten = 0;
                long bytesWritten = 0;
                bool needsTrailingNewline = false;

                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (!plan.IsInitialized)
                        plan = new DataReaderColumnPlan(reader);

                    EnforceMaxRows(options, recordsWritten);
                    if (needsTrailingNewline)
                    {
                        await stream.WriteAsync(newlineBytes.AsMemory(), cancellationToken).ConfigureAwait(false);
                        bytesWritten += newlineBytes.Length;
                        EnforceMaxOutputSize(options, bytesWritten);
                        needsTrailingNewline = false;
                    }

                    WriteDataReaderRecord(reader, fieldCount, columnNames, plan, writer, bufferWriter);
                    ReadOnlyMemory<byte> payload = bufferWriter.WrittenMemory;
                    await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
                    bytesWritten += payload.Length;
                    recordsWritten++;
                    needsTrailingNewline = true;
                    EnforceMaxOutputSize(options, bytesWritten);
                }

                if (options.WriteFinalNewline && needsTrailingNewline)
                {
                    await stream.WriteAsync(newlineBytes.AsMemory(), cancellationToken).ConfigureAwait(false);
                    bytesWritten += newlineBytes.Length;
                    EnforceMaxOutputSize(options, bytesWritten);
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
        var names = new JsonEncodedText[fieldCount];
        for (int i = 0; i < fieldCount; i++)
            names[i] = JsonEncodedText.Encode(reader.GetName(i), encoder);
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
