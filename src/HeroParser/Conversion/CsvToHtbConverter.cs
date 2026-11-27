using System;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HeroParser.Htbs;
using HeroParser.Htbs.Records;
using HeroParser.Htbs.Writing;
using HeroParser.Internal;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Detection;
using HeroParser.SeparatedValues.Reading.Rows;
using HeroParser.SeparatedValues.Reading.Streaming;

namespace HeroParser.Conversion;

/// <summary>
/// Converts CSV data directly into the High-Throughput Tabular Binary (HTB) format.
/// </summary>
public static class CsvToHtbConverter
{
    /// <summary>
    /// Converts a CSV string directly to the HTB format written into a stream.
    /// </summary>
    public static void Convert(string csvData, Stream htbStream, HtbSchema schema, CsvToHtbOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(csvData);
        ArgumentNullException.ThrowIfNull(htbStream);
        ArgumentNullException.ThrowIfNull(schema);
        options ??= CsvToHtbOptions.Default;

        var parserReadOptions = new CsvReadOptions
        {
            Delimiter = options.Delimiter,
            Quote = options.Quote,
            EscapeCharacter = options.EscapeCharacter,
            AllowNewlinesInsideQuotes = options.AllowNewlinesInsideQuotes,
            MaxColumnCount = Math.Max(100, schema.Columns.Count + 1),
            MaxRowCount = ResolveMaxInputRowCount(options),
            MaxRowSize = options.MaxInputRowSize
        };
        using var rowReader = Csv.ReadFromText(csvData, parserReadOptions);

        var htbWriteOptions = new HtbWriteOptions
        {
            MaxRowCount = options.MaxRowCount,
            Progress = options.Progress,
            ProgressIntervalRows = options.ProgressIntervalRows
        };
        using var writer = new HtbStreamWriter(htbStream, htbWriteOptions, leaveOpen: true);
        writer.WriteHeader(schema);

        bool isFirst = true;
        int rowNumber = 0;
        int[]? csvIndices = null;

        while (rowReader.MoveNext())
        {
            rowNumber++;
            var row = rowReader.Current;
            if (options.MaxInputRowSize is { } maxInputRowSize && row.Line.Length > maxInputRowSize)
                throw CsvException.RowSizeLimitExceeded(maxInputRowSize, isUtf8: false);
            if (options.RejectExtraColumns && row.ColumnCount > schema.Columns.Count)
            {
                throw new HtbException(
                    HtbErrorCode.SerializationError,
                    $"CSV row {rowNumber} has {row.ColumnCount} columns, which exceeds the HTB schema width of {schema.Columns.Count}.");
            }

            if (isFirst && options.HasHeaderRow)
            {
                isFirst = false;
                csvIndices = options.MapByOrdinal
                    ? MapSequential(schema)
                    : MapHeaders(row, schema, options);
                continue;
            }
            isFirst = false;

            csvIndices ??= MapSequential(schema);

            writer.IncrementRecordCount();
            ConvertRow(row, rowNumber, csvIndices, schema, writer, options);
        }

        writer.Flush();
    }

