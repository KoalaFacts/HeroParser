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
                if (string.Equals(reader.GetName(j), targetName, StringComparison.OrdinalIgnoreCase))
                {
                    found = j;
                    break;
                }
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

        DataReaderColumnPlan plan = default;
        string nullValue = fixedWriter.NullValue;

        while (reader.Read())
        {
            if (!plan.IsInitialized)
                plan = new DataReaderColumnPlan(reader);

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
                        fixedWriter.WriteField(reader.GetInt16(ordinal), col.Width, col.Alignment, col.PadChar);
                        break;
                    case DataReaderColumnKind.Byte:
                        fixedWriter.WriteField(reader.GetByte(ordinal), col.Width, col.Alignment, col.PadChar);
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

        DataReaderColumnPlan plan = default;
        string nullValue = fixedWriter.NullValue;
        string? numberFormat = fixedWriter.NumberFormat;
        string? dateTimeFormat = fixedWriter.DateTimeFormat;

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!plan.IsInitialized)
                plan = new DataReaderColumnPlan(reader);

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
                if (kind is not (DataReaderColumnKind.String or DataReaderColumnKind.Object) &&
                    await reader.IsDBNullAsync(ordinal, cancellationToken).ConfigureAwait(false))
                {
                    await fixedWriter.WriteFieldAsync(nullValue.AsMemory(), col.Width, col.Alignment, col.PadChar, cancellationToken).ConfigureAwait(false);
                    continue;
                }

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
            await fixedWriter.EndRowAsync(cancellationToken).ConfigureAwait(false);
        }

        await fixedWriter.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
