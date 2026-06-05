using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HeroParser.Htbs;
using HeroParser.Htbs.Reading;
using HeroParser.Htbs.Records;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Writing;

namespace HeroParser.Conversion;

/// <summary>
/// Converts HTB binary data directly into the CSV text format.
/// </summary>
public static class HtbToCsvConverter
{
    /// <summary>
    /// Converts HTB binary data from a stream directly to the CSV format written into a TextWriter.
    /// </summary>
    public static void Convert(Stream htbStream, TextWriter csvWriter, HtbToCsvOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(htbStream);
        ArgumentNullException.ThrowIfNull(csvWriter);
        options ??= HtbToCsvOptions.Default;

        using var reader = new HtbStreamReader(htbStream, HtbReadOptions.Default, leaveOpen: true);
        var schema = reader.ParseHeader();

        var csvWriteOptions = new CsvWriteOptions
        {
            Delimiter = options.Delimiter,
            NewLine = options.NewLine,
            QuoteStyle = options.QuoteAll ? QuoteStyle.Always : QuoteStyle.WhenNeeded
        };

        using var writer = new CsvStreamWriter(csvWriter, csvWriteOptions, leaveOpen: true);

        // 1. Write Header Row if configured
        if (options.IncludeHeaderRow)
        {
            for (int i = 0; i < schema.Columns.Count; i++)
            {
                writer.WriteField(schema.Columns[i].Name);
            }
            writer.EndRow();
        }

        int colCount = schema.Columns.Count;
        int maskLen = (colCount + 7) / 8;
        byte[]? rented = null;
        Span<byte> mask = maskLen <= 128
            ? stackalloc byte[maskLen]
            : (rented = System.Buffers.ArrayPool<byte>.Shared.Rent(maskLen)).AsSpan(0, maskLen);

        try
        {
            // 2. Parse Records
            while (!reader.IsEndOfStream())
            {
                reader.IncrementRecordCount();
                reader.ReadMask(mask);

                for (int i = 0; i < colCount; i++)
                {
                    var col = schema.Columns[i];
                    bool isNull = (mask[i / 8] & (1 << (i % 8))) != 0;

                    if (isNull)
                    {
                        if (!col.IsNullable)
                        {
                            throw new HtbException(HtbErrorCode.CorruptData, $"Column '{col.Name}' is not nullable but null was read.");
                        }
                        writer.WriteField(null);
                        continue;
                    }

                    // Read and format directly
                    switch (col.DataType)
                    {
                        case HtbDataType.Int32:
                            writer.WriteField(reader.ReadInt32());
                            break;
                        case HtbDataType.Int64:
                            writer.WriteField(reader.ReadInt64());
                            break;
                        case HtbDataType.Float:
                            writer.WriteField(reader.ReadFloat());
                            break;
                        case HtbDataType.Double:
                            writer.WriteField(reader.ReadDouble());
                            break;
                        case HtbDataType.Decimal:
                            writer.WriteField(reader.ReadDecimal());
                            break;
                        case HtbDataType.Boolean:
                            writer.WriteField(reader.ReadBoolean());
                            break;
                        case HtbDataType.DateTime:
                            writer.WriteField(reader.ReadDateTime());
                            break;
                        case HtbDataType.Guid:
                            writer.WriteField(reader.ReadGuid());
                            break;
                        case HtbDataType.String:
                            writer.WriteField(reader.ReadString());
                            break;
                        case HtbDataType.FloatArray:
                            float[] arr = reader.ReadFloatArray();
                            writer.WriteField(FormatFloatArray(arr));
                            break;
                        case HtbDataType.Unknown:
                        default:
                            reader.SkipColumnValue(col.DataType);
                            writer.WriteField(null);
                            break;
                    }
                }

                writer.EndRow();

                // Progress report
                if (options.Progress != null && reader.RecordsRead % options.ProgressIntervalRows == 0)
                {
                    options.Progress.Report(new HtbProgress
                    {
                        BytesRead = reader.BytesRead,
                        RecordsRead = reader.RecordsRead
                    });
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

        writer.Flush();

        // Final progress report
        if (options.Progress != null && reader.RecordsRead > 0)
        {
            options.Progress.Report(new HtbProgress
            {
                BytesRead = reader.BytesRead,
                RecordsRead = reader.RecordsRead
            });
        }
    }

    /// <summary>
    /// Converts an HTB file directly to the CSV format written into a file path.
    /// </summary>
    public static void ConvertFile(string htbPath, string csvPath, HtbToCsvOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(htbPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(csvPath);

        using var inputStream = File.OpenRead(htbPath);
        using var outputWriter = new StreamWriter(csvPath, append: false, Encoding.UTF8);
        Convert(inputStream, outputWriter, options);
    }

    /// <summary>
    /// Asynchronously converts HTB binary data from a stream directly to the CSV format written into an output TextWriter.
    /// </summary>
    public static async Task ConvertAsync(
        Stream htbStream,
        TextWriter csvWriter,
        HtbToCsvOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(htbStream);
        ArgumentNullException.ThrowIfNull(csvWriter);
        options ??= HtbToCsvOptions.Default;

        using var reader = new HtbStreamReader(htbStream, HtbReadOptions.Default, leaveOpen: true);
        var schema = await reader.ParseHeaderAsync().ConfigureAwait(false);

        var csvWriteOptions = new CsvWriteOptions
        {
            Delimiter = options.Delimiter,
            NewLine = options.NewLine,
            QuoteStyle = options.QuoteAll ? QuoteStyle.Always : QuoteStyle.WhenNeeded
        };

        using var writer = new CsvStreamWriter(csvWriter, csvWriteOptions, leaveOpen: true);

        // 1. Write Header Row if configured
        if (options.IncludeHeaderRow)
        {
            for (int i = 0; i < schema.Columns.Count; i++)
            {
                writer.WriteField(schema.Columns[i].Name);
            }
            writer.EndRow();
        }

        int colCount = schema.Columns.Count;
        int maskLen = (colCount + 7) / 8;
        byte[] rented = System.Buffers.ArrayPool<byte>.Shared.Rent(maskLen);

        try
        {
            var mask = rented.AsMemory(0, maskLen);

            // 2. Parse Records
            while (!await reader.IsEndOfStreamAsync().ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                reader.IncrementRecordCount();
                await reader.ReadMaskAsync(mask).ConfigureAwait(false);

                for (int i = 0; i < colCount; i++)
                {
                    var col = schema.Columns[i];
                    bool isNull = (mask.Span[i / 8] & (1 << (i % 8))) != 0;

                    if (isNull)
                    {
                        if (!col.IsNullable)
                        {
                            throw new HtbException(HtbErrorCode.CorruptData, $"Column '{col.Name}' is not nullable but null was read.");
                        }
                        writer.WriteField(null);
                        continue;
                    }

                    // Read and format directly
                    switch (col.DataType)
                    {
                        case HtbDataType.Int32:
                            writer.WriteField(await reader.ReadInt32Async().ConfigureAwait(false));
                            break;
                        case HtbDataType.Int64:
                            writer.WriteField(await reader.ReadInt64Async().ConfigureAwait(false));
                            break;
                        case HtbDataType.Float:
                            writer.WriteField(await reader.ReadFloatAsync().ConfigureAwait(false));
                            break;
                        case HtbDataType.Double:
                            writer.WriteField(await reader.ReadDoubleAsync().ConfigureAwait(false));
                            break;
                        case HtbDataType.Decimal:
                            writer.WriteField(await reader.ReadDecimalAsync().ConfigureAwait(false));
                            break;
                        case HtbDataType.Boolean:
                            writer.WriteField(await reader.ReadBooleanAsync().ConfigureAwait(false));
                            break;
                        case HtbDataType.DateTime:
                            writer.WriteField(await reader.ReadDateTimeAsync().ConfigureAwait(false));
                            break;
                        case HtbDataType.Guid:
                            writer.WriteField(await reader.ReadGuidAsync().ConfigureAwait(false));
                            break;
                        case HtbDataType.String:
                            writer.WriteField(await reader.ReadStringAsync().ConfigureAwait(false));
                            break;
                        case HtbDataType.FloatArray:
                            float[] arr = await reader.ReadFloatArrayAsync().ConfigureAwait(false);
                            writer.WriteField(FormatFloatArray(arr));
                            break;
                        case HtbDataType.Unknown:
                        default:
                            await reader.SkipColumnValueAsync(col.DataType).ConfigureAwait(false);
                            writer.WriteField(null);
                            break;
                    }
                }

                writer.EndRow();

                // Progress report
                if (options.Progress != null && reader.RecordsRead % options.ProgressIntervalRows == 0)
                {
                    options.Progress.Report(new HtbProgress
                    {
                        BytesRead = reader.BytesRead,
                        RecordsRead = reader.RecordsRead
                    });
                }
            }
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(rented);
        }

        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);

        // Final progress report
        if (options.Progress != null && reader.RecordsRead > 0)
        {
            options.Progress.Report(new HtbProgress
            {
                BytesRead = reader.BytesRead,
                RecordsRead = reader.RecordsRead
            });
        }
    }

    private static string FormatFloatArray(float[] arr)
    {
        if (arr.Length == 0) return "[]";
        var sb = new StringBuilder();
        sb.Append('[');
        for (int k = 0; k < arr.Length; k++)
        {
            if (k > 0) sb.Append(',');
            sb.Append(arr[k].ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        sb.Append(']');
        return sb.ToString();
    }
}