    /// <summary>
    /// Converts a CSV file directly to the HTB format written into a file path.
    /// </summary>
    public static void ConvertFile(string csvPath, string htbPath, HtbSchema schema, CsvToHtbOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(csvPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(htbPath);
        ArgumentNullException.ThrowIfNull(schema);

        using var inputStream = new CompressedFile(csvPath).OpenRead(
            bufferSize: 16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var outputStream = new CompressedFile(htbPath).OpenWrite();
        ConvertAsync(inputStream, outputStream, schema, options).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Asynchronously converts a CSV stream directly to the HTB format written into an output stream.
    /// </summary>
    public static async Task ConvertAsync(
        Stream csvStream,
        Stream htbStream,
        HtbSchema schema,
        CsvToHtbOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(csvStream);
        ArgumentNullException.ThrowIfNull(htbStream);
        ArgumentNullException.ThrowIfNull(schema);
        options ??= CsvToHtbOptions.Default;

        var (effectiveStream, ownsWrapper) = await PrepareUtf8InputStreamAsync(csvStream, cancellationToken).ConfigureAwait(false);
        try
        {
            var parserReadOptions = new CsvReadOptions
            {
                Delimiter = options.Delimiter,
                Quote = options.Quote,
                EscapeCharacter = options.EscapeCharacter,
                AllowNewlinesInsideQuotes = options.AllowNewlinesInsideQuotes,
                MaxColumnCount = Math.Max(100, schema.Columns.Count + 1),
                MaxRowCount = ResolveMaxInputRowCount(options),
                MaxRowSize = options.MaxInputRowSize
            };
            await using var rowReader = Csv.CreateAsyncStreamReader(effectiveStream, parserReadOptions, leaveOpen: true);

            var htbWriteOptions = new HtbWriteOptions
            {
                MaxRowCount = options.MaxRowCount,
                Progress = options.Progress,
                ProgressIntervalRows = options.ProgressIntervalRows
            };
            await using var writer = new HtbStreamWriter(htbStream, htbWriteOptions, leaveOpen: true);
            await writer.WriteHeaderAsync(schema, cancellationToken).ConfigureAwait(false);

            bool isFirst = true;
            int rowNumber = 0;
            int[]? csvIndices = null;

            while (await rowReader.MoveNextAsync(cancellationToken).ConfigureAwait(false))
            {
                rowNumber++;
                var row = rowReader.Current;
                if (options.RejectExtraColumns && row.ColumnCount > schema.Columns.Count)
                {
                    throw new HtbException(
                        HtbErrorCode.SerializationError,
                        $"CSV row {rowNumber} has {row.ColumnCount} columns, which exceeds the HTB schema width of {schema.Columns.Count}.");
                }

                if (isFirst && options.HasHeaderRow)
                {
                    isFirst = false;
                    csvIndices = options.MapByOrdinal
                        ? MapSequential(schema)
                        : MapHeaders(row, schema, options);
                    continue;
                }
                isFirst = false;

                csvIndices ??= MapSequential(schema);

                writer.IncrementRecordCount();
                ConvertRow(row, rowNumber, csvIndices, schema, writer, options);
            }

            await writer.FlushInternalAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (ownsWrapper)
                await effectiveStream.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static int ResolveMaxInputRowCount(CsvToHtbOptions options)
    {
        if (options.MaxInputRowCount is { } explicitCap)
            return explicitCap;

        if (options.MaxRowCount is > 0 and var recordCap)
        {
            long required = recordCap + (options.HasHeaderRow ? 1L : 0L);
            return (int)Math.Min(Math.Max(CsvReadOptions.Default.MaxRowCount, required), int.MaxValue);
        }

        return CsvReadOptions.Default.MaxRowCount;
    }

    private static async ValueTask<(Stream EffectiveStream, bool OwnsWrapper)> PrepareUtf8InputStreamAsync(
        Stream csvStream,
        CancellationToken cancellationToken)
    {
        var bom = new byte[4];
        int bytesRead = 0;
        while (bytesRead < bom.Length)
        {
            int read = await csvStream.ReadAsync(bom.AsMemory(bytesRead, bom.Length - bytesRead), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
                break;
            bytesRead += read;
        }

        if (bytesRead == 4 && bom[0] == 0xFF && bom[1] == 0xFE && bom[2] == 0x00 && bom[3] == 0x00)
            return (new Utf16ToUtf8ReadStream(csvStream, Encoding.UTF32, leaveOpen: true), true);

        if (bytesRead == 4 && bom[0] == 0x00 && bom[1] == 0x00 && bom[2] == 0xFE && bom[3] == 0xFF)
            return (new Utf16ToUtf8ReadStream(csvStream, new UTF32Encoding(bigEndian: true, byteOrderMark: true), leaveOpen: true), true);

        if (bytesRead >= 2 && ((bom[0] == 0xFF && bom[1] == 0xFE) || (bom[0] == 0xFE && bom[1] == 0xFF)))
        {
            Encoding utf16 = bom[0] == 0xFF ? Encoding.Unicode : Encoding.BigEndianUnicode;
            if (bytesRead > 2)
            {
                if (csvStream.CanSeek)
                {
                    csvStream.Seek(-(bytesRead - 2), SeekOrigin.Current);
                    return (new Utf16ToUtf8ReadStream(csvStream, utf16, leaveOpen: true), true);
                }

                var remainder = new PrefixReadStream(csvStream, bom[2..bytesRead], leaveOpen: true);
                return (new Utf16ToUtf8ReadStream(remainder, utf16, leaveOpen: false), true);
            }

            return (new Utf16ToUtf8ReadStream(csvStream, utf16, leaveOpen: true), true);
        }

        if (bytesRead == 0)
            return (csvStream, false);

        if (csvStream.CanSeek)
        {
            csvStream.Seek(-bytesRead, SeekOrigin.Current);
            return (csvStream, false);
        }

        return (new PrefixReadStream(csvStream, bom[..bytesRead], leaveOpen: true), true);
    }

    private static int[] MapHeaders<T>(CsvRow<T> headerRow, HtbSchema schema, CsvToHtbOptions options)
        where T : unmanaged, IEquatable<T>
    {
        if (headerRow.ColumnCount == 0)
        {
            throw new HtbException(HtbErrorCode.SerializationError, "CSV header row is empty.");
        }
        if (headerRow.ColumnCount > 2048)
        {
            throw new HtbException(HtbErrorCode.SerializationError, $"CSV header row contains too many columns ({headerRow.ColumnCount}). Maximum allowed is 2048.");
        }

        var headers = new string[headerRow.ColumnCount];
        for (int i = 0; i < headerRow.ColumnCount; i++)
        {
            headers[i] = UnquoteToString(headerRow[i], options);
        }

        var csvIndices = new int[schema.Columns.Count];
        for (int i = 0; i < schema.Columns.Count; i++)
        {
            csvIndices[i] = Array.FindIndex(headers, h => string.Equals(h, schema.Columns[i].Name, StringComparison.OrdinalIgnoreCase));
        }
        return csvIndices;
    }

    private static int[] MapSequential(HtbSchema schema)
    {
        var csvIndices = new int[schema.Columns.Count];
        for (int i = 0; i < schema.Columns.Count; i++)
        {
            csvIndices[i] = i;
        }
        return csvIndices;
    }

    private static void ConvertRow<T>(CsvRow<T> row, int rowNumber, int[] csvIndices, HtbSchema schema, HtbStreamWriter writer, CsvToHtbOptions options)
        where T : unmanaged, IEquatable<T>
    {
        int colCount = schema.Columns.Count;
        int maskLen = (colCount + 7) / 8;
        byte[]? rented = null;
        Span<byte> mask = maskLen <= 128
            ? stackalloc byte[maskLen]
            : (rented = System.Buffers.ArrayPool<byte>.Shared.Rent(maskLen)).AsSpan(0, maskLen);
        try
        {
            mask.Clear();

            // 1. First pass: Determine nulls and write null mask
            for (int i = 0; i < colCount; i++)
            {
                var col = schema.Columns[i];
                int csvIdx = csvIndices[i];
                if (csvIdx < 0 || csvIdx >= row.ColumnCount)
                {
                    if (!col.IsNullable)
                    {
                        throw new HtbException(HtbErrorCode.SerializationError, $"Column '{col.Name}' is not nullable but is missing in the CSV row.");
                    }
                    mask[i / 8] |= (byte)(1 << (i % 8));
                    continue;
                }

                if (IsEmptyValue(row[csvIdx], col.DataType, options))
                {
                    if (!col.IsNullable)
                    {
                        throw new HtbException(HtbErrorCode.SerializationError, $"Column '{col.Name}' is not nullable but has an empty value in the CSV row.");
                    }
                    mask[i / 8] |= (byte)(1 << (i % 8));
                    continue;
                }
            }

            writer.WriteMask(mask);

            // 2. Second pass: Write present values
            for (int i = 0; i < colCount; i++)
            {
                int csvIdx = csvIndices[i];
                if (csvIdx < 0 || csvIdx >= row.ColumnCount)
                    continue;

                var rawCol = row[csvIdx];
                var col = schema.Columns[i];
                if (IsEmptyValue(rawCol, col.DataType, options))
                    continue;

                // Typed values are parsed without their surrounding quotes ("42" is 42); strings and
                // vectors are fully unquoted with the configured quote and escape characters.
                var csvCol = new CsvColumn<T>(rawCol.Unquote(ToElement<T>(options.Quote)));

                switch (col.DataType)
                {
                    case HtbDataType.Int32:
                        if (!csvCol.TryParseInt32(out int i32))
                            throw new HtbException(HtbErrorCode.SerializationError, $"Failed to parse column '{col.Name}' value as Int32.");
                        EnsureLosslessNumeric(csvCol, i32, rowNumber, col, options);
                        writer.WriteInt32(i32);
                        break;
                    case HtbDataType.Int64:
                        if (!csvCol.TryParseInt64(out long i64))
                            throw new HtbException(HtbErrorCode.SerializationError, $"Failed to parse column '{col.Name}' value as Int64.");
                        EnsureLosslessNumeric(csvCol, i64, rowNumber, col, options);
                        writer.WriteInt64(i64);
                        break;
                    case HtbDataType.Float:
                        if (!csvCol.TryParseSingle(out float f32))
                            throw new HtbException(HtbErrorCode.SerializationError, $"Failed to parse column '{col.Name}' value as Float.");
                        EnsureLosslessNumeric(csvCol, f32, rowNumber, col, options);
                        writer.WriteFloat(f32);
                        break;
                    case HtbDataType.Double:
                        if (!csvCol.TryParseDouble(out double f64))
                            throw new HtbException(HtbErrorCode.SerializationError, $"Failed to parse column '{col.Name}' value as Double.");
                        EnsureLosslessNumeric(csvCol, f64, rowNumber, col, options);
                        writer.WriteDouble(f64);
                        break;
                    case HtbDataType.Decimal:
                        if (!csvCol.TryParseDecimal(out decimal dec))
                            throw new HtbException(HtbErrorCode.SerializationError, $"Failed to parse column '{col.Name}' value as Decimal.");
                        EnsureLosslessNumeric(csvCol, dec, rowNumber, col, options);
                        writer.WriteDecimal(dec);
                        break;
                    case HtbDataType.Boolean:
                        if (!csvCol.TryParseBoolean(out bool b))
                            throw new HtbException(HtbErrorCode.SerializationError, $"Failed to parse column '{col.Name}' value as Boolean.");
                        writer.WriteBoolean(b);
                        break;
                    case HtbDataType.DateTime:
                        if (!csvCol.TryParseDateTime(out DateTime dt))
                            throw new HtbException(HtbErrorCode.SerializationError, $"Failed to parse column '{col.Name}' value as DateTime.");
                        if (options.RejectLossyNumericValues
                            && (dt.Kind != DateTimeKind.Unspecified || HasExplicitTimeZoneOffset(csvCol) || !HasCompleteDate(csvCol)))
                        {
                            throw CreateLossyNumericException(rowNumber, col);
                        }
                        writer.WriteDateTime(dt);
                        break;
                    case HtbDataType.Guid:
                        if (!csvCol.TryParseGuid(out Guid g))
                            throw new HtbException(HtbErrorCode.SerializationError, $"Failed to parse column '{col.Name}' value as Guid.");
                        writer.WriteGuid(g);
                        break;
                    case HtbDataType.String:
                        writer.WriteString(UnquoteToString(rawCol, options));
                        break;
                    case HtbDataType.FloatArray:
                        writer.WriteFloatArray(Vectors.VectorParser.ParseFloats(UnquoteToString(rawCol, options)));
                        break;
                    case HtbDataType.DateTimeOffset:
                        if (!csvCol.TryParseDateTimeOffset(out DateTimeOffset dto))
                            throw new HtbException(HtbErrorCode.SerializationError, $"Failed to parse column '{col.Name}' value as DateTimeOffset.");
                        if (options.RejectLossyNumericValues
                            && (!HasExplicitTimeZoneOffset(csvCol) || !HasExplicitDateBeforeOffset(csvCol)))
                        {
                            throw CreateLossyNumericException(rowNumber, col);
                        }
                        writer.WriteDateTimeOffset(dto);
                        break;
                    case HtbDataType.DateOnly:
                        if (!csvCol.TryParseDateOnly(out DateOnly dOnly))
                            throw new HtbException(HtbErrorCode.SerializationError, $"Failed to parse column '{col.Name}' value as DateOnly.");
                        if (options.RejectLossyNumericValues && !HasCompleteDate(csvCol))
                            throw CreateLossyNumericException(rowNumber, col);
                        writer.WriteDateOnly(dOnly);
                        break;
                    case HtbDataType.TimeOnly:
                        if (!csvCol.TryParseTimeOnly(out TimeOnly tOnly))
                            throw new HtbException(HtbErrorCode.SerializationError, $"Failed to parse column '{col.Name}' value as TimeOnly.");
                        writer.WriteTimeOnly(tOnly);
                        break;
                    case HtbDataType.TimeSpan:
                        if (!csvCol.TryParseTimeSpan(out TimeSpan ts))
                            throw new HtbException(HtbErrorCode.SerializationError, $"Failed to parse column '{col.Name}' value as TimeSpan.");
                        writer.WriteTimeSpan(ts);
                        break;
                    case HtbDataType.Int16:
                        if (!csvCol.TryParseInt16(out short i16))
                            throw new HtbException(HtbErrorCode.SerializationError, $"Failed to parse column '{col.Name}' value as Int16.");
                        EnsureLosslessNumeric(csvCol, i16, rowNumber, col, options);
                        writer.WriteInt16(i16);
                        break;
                    case HtbDataType.UInt16:
                        if (!csvCol.TryParseUInt16(out ushort u16))
                            throw new HtbException(HtbErrorCode.SerializationError, $"Failed to parse column '{col.Name}' value as UInt16.");
                        EnsureLosslessNumeric(csvCol, u16, rowNumber, col, options);
                        writer.WriteUInt16(u16);
                        break;
                    case HtbDataType.Byte:
                        if (!csvCol.TryParseByte(out byte u8))
                            throw new HtbException(HtbErrorCode.SerializationError, $"Failed to parse column '{col.Name}' value as Byte.");
                        EnsureLosslessNumeric(csvCol, u8, rowNumber, col, options);
                        writer.WriteByte(u8);
                        break;
                    case HtbDataType.SByte:
                        if (!csvCol.TryParseSByte(out sbyte i8))
                            throw new HtbException(HtbErrorCode.SerializationError, $"Failed to parse column '{col.Name}' value as SByte.");
                        EnsureLosslessNumeric(csvCol, i8, rowNumber, col, options);
                        writer.WriteSByte(i8);
                        break;
                    case HtbDataType.UInt32:
                        if (!csvCol.TryParseUInt32(out uint u32))
                            throw new HtbException(HtbErrorCode.SerializationError, $"Failed to parse column '{col.Name}' value as UInt32.");
                        EnsureLosslessNumeric(csvCol, u32, rowNumber, col, options);
                        writer.WriteUInt32(u32);
                        break;
                    case HtbDataType.UInt64:
                        if (!csvCol.TryParseUInt64(out ulong u64))
                            throw new HtbException(HtbErrorCode.SerializationError, $"Failed to parse column '{col.Name}' value as UInt64.");
                        EnsureLosslessNumeric(csvCol, u64, rowNumber, col, options);
                        writer.WriteUInt64(u64);
                        break;
                    case HtbDataType.Char:
                        {
                            string decodedChar = UnquoteToString(rawCol, options);
                            if (!Utf8SpanParser.TryParseCharSpan(decodedChar.AsSpan(), out char ch))
                                throw new HtbException(HtbErrorCode.SerializationError, $"Failed to parse column '{col.Name}' value as Char.");
                            writer.WriteChar(ch);
                        }
                        break;
                    case HtbDataType.Half:
                        if (!csvCol.TryParseHalf(out Half h))
                            throw new HtbException(HtbErrorCode.SerializationError, $"Failed to parse column '{col.Name}' value as Half.");
                        EnsureLosslessNumeric(csvCol, h, rowNumber, col, options);
                        writer.WriteHalf(h);
                        break;
                    case HtbDataType.Int128:
                        if (!csvCol.TryParseInt128(out Int128 i128))
                            throw new HtbException(HtbErrorCode.SerializationError, $"Failed to parse column '{col.Name}' value as Int128.");
                        EnsureLosslessNumeric(csvCol, i128, rowNumber, col, options);
                        writer.WriteInt128(i128);
                        break;
                    case HtbDataType.UInt128:
                        if (!csvCol.TryParseUInt128(out UInt128 u128))
                            throw new HtbException(HtbErrorCode.SerializationError, $"Failed to parse column '{col.Name}' value as UInt128.");
                        EnsureLosslessNumeric(csvCol, u128, rowNumber, col, options);
                        writer.WriteUInt128(u128);
                        break;
                    case HtbDataType.ByteArray:
                        if (!csvCol.TryParseBase64Bytes(out byte[]? bytes))
                            throw new HtbException(HtbErrorCode.SerializationError, $"Failed to parse column '{col.Name}' value as ByteArray.");
                        writer.WriteByteArray(bytes);
                        break;
                    case HtbDataType.BigInteger:
                        if (!csvCol.TryParseBigInteger(out BigInteger bi))
                            throw new HtbException(HtbErrorCode.SerializationError, $"Failed to parse column '{col.Name}' value as BigInteger.");
                        EnsureLosslessNumeric(csvCol, bi, rowNumber, col, options);
                        writer.WriteBigInteger(bi);
                        break;
                    case HtbDataType.Uri:
                        {
                            string rawUri = UnquoteToString(rawCol, options);
                            if (!Uri.TryCreate(rawUri, UriKind.RelativeOrAbsolute, out Uri? uri))
                                throw new HtbException(HtbErrorCode.SerializationError, $"Failed to parse column '{col.Name}' value as Uri.");
                            writer.WriteUri(uri);
                        }
                        break;
                    case HtbDataType.Unknown:
                    default:
                        throw new HtbException(HtbErrorCode.SerializationError, $"Unsupported column type: {col.DataType}");
                }
            }
        }
        finally
        {
            if (rented != null)
            {
                System.Buffers.ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }

    private static void EnsureLosslessNumeric<T, TNum>(
        CsvColumn<T> csvCol,
        TNum parsed,
        int rowNumber,
        HtbColumn col,
        CsvToHtbOptions options)
        where T : unmanaged, IEquatable<T>
        where TNum : ISpanFormattable
    {
        if (!options.RejectLossyNumericValues)
            return;

        if (typeof(T) == typeof(byte) && parsed is IUtf8SpanFormattable utf8Formattable)
        {
            Span<byte> utf8Buf = stackalloc byte[64];
            if (utf8Formattable.TryFormat(utf8Buf, out int utf8Written, default, CultureInfo.InvariantCulture))
            {
                if (!utf8Buf[..utf8Written].SequenceEqual(MemoryMarshal.AsBytes(csvCol.Span)))
                    throw CreateLossyNumericException(rowNumber, col);
                return;
            }
        }

        Span<char> charBuf = stackalloc char[64];
        string? fallback = null;
        ReadOnlySpan<char> formatted = parsed.TryFormat(charBuf, out int written, default, CultureInfo.InvariantCulture)
            ? charBuf[..written]
            : (fallback = parsed.ToString(null, CultureInfo.InvariantCulture)).AsSpan();

        if (typeof(T) == typeof(char))
        {
            if (!formatted.SequenceEqual(MemoryMarshal.Cast<T, char>(csvCol.Span)))
                throw CreateLossyNumericException(rowNumber, col);
            return;
        }

        if (typeof(T) == typeof(byte))
        {
            ReadOnlySpan<byte> rawBytes = MemoryMarshal.AsBytes(csvCol.Span);
            int maxBytes = Encoding.UTF8.GetMaxByteCount(formatted.Length);
            if (maxBytes <= 128)
            {
                Span<byte> encoded = stackalloc byte[128];
                int byteCount = Encoding.UTF8.GetBytes(formatted, encoded);
                if (!encoded[..byteCount].SequenceEqual(rawBytes))
                    throw CreateLossyNumericException(rowNumber, col);
                return;
            }

            if (!string.Equals(fallback ?? formatted.ToString(), csvCol.ToString(), StringComparison.Ordinal))
                throw CreateLossyNumericException(rowNumber, col);
        }
    }

    private static bool HasCompleteDate<T>(CsvColumn<T> csvCol)
        where T : unmanaged, IEquatable<T>
    {
        if (typeof(T) == typeof(char))
            return CsvSchemaInference.HasCompleteDate(MemoryMarshal.Cast<T, char>(csvCol.Span));

        if (typeof(T) == typeof(byte))
        {
            ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(csvCol.Span);
            int maxChars = Encoding.UTF8.GetMaxCharCount(bytes.Length);
            if (maxChars <= 128)
            {
                Span<char> chars = stackalloc char[128];
                int written = Encoding.UTF8.GetChars(bytes, chars);
                return CsvSchemaInference.HasCompleteDate(chars[..written]);
            }

            return CsvSchemaInference.HasCompleteDate(Encoding.UTF8.GetString(bytes));
        }

        throw new NotSupportedException($"Element type {typeof(T)} is not supported.");
    }

    private static bool HasExplicitDateBeforeOffset<T>(CsvColumn<T> csvCol)
        where T : unmanaged, IEquatable<T>
    {
        if (typeof(T) == typeof(char))
            return CsvSchemaInference.HasExplicitDateBeforeOffset(MemoryMarshal.Cast<T, char>(csvCol.Span));

        if (typeof(T) == typeof(byte))
        {
            ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(csvCol.Span);
            int maxChars = Encoding.UTF8.GetMaxCharCount(bytes.Length);
            if (maxChars <= 128)
            {
                Span<char> chars = stackalloc char[128];
                int written = Encoding.UTF8.GetChars(bytes, chars);
                return CsvSchemaInference.HasExplicitDateBeforeOffset(chars[..written]);
            }

            return CsvSchemaInference.HasExplicitDateBeforeOffset(Encoding.UTF8.GetString(bytes));
        }

        throw new NotSupportedException($"Element type {typeof(T)} is not supported.");
    }

    private static HtbException CreateLossyNumericException(int rowNumber, HtbColumn col)
        => new(
            HtbErrorCode.SerializationError,
            $"CSV row {rowNumber} column '{col.Name}' value cannot be losslessly represented as {col.DataType}.");

    private static bool HasExplicitTimeZoneOffset<T>(CsvColumn<T> csvCol)
        where T : unmanaged, IEquatable<T>
    {
        if (typeof(T) == typeof(char))
        {
            ReadOnlySpan<char> trimmed = MemoryMarshal.Cast<T, char>(csvCol.Span).TrimEnd();
            if (trimmed.IsEmpty)
                return false;

            if (trimmed[^1] is 'Z' or 'z' || trimmed.Contains('+'))
                return true;

            int lastDash = trimmed.LastIndexOf('-');
            return lastDash > 0 && trimmed[..lastDash].Contains(':');
        }

        if (typeof(T) == typeof(byte))
        {
            ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(csvCol.Span);
            int end = bytes.Length;
            while (end > 0 && bytes[end - 1] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
                end--;

            ReadOnlySpan<byte> trimmed = bytes[..end];
            if (trimmed.IsEmpty)
                return false;

            if (trimmed[^1] is (byte)'Z' or (byte)'z' || trimmed.Contains((byte)'+'))
                return true;

            int lastDash = trimmed.LastIndexOf((byte)'-');
            return lastDash > 0 && trimmed[..lastDash].Contains((byte)':');
        }

        throw new NotSupportedException($"Element type {typeof(T)} is not supported.");
    }

    /// <summary>
    /// A field is empty (written as null) when it has no text, or when it is a non-string/non-URI typed value
    /// whose only text is a pair of quotes (<c>""</c>). A quoted empty <c>String</c> stays an empty string,
    /// a quoted empty <c>Uri</c> stays an empty relative URI, and a non-null empty <c>FloatArray</c> is written as <c>[]</c>.
    /// </summary>
    private static bool IsEmptyValue<T>(CsvColumn<T> column, HtbDataType dataType, CsvToHtbOptions options)
        where T : unmanaged, IEquatable<T>
    {
        if (column.IsEmpty)
            return true;
        if (dataType is HtbDataType.String or HtbDataType.Uri)
            return false;
        return column.Unquote(ToElement<T>(options.Quote)).IsEmpty;
    }

    private static string UnquoteToString<T>(CsvColumn<T> column, CsvToHtbOptions options)
        where T : unmanaged, IEquatable<T>
    {
        var quote = ToElement<T>(options.Quote);
        return options.EscapeCharacter is char escape
            ? column.UnquoteToString(quote, ToElement<T>(escape))
            : column.UnquoteToString(quote);
    }

    private static T ToElement<T>(char value)
        where T : unmanaged, IEquatable<T>
    {
        if (typeof(T) == typeof(char))
            return (T)(object)value;
        if (typeof(T) == typeof(byte))
            return (T)(object)(byte)value;
        throw new NotSupportedException($"Element type {typeof(T)} is not supported.");
    }
}
