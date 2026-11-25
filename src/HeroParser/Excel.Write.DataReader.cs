using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Numerics;
using HeroParser.Excels.Core;
using HeroParser.Excels.Xlsx;
using HeroParser.Internal;

namespace HeroParser;

public static partial class Excel
{
    /// <summary>
    /// Streams rows from an <see cref="IDataReader"/> into an Excel (.xlsx) <see cref="Stream"/> without reflection.
    /// </summary>
    /// <param name="reader">The data reader providing columns and rows.</param>
    /// <param name="stream">The writable destination stream.</param>
    /// <param name="options">Optional Excel writer configuration; defaults to <see cref="ExcelWriteOptions.Default"/>.</param>
    /// <param name="sheetName">Worksheet name; defaults to <c>"Sheet1"</c>.</param>
    /// <param name="leaveOpen">When <see langword="true"/>, leaves <paramref name="stream"/> open after writing completes.</param>
    public static void WriteFromDataReader(
        IDataReader reader,
        Stream stream,
        ExcelWriteOptions? options = null,
        string sheetName = "Sheet1",
        bool leaveOpen = true)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentException.ThrowIfNullOrEmpty(sheetName);

        options ??= ExcelWriteOptions.Default;
        options.Validate();

        using var xlsxWriter = new XlsxWriter(stream, leaveOpen, options.InjectionProtection);
        using var sheetWriter = xlsxWriter.StartSheet(sheetName);

        int fieldCount = reader.FieldCount;
        int rowNumber = 1;

        if (options.WriteHeader && fieldCount > 0)
        {
            var headers = new string[fieldCount];
            for (int i = 0; i < fieldCount; i++)
                headers[i] = reader.GetName(i);
            sheetWriter.WriteHeaderRow(headers);
            rowNumber = 2;
        }

        DataReaderColumnPlan plan = default;
        int dataRowsWritten = 0;
        while (reader.Read())
        {
            if (options.MaxRowCount is { } maxRows && dataRowsWritten >= maxRows)
                break;

            if (!plan.IsInitialized)
                plan = new DataReaderColumnPlan(reader);

            WriteDataReaderRowToSheet(reader, sheetWriter, fieldCount, rowNumber++, options, plan);
            dataRowsWritten++;
        }

