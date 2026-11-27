using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Numerics;
using HeroParser.Excels.Core;
using HeroParser.Excels.Writing;
using HeroParser.Excels.Xlsx;
using HeroParser.Internal;

namespace HeroParser;

public static partial class Excel
{
    private static readonly DateTime excelFirstExactSerialDateTime = new(1900, 3, 1);
    private static readonly DateOnly excelFirstExactSerialDateOnly = new(1900, 3, 1);

    private static bool IsExcelNumericDouble(double value) => value == 0d || double.IsNormal(value);

    private static bool RequiresTextDateTimeFallback(DateTime value)
        => value < excelFirstExactSerialDateTime || value.Ticks % TimeSpan.TicksPerSecond != 0;

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

        bool streamOwnedByWriter = false;
        try
        {
            options ??= ExcelWriteOptions.Default;
            options.Validate();

            IProgress<ExcelWriteProgress>? progress = options.WriteProgress;
            int intervalRows = options.WriteProgressIntervalRows > 0
                ? options.WriteProgressIntervalRows
                : 1000;

            using var xlsxWriter = XlsxWriter.CreateStreaming(stream, leaveOpen, options.InjectionProtection);
            streamOwnedByWriter = true;
            using var sheetWriter = xlsxWriter.StartSheet(sheetName);

            int fieldCount = reader.FieldCount;
            int rowNumber = 1;

            if (options.WriteHeader && fieldCount > 0)
            {
                var headers = new string[fieldCount];
                for (int i = 0; i < fieldCount; i++)
                    headers[i] = reader.GetName(i);
                sheetWriter.WriteHeaderRow(headers);
                EnforceMaxOutputSize(sheetWriter, options);
                rowNumber = 2;
            }

            DataReaderColumnPlan plan = default;
            string[]? columnNames = null;
            Type[]? columnTypes = null;
            StagedExcelCell[]? stagedRow = null;
            int dataRowsWritten = 0;
            int sourceRowNumber = 0;
            while ((options.MaxRowCount is not { } maxRows || dataRowsWritten < maxRows) && reader.Read())
            {
                sourceRowNumber++;
                if (!plan.IsInitialized)
                    plan = new DataReaderColumnPlan(reader);

                if (options.OnSerializeError is { } errorHandler)
                {
                    if (stagedRow is null)
                    {
                        columnNames = CreateColumnNames(reader, fieldCount);
                        columnTypes = CreateColumnTypes(reader, fieldCount);
                        stagedRow = new StagedExcelCell[fieldCount];
                    }

                    if (!TryStageDataReaderRow(reader, sheetWriter, fieldCount, plan, options, errorHandler, sheetName, sourceRowNumber, columnNames!, columnTypes!, stagedRow))
                        continue;

                    CommitStagedRow(sheetWriter, rowNumber++, stagedRow);
                }
                else
                {
                    WriteDataReaderRowToSheet(reader, sheetWriter, fieldCount, rowNumber++, options, plan);
                }

                EnforceMaxOutputSize(sheetWriter, options);
                dataRowsWritten++;

                if (progress is not null && dataRowsWritten % intervalRows == 0)
                    progress.Report(new ExcelWriteProgress { RowsWritten = dataRowsWritten, SheetName = sheetName });
            }

            if (progress is not null && (dataRowsWritten == 0 || dataRowsWritten % intervalRows != 0))
                progress.Report(new ExcelWriteProgress { RowsWritten = dataRowsWritten, SheetName = sheetName });

            sheetWriter.Close();
            EnforceMaxOutputSize(sheetWriter, options);
        }
        finally
        {
            if (!leaveOpen && !streamOwnedByWriter)
                stream.Dispose();
        }
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

