using System.Data;
using System.Data.Common;
using System.Text;
using HeroParser.Conversion;
using HeroParser.FixedWidths;
using HeroParser.FixedWidths.Streaming;
using HeroParser.FixedWidths.Writing;
using HeroParser.Internal;

namespace HeroParser;

public static partial class FixedWidth
{
    /// <summary>
    /// Streams rows from an <see cref="IDataReader"/> into a <see cref="TextWriter"/> in fixed-width format without reflection.
    /// </summary>
    /// <param name="reader">The source data reader providing columns and rows.</param>
    /// <param name="writer">The destination text writer.</param>
    /// <param name="columns">The fixed-width field layout definitions.</param>
    /// <param name="options">Optional fixed-width formatting options; defaults to <see cref="FixedWidthWriteOptions.Default"/>.</param>
    /// <param name="writeHeader">When <see langword="true"/>, writes a fixed-width header row using column names from <paramref name="columns"/>.</param>
    /// <param name="leaveOpen">When <see langword="true"/>, leaves <paramref name="writer"/> open after writing completes.</param>
    public static void WriteFromDataReader(
        IDataReader reader,
        TextWriter writer,
        IReadOnlyList<FixedWidthFieldDefinition> columns,
        FixedWidthWriteOptions? options = null,
        bool writeHeader = false,
        bool leaveOpen = true)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(writer);
        ValidateColumns(columns);