        sheetWriter.Close();
    }

    /// <summary>
    /// Streams rows from an <see cref="IDataReader"/> to an Excel (.xlsx) file without reflection.
    /// </summary>
    /// <param name="path">Destination file path.</param>
    /// <param name="reader">The data reader providing columns and rows.</param>
    /// <param name="options">Optional Excel writer configuration; defaults to <see cref="ExcelWriteOptions.Default"/>.</param>
    /// <param name="sheetName">Worksheet name; defaults to <c>"Sheet1"</c>.</param>
    public static void WriteFromDataReaderToFile(
        string path,
        IDataReader reader,
        ExcelWriteOptions? options = null,
        string sheetName = "Sheet1")
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentException.ThrowIfNullOrEmpty(sheetName);

        using var fileStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        WriteFromDataReader(reader, fileStream, options, sheetName, leaveOpen: false);
    }

    /// <summary>
    /// Serializes rows from an <see cref="IDataReader"/> into an in-memory Excel (.xlsx) byte array without reflection.
    /// </summary>
    /// <param name="reader">The data reader providing columns and rows.</param>
    /// <param name="options">Optional Excel writer configuration; defaults to <see cref="ExcelWriteOptions.Default"/>.</param>
    /// <param name="sheetName">Worksheet name; defaults to <c>"Sheet1"</c>.</param>
    /// <returns>The generated .xlsx file payload.</returns>
    public static byte[] WriteFromDataReaderToBytes(
        IDataReader reader,
        ExcelWriteOptions? options = null,
        string sheetName = "Sheet1")
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentException.ThrowIfNullOrEmpty(sheetName);

        using var ms = new MemoryStream();
        WriteFromDataReader(reader, ms, options, sheetName, leaveOpen: true);
        return ms.ToArray();
    }

    /// <summary>
    /// Asynchronously streams rows from a <see cref="DbDataReader"/> into an Excel (.xlsx) <see cref="Stream"/> without reflection.
    /// </summary>
    /// <param name="reader">The async-capable database data reader providing columns and rows.</param>
    /// <param name="stream">The writable destination stream.</param>
    /// <param name="options">Optional Excel writer configuration; defaults to <see cref="ExcelWriteOptions.Default"/>.</param>
    /// <param name="sheetName">Worksheet name; defaults to <c>"Sheet1"</c>.</param>
    /// <param name="leaveOpen">When <see langword="true"/>, leaves <paramref name="stream"/> open after writing completes.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public static async Task WriteFromDataReaderAsync(
        DbDataReader reader,
        Stream stream,
        ExcelWriteOptions? options = null,
        string sheetName = "Sheet1",
        bool leaveOpen = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentException.ThrowIfNullOrEmpty(sheetName);

        options ??= ExcelWriteOptions.Default;
        options.Validate();

        using var xlsxWriter = new XlsxWriter(stream, leaveOpen, options.InjectionProtection);
        using var sheetWriter = xlsxWriter.StartSheet(sheetName);

        int fieldCount = reader.FieldCount;
        int rowNumber = 1;

        if (options.WriteHeader && fieldCount > 0)
        {
            var headers = new string[fieldCount];
            for (int i = 0; i < fieldCount; i++)
                headers[i] = reader.GetName(i);
            sheetWriter.WriteHeaderRow(headers);
            rowNumber = 2;
        }

        DataReaderColumnPlan plan = default;
        int dataRowsWritten = 0;
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (options.MaxRowCount is { } maxRows && dataRowsWritten >= maxRows)
                break;

            if (!plan.IsInitialized)
                plan = new DataReaderColumnPlan(reader);

            WriteDataReaderRowToSheet(reader, sheetWriter, fieldCount, rowNumber++, options, plan);
            dataRowsWritten++;
        }

        sheetWriter.Close();
    }

    /// <summary>
    /// Asynchronously streams rows from a <see cref="DbDataReader"/> directly to an Excel (.xlsx) file without reflection.
    /// </summary>
    /// <param name="path">Destination file path.</param>
    /// <param name="reader">The async-capable database data reader providing columns and rows.</param>
    /// <param name="options">Optional Excel writer configuration; defaults to <see cref="ExcelWriteOptions.Default"/>.</param>
    /// <param name="sheetName">Worksheet name; defaults to <c>"Sheet1"</c>.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public static async Task WriteFromDataReaderToFileAsync(
        string path,
        DbDataReader reader,
        ExcelWriteOptions? options = null,
        string sheetName = "Sheet1",
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentException.ThrowIfNullOrEmpty(sheetName);

        var fileStream = new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.Asynchronous);
        await using (fileStream.ConfigureAwait(false))
        {
            await WriteFromDataReaderAsync(reader, fileStream, options, sheetName, leaveOpen: false, cancellationToken).ConfigureAwait(false);
        }
    }

    private static void WriteDataReaderRowToSheet(
        IDataRecord reader,
        XlsxWriter.SheetWriter sheetWriter,
        int fieldCount,
        int rowNumber,
        ExcelWriteOptions options,
        DataReaderColumnPlan plan)
    {
        sheetWriter.StartRow(rowNumber);
        for (int i = 0; i < fieldCount; i++)
        {
            int colIndex = i + 1;
            DataReaderColumnKind kind = plan[i];
            if (kind is not (DataReaderColumnKind.String or DataReaderColumnKind.Object) && reader.IsDBNull(i))
            {
                if (string.IsNullOrEmpty(options.NullValue))
                    sheetWriter.WriteCellEmpty(colIndex);
                else
                    sheetWriter.WriteCellString(colIndex, options.NullValue);
                continue;
            }

            switch (kind)
            {
                case DataReaderColumnKind.String:
                    {
                        object? raw = reader.GetValue(i);
                        if (raw is null or DBNull)
                        {
                            if (string.IsNullOrEmpty(options.NullValue))
                                sheetWriter.WriteCellEmpty(colIndex);
                            else
                                sheetWriter.WriteCellString(colIndex, options.NullValue);
                        }
                        else if (raw is string str)
                        {
                            sheetWriter.WriteCellString(colIndex, str);
                        }
                        else
                        {
                            WriteExcelCellValue(sheetWriter, colIndex, raw, options);
                        }
                    }
                    break;
                case DataReaderColumnKind.Object:
                    WriteExcelCellValue(sheetWriter, colIndex, reader.GetValue(i), options);
                    break;
                case DataReaderColumnKind.Boolean:
                    sheetWriter.WriteCellBoolean(colIndex, reader.GetBoolean(i));
                    break;
                case DataReaderColumnKind.Int32:
                    if (options.NumberFormat is not null)
                        WriteCellSpanFormattable(sheetWriter, colIndex, reader.GetInt32(i), options.NumberFormat, options.Culture);
                    else
                        sheetWriter.WriteCellNumber(colIndex, reader.GetInt32(i));
                    break;
                case DataReaderColumnKind.Int16:
                    if (options.NumberFormat is not null)
                        WriteCellSpanFormattable(sheetWriter, colIndex, reader.GetInt16(i), options.NumberFormat, options.Culture);
                    else
                        sheetWriter.WriteCellNumber(colIndex, reader.GetInt16(i));
                    break;
                case DataReaderColumnKind.Byte:
                    if (options.NumberFormat is not null)
                        WriteCellSpanFormattable(sheetWriter, colIndex, reader.GetByte(i), options.NumberFormat, options.Culture);
                    else
                        sheetWriter.WriteCellNumber(colIndex, reader.GetByte(i));
                    break;
                case DataReaderColumnKind.Int64:
                    {
                        long l = reader.GetInt64(i);
                        if (options.NumberFormat is not null || l is < -9_007_199_254_740_992L or > 9_007_199_254_740_992L)
                            WriteCellSpanFormattable(sheetWriter, colIndex, l, options.NumberFormat, options.Culture);
                        else
                            sheetWriter.WriteCellNumber(colIndex, l);
                    }
                    break;
                case DataReaderColumnKind.Single:
                    {
                        float f = reader.GetFloat(i);
                        if (options.NumberFormat is not null)
                            WriteCellSpanFormattable(sheetWriter, colIndex, f, options.NumberFormat, options.Culture);
                        else if (float.IsFinite(f))
                            sheetWriter.WriteCellNumber(colIndex, f);
                        else
                            WriteCellSpanFormattable(sheetWriter, colIndex, f, format: null, options.Culture);
                    }
                    break;
                case DataReaderColumnKind.Double:
                    {
                        double d = reader.GetDouble(i);
                        if (options.NumberFormat is not null)
                            WriteCellSpanFormattable(sheetWriter, colIndex, d, options.NumberFormat, options.Culture);
                        else if (double.IsFinite(d))
                            sheetWriter.WriteCellNumber(colIndex, d);
                        else
                            WriteCellSpanFormattable(sheetWriter, colIndex, d, format: null, options.Culture);
                    }
                    break;
                case DataReaderColumnKind.Decimal:
                    WriteCellSpanFormattable(sheetWriter, colIndex, reader.GetDecimal(i), options.NumberFormat, options.Culture);
                    break;
                case DataReaderColumnKind.DateTime:
                    {
                        DateTime dt = reader.GetDateTime(i);
                        if (options.DateTimeFormat is not null)
                            WriteCellSpanFormattable(sheetWriter, colIndex, dt, options.DateTimeFormat, options.Culture);
                        else
                            sheetWriter.WriteCellDate(colIndex, dt);
                    }
                    break;
                case DataReaderColumnKind.Guid:
                    WriteCellSpanFormattable(sheetWriter, colIndex, reader.GetGuid(i), format: null, options.Culture);
                    break;
                case DataReaderColumnKind.Char:
                    {
                        Span<char> chBuf = [reader.GetChar(i)];
                        sheetWriter.WriteCellString(colIndex, chBuf);
                    }
                    break;
                default:
                    break;
            }
        }
        sheetWriter.EndRow();
    }

    private static void WriteCellSpanFormattable<TValue>(
        XlsxWriter.SheetWriter sheetWriter,
        int columnIndex,
        TValue value,
        string? format,
        CultureInfo culture)
        where TValue : ISpanFormattable
    {
        Span<char> tmp = stackalloc char[128];
        if (value.TryFormat(tmp, out int written, format, culture))
            sheetWriter.WriteCellString(columnIndex, tmp[..written]);
        else
            sheetWriter.WriteCellString(columnIndex, ((IFormattable)value).ToString(format, culture));
    }

    private static void WriteExcelCellValue(
        XlsxWriter.SheetWriter sheetWriter,
        int columnIndex,
        object? value,
        ExcelWriteOptions options)
    {
        switch (value)
        {
            case null or DBNull:
                if (string.IsNullOrEmpty(options.NullValue))
                    sheetWriter.WriteCellEmpty(columnIndex);
                else
                    sheetWriter.WriteCellString(columnIndex, options.NullValue);
                break;

            case bool b:
                sheetWriter.WriteCellBoolean(columnIndex, b);
                break;

            case DateTime dt:
                if (options.DateTimeFormat is not null)
                    WriteCellSpanFormattable(sheetWriter, columnIndex, dt, options.DateTimeFormat, options.Culture);
                else
                    sheetWriter.WriteCellDate(columnIndex, dt);
                break;

            case DateTimeOffset dto:
                WriteCellSpanFormattable(sheetWriter, columnIndex, dto, options.DateTimeFormat ?? "O", options.Culture);
                break;

            case DateOnly d:
                if (options.DateOnlyFormat is not null)
                    WriteCellSpanFormattable(sheetWriter, columnIndex, d, options.DateOnlyFormat, options.Culture);
                else
                    sheetWriter.WriteCellDate(columnIndex, d.ToDateTime(TimeOnly.MinValue));
                break;

            case TimeOnly t:
                WriteCellSpanFormattable(sheetWriter, columnIndex, t, options.TimeOnlyFormat ?? "HH:mm:ss", options.Culture);
                break;

            case TimeSpan ts:
                WriteCellSpanFormattable(sheetWriter, columnIndex, ts, options.TimeSpanFormat ?? "c", options.Culture);
                break;

            case char ch:
                {
                    Span<char> chBuf = [ch];
                    sheetWriter.WriteCellString(columnIndex, chBuf);
                }
                break;

            case Half half:
                if (options.NumberFormat is not null)
                {
                    WriteCellSpanFormattable(sheetWriter, columnIndex, half, options.NumberFormat, options.Culture);
                }
                else
                {
                    double halfDouble = (double)half;
                    if (double.IsFinite(halfDouble))
                        sheetWriter.WriteCellNumber(columnIndex, halfDouble);
                    else
                        WriteCellSpanFormattable(sheetWriter, columnIndex, half, format: null, options.Culture);
                }
                break;

            case Int128 i128:
                WriteCellSpanFormattable(sheetWriter, columnIndex, i128, options.NumberFormat, options.Culture);
                break;

            case UInt128 u128:
                WriteCellSpanFormattable(sheetWriter, columnIndex, u128, options.NumberFormat, options.Culture);
                break;

            case BigInteger bi:
                WriteCellSpanFormattable(sheetWriter, columnIndex, bi, options.NumberFormat, options.Culture);
                break;

            case Uri uri:
                sheetWriter.WriteCellString(columnIndex, uri.OriginalString);
                break;

            case byte[] bytes:
                if (bytes.Length == 0)
                    sheetWriter.WriteCellEmpty(columnIndex);
                else
                    sheetWriter.WriteCellString(columnIndex, Convert.ToBase64String(bytes));
                break;

            case long l:
                if (options.NumberFormat is not null || l is < -9_007_199_254_740_992L or > 9_007_199_254_740_992L)
                    WriteCellSpanFormattable(sheetWriter, columnIndex, l, options.NumberFormat, options.Culture);
                else
                    sheetWriter.WriteCellNumber(columnIndex, l);
                break;

            case ulong ul:
                if (options.NumberFormat is not null || ul > 9_007_199_254_740_992UL)
                    WriteCellSpanFormattable(sheetWriter, columnIndex, ul, options.NumberFormat, options.Culture);
                else
                    sheetWriter.WriteCellNumber(columnIndex, ul);
                break;

            case decimal dec:
                WriteCellSpanFormattable(sheetWriter, columnIndex, dec, options.NumberFormat, options.Culture);
                break;

            case byte or sbyte or short or ushort or int or uint or float or double:
                if (options.NumberFormat is not null && value is ISpanFormattable numSpan)
                {
                    WriteCellSpanFormattable(sheetWriter, columnIndex, numSpan, options.NumberFormat, options.Culture);
                }
                else if (options.NumberFormat is not null && value is IFormattable numFormattable)
                {
                    sheetWriter.WriteCellString(columnIndex, numFormattable.ToString(options.NumberFormat, options.Culture));
                }
                else
                {
                    double num = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                    if (double.IsFinite(num))
                        sheetWriter.WriteCellNumber(columnIndex, num);
                    else
                        WriteCellSpanFormattable(sheetWriter, columnIndex, num, format: null, options.Culture);
                }
                break;

            case ISpanFormattable spanFormattable:
                WriteCellSpanFormattable(sheetWriter, columnIndex, spanFormattable, format: null, options.Culture);
                break;

            case IFormattable formattable:
                sheetWriter.WriteCellString(columnIndex, formattable.ToString(null, options.Culture));
                break;

            default:
                sheetWriter.WriteCellString(columnIndex, value.ToString() ?? string.Empty);
                break;
        }
    }
}
