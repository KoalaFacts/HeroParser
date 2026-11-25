using System.Data;
using System.Data.Common;
using System.Text;
using HeroParser.Internal;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Writing;

namespace HeroParser;

public static partial class Csv
{
    /// <summary>
    /// Streams rows from an <see cref="IDataReader"/> into a <see cref="TextWriter"/> in CSV format without reflection.
    /// </summary>
    /// <param name="reader">The data reader providing columns and rows.</param>
    /// <param name="writer">The destination text writer.</param>
    /// <param name="options">Optional CSV formatting options; defaults to <see cref="CsvWriteOptions.Default"/>.</param>
    /// <param name="writeHeader">When <see langword="true"/>, emits a header row using column names from <paramref name="reader"/>.</param>
    /// <param name="leaveOpen">When <see langword="true"/>, leaves <paramref name="writer"/> open after writing completes.</param>
    public static void WriteFromDataReader(
        IDataReader reader,
        TextWriter writer,
        CsvWriteOptions? options = null,
        bool writeHeader = true,
        bool leaveOpen = true)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(writer);

        using var csvWriter = new CsvStreamWriter(writer, options, leaveOpen);
        WriteDataReaderRows(reader, csvWriter, writeHeader);
    }

    /// <summary>
    /// Streams rows from an <see cref="IDataReader"/> into a <see cref="Stream"/> in CSV format without reflection.
    /// </summary>
    /// <param name="reader">The data reader providing columns and rows.</param>
    /// <param name="stream">The destination writable stream.</param>
    /// <param name="options">Optional CSV formatting options; defaults to <see cref="CsvWriteOptions.Default"/>.</param>
    /// <param name="encoding">Optional text encoding; defaults to UTF-8.</param>
    /// <param name="writeHeader">When <see langword="true"/>, emits a header row using column names from <paramref name="reader"/>.</param>
    /// <param name="leaveOpen">When <see langword="true"/>, leaves <paramref name="stream"/> open after writing completes.</param>
    public static void WriteFromDataReader(
        IDataReader reader,
        Stream stream,
        CsvWriteOptions? options = null,
        Encoding? encoding = null,
        bool writeHeader = true,
        bool leaveOpen = true)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(stream);

        encoding ??= Encoding.UTF8;
        using var textWriter = new StreamWriter(stream, encoding, bufferSize: 16 * 1024, leaveOpen: leaveOpen);
        using var csvWriter = new CsvStreamWriter(textWriter, options, leaveOpen: true);
        WriteDataReaderRows(reader, csvWriter, writeHeader);
    }

    /// <summary>
    /// Streams rows from an <see cref="IDataReader"/> directly to a CSV file without reflection.
    /// </summary>
    /// <param name="path">Destination file path.</param>
    /// <param name="reader">The data reader providing columns and rows.</param>
    /// <param name="options">Optional CSV formatting options; defaults to <see cref="CsvWriteOptions.Default"/>.</param>
    /// <param name="encoding">Optional text encoding; defaults to UTF-8.</param>
    /// <param name="writeHeader">When <see langword="true"/>, emits a header row using column names from <paramref name="reader"/>.</param>
    public static void WriteFromDataReaderToFile(
        string path,
        IDataReader reader,
        CsvWriteOptions? options = null,
        Encoding? encoding = null,
        bool writeHeader = true)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(reader);

        encoding ??= Encoding.UTF8;
        using var stream = new CompressedFile(path).OpenWrite();
        WriteFromDataReader(reader, stream, options, encoding, writeHeader, leaveOpen: false);
    }

    /// <summary>
    /// Serializes rows from an <see cref="IDataReader"/> into a CSV string without reflection.
    /// </summary>
    /// <param name="reader">The data reader providing columns and rows.</param>
    /// <param name="options">Optional CSV formatting options; defaults to <see cref="CsvWriteOptions.Default"/>.</param>
    /// <param name="writeHeader">When <see langword="true"/>, emits a header row using column names from <paramref name="reader"/>.</param>
    /// <returns>The serialized CSV text.</returns>
    public static string WriteFromDataReaderToText(
        IDataReader reader,
        CsvWriteOptions? options = null,
        bool writeHeader = true)
    {
        ArgumentNullException.ThrowIfNull(reader);

        using var sw = new StringWriter();
        using (var csvWriter = new CsvStreamWriter(sw, options, leaveOpen: true))
        {
            WriteDataReaderRows(reader, csvWriter, writeHeader);
        }
        return sw.ToString();
    }

    /// <summary>
    /// Asynchronously streams rows from a <see cref="DbDataReader"/> into a <see cref="Stream"/> in CSV format without reflection.
    /// </summary>
    /// <param name="reader">The async-capable database data reader providing columns and rows.</param>
    /// <param name="stream">The destination writable stream.</param>
    /// <param name="options">Optional CSV formatting options; defaults to <see cref="CsvWriteOptions.Default"/>.</param>
    /// <param name="encoding">Optional text encoding; defaults to UTF-8.</param>
    /// <param name="writeHeader">When <see langword="true"/>, emits a header row using column names from <paramref name="reader"/>.</param>
    /// <param name="leaveOpen">When <see langword="true"/>, leaves <paramref name="stream"/> open after writing completes.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public static async ValueTask WriteFromDataReaderAsync(
        DbDataReader reader,
        Stream stream,
        CsvWriteOptions? options = null,
        Encoding? encoding = null,
        bool writeHeader = true,
        bool leaveOpen = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(stream);

        encoding ??= Encoding.UTF8;
        var csvWriter = new CsvAsyncStreamWriter(stream, options, encoding, leaveOpen);
        await using (csvWriter.ConfigureAwait(false))
        {
            await WriteDataReaderRowsAsync(reader, csvWriter, writeHeader, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Asynchronously streams rows from a <see cref="DbDataReader"/> directly to a CSV file without reflection.
    /// </summary>
    /// <param name="path">Destination file path.</param>
    /// <param name="reader">The async-capable database data reader providing columns and rows.</param>
    /// <param name="options">Optional CSV formatting options; defaults to <see cref="CsvWriteOptions.Default"/>.</param>
    /// <param name="encoding">Optional text encoding; defaults to UTF-8.</param>
    /// <param name="writeHeader">When <see langword="true"/>, emits a header row using column names from <paramref name="reader"/>.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public static async ValueTask WriteFromDataReaderToFileAsync(
        string path,
        DbDataReader reader,
        CsvWriteOptions? options = null,
        Encoding? encoding = null,
        bool writeHeader = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(reader);

        encoding ??= Encoding.UTF8;
        var stream = new CompressedFile(path).OpenWrite(
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using (stream.ConfigureAwait(false))
        {
            await WriteFromDataReaderAsync(reader, stream, options, encoding, writeHeader, leaveOpen: false, cancellationToken).ConfigureAwait(false);
        }
    }

    private static void WriteDataReaderRows(IDataReader reader, CsvStreamWriter csvWriter, bool writeHeader)
    {
        int fieldCount = reader.FieldCount;
        if (fieldCount <= 0)
            return;

        if (writeHeader)
        {
            for (int i = 0; i < fieldCount; i++)
                csvWriter.WriteField(reader.GetName(i));
            csvWriter.EndRow();
        }

        DataReaderColumnPlan plan = default;
        string nullValue = csvWriter.NullValue;
        string? numberFormat = csvWriter.NumberFormat;

        while (reader.Read())
        {
            if (!plan.IsInitialized)
                plan = new DataReaderColumnPlan(reader);

            for (int i = 0; i < fieldCount; i++)
            {
                DataReaderColumnKind kind = plan[i];
                if (kind is not (DataReaderColumnKind.String or DataReaderColumnKind.Object) && reader.IsDBNull(i))
                {
                    csvWriter.WriteField(nullValue);
                    continue;
                }

                switch (kind)
                {
                    case DataReaderColumnKind.String:
                        {
                            object? raw = reader.GetValue(i);
                            if (raw is null or DBNull)
                                csvWriter.WriteField(nullValue);
                            else if (raw is string str)
                                csvWriter.WriteField(str);
                            else
                                csvWriter.WriteFormattedField(raw);
                        }
                        break;
                    case DataReaderColumnKind.Object:
                        {
                            object? raw = reader.GetValue(i);
                            csvWriter.WriteFormattedField(raw is DBNull ? null : raw);
                        }
                        break;
                    case DataReaderColumnKind.Int32:
                        csvWriter.WriteField(reader.GetInt32(i), numberFormat);
                        break;
                    case DataReaderColumnKind.Int64:
                        csvWriter.WriteField(reader.GetInt64(i), numberFormat);
                        break;
                    case DataReaderColumnKind.Int16:
                        csvWriter.WriteField(reader.GetInt16(i), numberFormat);
                        break;
                    case DataReaderColumnKind.Byte:
                        csvWriter.WriteField(reader.GetByte(i), numberFormat);
                        break;
                    case DataReaderColumnKind.Single:
                        csvWriter.WriteField(reader.GetFloat(i), numberFormat);
                        break;
                    case DataReaderColumnKind.Double:
                        csvWriter.WriteField(reader.GetDouble(i), numberFormat);
                        break;
                    case DataReaderColumnKind.Decimal:
                        csvWriter.WriteField(reader.GetDecimal(i), numberFormat);
                        break;
                    case DataReaderColumnKind.Boolean:
                        csvWriter.WriteField(reader.GetBoolean(i));
                        break;
                    case DataReaderColumnKind.DateTime:
                        csvWriter.WriteField(reader.GetDateTime(i));
                        break;
                    case DataReaderColumnKind.Guid:
                        csvWriter.WriteField(reader.GetGuid(i));
                        break;
                    case DataReaderColumnKind.Char:
                        csvWriter.WriteField(reader.GetChar(i));
                        break;
                    default:
                        break;
                }
            }
            csvWriter.EndRow();
        }

        csvWriter.Flush();
    }

    private static async ValueTask WriteDataReaderRowsAsync(
        DbDataReader reader,
        CsvAsyncStreamWriter csvWriter,
        bool writeHeader,
        CancellationToken cancellationToken)
    {
        int fieldCount = reader.FieldCount;
        if (fieldCount <= 0)
            return;

        if (writeHeader)
        {
            for (int i = 0; i < fieldCount; i++)
                await csvWriter.WriteFieldAsync(reader.GetName(i), cancellationToken).ConfigureAwait(false);
            await csvWriter.EndRowAsync(cancellationToken).ConfigureAwait(false);
        }

        DataReaderColumnPlan plan = default;
        string nullValue = csvWriter.NullValue;
        string? numberFormat = csvWriter.NumberFormat;

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!plan.IsInitialized)
                plan = new DataReaderColumnPlan(reader);

            for (int i = 0; i < fieldCount; i++)
            {
                DataReaderColumnKind kind = plan[i];
                if (kind is not (DataReaderColumnKind.String or DataReaderColumnKind.Object) && reader.IsDBNull(i))
                {
                    await csvWriter.WriteFieldAsync(nullValue, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                switch (kind)
                {
                    case DataReaderColumnKind.String:
                        {
                            object? raw = reader.GetValue(i);
                            if (raw is null or DBNull)
                                await csvWriter.WriteFieldAsync(nullValue, cancellationToken).ConfigureAwait(false);
                            else if (raw is string str)
                                await csvWriter.WriteFieldAsync(str, cancellationToken).ConfigureAwait(false);
                            else
                                await csvWriter.WriteFormattedFieldAsync(raw, cancellationToken).ConfigureAwait(false);
                        }
                        break;
                    case DataReaderColumnKind.Object:
                        {
                            object? raw = reader.GetValue(i);
                            await csvWriter.WriteFormattedFieldAsync(raw is DBNull ? null : raw, cancellationToken).ConfigureAwait(false);
                        }
                        break;
                    case DataReaderColumnKind.Int32:
                        await csvWriter.WriteFieldAsync(reader.GetInt32(i), numberFormat, cancellationToken).ConfigureAwait(false);
                        break;
                    case DataReaderColumnKind.Int64:
                        await csvWriter.WriteFieldAsync(reader.GetInt64(i), numberFormat, cancellationToken).ConfigureAwait(false);
                        break;
                    case DataReaderColumnKind.Int16:
                        await csvWriter.WriteFieldAsync(reader.GetInt16(i), numberFormat, cancellationToken).ConfigureAwait(false);
                        break;
                    case DataReaderColumnKind.Byte:
                        await csvWriter.WriteFieldAsync(reader.GetByte(i), numberFormat, cancellationToken).ConfigureAwait(false);
                        break;
                    case DataReaderColumnKind.Single:
                        await csvWriter.WriteFieldAsync(reader.GetFloat(i), numberFormat, cancellationToken).ConfigureAwait(false);
                        break;
                    case DataReaderColumnKind.Double:
                        await csvWriter.WriteFieldAsync(reader.GetDouble(i), numberFormat, cancellationToken).ConfigureAwait(false);
                        break;
                    case DataReaderColumnKind.Decimal:
                        await csvWriter.WriteFieldAsync(reader.GetDecimal(i), numberFormat, cancellationToken).ConfigureAwait(false);
                        break;
                    case DataReaderColumnKind.Boolean:
                        await csvWriter.WriteFieldAsync(reader.GetBoolean(i), cancellationToken).ConfigureAwait(false);
                        break;
                    case DataReaderColumnKind.DateTime:
                        await csvWriter.WriteFieldAsync(reader.GetDateTime(i), cancellationToken).ConfigureAwait(false);
                        break;
                    case DataReaderColumnKind.Guid:
                        await csvWriter.WriteFieldAsync(reader.GetGuid(i), cancellationToken).ConfigureAwait(false);
                        break;
                    case DataReaderColumnKind.Char:
                        await csvWriter.WriteFieldAsync(reader.GetChar(i), cancellationToken).ConfigureAwait(false);
                        break;
                    default:
                        break;
                }
            }
            await csvWriter.EndRowAsync(cancellationToken).ConfigureAwait(false);
        }

        await csvWriter.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
