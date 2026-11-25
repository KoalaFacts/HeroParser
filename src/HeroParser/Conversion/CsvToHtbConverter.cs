using System;
using System.IO;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using HeroParser.Htbs;
using HeroParser.Htbs.Records;
using HeroParser.Htbs.Writing;
using HeroParser.Internal;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Reading.Rows;

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
            MaxColumnCount = Math.Max(100, schema.Columns.Count)
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
        int[]? csvIndices = null;

        while (rowReader.MoveNext())
        {
            var row = rowReader.Current;
            if (isFirst && options.HasHeaderRow)
            {
                isFirst = false;
                csvIndices = MapHeaders(row, schema, options);
                continue;
            }
            isFirst = false;

            csvIndices ??= MapSequential(row.ColumnCount, schema);

            writer.IncrementRecordCount();
            ConvertRow(row, csvIndices, schema, writer, options);
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

        string csvData = new CompressedFile(csvPath).ReadAllText();
        using var outputStream = new CompressedFile(htbPath).OpenWrite();
        Convert(csvData, outputStream, schema, options);
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

        var parserReadOptions = new CsvReadOptions
        {
            Delimiter = options.Delimiter,
            Quote = options.Quote,
            EscapeCharacter = options.EscapeCharacter,
            AllowNewlinesInsideQuotes = options.AllowNewlinesInsideQuotes,
            MaxColumnCount = Math.Max(100, schema.Columns.Count)
        };
        await using var rowReader = Csv.CreateAsyncStreamReader(csvStream, parserReadOptions, leaveOpen: true);

        var htbWriteOptions = new HtbWriteOptions
        {
            MaxRowCount = options.MaxRowCount,
            Progress = options.Progress,
            ProgressIntervalRows = options.ProgressIntervalRows
        };
        using var writer = new HtbStreamWriter(htbStream, htbWriteOptions, leaveOpen: true);
        await writer.WriteHeaderAsync(schema).ConfigureAwait(false);

        bool isFirst = true;
        int[]? csvIndices = null;

        while (await rowReader.MoveNextAsync(cancellationToken).ConfigureAwait(false))
        {
            var row = rowReader.Current;
            if (isFirst && options.HasHeaderRow)
            {
                isFirst = false;
                csvIndices = MapHeaders(row, schema, options);
                continue;
            }
            isFirst = false;

            csvIndices ??= MapSequential(row.ColumnCount, schema);

            writer.IncrementRecordCount();
            ConvertRow(row, csvIndices, schema, writer, options);
        }

        await writer.FlushAsync().ConfigureAwait(false);
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

    private static int[] MapSequential(int columnCount, HtbSchema schema)
    {
        var csvIndices = new int[schema.Columns.Count];
        for (int i = 0; i < schema.Columns.Count; i++)
        {
            csvIndices[i] = i < columnCount ? i : -1;
        }
        return csvIndices;
    }

    private static void ConvertRow<T>(CsvRow<T> row, int[] csvIndices, HtbSchema schema, HtbStreamWriter writer, CsvToHtbOptions options)
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
                        writer.WriteInt32(i32);
                        break;
                    case HtbDataType.Int64:
                        if (!csvCol.TryParseInt64(out long i64))
                            throw new HtbException(HtbErrorCode.SerializationError, $"Failed to parse column '{col.Name}' value as Int64.");
                        writer.WriteInt64(i64);
                        break;
                    case HtbDataType.Float:
                        if (!csvCol.TryParseSingle(out float f32))
                            throw new HtbException(HtbErrorCode.SerializationError, $"Failed to parse column '{col.Name}' value as Float.");
                        writer.WriteFloat(f32);
                        break;
                    case HtbDataType.Double:
                        if (!csvCol.TryParseDouble(out double f64))
                            throw new HtbException(HtbErrorCode.SerializationError, $"Failed to parse column '{col.Name}' value as Double.");
                        writer.WriteDouble(f64);
                        break;
                    case HtbDataType.Decimal:
                        if (!csvCol.TryParseDecimal(out decimal dec))
                            throw new HtbException(HtbErrorCode.SerializationError, $"Failed to parse column '{col.Name}' value as Decimal.");
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
                        writer.WriteDateTimeOffset(dto);
                        break;
                    case HtbDataType.DateOnly:
                        if (!csvCol.TryParseDateOnly(out DateOnly dOnly))
                            throw new HtbException(HtbErrorCode.SerializationError, $"Failed to parse column '{col.Name}' value as DateOnly.");
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
                        writer.WriteInt16(i16);
                        break;
                    case HtbDataType.UInt16:
                        if (!csvCol.TryParseUInt16(out ushort u16))
                            throw new HtbException(HtbErrorCode.SerializationError, $"Failed to parse column '{col.Name}' value as UInt16.");
                        writer.WriteUInt16(u16);
                        break;
                    case HtbDataType.Byte:
                        if (!csvCol.TryParseByte(out byte u8))
                            throw new HtbException(HtbErrorCode.SerializationError, $"Failed to parse column '{col.Name}' value as Byte.");
                        writer.WriteByte(u8);
                        break;
                    case HtbDataType.SByte:
                        if (!csvCol.TryParseSByte(out sbyte i8))
                            throw new HtbException(HtbErrorCode.SerializationError, $"Failed to parse column '{col.Name}' value as SByte.");
                        writer.WriteSByte(i8);
                        break;
                    case HtbDataType.UInt32:
                        if (!csvCol.TryParseUInt32(out uint u32))
                            throw new HtbException(HtbErrorCode.SerializationError, $"Failed to parse column '{col.Name}' value as UInt32.");
                        writer.WriteUInt32(u32);
                        break;
                    case HtbDataType.UInt64:
                        if (!csvCol.TryParseUInt64(out ulong u64))
                            throw new HtbException(HtbErrorCode.SerializationError, $"Failed to parse column '{col.Name}' value as UInt64.");
                        writer.WriteUInt64(u64);
                        break;
                    case HtbDataType.Char:
                        if (!csvCol.TryParseChar(out char ch))
                            throw new HtbException(HtbErrorCode.SerializationError, $"Failed to parse column '{col.Name}' value as Char.");
                        writer.WriteChar(ch);
                        break;
                    case HtbDataType.Half:
                        if (!csvCol.TryParseHalf(out Half h))
                            throw new HtbException(HtbErrorCode.SerializationError, $"Failed to parse column '{col.Name}' value as Half.");
                        writer.WriteHalf(h);
                        break;
                    case HtbDataType.Int128:
                        if (!csvCol.TryParseInt128(out Int128 i128))
                            throw new HtbException(HtbErrorCode.SerializationError, $"Failed to parse column '{col.Name}' value as Int128.");
                        writer.WriteInt128(i128);
                        break;
                    case HtbDataType.UInt128:
                        if (!csvCol.TryParseUInt128(out UInt128 u128))
                            throw new HtbException(HtbErrorCode.SerializationError, $"Failed to parse column '{col.Name}' value as UInt128.");
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
                        writer.WriteBigInteger(bi);
                        break;
                    case HtbDataType.Uri:
                        if (!csvCol.TryParseUri(out Uri? uri))
                            throw new HtbException(HtbErrorCode.SerializationError, $"Failed to parse column '{col.Name}' value as Uri.");
                        writer.WriteUri(uri);
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

    /// <summary>
    /// A field is empty (written as null) when it has no text, or when it is a typed value whose only
    /// text is a pair of quotes (<c>""</c>). A quoted empty <c>String</c> or <c>FloatArray</c> stays an empty value.
    /// </summary>
    private static bool IsEmptyValue<T>(CsvColumn<T> column, HtbDataType dataType, CsvToHtbOptions options)
        where T : unmanaged, IEquatable<T>
    {
        if (column.IsEmpty)
            return true;
        if (dataType is HtbDataType.String or HtbDataType.FloatArray)
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
