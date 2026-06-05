using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HeroParser.Htbs;
using HeroParser.Htbs.Records;
using HeroParser.Htbs.Writing;
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
                csvIndices = MapHeaders(row, schema);
                continue;
            }
            isFirst = false;

            csvIndices ??= MapSequential(row.ColumnCount, schema);

            writer.IncrementRecordCount();
            ConvertRow(row, csvIndices, schema, writer);
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

        string csvData = File.ReadAllText(csvPath);
        using var outputStream = File.Create(htbPath);
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
                csvIndices = MapHeaders(row, schema);
                continue;
            }
            isFirst = false;

            csvIndices ??= MapSequential(row.ColumnCount, schema);

            writer.IncrementRecordCount();
            ConvertRow(row, csvIndices, schema, writer);
        }

        await writer.FlushAsync().ConfigureAwait(false);
    }

    private static int[] MapHeaders<T>(CsvRow<T> headerRow, HtbSchema schema)
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
            headers[i] = headerRow.GetString(i);
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

    private static void ConvertRow<T>(CsvRow<T> row, int[] csvIndices, HtbSchema schema, HtbStreamWriter writer)
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

                var csvCol = row[csvIdx];
                if (csvCol.IsEmpty)
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

                var csvCol = row[csvIdx];
                if (csvCol.IsEmpty)
                    continue;

                var col = schema.Columns[i];
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
                        writer.WriteString(csvCol.UnquoteToString());
                        break;
                    case HtbDataType.FloatArray:
                        writer.WriteFloatArray(Vectors.VectorParser.ParseFloats(csvCol.UnquoteToString()));
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
}