        try
        {
            options ??= ExcelWriteOptions.Default;
            options.Validate();

            // Stage small exports in memory and spill large exports to a temp file so the destination stream receives only async writes with bounded RAM.
            await using var staging = new SpillToFileBufferStream();
            await WriteDataReaderToXlsxAsync(reader, staging, options, sheetName, cancellationToken).ConfigureAwait(false);

            staging.Position = 0;
            await staging.CopyToAsync(stream, 65_536, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (!leaveOpen)
                await stream.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static async Task WriteDataReaderToXlsxAsync(
        DbDataReader reader,
        Stream xlsxTarget,
        ExcelWriteOptions options,
        string sheetName,
        CancellationToken cancellationToken)
    {
        IProgress<ExcelWriteProgress>? progress = options.WriteProgress;
        int intervalRows = options.WriteProgressIntervalRows > 0
            ? options.WriteProgressIntervalRows
            : 1000;

        bool hasRow = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);

        using var xlsxWriter = XlsxWriter.CreateStreaming(xlsxTarget, leaveOpen: true, options.InjectionProtection);
        using var sheetWriter = xlsxWriter.StartSheet(sheetName);

        int fieldCount = reader.FieldCount;
        int rowNumber = 1;

        if (options.WriteHeader && fieldCount > 0)
        {
            var headers = new string[fieldCount];
            for (int i = 0; i < fieldCount; i++)
                headers[i] = reader.GetName(i);
            sheetWriter.WriteHeaderRow(headers);
            EnforceMaxOutputSize(sheetWriter, options);
            rowNumber = 2;
        }

        bool useAsyncFieldValues = DataReaderColumnPlan.HasAsyncFieldValueOverride(reader);
        DataReaderColumnPlan plan = default;
        string[]? columnNames = null;
        Type[]? columnTypes = null;
        StagedExcelCell[]? stagedRow = null;
        int dataRowsWritten = 0;
        int sourceRowNumber = 0;
        while (hasRow)
        {
            if (options.MaxRowCount is { } maxRowsBefore && dataRowsWritten >= maxRowsBefore)
                break;

            sourceRowNumber++;
            if (!plan.IsInitialized)
                plan = new DataReaderColumnPlan(reader);

            bool written = true;
            if (options.OnSerializeError is { } errorHandler)
            {
                if (stagedRow is null)
                {
                    columnNames = CreateColumnNames(reader, fieldCount);
                    columnTypes = CreateColumnTypes(reader, fieldCount);
                    stagedRow = new StagedExcelCell[fieldCount];
                }

                written = useAsyncFieldValues
                    ? await TryStageDataReaderRowAsync(reader, sheetWriter, fieldCount, plan, options, errorHandler, sheetName, sourceRowNumber, columnNames!, columnTypes!, stagedRow, cancellationToken).ConfigureAwait(false)
                    : TryStageDataReaderRow(reader, sheetWriter, fieldCount, plan, options, errorHandler, sheetName, sourceRowNumber, columnNames!, columnTypes!, stagedRow);

                if (written)
                    CommitStagedRow(sheetWriter, rowNumber++, stagedRow);
            }
            else
            {
                if (useAsyncFieldValues)
                    await WriteDataReaderRowToSheetAsync(reader, sheetWriter, fieldCount, rowNumber++, options, plan, cancellationToken).ConfigureAwait(false);
                else
                    WriteDataReaderRowToSheet(reader, sheetWriter, fieldCount, rowNumber++, options, plan);
            }

            if (written)
            {
                EnforceMaxOutputSize(sheetWriter, options);
                dataRowsWritten++;

                if (progress is not null && dataRowsWritten % intervalRows == 0)
                    progress.Report(new ExcelWriteProgress { RowsWritten = dataRowsWritten, SheetName = sheetName });

                if (options.MaxRowCount is { } maxRows && dataRowsWritten >= maxRows)
                    break;
            }

            hasRow = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        }

        if (progress is not null && (dataRowsWritten == 0 || dataRowsWritten % intervalRows != 0))
            progress.Report(new ExcelWriteProgress { RowsWritten = dataRowsWritten, SheetName = sheetName });

        sheetWriter.Close();
        EnforceMaxOutputSize(sheetWriter, options);
    }

    private static void EnforceMaxOutputSize(XlsxWriter.SheetWriter sheetWriter, ExcelWriteOptions options)
    {
        if (options.MaxOutputSize is { } maxOutputSize && sheetWriter.BytesWritten > maxOutputSize)
            throw new ExcelException($"Maximum output size of {maxOutputSize} bytes exceeded.");
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

        options ??= ExcelWriteOptions.Default;
        options.Validate();
        cancellationToken.ThrowIfCancellationRequested();

        var fileStream = new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 65_536,
            FileOptions.SequentialScan);
        await using (fileStream.ConfigureAwait(false))
        {
            await WriteDataReaderToXlsxAsync(reader, fileStream, options, sheetName, cancellationToken).ConfigureAwait(false);
            await fileStream.FlushAsync(cancellationToken).ConfigureAwait(false);
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
                        if (options.NumberFormat is not null)
                            WriteCellSpanFormattable(sheetWriter, colIndex, l, options.NumberFormat, options.Culture);
                        else if (l is < -999_999_999_999_999L or > 999_999_999_999_999L)
                            WriteCellSpanFormattable(sheetWriter, colIndex, l, format: null, CultureInfo.InvariantCulture);
                        else
                            sheetWriter.WriteCellNumber(colIndex, l);
                    }
                    break;
                case DataReaderColumnKind.Single:
                    {
                        float f = reader.GetFloat(i);
                        if (options.NumberFormat is not null)
                            WriteCellSpanFormattable(sheetWriter, colIndex, f, options.NumberFormat, options.Culture);
                        else if (IsExcelNumericDouble(f))
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
                        else if (IsExcelNumericDouble(d))
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
                        else if (RequiresTextDateTimeFallback(dt))
                            WriteCellSpanFormattable(sheetWriter, colIndex, dt, "yyyy-MM-ddTHH:mm:ss.fffffff", CultureInfo.InvariantCulture);
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
                        sheetWriter.WriteCellLiteralString(colIndex, chBuf);
                    }
                    break;
                default:
                    break;
            }
        }
        sheetWriter.EndRow();
    }

    private static async Task WriteDataReaderRowToSheetAsync(
        DbDataReader reader,
        XlsxWriter.SheetWriter sheetWriter,
        int fieldCount,
        int rowNumber,
        ExcelWriteOptions options,
        DataReaderColumnPlan plan,
        CancellationToken cancellationToken)
    {
        sheetWriter.StartRow(rowNumber);
        for (int i = 0; i < fieldCount; i++)
        {
            int colIndex = i + 1;
            if (await reader.IsDBNullAsync(i, cancellationToken).ConfigureAwait(false))
            {
                if (string.IsNullOrEmpty(options.NullValue))
                    sheetWriter.WriteCellEmpty(colIndex);
                else
                    sheetWriter.WriteCellString(colIndex, options.NullValue);
                continue;
            }

            switch (plan[i])
            {
                case DataReaderColumnKind.String:
                    {
                        string? str = await reader.GetFieldValueAsync<string?>(i, cancellationToken).ConfigureAwait(false);
                        if (str is null)
                        {
                            if (string.IsNullOrEmpty(options.NullValue))
                                sheetWriter.WriteCellEmpty(colIndex);
                            else
                                sheetWriter.WriteCellString(colIndex, options.NullValue);
                        }
                        else
                        {
                            sheetWriter.WriteCellString(colIndex, str);
                        }
                    }
                    break;
                case DataReaderColumnKind.Object:
                    WriteExcelCellValue(sheetWriter, colIndex, await reader.GetFieldValueAsync<object?>(i, cancellationToken).ConfigureAwait(false), options);
                    break;
                case DataReaderColumnKind.Boolean:
                    sheetWriter.WriteCellBoolean(colIndex, await reader.GetFieldValueAsync<bool>(i, cancellationToken).ConfigureAwait(false));
                    break;
                case DataReaderColumnKind.Int32:
                    {
                        int i32 = await reader.GetFieldValueAsync<int>(i, cancellationToken).ConfigureAwait(false);
                        if (options.NumberFormat is not null)
                            WriteCellSpanFormattable(sheetWriter, colIndex, i32, options.NumberFormat, options.Culture);
                        else
                            sheetWriter.WriteCellNumber(colIndex, i32);
                    }
                    break;
                case DataReaderColumnKind.Int16:
                    {
                        short i16 = await reader.GetFieldValueAsync<short>(i, cancellationToken).ConfigureAwait(false);
                        if (options.NumberFormat is not null)
                            WriteCellSpanFormattable(sheetWriter, colIndex, i16, options.NumberFormat, options.Culture);
                        else
                            sheetWriter.WriteCellNumber(colIndex, i16);
                    }
                    break;
                case DataReaderColumnKind.Byte:
                    {
                        byte u8 = await reader.GetFieldValueAsync<byte>(i, cancellationToken).ConfigureAwait(false);
                        if (options.NumberFormat is not null)
                            WriteCellSpanFormattable(sheetWriter, colIndex, u8, options.NumberFormat, options.Culture);
                        else
                            sheetWriter.WriteCellNumber(colIndex, u8);
                    }
                    break;
                case DataReaderColumnKind.Int64:
                    {
                        long l = await reader.GetFieldValueAsync<long>(i, cancellationToken).ConfigureAwait(false);
                        if (options.NumberFormat is not null)
                            WriteCellSpanFormattable(sheetWriter, colIndex, l, options.NumberFormat, options.Culture);
                        else if (l is < -999_999_999_999_999L or > 999_999_999_999_999L)
                            WriteCellSpanFormattable(sheetWriter, colIndex, l, format: null, CultureInfo.InvariantCulture);
                        else
                            sheetWriter.WriteCellNumber(colIndex, l);
                    }
                    break;
                case DataReaderColumnKind.Single:
                    {
                        float f = await reader.GetFieldValueAsync<float>(i, cancellationToken).ConfigureAwait(false);
                        if (options.NumberFormat is not null)
                            WriteCellSpanFormattable(sheetWriter, colIndex, f, options.NumberFormat, options.Culture);
                        else if (IsExcelNumericDouble(f))
                            sheetWriter.WriteCellNumber(colIndex, f);
                        else
                            WriteCellSpanFormattable(sheetWriter, colIndex, f, format: null, options.Culture);
                    }
                    break;
                case DataReaderColumnKind.Double:
                    {
                        double d = await reader.GetFieldValueAsync<double>(i, cancellationToken).ConfigureAwait(false);
                        if (options.NumberFormat is not null)
                            WriteCellSpanFormattable(sheetWriter, colIndex, d, options.NumberFormat, options.Culture);
                        else if (IsExcelNumericDouble(d))
                            sheetWriter.WriteCellNumber(colIndex, d);
                        else
                            WriteCellSpanFormattable(sheetWriter, colIndex, d, format: null, options.Culture);
                    }
                    break;
                case DataReaderColumnKind.Decimal:
                    WriteCellSpanFormattable(sheetWriter, colIndex, await reader.GetFieldValueAsync<decimal>(i, cancellationToken).ConfigureAwait(false), options.NumberFormat, options.Culture);
                    break;
                case DataReaderColumnKind.DateTime:
                    {
                        DateTime dt = await reader.GetFieldValueAsync<DateTime>(i, cancellationToken).ConfigureAwait(false);
                        if (options.DateTimeFormat is not null)
                            WriteCellSpanFormattable(sheetWriter, colIndex, dt, options.DateTimeFormat, options.Culture);
                        else if (RequiresTextDateTimeFallback(dt))
                            WriteCellSpanFormattable(sheetWriter, colIndex, dt, "yyyy-MM-ddTHH:mm:ss.fffffff", CultureInfo.InvariantCulture);
                        else
                            sheetWriter.WriteCellDate(colIndex, dt);
                    }
                    break;
                case DataReaderColumnKind.Guid:
                    WriteCellSpanFormattable(sheetWriter, colIndex, await reader.GetFieldValueAsync<Guid>(i, cancellationToken).ConfigureAwait(false), format: null, options.Culture);
                    break;
                case DataReaderColumnKind.Char:
                    WriteExcelCharCell(sheetWriter, colIndex, await reader.GetFieldValueAsync<char>(i, cancellationToken).ConfigureAwait(false));
                    break;
                default:
                    break;
            }
        }
        sheetWriter.EndRow();
    }

    private static void WriteExcelCharCell(XlsxWriter.SheetWriter sheetWriter, int columnIndex, char value)
    {
        Span<char> chBuf = [value];
        sheetWriter.WriteCellLiteralString(columnIndex, chBuf);
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
                else if (RequiresTextDateTimeFallback(dt))
                    WriteCellSpanFormattable(sheetWriter, columnIndex, dt, "yyyy-MM-ddTHH:mm:ss.fffffff", CultureInfo.InvariantCulture);
                else
                    sheetWriter.WriteCellDate(columnIndex, dt);
                break;

            case DateTimeOffset dto:
                WriteCellSpanFormattable(sheetWriter, columnIndex, dto, options.DateTimeFormat ?? "O", options.Culture);
                break;

            case DateOnly d:
                if (options.DateOnlyFormat is not null)
                    WriteCellSpanFormattable(sheetWriter, columnIndex, d, options.DateOnlyFormat, options.Culture);
                else if (d < excelFirstExactSerialDateOnly)
                    WriteCellSpanFormattable(sheetWriter, columnIndex, d, "yyyy-MM-dd", CultureInfo.InvariantCulture);
                else
                    sheetWriter.WriteCellDate(columnIndex, d.ToDateTime(TimeOnly.MinValue));
                break;

            case TimeOnly t:
                WriteCellSpanFormattable(sheetWriter, columnIndex, t, options.TimeOnlyFormat ?? (t.Ticks % TimeSpan.TicksPerSecond == 0 ? "HH:mm:ss" : "O"), options.Culture);
                break;

            case TimeSpan ts:
                WriteCellSpanFormattable(sheetWriter, columnIndex, ts, options.TimeSpanFormat ?? "c", options.Culture);
                break;

            case char ch:
                {
                    Span<char> chBuf = [ch];
                    sheetWriter.WriteCellLiteralString(columnIndex, chBuf);
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
                sheetWriter.WriteCellLiteralString(
                    columnIndex,
                    bytes.Length == 0 ? Utf8SpanParser.EMPTY_BASE64_TOKEN : Convert.ToBase64String(bytes));
                break;

            case long l:
                if (options.NumberFormat is not null)
                    WriteCellSpanFormattable(sheetWriter, columnIndex, l, options.NumberFormat, options.Culture);
                else if (l is < -999_999_999_999_999L or > 999_999_999_999_999L)
                    WriteCellSpanFormattable(sheetWriter, columnIndex, l, format: null, CultureInfo.InvariantCulture);
                else
                    sheetWriter.WriteCellNumber(columnIndex, l);
                break;

            case ulong ul:
                if (options.NumberFormat is not null)
                    WriteCellSpanFormattable(sheetWriter, columnIndex, ul, options.NumberFormat, options.Culture);
                else if (ul > 999_999_999_999_999UL)
                    WriteCellSpanFormattable(sheetWriter, columnIndex, ul, format: null, CultureInfo.InvariantCulture);
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
                    if (IsExcelNumericDouble(num))
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

    private static string[] CreateColumnNames(IDataRecord reader, int fieldCount)
    {
        var names = new string[fieldCount];
        for (int i = 0; i < fieldCount; i++)
            names[i] = reader.GetName(i);
        return names;
    }

    private static Type[] CreateColumnTypes(IDataRecord reader, int fieldCount)
    {
        var types = new Type[fieldCount];
        for (int i = 0; i < fieldCount; i++)
            types[i] = reader.GetFieldType(i) ?? typeof(object);
        return types;
    }

    private static bool TryStageDataReaderRow(
        IDataRecord reader,
        XlsxWriter.SheetWriter sheetWriter,
        int fieldCount,
        DataReaderColumnPlan plan,
        ExcelWriteOptions options,
        ExcelSerializeErrorHandler errorHandler,
        string sheetName,
        int sourceRowNumber,
        string[] columnNames,
        Type[] columnTypes,
        StagedExcelCell[] stagedRow)
    {
        for (int i = 0; i < fieldCount; i++)
        {
            object? rawValue = null;
            try
            {
                stagedRow[i] = ReadAndStageDataReaderCell(reader, sheetWriter, i, plan[i], options, out rawValue);
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
                if (!HandleDataReaderSerializeError(errorHandler, sourceRowNumber, i + 1, columnNames[i], columnTypes[i], rawValue, ex, sheetName))
                    return false;
                stagedRow[i] = StagedExcelCell.Empty;
            }
        }
        return true;
    }

    private static async Task<bool> TryStageDataReaderRowAsync(
        DbDataReader reader,
        XlsxWriter.SheetWriter sheetWriter,
        int fieldCount,
        DataReaderColumnPlan plan,
        ExcelWriteOptions options,
        ExcelSerializeErrorHandler errorHandler,
        string sheetName,
        int sourceRowNumber,
        string[] columnNames,
        Type[] columnTypes,
        StagedExcelCell[] stagedRow,
        CancellationToken cancellationToken)
    {
        for (int i = 0; i < fieldCount; i++)
        {
            object? rawValue = null;
            try
            {
                if (await reader.IsDBNullAsync(i, cancellationToken).ConfigureAwait(false))
                {
                    stagedRow[i] = StageNullCell(sheetWriter, options);
                    continue;
                }

                if (plan[i] == DataReaderColumnKind.String)
                {
                    string? str = await reader.GetFieldValueAsync<string?>(i, cancellationToken).ConfigureAwait(false);
                    rawValue = str;
                    stagedRow[i] = StageStringOrObjectCell(sheetWriter, str, options);
                    continue;
                }

                stagedRow[i] = plan[i] switch
                {
                    DataReaderColumnKind.Object => StageExcelCellValue(sheetWriter, rawValue = await reader.GetFieldValueAsync<object?>(i, cancellationToken).ConfigureAwait(false), options),
                    DataReaderColumnKind.Boolean => StagedExcelCell.FromBool(await reader.GetFieldValueAsync<bool>(i, cancellationToken).ConfigureAwait(false)),
                    DataReaderColumnKind.Int32 => StageInt32(sheetWriter, (int)(rawValue = await reader.GetFieldValueAsync<int>(i, cancellationToken).ConfigureAwait(false)), options),
                    DataReaderColumnKind.Int16 => StageInt16(sheetWriter, (short)(rawValue = await reader.GetFieldValueAsync<short>(i, cancellationToken).ConfigureAwait(false)), options),
                    DataReaderColumnKind.Byte => StageByte(sheetWriter, (byte)(rawValue = await reader.GetFieldValueAsync<byte>(i, cancellationToken).ConfigureAwait(false)), options),
                    DataReaderColumnKind.Int64 => StageInt64(sheetWriter, (long)(rawValue = await reader.GetFieldValueAsync<long>(i, cancellationToken).ConfigureAwait(false)), options),
                    DataReaderColumnKind.Single => StageSingle(sheetWriter, (float)(rawValue = await reader.GetFieldValueAsync<float>(i, cancellationToken).ConfigureAwait(false)), options),
                    DataReaderColumnKind.Double => StageDouble(sheetWriter, (double)(rawValue = await reader.GetFieldValueAsync<double>(i, cancellationToken).ConfigureAwait(false)), options),
                    DataReaderColumnKind.Decimal => StageSpanFormattable(sheetWriter, (decimal)(rawValue = await reader.GetFieldValueAsync<decimal>(i, cancellationToken).ConfigureAwait(false)), options.NumberFormat, options.Culture),
                    DataReaderColumnKind.DateTime => StageDateTime(sheetWriter, (DateTime)(rawValue = await reader.GetFieldValueAsync<DateTime>(i, cancellationToken).ConfigureAwait(false)), options),
                    DataReaderColumnKind.Guid => StageSpanFormattable(sheetWriter, (Guid)(rawValue = await reader.GetFieldValueAsync<Guid>(i, cancellationToken).ConfigureAwait(false)), format: null, options.Culture),
                    DataReaderColumnKind.Char => StageChar(sheetWriter, (char)(rawValue = await reader.GetFieldValueAsync<char>(i, cancellationToken).ConfigureAwait(false))),
                    _ => StagedExcelCell.Empty,
                };
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
                if (!HandleDataReaderSerializeError(errorHandler, sourceRowNumber, i + 1, columnNames[i], columnTypes[i], rawValue, ex, sheetName))
                    return false;
                stagedRow[i] = StagedExcelCell.Empty;
            }
        }
        return true;
    }

    private static bool HandleDataReaderSerializeError(
        ExcelSerializeErrorHandler errorHandler,
        int row,
        int column,
        string memberName,
        Type sourceType,
        object? value,
        Exception ex,
        string sheetName)
    {
        var ctx = new ExcelSerializeErrorContext
        {
            Row = row,
            Column = column,
            MemberName = memberName,
            SourceType = sourceType,
            Value = value,
            Exception = ex,
            SheetName = sheetName
        };

        return errorHandler(ctx) switch
        {
            ExcelSerializeErrorAction.SkipRow => false,
            ExcelSerializeErrorAction.WriteEmpty => true,
            ExcelSerializeErrorAction.Throw or _ => throw new ExcelException(
                $"Serialization error at row {row}, column {column} ({memberName}).", ex)
        };
    }

    private static StagedExcelCell ReadAndStageDataReaderCell(
        IDataRecord reader,
        XlsxWriter.SheetWriter sheetWriter,
        int ordinal,
        DataReaderColumnKind kind,
        ExcelWriteOptions options,
        out object? rawValue)
    {
        rawValue = null;
        if (kind is not (DataReaderColumnKind.String or DataReaderColumnKind.Object) && reader.IsDBNull(ordinal))
            return StageNullCell(sheetWriter, options);

        switch (kind)
        {
            case DataReaderColumnKind.String:
                rawValue = reader.GetValue(ordinal);
                if (rawValue is null or DBNull)
                    return StageNullCell(sheetWriter, options);
                if (rawValue is string str)
                    return StagedExcelCell.Literal(sheetWriter.PrepareStringCell(str, applyInjectionProtection: true));
                return StageExcelCellValue(sheetWriter, rawValue, options);

            case DataReaderColumnKind.Object:
                rawValue = reader.GetValue(ordinal);
                return StageExcelCellValue(sheetWriter, rawValue, options);

            case DataReaderColumnKind.Boolean:
                bool b = reader.GetBoolean(ordinal);
                rawValue = b;
                return StagedExcelCell.FromBool(b);

            case DataReaderColumnKind.Int32:
                int i32 = reader.GetInt32(ordinal);
                rawValue = i32;
                return StageInt32(sheetWriter, i32, options);

            case DataReaderColumnKind.Int16:
                short i16 = reader.GetInt16(ordinal);
                rawValue = i16;
                return StageInt16(sheetWriter, i16, options);

            case DataReaderColumnKind.Byte:
                byte u8 = reader.GetByte(ordinal);
                rawValue = u8;
                return StageByte(sheetWriter, u8, options);

            case DataReaderColumnKind.Int64:
                long l = reader.GetInt64(ordinal);
                rawValue = l;
                return StageInt64(sheetWriter, l, options);

            case DataReaderColumnKind.Single:
                float f = reader.GetFloat(ordinal);
                rawValue = f;
                return StageSingle(sheetWriter, f, options);

            case DataReaderColumnKind.Double:
                double d = reader.GetDouble(ordinal);
                rawValue = d;
                return StageDouble(sheetWriter, d, options);

            case DataReaderColumnKind.Decimal:
                decimal dec = reader.GetDecimal(ordinal);
                rawValue = dec;
                return StageSpanFormattable(sheetWriter, dec, options.NumberFormat, options.Culture);

            case DataReaderColumnKind.DateTime:
                DateTime dt = reader.GetDateTime(ordinal);
                rawValue = dt;
                return StageDateTime(sheetWriter, dt, options);

            case DataReaderColumnKind.Guid:
                Guid g = reader.GetGuid(ordinal);
                rawValue = g;
                return StageSpanFormattable(sheetWriter, g, format: null, options.Culture);

            case DataReaderColumnKind.Char:
                char ch = reader.GetChar(ordinal);
                rawValue = ch;
                return StageChar(sheetWriter, ch);

            default:
                return StagedExcelCell.Empty;
        }
    }

    private static StagedExcelCell StageNullCell(XlsxWriter.SheetWriter sheetWriter, ExcelWriteOptions options)
        => string.IsNullOrEmpty(options.NullValue)
            ? StagedExcelCell.Empty
            : StagedExcelCell.Literal(sheetWriter.PrepareStringCell(options.NullValue, applyInjectionProtection: true));

    private static StagedExcelCell StageStringOrObjectCell(XlsxWriter.SheetWriter sheetWriter, string? value, ExcelWriteOptions options)
        => value is null
            ? StageNullCell(sheetWriter, options)
            : StagedExcelCell.Literal(sheetWriter.PrepareStringCell(value, applyInjectionProtection: true));

    private static StagedExcelCell StageInt32(XlsxWriter.SheetWriter sheetWriter, int value, ExcelWriteOptions options)
        => options.NumberFormat is not null
            ? StageSpanFormattable(sheetWriter, value, options.NumberFormat, options.Culture)
            : StagedExcelCell.FromNumber(value);

    private static StagedExcelCell StageInt16(XlsxWriter.SheetWriter sheetWriter, short value, ExcelWriteOptions options)
        => options.NumberFormat is not null
            ? StageSpanFormattable(sheetWriter, value, options.NumberFormat, options.Culture)
            : StagedExcelCell.FromNumber(value);

    private static StagedExcelCell StageByte(XlsxWriter.SheetWriter sheetWriter, byte value, ExcelWriteOptions options)
        => options.NumberFormat is not null
            ? StageSpanFormattable(sheetWriter, value, options.NumberFormat, options.Culture)
            : StagedExcelCell.FromNumber(value);

    private static StagedExcelCell StageInt64(XlsxWriter.SheetWriter sheetWriter, long value, ExcelWriteOptions options)
    {
        if (options.NumberFormat is not null)
            return StageSpanFormattable(sheetWriter, value, options.NumberFormat, options.Culture);
        if (value is < -999_999_999_999_999L or > 999_999_999_999_999L)
            return StageSpanFormattable(sheetWriter, value, format: null, CultureInfo.InvariantCulture);
        return StagedExcelCell.FromNumber(value);
    }

    private static StagedExcelCell StageSingle(XlsxWriter.SheetWriter sheetWriter, float value, ExcelWriteOptions options)
    {
        if (options.NumberFormat is not null)
            return StageSpanFormattable(sheetWriter, value, options.NumberFormat, options.Culture);
        if (IsExcelNumericDouble(value))
            return StagedExcelCell.FromNumber(value);
        return StageSpanFormattable(sheetWriter, value, format: null, options.Culture);
    }

    private static StagedExcelCell StageDouble(XlsxWriter.SheetWriter sheetWriter, double value, ExcelWriteOptions options)
    {
        if (options.NumberFormat is not null)
            return StageSpanFormattable(sheetWriter, value, options.NumberFormat, options.Culture);
        if (IsExcelNumericDouble(value))
            return StagedExcelCell.FromNumber(value);
        return StageSpanFormattable(sheetWriter, value, format: null, options.Culture);
    }

    private static StagedExcelCell StageDateTime(XlsxWriter.SheetWriter sheetWriter, DateTime value, ExcelWriteOptions options)
    {
        if (options.DateTimeFormat is not null)
            return StageSpanFormattable(sheetWriter, value, options.DateTimeFormat, options.Culture);
        if (RequiresTextDateTimeFallback(value))
            return StageSpanFormattable(sheetWriter, value, "yyyy-MM-ddTHH:mm:ss.fffffff", CultureInfo.InvariantCulture);
        _ = value.ToOADate();
        return StagedExcelCell.FromDate(value);
    }

    private static StagedExcelCell StageChar(XlsxWriter.SheetWriter sheetWriter, char ch)
        => StagedExcelCell.Literal(sheetWriter.PrepareStringCell(ch.ToString(), applyInjectionProtection: false));

    private static StagedExcelCell StageSpanFormattable<TValue>(
        XlsxWriter.SheetWriter sheetWriter,
        TValue value,
        string? format,
        CultureInfo culture)
        where TValue : ISpanFormattable
    {
        Span<char> tmp = stackalloc char[128];
        string text = value.TryFormat(tmp, out int written, format, culture)
            ? tmp[..written].ToString()
            : ((IFormattable)value).ToString(format, culture);
        return StagedExcelCell.Literal(sheetWriter.PrepareStringCell(text, applyInjectionProtection: true));
    }

    private static StagedExcelCell StageExcelCellValue(
        XlsxWriter.SheetWriter sheetWriter,
        object? value,
        ExcelWriteOptions options)
    {
        switch (value)
        {
            case null or DBNull:
                return StageNullCell(sheetWriter, options);

            case bool b:
                return StagedExcelCell.FromBool(b);

            case DateTime dt:
                return StageDateTime(sheetWriter, dt, options);

            case DateTimeOffset dto:
                return StageSpanFormattable(sheetWriter, dto, options.DateTimeFormat ?? "O", options.Culture);

            case DateOnly d:
                if (options.DateOnlyFormat is not null)
                    return StageSpanFormattable(sheetWriter, d, options.DateOnlyFormat, options.Culture);
                if (d < excelFirstExactSerialDateOnly)
                    return StageSpanFormattable(sheetWriter, d, "yyyy-MM-dd", CultureInfo.InvariantCulture);
                DateTime dateDt = d.ToDateTime(TimeOnly.MinValue);
                _ = dateDt.ToOADate();
                return StagedExcelCell.FromDate(dateDt);

            case TimeOnly t:
                return StageSpanFormattable(sheetWriter, t, options.TimeOnlyFormat ?? (t.Ticks % TimeSpan.TicksPerSecond == 0 ? "HH:mm:ss" : "O"), options.Culture);

            case TimeSpan ts:
                return StageSpanFormattable(sheetWriter, ts, options.TimeSpanFormat ?? "c", options.Culture);

            case char ch:
                return StageChar(sheetWriter, ch);

            case Half half:
                if (options.NumberFormat is not null)
                    return StageSpanFormattable(sheetWriter, half, options.NumberFormat, options.Culture);
                double halfDouble = (double)half;
                return double.IsFinite(halfDouble)
                    ? StagedExcelCell.FromNumber(halfDouble)
                    : StageSpanFormattable(sheetWriter, half, format: null, options.Culture);

            case Int128 i128:
                return StageSpanFormattable(sheetWriter, i128, options.NumberFormat, options.Culture);

            case UInt128 u128:
                return StageSpanFormattable(sheetWriter, u128, options.NumberFormat, options.Culture);

            case BigInteger bi:
                return StageSpanFormattable(sheetWriter, bi, options.NumberFormat, options.Culture);

            case Uri uri:
                return StagedExcelCell.Literal(sheetWriter.PrepareStringCell(uri.OriginalString, applyInjectionProtection: true));

            case byte[] bytes:
                return StagedExcelCell.Literal(sheetWriter.PrepareStringCell(
                    bytes.Length == 0 ? Utf8SpanParser.EMPTY_BASE64_TOKEN : Convert.ToBase64String(bytes),
                    applyInjectionProtection: false));

            case long l:
                return StageInt64(sheetWriter, l, options);

            case ulong ul:
                if (options.NumberFormat is not null)
                    return StageSpanFormattable(sheetWriter, ul, options.NumberFormat, options.Culture);
                if (ul > 999_999_999_999_999UL)
                    return StageSpanFormattable(sheetWriter, ul, format: null, CultureInfo.InvariantCulture);
                return StagedExcelCell.FromNumber(ul);

            case decimal dec:
                return StageSpanFormattable(sheetWriter, dec, options.NumberFormat, options.Culture);

            case byte or sbyte or short or ushort or int or uint or float or double:
                if (options.NumberFormat is not null && value is ISpanFormattable numSpan)
                    return StageSpanFormattable(sheetWriter, numSpan, options.NumberFormat, options.Culture);
                if (options.NumberFormat is not null && value is IFormattable numFormattable)
                    return StagedExcelCell.Literal(sheetWriter.PrepareStringCell(numFormattable.ToString(options.NumberFormat, options.Culture), applyInjectionProtection: true));
                double num = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                return IsExcelNumericDouble(num)
                    ? StagedExcelCell.FromNumber(num)
                    : StageSpanFormattable(sheetWriter, num, format: null, options.Culture);

            case ISpanFormattable spanFormattable:
                return StageSpanFormattable(sheetWriter, spanFormattable, format: null, options.Culture);

            case IFormattable formattable:
                return StagedExcelCell.Literal(sheetWriter.PrepareStringCell(formattable.ToString(null, options.Culture), applyInjectionProtection: true));

            default:
                return StagedExcelCell.Literal(sheetWriter.PrepareStringCell(value.ToString() ?? string.Empty, applyInjectionProtection: true));
        }
    }

    private static void CommitStagedRow(XlsxWriter.SheetWriter sheetWriter, int rowNumber, StagedExcelCell[] stagedRow)
    {
        sheetWriter.StartRow(rowNumber);
        for (int i = 0; i < stagedRow.Length; i++)
        {
            int colIndex = i + 1;
            StagedExcelCell cell = stagedRow[i];
            switch (cell.Kind)
            {
                case StagedExcelCellKind.Empty:
                    sheetWriter.WriteCellEmpty(colIndex);
                    break;
                case StagedExcelCellKind.LiteralString:
                    sheetWriter.WriteCellLiteralString(colIndex, cell.Text!);
                    break;
                case StagedExcelCellKind.Number:
                    sheetWriter.WriteCellNumber(colIndex, cell.Number);
                    break;
                case StagedExcelCellKind.Boolean:
                    sheetWriter.WriteCellBoolean(colIndex, cell.Bool);
                    break;
                case StagedExcelCellKind.Date:
                    sheetWriter.WriteCellDate(colIndex, cell.Date);
                    break;
                default:
                    break;
            }
        }
        sheetWriter.EndRow();
    }

    private enum StagedExcelCellKind : byte
    {
        Empty,
        LiteralString,
        Number,
        Boolean,
        Date
    }

    private readonly struct StagedExcelCell
    {
        public static StagedExcelCell Empty => new(StagedExcelCellKind.Empty, null, 0d, false, default);
        public static StagedExcelCell Literal(string text) => new(StagedExcelCellKind.LiteralString, text, 0d, false, default);
        public static StagedExcelCell FromNumber(double number) => new(StagedExcelCellKind.Number, null, number, false, default);
        public static StagedExcelCell FromBool(bool value) => new(StagedExcelCellKind.Boolean, null, 0d, value, default);
        public static StagedExcelCell FromDate(DateTime date) => new(StagedExcelCellKind.Date, null, 0d, false, date);

        private StagedExcelCell(StagedExcelCellKind kind, string? text, double number, bool boolValue, DateTime date)
        {
            Kind = kind;
            Text = text;
            Number = number;
            Bool = boolValue;
            Date = date;
        }

        public StagedExcelCellKind Kind { get; }
        public string? Text { get; }
        public double Number { get; }
        public bool Bool { get; }
        public DateTime Date { get; }
    }
}