        using var fixedWriter = new FixedWidthStreamWriter(writer, options, leaveOpen);
        WriteDataReaderRows(reader, fixedWriter, columns, writeHeader);
    }

    /// <summary>
    /// Streams rows from an <see cref="IDataReader"/> into a <see cref="Stream"/> in fixed-width format without reflection.
    /// </summary>
    /// <param name="reader">The source data reader providing columns and rows.</param>
    /// <param name="stream">The destination writable stream.</param>
    /// <param name="columns">The fixed-width field layout definitions.</param>
    /// <param name="options">Optional fixed-width formatting options; defaults to <see cref="FixedWidthWriteOptions.Default"/>.</param>
    /// <param name="encoding">Optional text encoding; defaults to UTF-8.</param>
    /// <param name="writeHeader">When <see langword="true"/>, writes a fixed-width header row using column names from <paramref name="columns"/>.</param>
    /// <param name="leaveOpen">When <see langword="true"/>, leaves <paramref name="stream"/> open after writing completes.</param>
    public static void WriteFromDataReader(
        IDataReader reader,
        Stream stream,
        IReadOnlyList<FixedWidthFieldDefinition> columns,
        FixedWidthWriteOptions? options = null,
        Encoding? encoding = null,
        bool writeHeader = false,
        bool leaveOpen = true)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(stream);
        ValidateColumns(columns);

        encoding ??= Encoding.UTF8;
        using var textWriter = new StreamWriter(stream, encoding, bufferSize: 16 * 1024, leaveOpen: leaveOpen);
        using var fixedWriter = new FixedWidthStreamWriter(textWriter, options, leaveOpen: true);
        WriteDataReaderRows(reader, fixedWriter, columns, writeHeader);
    }

    /// <summary>
    /// Streams rows from an <see cref="IDataReader"/> directly to a fixed-width file (including transparent <c>.gz</c>, <c>.br</c>, and <c>.zst</c>/<c>.zstd</c> compression) without reflection.
    /// </summary>
    /// <param name="path">Destination file path.</param>
    /// <param name="reader">The source data reader providing columns and rows.</param>
    /// <param name="columns">The fixed-width field layout definitions.</param>
    /// <param name="options">Optional fixed-width formatting options; defaults to <see cref="FixedWidthWriteOptions.Default"/>.</param>
    /// <param name="encoding">Optional text encoding; defaults to UTF-8.</param>
    /// <param name="writeHeader">When <see langword="true"/>, writes a fixed-width header row using column names from <paramref name="columns"/>.</param>
    public static void WriteFromDataReaderToFile(
        string path,
        IDataReader reader,
        IReadOnlyList<FixedWidthFieldDefinition> columns,
        FixedWidthWriteOptions? options = null,
        Encoding? encoding = null,
        bool writeHeader = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(reader);
        ValidateColumns(columns);

        using var stream = new CompressedFile(path).OpenWrite();
        WriteFromDataReader(reader, stream, columns, options, encoding, writeHeader, leaveOpen: true);
    }

    /// <summary>
    /// Serializes rows from an <see cref="IDataReader"/> into a fixed-width string without reflection.
    /// </summary>
    /// <param name="reader">The source data reader providing columns and rows.</param>
    /// <param name="columns">The fixed-width field layout definitions.</param>
    /// <param name="options">Optional fixed-width formatting options; defaults to <see cref="FixedWidthWriteOptions.Default"/>.</param>
    /// <param name="writeHeader">When <see langword="true"/>, writes a fixed-width header row using column names from <paramref name="columns"/>.</param>
    /// <returns>The serialized fixed-width text.</returns>
    public static string WriteFromDataReaderToText(
        IDataReader reader,
        IReadOnlyList<FixedWidthFieldDefinition> columns,
        FixedWidthWriteOptions? options = null,
        bool writeHeader = false)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ValidateColumns(columns);

        using var sw = new StringWriter();
        using (var fixedWriter = new FixedWidthStreamWriter(sw, options, leaveOpen: true))
        {
            WriteDataReaderRows(reader, fixedWriter, columns, writeHeader);
        }
        return sw.ToString();
    }

    /// <summary>
    /// Asynchronously streams rows from a <see cref="DbDataReader"/> into a <see cref="Stream"/> in fixed-width format without reflection.
    /// </summary>
    /// <param name="reader">The async-capable database data reader providing columns and rows.</param>
    /// <param name="stream">The destination writable stream.</param>
    /// <param name="columns">The fixed-width field layout definitions.</param>
    /// <param name="options">Optional fixed-width formatting options; defaults to <see cref="FixedWidthWriteOptions.Default"/>.</param>
    /// <param name="encoding">Optional text encoding; defaults to UTF-8.</param>
    /// <param name="writeHeader">When <see langword="true"/>, writes a fixed-width header row using column names from <paramref name="columns"/>.</param>
    /// <param name="leaveOpen">When <see langword="true"/>, leaves <paramref name="stream"/> open after writing completes.</param>
    /// <param name="cancellationToken">Token used to cancel async I/O.</param>
    public static async ValueTask WriteFromDataReaderAsync(
        DbDataReader reader,
        Stream stream,
        IReadOnlyList<FixedWidthFieldDefinition> columns,
        FixedWidthWriteOptions? options = null,
        Encoding? encoding = null,
        bool writeHeader = false,
        bool leaveOpen = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(stream);
        ValidateColumns(columns);

        encoding ??= Encoding.UTF8;
        var fixedWriter = new FixedWidthAsyncStreamWriter(stream, options, encoding, leaveOpen);
        await using (fixedWriter.ConfigureAwait(false))
        {
            await WriteDataReaderRowsAsync(reader, fixedWriter, columns, writeHeader, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Asynchronously streams rows from a <see cref="DbDataReader"/> directly to a fixed-width file (including transparent <c>.gz</c>, <c>.br</c>, and <c>.zst</c>/<c>.zstd</c> compression) without reflection.
    /// </summary>
    /// <param name="path">Destination file path.</param>
    /// <param name="reader">The async-capable database data reader providing columns and rows.</param>
    /// <param name="columns">The fixed-width field layout definitions.</param>
    /// <param name="options">Optional fixed-width formatting options; defaults to <see cref="FixedWidthWriteOptions.Default"/>.</param>
    /// <param name="encoding">Optional text encoding; defaults to UTF-8.</param>
    /// <param name="writeHeader">When <see langword="true"/>, writes a fixed-width header row using column names from <paramref name="columns"/>.</param>
    /// <param name="cancellationToken">Token used to cancel async I/O.</param>
    public static async ValueTask WriteFromDataReaderToFileAsync(
        string path,
        DbDataReader reader,
        IReadOnlyList<FixedWidthFieldDefinition> columns,
        FixedWidthWriteOptions? options = null,
        Encoding? encoding = null,
        bool writeHeader = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(reader);
        ValidateColumns(columns);
        cancellationToken.ThrowIfCancellationRequested();

        var stream = new CompressedFile(path).OpenWrite(
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using (stream.ConfigureAwait(false))
        {
            await WriteFromDataReaderAsync(reader, stream, columns, options, encoding, writeHeader, leaveOpen: true, cancellationToken).ConfigureAwait(false);
        }
    }

    private static void ValidateColumns(IReadOnlyList<FixedWidthFieldDefinition> columns)
    {
        ArgumentNullException.ThrowIfNull(columns);
        if (columns.Count == 0)
            throw new ArgumentException("At least one column definition is required.", nameof(columns));
        for (int i = 0; i < columns.Count; i++)
        {
            if (columns[i] is null)
                throw new ArgumentException($"Column definition at index {i} cannot be null.", nameof(columns));
        }
    }

    private static int[] ResolveColumnOrdinals(IDataReader reader, IReadOnlyList<FixedWidthFieldDefinition> columns)
    {
        int fieldCount = reader.FieldCount;
        var ordinals = new int[columns.Count];
        for (int i = 0; i < columns.Count; i++)
        {
            string targetName = columns[i].Name;
            int found = -1;
            for (int j = 0; j < fieldCount; j++)
            {
                if (string.Equals(reader.GetName(j), targetName, StringComparison.Ordinal))
                {
                    found = j;
                    break;
                }
            }

            if (found < 0)
            {
                int caseInsensitiveMatches = 0;
                for (int j = 0; j < fieldCount; j++)
                {
                    if (string.Equals(reader.GetName(j), targetName, StringComparison.OrdinalIgnoreCase))
                    {
                        if (caseInsensitiveMatches == 0)
                            found = j;
                        caseInsensitiveMatches++;
                    }
                }

                if (caseInsensitiveMatches > 1)
                    throw new InvalidOperationException(
                        $"Column '{targetName}' matches multiple DataReader columns case-insensitively. Specify the exact column name.");
            }

            ordinals[i] = found;
        }
        return ordinals;
    }

    private static void WriteDataReaderRows(
        IDataReader reader,
        FixedWidthStreamWriter fixedWriter,
        IReadOnlyList<FixedWidthFieldDefinition> columns,
        bool writeHeader)
    {
        int[] ordinals = ResolveColumnOrdinals(reader, columns);
        if (writeHeader)
        {
            for (int i = 0; i < columns.Count; i++)
            {
                FixedWidthFieldDefinition col = columns[i];
                fixedWriter.WriteField(col.Name, col.Width, col.Alignment, col.PadChar);
            }
            fixedWriter.EndRow();
        }

        bool needsBufferedRead = BuildSequentialReadPlan(ordinals, out int[] uniqueOrdinals, out int[] layoutToSlot);
        BufferedCellValue[]? rowBuffer = needsBufferedRead ? new BufferedCellValue[uniqueOrdinals.Length] : null;
        DataReaderColumnPlan plan = default;
        string nullValue = fixedWriter.NullValue;
        int? maxRows = fixedWriter.MaxRowCount;
        int dataRowCount = 0;

        while (reader.Read())
        {
            if (maxRows.HasValue && dataRowCount >= maxRows.Value)
                throw new FixedWidthException(FixedWidthErrorCode.TooManyRowsWritten, $"Maximum row count of {maxRows.Value} exceeded");
            dataRowCount++;

            if (!plan.IsInitialized)
                plan = new DataReaderColumnPlan(reader);

            if (rowBuffer is not null)
            {
                for (int s = 0; s < uniqueOrdinals.Length; s++)
                {
                    int ordinal = uniqueOrdinals[s];
                    rowBuffer[s] = ReadBufferedCell(reader, ordinal, plan[ordinal]);
                }

                for (int i = 0; i < columns.Count; i++)
                {
                    FixedWidthFieldDefinition col = columns[i];
                    int slot = layoutToSlot[i];
                    if (slot < 0)
                    {
                        fixedWriter.WriteField(nullValue, col.Width, col.Alignment, col.PadChar);
                        continue;
                    }

                    WriteBufferedCell(fixedWriter, in rowBuffer[slot], col, nullValue);
                }
            }
            else
            {
                for (int i = 0; i < columns.Count; i++)
                {
                    FixedWidthFieldDefinition col = columns[i];
                    int ordinal = ordinals[i];
                    if (ordinal < 0)
                    {
                        fixedWriter.WriteField(nullValue, col.Width, col.Alignment, col.PadChar);
                        continue;
                    }

                    DataReaderColumnKind kind = plan[ordinal];
                    if (kind is not (DataReaderColumnKind.String or DataReaderColumnKind.Object) && reader.IsDBNull(ordinal))
                    {
                        fixedWriter.WriteField(nullValue, col.Width, col.Alignment, col.PadChar);
                        continue;
                    }

                    switch (kind)
                    {
                        case DataReaderColumnKind.String:
                            {
                                object? raw = reader.GetValue(ordinal);
                                if (raw is null or DBNull)
                                    fixedWriter.WriteField(nullValue, col.Width, col.Alignment, col.PadChar);
                                else if (raw is string str)
                                    fixedWriter.WriteField(str, col.Width, col.Alignment, col.PadChar);
                                else
                                    fixedWriter.WriteField(raw, col.Width, col.Alignment, col.PadChar);
                            }
                            break;
                        case DataReaderColumnKind.Object:
                            {
                                object? raw = reader.GetValue(ordinal);
                                fixedWriter.WriteField(raw is DBNull ? null : raw, col.Width, col.Alignment, col.PadChar);
                            }
                            break;
                        case DataReaderColumnKind.Int32:
                            fixedWriter.WriteField(reader.GetInt32(ordinal), col.Width, col.Alignment, col.PadChar);
                            break;
                        case DataReaderColumnKind.Int64:
                            fixedWriter.WriteField(reader.GetInt64(ordinal), col.Width, col.Alignment, col.PadChar);
                            break;
                        case DataReaderColumnKind.Int16:
                            fixedWriter.WriteInt16Field(reader.GetInt16(ordinal), col.Width, col.Alignment, col.PadChar);
                            break;
                        case DataReaderColumnKind.Byte:
                            fixedWriter.WriteByteField(reader.GetByte(ordinal), col.Width, col.Alignment, col.PadChar);
                            break;
                        case DataReaderColumnKind.Single:
                            fixedWriter.WriteFloatField(reader.GetFloat(ordinal), col.Width, col.Alignment, col.PadChar);
                            break;
                        case DataReaderColumnKind.Double:
                            fixedWriter.WriteField(reader.GetDouble(ordinal), col.Width, col.Alignment, col.PadChar);
                            break;
                        case DataReaderColumnKind.Decimal:
                            fixedWriter.WriteField(reader.GetDecimal(ordinal), col.Width, col.Alignment, col.PadChar);
                            break;
                        case DataReaderColumnKind.Boolean:
                            fixedWriter.WriteField(reader.GetBoolean(ordinal), col.Width, col.Alignment, col.PadChar);
                            break;
                        case DataReaderColumnKind.DateTime:
                            fixedWriter.WriteField(reader.GetDateTime(ordinal), col.Width, col.Alignment, col.PadChar);
                            break;
                        case DataReaderColumnKind.Guid:
                            fixedWriter.WriteField(reader.GetGuid(ordinal), col.Width, col.Alignment, col.PadChar);
                            break;
                        case DataReaderColumnKind.Char:
                            {
                                Span<char> chBuf = [reader.GetChar(ordinal)];
                                fixedWriter.WriteField(chBuf, col.Width, col.Alignment, col.PadChar);
                            }
                            break;
                        default:
                            break;
                    }
                }
            }
            fixedWriter.EndRow();
        }

        fixedWriter.Flush();
    }

    private static async ValueTask WriteDataReaderRowsAsync(
        DbDataReader reader,
        FixedWidthAsyncStreamWriter fixedWriter,
        IReadOnlyList<FixedWidthFieldDefinition> columns,
        bool writeHeader,
        CancellationToken cancellationToken)
    {
        bool hasRow = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);

        int[] ordinals = ResolveColumnOrdinals(reader, columns);
        if (writeHeader)
        {
            for (int i = 0; i < columns.Count; i++)
            {
                FixedWidthFieldDefinition col = columns[i];
                await fixedWriter.WriteFieldAsync(col.Name.AsMemory(), col.Width, col.Alignment, col.PadChar, cancellationToken).ConfigureAwait(false);
            }
            await fixedWriter.EndRowAsync(cancellationToken).ConfigureAwait(false);
        }

        if (hasRow)
        {
            bool useAsyncFieldValues = DataReaderColumnPlan.HasAsyncFieldValueOverride(reader);
            bool needsBufferedRead = BuildSequentialReadPlan(ordinals, out int[] uniqueOrdinals, out int[] layoutToSlot);
            BufferedCellValue[]? rowBuffer = needsBufferedRead ? new BufferedCellValue[uniqueOrdinals.Length] : null;
            DataReaderColumnPlan plan = default;
            string nullValue = fixedWriter.NullValue;
            string? numberFormat = fixedWriter.NumberFormat;
            string? dateTimeFormat = fixedWriter.DateTimeFormat;
            int? maxRows = fixedWriter.MaxRowCount;
            int dataRowCount = 0;

            do
            {
                if (maxRows.HasValue && dataRowCount >= maxRows.Value)
                    throw new FixedWidthException(FixedWidthErrorCode.TooManyRowsWritten, $"Maximum row count of {maxRows.Value} exceeded");
                dataRowCount++;

                if (!plan.IsInitialized)
                    plan = new DataReaderColumnPlan(reader);

                if (rowBuffer is not null)
                {
                    for (int s = 0; s < uniqueOrdinals.Length; s++)
                    {
                        int ordinal = uniqueOrdinals[s];
                        rowBuffer[s] = await ReadBufferedCellAsync(reader, ordinal, plan[ordinal], useAsyncFieldValues, cancellationToken).ConfigureAwait(false);
                    }

                    for (int i = 0; i < columns.Count; i++)
                    {
                        FixedWidthFieldDefinition col = columns[i];
                        int slot = layoutToSlot[i];
                        if (slot < 0)
                        {
                            await fixedWriter.WriteFieldAsync(nullValue.AsMemory(), col.Width, col.Alignment, col.PadChar, cancellationToken).ConfigureAwait(false);
                            continue;
                        }

                        await WriteBufferedCellAsync(
                            fixedWriter,
                            rowBuffer[slot],
                            col,
                            nullValue,
                            numberFormat,
                            dateTimeFormat,
                            cancellationToken).ConfigureAwait(false);
                    }
                }
                else
                {
                    for (int i = 0; i < columns.Count; i++)
                    {
                        FixedWidthFieldDefinition col = columns[i];
                        int ordinal = ordinals[i];
                        if (ordinal < 0)
                        {
                            await fixedWriter.WriteFieldAsync(nullValue.AsMemory(), col.Width, col.Alignment, col.PadChar, cancellationToken).ConfigureAwait(false);
                            continue;
                        }

                        DataReaderColumnKind kind = plan[ordinal];
                        if (await reader.IsDBNullAsync(ordinal, cancellationToken).ConfigureAwait(false))
                        {
                            await fixedWriter.WriteFieldAsync(nullValue.AsMemory(), col.Width, col.Alignment, col.PadChar, cancellationToken).ConfigureAwait(false);
                            continue;
                        }

                        if (useAsyncFieldValues)
                        {
                            switch (kind)
                            {
                                case DataReaderColumnKind.String:
                                    {
                                        string? str = await reader.GetFieldValueAsync<string?>(ordinal, cancellationToken).ConfigureAwait(false);
                                        if (str is null)
                                            await fixedWriter.WriteFieldAsync(nullValue.AsMemory(), col.Width, col.Alignment, col.PadChar, cancellationToken).ConfigureAwait(false);
                                        else
                                            await fixedWriter.WriteFieldAsync(str.AsMemory(), col.Width, col.Alignment, col.PadChar, cancellationToken).ConfigureAwait(false);
                                    }
                                    break;
                                case DataReaderColumnKind.Object:
                                    {
                                        object? raw = await reader.GetFieldValueAsync<object?>(ordinal, cancellationToken).ConfigureAwait(false);
                                        await fixedWriter.WriteFieldAsync(raw is DBNull ? null : raw, col.Width, col.Alignment, col.PadChar, null, cancellationToken).ConfigureAwait(false);
                                    }
                                    break;
                                case DataReaderColumnKind.Int32:
                                    await fixedWriter.WriteSpanFormattableFieldAsync(await reader.GetFieldValueAsync<int>(ordinal, cancellationToken).ConfigureAwait(false), col.Width, col.Alignment, col.PadChar, numberFormat, cancellationToken).ConfigureAwait(false);
                                    break;
                                case DataReaderColumnKind.Int64:
                                    await fixedWriter.WriteSpanFormattableFieldAsync(await reader.GetFieldValueAsync<long>(ordinal, cancellationToken).ConfigureAwait(false), col.Width, col.Alignment, col.PadChar, numberFormat, cancellationToken).ConfigureAwait(false);
                                    break;
                                case DataReaderColumnKind.Int16:
                                    await fixedWriter.WriteSpanFormattableFieldAsync(await reader.GetFieldValueAsync<short>(ordinal, cancellationToken).ConfigureAwait(false), col.Width, col.Alignment, col.PadChar, numberFormat, cancellationToken).ConfigureAwait(false);
                                    break;
                                case DataReaderColumnKind.Byte:
                                    await fixedWriter.WriteSpanFormattableFieldAsync(await reader.GetFieldValueAsync<byte>(ordinal, cancellationToken).ConfigureAwait(false), col.Width, col.Alignment, col.PadChar, numberFormat, cancellationToken).ConfigureAwait(false);
                                    break;
                                case DataReaderColumnKind.Single:
                                    await fixedWriter.WriteSpanFormattableFieldAsync(await reader.GetFieldValueAsync<float>(ordinal, cancellationToken).ConfigureAwait(false), col.Width, col.Alignment, col.PadChar, numberFormat, cancellationToken).ConfigureAwait(false);
                                    break;
                                case DataReaderColumnKind.Double:
                                    await fixedWriter.WriteSpanFormattableFieldAsync(await reader.GetFieldValueAsync<double>(ordinal, cancellationToken).ConfigureAwait(false), col.Width, col.Alignment, col.PadChar, numberFormat, cancellationToken).ConfigureAwait(false);
                                    break;
                                case DataReaderColumnKind.Decimal:
                                    await fixedWriter.WriteSpanFormattableFieldAsync(await reader.GetFieldValueAsync<decimal>(ordinal, cancellationToken).ConfigureAwait(false), col.Width, col.Alignment, col.PadChar, numberFormat, cancellationToken).ConfigureAwait(false);
                                    break;
                                case DataReaderColumnKind.Boolean:
                                    await fixedWriter.WriteBoolFieldAsync(await reader.GetFieldValueAsync<bool>(ordinal, cancellationToken).ConfigureAwait(false), col.Width, col.Alignment, col.PadChar, cancellationToken).ConfigureAwait(false);
                                    break;
                                case DataReaderColumnKind.DateTime:
                                    await fixedWriter.WriteSpanFormattableFieldAsync(await reader.GetFieldValueAsync<DateTime>(ordinal, cancellationToken).ConfigureAwait(false), col.Width, col.Alignment, col.PadChar, dateTimeFormat, cancellationToken).ConfigureAwait(false);
                                    break;
                                case DataReaderColumnKind.Guid:
                                    await fixedWriter.WriteSpanFormattableFieldAsync(await reader.GetFieldValueAsync<Guid>(ordinal, cancellationToken).ConfigureAwait(false), col.Width, col.Alignment, col.PadChar, format: null, cancellationToken).ConfigureAwait(false);
                                    break;
                                case DataReaderColumnKind.Char:
                                    await fixedWriter.WriteCharFieldAsync(await reader.GetFieldValueAsync<char>(ordinal, cancellationToken).ConfigureAwait(false), col.Width, col.Alignment, col.PadChar, cancellationToken).ConfigureAwait(false);
                                    break;
                                default:
                                    break;
                            }
                        }
                        else
                        {
                            switch (kind)
                            {
                                case DataReaderColumnKind.String:
                                    {
                                        object? raw = reader.GetValue(ordinal);
                                        if (raw is null or DBNull)
                                            await fixedWriter.WriteFieldAsync(nullValue.AsMemory(), col.Width, col.Alignment, col.PadChar, cancellationToken).ConfigureAwait(false);
                                        else if (raw is string str)
                                            await fixedWriter.WriteFieldAsync(str.AsMemory(), col.Width, col.Alignment, col.PadChar, cancellationToken).ConfigureAwait(false);
                                        else
                                            await fixedWriter.WriteFieldAsync(raw, col.Width, col.Alignment, col.PadChar, null, cancellationToken).ConfigureAwait(false);
                                    }
                                    break;
                                case DataReaderColumnKind.Object:
                                    {
                                        object? raw = reader.GetValue(ordinal);
                                        await fixedWriter.WriteFieldAsync(raw is DBNull ? null : raw, col.Width, col.Alignment, col.PadChar, null, cancellationToken).ConfigureAwait(false);
                                    }
                                    break;
                                case DataReaderColumnKind.Int32:
                                    await fixedWriter.WriteSpanFormattableFieldAsync(reader.GetInt32(ordinal), col.Width, col.Alignment, col.PadChar, numberFormat, cancellationToken).ConfigureAwait(false);
                                    break;
                                case DataReaderColumnKind.Int64:
                                    await fixedWriter.WriteSpanFormattableFieldAsync(reader.GetInt64(ordinal), col.Width, col.Alignment, col.PadChar, numberFormat, cancellationToken).ConfigureAwait(false);
                                    break;
                                case DataReaderColumnKind.Int16:
                                    await fixedWriter.WriteSpanFormattableFieldAsync(reader.GetInt16(ordinal), col.Width, col.Alignment, col.PadChar, numberFormat, cancellationToken).ConfigureAwait(false);
                                    break;
                                case DataReaderColumnKind.Byte:
                                    await fixedWriter.WriteSpanFormattableFieldAsync(reader.GetByte(ordinal), col.Width, col.Alignment, col.PadChar, numberFormat, cancellationToken).ConfigureAwait(false);
                                    break;
                                case DataReaderColumnKind.Single:
                                    await fixedWriter.WriteSpanFormattableFieldAsync(reader.GetFloat(ordinal), col.Width, col.Alignment, col.PadChar, numberFormat, cancellationToken).ConfigureAwait(false);
                                    break;
                                case DataReaderColumnKind.Double:
                                    await fixedWriter.WriteSpanFormattableFieldAsync(reader.GetDouble(ordinal), col.Width, col.Alignment, col.PadChar, numberFormat, cancellationToken).ConfigureAwait(false);
                                    break;
                                case DataReaderColumnKind.Decimal:
                                    await fixedWriter.WriteSpanFormattableFieldAsync(reader.GetDecimal(ordinal), col.Width, col.Alignment, col.PadChar, numberFormat, cancellationToken).ConfigureAwait(false);
                                    break;
                                case DataReaderColumnKind.Boolean:
                                    await fixedWriter.WriteBoolFieldAsync(reader.GetBoolean(ordinal), col.Width, col.Alignment, col.PadChar, cancellationToken).ConfigureAwait(false);
                                    break;
                                case DataReaderColumnKind.DateTime:
                                    await fixedWriter.WriteSpanFormattableFieldAsync(reader.GetDateTime(ordinal), col.Width, col.Alignment, col.PadChar, dateTimeFormat, cancellationToken).ConfigureAwait(false);
                                    break;
                                case DataReaderColumnKind.Guid:
                                    await fixedWriter.WriteSpanFormattableFieldAsync(reader.GetGuid(ordinal), col.Width, col.Alignment, col.PadChar, format: null, cancellationToken).ConfigureAwait(false);
                                    break;
                                case DataReaderColumnKind.Char:
                                    await fixedWriter.WriteCharFieldAsync(reader.GetChar(ordinal), col.Width, col.Alignment, col.PadChar, cancellationToken).ConfigureAwait(false);
                                    break;
                                default:
                                    break;
                            }
                        }
                    }
                }
                await fixedWriter.EndRowAsync(cancellationToken).ConfigureAwait(false);
            }
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false));
        }

        await fixedWriter.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static bool BuildSequentialReadPlan(int[] ordinals, out int[] uniqueOrdinals, out int[] layoutToSlot)
    {
        bool strictlyIncreasing = true;
        int lastOrdinal = -1;
        for (int i = 0; i < ordinals.Length; i++)
        {
            int ordinal = ordinals[i];
            if (ordinal < 0)
                continue;

            if (ordinal <= lastOrdinal)
            {
                strictlyIncreasing = false;
                break;
            }
            lastOrdinal = ordinal;
        }

        if (strictlyIncreasing)
        {
            uniqueOrdinals = [];
            layoutToSlot = [];
            return false;
        }

        var uniqueList = new List<int>(ordinals.Length);
        for (int i = 0; i < ordinals.Length; i++)
        {
            int ordinal = ordinals[i];
            if (ordinal >= 0 && !uniqueList.Contains(ordinal))
                uniqueList.Add(ordinal);
        }
        uniqueList.Sort();
        uniqueOrdinals = [.. uniqueList];

        layoutToSlot = new int[ordinals.Length];
        for (int i = 0; i < ordinals.Length; i++)
        {
            layoutToSlot[i] = ordinals[i] < 0 ? -1 : Array.BinarySearch(uniqueOrdinals, ordinals[i]);
        }

        return true;
    }

    private static BufferedCellValue ReadBufferedCell(IDataReader reader, int ordinal, DataReaderColumnKind kind)
    {
        if (kind is not (DataReaderColumnKind.String or DataReaderColumnKind.Object) && reader.IsDBNull(ordinal))
            return BufferedCellValue.Null(kind);

        return kind switch
        {
            DataReaderColumnKind.String => BufferedCellValue.FromObject(kind, reader.GetValue(ordinal)),
            DataReaderColumnKind.Object => BufferedCellValue.FromObject(kind, reader.GetValue(ordinal)),
            DataReaderColumnKind.Int32 => BufferedCellValue.FromInt64(kind, reader.GetInt32(ordinal)),
            DataReaderColumnKind.Int64 => BufferedCellValue.FromInt64(kind, reader.GetInt64(ordinal)),
            DataReaderColumnKind.Int16 => BufferedCellValue.FromInt64(kind, reader.GetInt16(ordinal)),
            DataReaderColumnKind.Byte => BufferedCellValue.FromInt64(kind, reader.GetByte(ordinal)),
            DataReaderColumnKind.Single => BufferedCellValue.FromSingle(reader.GetFloat(ordinal)),
            DataReaderColumnKind.Double => BufferedCellValue.FromDouble(reader.GetDouble(ordinal)),
            DataReaderColumnKind.Decimal => BufferedCellValue.FromDecimal(reader.GetDecimal(ordinal)),
            DataReaderColumnKind.Boolean => BufferedCellValue.FromInt64(kind, reader.GetBoolean(ordinal) ? 1L : 0L),
            DataReaderColumnKind.DateTime => BufferedCellValue.FromDateTime(reader.GetDateTime(ordinal)),
            DataReaderColumnKind.Guid => BufferedCellValue.FromGuid(reader.GetGuid(ordinal)),
            DataReaderColumnKind.Char => BufferedCellValue.FromInt64(kind, reader.GetChar(ordinal)),
            _ => BufferedCellValue.Null(kind)
        };
    }

    private static async ValueTask<BufferedCellValue> ReadBufferedCellAsync(
        DbDataReader reader,
        int ordinal,
        DataReaderColumnKind kind,
        bool useAsyncFieldValues,
        CancellationToken cancellationToken)
    {
        if (await reader.IsDBNullAsync(ordinal, cancellationToken).ConfigureAwait(false))
            return BufferedCellValue.Null(kind);

        if (useAsyncFieldValues)
        {
            return kind switch
            {
                DataReaderColumnKind.String => BufferedCellValue.FromObject(kind, await reader.GetFieldValueAsync<string?>(ordinal, cancellationToken).ConfigureAwait(false)),
                DataReaderColumnKind.Object => BufferedCellValue.FromObject(kind, await reader.GetFieldValueAsync<object?>(ordinal, cancellationToken).ConfigureAwait(false)),
                DataReaderColumnKind.Int32 => BufferedCellValue.FromInt64(kind, await reader.GetFieldValueAsync<int>(ordinal, cancellationToken).ConfigureAwait(false)),
                DataReaderColumnKind.Int64 => BufferedCellValue.FromInt64(kind, await reader.GetFieldValueAsync<long>(ordinal, cancellationToken).ConfigureAwait(false)),
                DataReaderColumnKind.Int16 => BufferedCellValue.FromInt64(kind, await reader.GetFieldValueAsync<short>(ordinal, cancellationToken).ConfigureAwait(false)),
                DataReaderColumnKind.Byte => BufferedCellValue.FromInt64(kind, await reader.GetFieldValueAsync<byte>(ordinal, cancellationToken).ConfigureAwait(false)),
                DataReaderColumnKind.Single => BufferedCellValue.FromSingle(await reader.GetFieldValueAsync<float>(ordinal, cancellationToken).ConfigureAwait(false)),
                DataReaderColumnKind.Double => BufferedCellValue.FromDouble(await reader.GetFieldValueAsync<double>(ordinal, cancellationToken).ConfigureAwait(false)),
                DataReaderColumnKind.Decimal => BufferedCellValue.FromDecimal(await reader.GetFieldValueAsync<decimal>(ordinal, cancellationToken).ConfigureAwait(false)),
                DataReaderColumnKind.Boolean => BufferedCellValue.FromInt64(kind, await reader.GetFieldValueAsync<bool>(ordinal, cancellationToken).ConfigureAwait(false) ? 1L : 0L),
                DataReaderColumnKind.DateTime => BufferedCellValue.FromDateTime(await reader.GetFieldValueAsync<DateTime>(ordinal, cancellationToken).ConfigureAwait(false)),
                DataReaderColumnKind.Guid => BufferedCellValue.FromGuid(await reader.GetFieldValueAsync<Guid>(ordinal, cancellationToken).ConfigureAwait(false)),
                DataReaderColumnKind.Char => BufferedCellValue.FromInt64(kind, await reader.GetFieldValueAsync<char>(ordinal, cancellationToken).ConfigureAwait(false)),
                _ => BufferedCellValue.Null(kind)
            };
        }

        return kind switch
        {
            DataReaderColumnKind.String => BufferedCellValue.FromObject(kind, reader.GetValue(ordinal)),
            DataReaderColumnKind.Object => BufferedCellValue.FromObject(kind, reader.GetValue(ordinal)),
            DataReaderColumnKind.Int32 => BufferedCellValue.FromInt64(kind, reader.GetInt32(ordinal)),
            DataReaderColumnKind.Int64 => BufferedCellValue.FromInt64(kind, reader.GetInt64(ordinal)),
            DataReaderColumnKind.Int16 => BufferedCellValue.FromInt64(kind, reader.GetInt16(ordinal)),
            DataReaderColumnKind.Byte => BufferedCellValue.FromInt64(kind, reader.GetByte(ordinal)),
            DataReaderColumnKind.Single => BufferedCellValue.FromSingle(reader.GetFloat(ordinal)),
            DataReaderColumnKind.Double => BufferedCellValue.FromDouble(reader.GetDouble(ordinal)),
            DataReaderColumnKind.Decimal => BufferedCellValue.FromDecimal(reader.GetDecimal(ordinal)),
            DataReaderColumnKind.Boolean => BufferedCellValue.FromInt64(kind, reader.GetBoolean(ordinal) ? 1L : 0L),
            DataReaderColumnKind.DateTime => BufferedCellValue.FromDateTime(reader.GetDateTime(ordinal)),
            DataReaderColumnKind.Guid => BufferedCellValue.FromGuid(reader.GetGuid(ordinal)),
            DataReaderColumnKind.Char => BufferedCellValue.FromInt64(kind, reader.GetChar(ordinal)),
            _ => BufferedCellValue.Null(kind)
        };
    }

    private static void WriteBufferedCell(
        FixedWidthStreamWriter fixedWriter,
        in BufferedCellValue cell,
        FixedWidthFieldDefinition col,
        string nullValue)
    {
        if (cell.IsNull)
        {
            fixedWriter.WriteField(
                cell.Kind == DataReaderColumnKind.Object ? null : nullValue,
                col.Width,
                col.Alignment,
                col.PadChar);
            return;
        }

        switch (cell.Kind)
        {
            case DataReaderColumnKind.String:
                if (cell.ObjectValue is string str)
                    fixedWriter.WriteField(str, col.Width, col.Alignment, col.PadChar);
                else
                    fixedWriter.WriteField(cell.ObjectValue, col.Width, col.Alignment, col.PadChar);
                break;
            case DataReaderColumnKind.Object:
                fixedWriter.WriteField(cell.ObjectValue, col.Width, col.Alignment, col.PadChar);
                break;
            case DataReaderColumnKind.Int32:
                fixedWriter.WriteField(unchecked((int)cell.Int64Value), col.Width, col.Alignment, col.PadChar);
                break;
            case DataReaderColumnKind.Int64:
                fixedWriter.WriteField(cell.Int64Value, col.Width, col.Alignment, col.PadChar);
                break;
            case DataReaderColumnKind.Int16:
                fixedWriter.WriteInt16Field(unchecked((short)cell.Int64Value), col.Width, col.Alignment, col.PadChar);
                break;
            case DataReaderColumnKind.Byte:
                fixedWriter.WriteByteField(unchecked((byte)cell.Int64Value), col.Width, col.Alignment, col.PadChar);
                break;
            case DataReaderColumnKind.Single:
                fixedWriter.WriteFloatField(cell.SingleValue, col.Width, col.Alignment, col.PadChar);
                break;
            case DataReaderColumnKind.Double:
                fixedWriter.WriteField(cell.DoubleValue, col.Width, col.Alignment, col.PadChar);
                break;
            case DataReaderColumnKind.Decimal:
                fixedWriter.WriteField(cell.DecimalValue, col.Width, col.Alignment, col.PadChar);
                break;
            case DataReaderColumnKind.Boolean:
                fixedWriter.WriteField(cell.Int64Value != 0, col.Width, col.Alignment, col.PadChar);
                break;
            case DataReaderColumnKind.DateTime:
                fixedWriter.WriteField(cell.DateTimeValue, col.Width, col.Alignment, col.PadChar);
                break;
            case DataReaderColumnKind.Guid:
                fixedWriter.WriteField(cell.GuidValue, col.Width, col.Alignment, col.PadChar);
                break;
            case DataReaderColumnKind.Char:
                {
                    Span<char> chBuf = [unchecked((char)cell.Int64Value)];
                    fixedWriter.WriteField(chBuf, col.Width, col.Alignment, col.PadChar);
                }
                break;
            default:
                break;
        }
    }

    private static ValueTask WriteBufferedCellAsync(
        FixedWidthAsyncStreamWriter fixedWriter,
        BufferedCellValue cell,
        FixedWidthFieldDefinition col,
        string nullValue,
        string? numberFormat,
        string? dateTimeFormat,
        CancellationToken cancellationToken)
    {
        if (cell.IsNull)
        {
            return cell.Kind == DataReaderColumnKind.Object
                ? fixedWriter.WriteFieldAsync(null, col.Width, col.Alignment, col.PadChar, null, cancellationToken)
                : fixedWriter.WriteFieldAsync(nullValue.AsMemory(), col.Width, col.Alignment, col.PadChar, cancellationToken);
        }

        return cell.Kind switch
        {
            DataReaderColumnKind.String => cell.ObjectValue is string str
                ? fixedWriter.WriteFieldAsync(str.AsMemory(), col.Width, col.Alignment, col.PadChar, cancellationToken)
                : fixedWriter.WriteFieldAsync(cell.ObjectValue, col.Width, col.Alignment, col.PadChar, null, cancellationToken),
            DataReaderColumnKind.Object => fixedWriter.WriteFieldAsync(cell.ObjectValue, col.Width, col.Alignment, col.PadChar, null, cancellationToken),
            DataReaderColumnKind.Int32 => fixedWriter.WriteSpanFormattableFieldAsync(unchecked((int)cell.Int64Value), col.Width, col.Alignment, col.PadChar, numberFormat, cancellationToken),
            DataReaderColumnKind.Int64 => fixedWriter.WriteSpanFormattableFieldAsync(cell.Int64Value, col.Width, col.Alignment, col.PadChar, numberFormat, cancellationToken),
            DataReaderColumnKind.Int16 => fixedWriter.WriteSpanFormattableFieldAsync(unchecked((short)cell.Int64Value), col.Width, col.Alignment, col.PadChar, numberFormat, cancellationToken),
            DataReaderColumnKind.Byte => fixedWriter.WriteSpanFormattableFieldAsync(unchecked((byte)cell.Int64Value), col.Width, col.Alignment, col.PadChar, numberFormat, cancellationToken),
            DataReaderColumnKind.Single => fixedWriter.WriteSpanFormattableFieldAsync(cell.SingleValue, col.Width, col.Alignment, col.PadChar, numberFormat, cancellationToken),
            DataReaderColumnKind.Double => fixedWriter.WriteSpanFormattableFieldAsync(cell.DoubleValue, col.Width, col.Alignment, col.PadChar, numberFormat, cancellationToken),
            DataReaderColumnKind.Decimal => fixedWriter.WriteSpanFormattableFieldAsync(cell.DecimalValue, col.Width, col.Alignment, col.PadChar, numberFormat, cancellationToken),
            DataReaderColumnKind.Boolean => fixedWriter.WriteBoolFieldAsync(cell.Int64Value != 0, col.Width, col.Alignment, col.PadChar, cancellationToken),
            DataReaderColumnKind.DateTime => fixedWriter.WriteSpanFormattableFieldAsync(cell.DateTimeValue, col.Width, col.Alignment, col.PadChar, dateTimeFormat, cancellationToken),
            DataReaderColumnKind.Guid => fixedWriter.WriteSpanFormattableFieldAsync(cell.GuidValue, col.Width, col.Alignment, col.PadChar, format: null, cancellationToken),
            DataReaderColumnKind.Char => fixedWriter.WriteCharFieldAsync(unchecked((char)cell.Int64Value), col.Width, col.Alignment, col.PadChar, cancellationToken),
            _ => ValueTask.CompletedTask
        };
    }

    private struct BufferedCellValue
    {
        public DataReaderColumnKind Kind;
        public bool IsNull;
        public long Int64Value;
        public float SingleValue;
        public double DoubleValue;
        public decimal DecimalValue;
        public DateTime DateTimeValue;
        public Guid GuidValue;
        public object? ObjectValue;

        public static BufferedCellValue Null(DataReaderColumnKind kind) => new() { Kind = kind, IsNull = true };

        public static BufferedCellValue FromObject(DataReaderColumnKind kind, object? value)
            => value is null or DBNull
                ? new() { Kind = kind, IsNull = true }
                : new() { Kind = kind, ObjectValue = value };

        public static BufferedCellValue FromInt64(DataReaderColumnKind kind, long value)
            => new() { Kind = kind, Int64Value = value };

        public static BufferedCellValue FromSingle(float value)
            => new() { Kind = DataReaderColumnKind.Single, SingleValue = value };

        public static BufferedCellValue FromDouble(double value)
            => new() { Kind = DataReaderColumnKind.Double, DoubleValue = value };

        public static BufferedCellValue FromDecimal(decimal value)
            => new() { Kind = DataReaderColumnKind.Decimal, DecimalValue = value };

        public static BufferedCellValue FromDateTime(DateTime value)
            => new() { Kind = DataReaderColumnKind.DateTime, DateTimeValue = value };

        public static BufferedCellValue FromGuid(Guid value)
            => new() { Kind = DataReaderColumnKind.Guid, GuidValue = value };
    }
}
