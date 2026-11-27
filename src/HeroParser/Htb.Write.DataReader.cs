using System.Buffers;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Numerics;
using HeroParser.Htbs;
using HeroParser.Htbs.Records;
using HeroParser.Htbs.Writing;
using HeroParser.Internal;

namespace HeroParser;

public static partial class Htb
{
    /// <summary>
    /// Streams rows from an <see cref="IDataReader"/> into an HTB <see cref="Stream"/> without reflection.
    /// </summary>
    /// <param name="reader">The source data reader providing columns and rows.</param>
    /// <param name="stream">The destination writable stream.</param>
    /// <param name="options">Optional HTB write options; defaults to <see cref="HtbWriteOptions.Default"/>.</param>
    /// <param name="schema">Optional explicit HTB schema; inferred from <paramref name="reader"/> when <see langword="null"/>.</param>
    /// <param name="leaveOpen">When <see langword="true"/>, leaves <paramref name="stream"/> open after writing completes.</param>
    public static void WriteFromDataReader(
        IDataReader reader,
        Stream stream,
        HtbWriteOptions? options = null,
        HtbSchema? schema = null,
        bool leaveOpen = true)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(stream);

        bool streamOwnedByWriter = false;
        try
        {
            options ??= HtbWriteOptions.Default;
            options.Validate();

            HtbSchema resolvedSchema = ResolveDataReaderSchema(reader, schema);
            int colCount = resolvedSchema.Columns.Count;
            int maskLen = (colCount + 7) / 8;

            using var writer = new HtbStreamWriter(stream, options, leaveOpen);
            streamOwnedByWriter = true;
            writer.WriteHeader(resolvedSchema);

            byte[]? rented = null;
            Span<byte> mask = maskLen <= 128
                ? stackalloc byte[maskLen]
                : (rented = ArrayPool<byte>.Shared.Rent(maskLen)).AsSpan(0, maskLen);
            using var rowStage = new MemoryStream();
            using var rowWriter = new HtbStreamWriter(rowStage, HtbWriteOptions.Default, leaveOpen: true);
            DataReaderColumnPlan plan = default;
            long recordIndex = 0;
            try
            {
                while (reader.Read())
                {
                    if (!plan.IsInitialized)
                        plan = new DataReaderColumnPlan(reader);

                    mask.Clear();
                    rowWriter.ResetBuffer();
                    rowStage.SetLength(0);
                    if (options.MaxOutputSize is { } maxOutputSize)
                    {
                        long remainingForPayload = maxOutputSize - writer.BytesWritten - maskLen;
                        if (remainingForPayload < 0)
                            throw new HtbException(HtbErrorCode.LimitExceeded, "MaxOutputSize limit exceeded.");
                        rowWriter.SetMaxOutputSize(remainingForPayload);
                    }

                    int failedOrdinal = -1;
                    try
                    {
                        // Read each column in strictly forward ordinal order so CommandBehavior.SequentialAccess readers succeed.
                        for (int i = 0; i < colCount; i++)
                        {
                            failedOrdinal = i;
                            if (reader.IsDBNull(i))
                            {
                                if (!resolvedSchema.Columns[i].IsNullable)
                                {
                                    throw new HtbException(
                                        HtbErrorCode.SerializationError,
                                        $"Column '{resolvedSchema.Columns[i].Name}' is not nullable but DataReader returned null.");
                                }
                                mask[i / 8] |= (byte)(1 << (i % 8));
                            }
                            else
                            {
                                WritePlannedColumnValue(reader, i, plan[i], rowWriter, resolvedSchema.Columns[i], ref mask[i / 8]);
                            }
                        }

                        failedOrdinal = -1;
                        rowWriter.Flush();
                    }
                    catch (Exception ex) when (
                        options.OnError is not null
                        && ex is not (OutOfMemoryException or OperationCanceledException)
                        && ex is not HtbException { ErrorCode: HtbErrorCode.LimitExceeded })
                    {
                        rowWriter.ResetBuffer();
                        rowStage.SetLength(0);
                        HtbColumn? failedColumn = failedOrdinal >= 0 ? resolvedSchema.Columns[failedOrdinal] : null;
                        var ctx = new HtbSerializeErrorContext
                        {
                            RecordIndex = recordIndex,
                            MemberName = failedColumn?.Name,
                            TargetType = failedColumn is not null ? GetHtbColumnClrType(failedColumn.DataType) : null
                        };
                        if (options.OnError(ctx, ex) == HtbSerializeErrorAction.SkipRow)
                        {
                            recordIndex++;
                            continue;
                        }
                        throw;
                    }

                    int stagedLen = rowStage.TryGetBuffer(out ArraySegment<byte> stagedBytes) ? stagedBytes.Count : 0;
                    writer.EnsureCanWriteBytes((long)maskLen + stagedLen);
                    writer.IncrementRecordCount();
                    writer.WriteMask(mask);
                    if (stagedLen > 0)
                    {
                        writer.WriteRawBytes(stagedBytes.AsSpan());
                    }
                    recordIndex++;
                }

                writer.Flush();
            }
            finally
            {
                if (rented is not null)
                    ArrayPool<byte>.Shared.Return(rented);
            }
        }
        finally
        {
            if (!leaveOpen && !streamOwnedByWriter)
                stream.Dispose();
        }
    }

    /// <summary>
    /// Serializes rows from an <see cref="IDataReader"/> into an in-memory HTB byte array without reflection.
    /// </summary>
    /// <param name="reader">The source data reader providing columns and rows.</param>
    /// <param name="options">Optional HTB write options; defaults to <see cref="HtbWriteOptions.Default"/>.</param>
    /// <param name="schema">Optional explicit HTB schema; inferred from <paramref name="reader"/> when <see langword="null"/>.</param>
    /// <returns>The generated HTB binary payload.</returns>
    public static byte[] WriteFromDataReaderToBytes(
        IDataReader reader,
        HtbWriteOptions? options = null,
        HtbSchema? schema = null)
    {
        ArgumentNullException.ThrowIfNull(reader);

        using var ms = new MemoryStream();
        WriteFromDataReader(reader, ms, options, schema, leaveOpen: true);
        return ms.ToArray();
    }

    /// <summary>
    /// Streams rows from an <see cref="IDataReader"/> directly to an HTB file (including transparent <c>.gz</c>, <c>.br</c>, and <c>.zst</c>/<c>.zstd</c> compression) without reflection.
    /// </summary>
    /// <param name="path">Destination HTB file path.</param>
    /// <param name="reader">The source data reader providing columns and rows.</param>
    /// <param name="options">Optional HTB write options; defaults to <see cref="HtbWriteOptions.Default"/>.</param>
    /// <param name="schema">Optional explicit HTB schema; inferred from <paramref name="reader"/> when <see langword="null"/>.</param>
    public static void WriteFromDataReaderToFile(
        string path,
        IDataReader reader,
        HtbWriteOptions? options = null,
        HtbSchema? schema = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(reader);

        using var stream = new CompressedFile(path).OpenWrite();
        WriteFromDataReader(reader, stream, options, schema, leaveOpen: true);
    }

    /// <summary>
    /// Asynchronously streams rows from a <see cref="DbDataReader"/> into an HTB <see cref="Stream"/> without reflection.
    /// </summary>
    /// <param name="reader">The async-capable source database reader.</param>
    /// <param name="stream">The destination writable stream.</param>
    /// <param name="options">Optional HTB write options; defaults to <see cref="HtbWriteOptions.Default"/>.</param>
    /// <param name="schema">Optional explicit HTB schema; inferred from <paramref name="reader"/> when <see langword="null"/>.</param>
    /// <param name="leaveOpen">When <see langword="true"/>, leaves <paramref name="stream"/> open after writing completes.</param>
    /// <param name="cancellationToken">Token used to cancel async I/O.</param>
    public static async ValueTask WriteFromDataReaderAsync(
        DbDataReader reader,
        Stream stream,
        HtbWriteOptions? options = null,
        HtbSchema? schema = null,
        bool leaveOpen = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(stream);

        bool streamOwnedByWriter = false;
        try
        {
            options ??= HtbWriteOptions.Default;
            options.Validate();

            bool hasRow = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);

            HtbSchema resolvedSchema = ResolveDataReaderSchema(reader, schema);
            int colCount = resolvedSchema.Columns.Count;
            int maskLen = (colCount + 7) / 8;

            var writer = new HtbStreamWriter(stream, options, leaveOpen);
            streamOwnedByWriter = true;
            await using (writer.ConfigureAwait(false))
            {
                await writer.WriteHeaderAsync(resolvedSchema, cancellationToken).ConfigureAwait(false);

                byte[] mask = ArrayPool<byte>.Shared.Rent(maskLen);
                var rowStage = new MemoryStream();
                await using (rowStage.ConfigureAwait(false))
                {
                    var rowWriter = new HtbStreamWriter(rowStage, HtbWriteOptions.Default, leaveOpen: true);
                    await using (rowWriter.ConfigureAwait(false))
                    {
                        bool useAsyncFieldValues = DataReaderColumnPlan.HasAsyncFieldValueOverride(reader);
                        DataReaderColumnPlan plan = default;
                        long recordIndex = 0;
                        try
                        {
                            if (hasRow)
                            {
                                do
                                {
                                    if (!plan.IsInitialized)
                                        plan = new DataReaderColumnPlan(reader);

                                    Array.Clear(mask, 0, maskLen);
                                    rowWriter.ResetBuffer();
                                    rowStage.SetLength(0);
                                    if (options.MaxOutputSize is { } maxOutputSize)
                                    {
                                        long remainingForPayload = maxOutputSize - writer.BytesWritten - maskLen;
                                        if (remainingForPayload < 0)
                                            throw new HtbException(HtbErrorCode.LimitExceeded, "MaxOutputSize limit exceeded.");
                                        rowWriter.SetMaxOutputSize(remainingForPayload);
                                    }

                                    int failedOrdinal = -1;
                                    try
                                    {
                                        // Read each column in strictly forward ordinal order so CommandBehavior.SequentialAccess readers succeed.
                                        for (int i = 0; i < colCount; i++)
                                        {
                                            failedOrdinal = i;
                                            if (await reader.IsDBNullAsync(i, cancellationToken).ConfigureAwait(false))
                                            {
                                                if (!resolvedSchema.Columns[i].IsNullable)
                                                {
                                                    throw new HtbException(
                                                        HtbErrorCode.SerializationError,
                                                        $"Column '{resolvedSchema.Columns[i].Name}' is not nullable but DataReader returned null.");
                                                }
                                                mask[i / 8] |= (byte)(1 << (i % 8));
                                            }
                                            else if (useAsyncFieldValues)
                                            {
                                                await WritePlannedColumnValueAsync(reader, i, plan[i], rowWriter, resolvedSchema.Columns[i], mask, cancellationToken).ConfigureAwait(false);
                                            }
                                            else
                                            {
                                                WritePlannedColumnValue(reader, i, plan[i], rowWriter, resolvedSchema.Columns[i], ref mask[i / 8]);
                                            }
                                        }

                                        failedOrdinal = -1;
                                        rowWriter.Flush();
                                    }
                                    catch (Exception ex) when (
                                        options.OnError is not null
                                        && ex is not (OutOfMemoryException or OperationCanceledException)
                                        && ex is not HtbException { ErrorCode: HtbErrorCode.LimitExceeded })
                                    {
                                        rowWriter.ResetBuffer();
                                        rowStage.SetLength(0);
                                        HtbColumn? failedColumn = failedOrdinal >= 0 ? resolvedSchema.Columns[failedOrdinal] : null;
                                        var ctx = new HtbSerializeErrorContext
                                        {
                                            RecordIndex = recordIndex,
                                            MemberName = failedColumn?.Name,
                                            TargetType = failedColumn is not null ? GetHtbColumnClrType(failedColumn.DataType) : null
                                        };
                                        if (options.OnError(ctx, ex) == HtbSerializeErrorAction.SkipRow)
                                        {
                                            recordIndex++;
                                            continue;
                                        }
                                        throw;
                                    }

                                    int stagedLen = rowStage.TryGetBuffer(out ArraySegment<byte> stagedBytes) ? stagedBytes.Count : 0;
                                    writer.EnsureCanWriteBytes((long)maskLen + stagedLen);
                                    writer.IncrementRecordCount();
                                    await writer.WriteMaskAsync(mask.AsMemory(0, maskLen), cancellationToken).ConfigureAwait(false);
                                    if (stagedLen > 0)
                                    {
                                        await writer.WriteRawBytesAsync(stagedBytes.AsMemory(), cancellationToken).ConfigureAwait(false);
                                    }
                                    recordIndex++;
                                }
                                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false));
                            }

                            await writer.FlushInternalAsync(cancellationToken).ConfigureAwait(false);
                        }
                        finally
                        {
                            ArrayPool<byte>.Shared.Return(mask);
                        }
                    }
                }
            }
        }
        finally
        {
            if (!leaveOpen && !streamOwnedByWriter)
                await stream.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Asynchronously streams rows from a <see cref="DbDataReader"/> directly to an HTB file (including transparent <c>.gz</c>, <c>.br</c>, and <c>.zst</c>/<c>.zstd</c> compression) without reflection.
    /// </summary>
    /// <param name="path">Destination HTB file path.</param>
    /// <param name="reader">The async-capable source database reader.</param>
    /// <param name="options">Optional HTB write options; defaults to <see cref="HtbWriteOptions.Default"/>.</param>
    /// <param name="schema">Optional explicit HTB schema; inferred from <paramref name="reader"/> when <see langword="null"/>.</param>
    /// <param name="cancellationToken">Token used to cancel async I/O.</param>
    public static async ValueTask WriteFromDataReaderToFileAsync(
        string path,
        DbDataReader reader,
        HtbWriteOptions? options = null,
        HtbSchema? schema = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(reader);
        cancellationToken.ThrowIfCancellationRequested();

        var stream = new CompressedFile(path).OpenWrite(bufferSize: 4096, FileOptions.Asynchronous);
        await using (stream.ConfigureAwait(false))
        {
            await WriteFromDataReaderAsync(reader, stream, options, schema, leaveOpen: true, cancellationToken).ConfigureAwait(false);
        }
    }

    private static HtbSchema ResolveDataReaderSchema(IDataReader reader, HtbSchema? schema)
    {
        if (schema is not null)
        {
            if (schema.Columns.Count != reader.FieldCount)
            {
                throw new HtbException(
                    HtbErrorCode.SerializationError,
                    $"Schema column count ({schema.Columns.Count}) does not match DataReader FieldCount ({reader.FieldCount}).");
            }
            return schema;
        }

        int fieldCount = reader.FieldCount;
        var columns = new HtbColumn[fieldCount];
        for (int i = 0; i < fieldCount; i++)
        {
            string name = reader.GetName(i);
            if (string.IsNullOrWhiteSpace(name))
                name = $"Column{i}";

            HtbDataType dataType = MapHtbDataType(reader.GetFieldType(i));
            columns[i] = new HtbColumn(name, dataType, isNullable: true);
        }

        return new HtbSchema(columns);
    }

    private static Type GetHtbColumnClrType(HtbDataType dataType) => dataType switch
    {
        HtbDataType.Int32 => typeof(int),
        HtbDataType.Int64 => typeof(long),
        HtbDataType.Float => typeof(float),
        HtbDataType.Double => typeof(double),
        HtbDataType.Decimal => typeof(decimal),
        HtbDataType.Boolean => typeof(bool),
        HtbDataType.DateTime => typeof(DateTime),
        HtbDataType.String => typeof(string),
        HtbDataType.Guid => typeof(Guid),
        HtbDataType.FloatArray => typeof(float[]),
        HtbDataType.DateTimeOffset => typeof(DateTimeOffset),
        HtbDataType.DateOnly => typeof(DateOnly),
        HtbDataType.TimeOnly => typeof(TimeOnly),
        HtbDataType.TimeSpan => typeof(TimeSpan),
        HtbDataType.Int16 => typeof(short),
        HtbDataType.UInt16 => typeof(ushort),
        HtbDataType.Byte => typeof(byte),
        HtbDataType.SByte => typeof(sbyte),
        HtbDataType.UInt32 => typeof(uint),
        HtbDataType.UInt64 => typeof(ulong),
        HtbDataType.Char => typeof(char),
        HtbDataType.Half => typeof(Half),
        HtbDataType.Int128 => typeof(Int128),
        HtbDataType.UInt128 => typeof(UInt128),
        HtbDataType.ByteArray => typeof(byte[]),
        HtbDataType.BigInteger => typeof(BigInteger),
        HtbDataType.Uri => typeof(Uri),
        _ => typeof(object)
    };

    private static HtbDataType MapHtbDataType(Type type)
    {
        if (type == typeof(int))
            return HtbDataType.Int32;
        if (type == typeof(long))
            return HtbDataType.Int64;
        if (type == typeof(short))
            return HtbDataType.Int16;
        if (type == typeof(ushort))
            return HtbDataType.UInt16;
        if (type == typeof(byte))
            return HtbDataType.Byte;
        if (type == typeof(sbyte))
            return HtbDataType.SByte;
        if (type == typeof(uint))
            return HtbDataType.UInt32;
        if (type == typeof(ulong))
            return HtbDataType.UInt64;
        if (type == typeof(float))
            return HtbDataType.Float;
        if (type == typeof(double))
            return HtbDataType.Double;
        if (type == typeof(decimal))
            return HtbDataType.Decimal;
        if (type == typeof(bool))
            return HtbDataType.Boolean;
        if (type == typeof(char))
            return HtbDataType.Char;
        if (type == typeof(DateTime))
            return HtbDataType.DateTime;
        if (type == typeof(DateTimeOffset))
            return HtbDataType.DateTimeOffset;
        if (type == typeof(DateOnly))
            return HtbDataType.DateOnly;
        if (type == typeof(TimeOnly))
            return HtbDataType.TimeOnly;
        if (type == typeof(TimeSpan))
            return HtbDataType.TimeSpan;
        if (type == typeof(Guid))
            return HtbDataType.Guid;
        if (type == typeof(Half))
            return HtbDataType.Half;
        if (type == typeof(Int128))
            return HtbDataType.Int128;
        if (type == typeof(UInt128))
            return HtbDataType.UInt128;
        if (type == typeof(byte[]))
            return HtbDataType.ByteArray;
        if (type == typeof(BigInteger))
            return HtbDataType.BigInteger;
        if (type == typeof(Uri))
            return HtbDataType.Uri;
        if (type == typeof(float[]))
            return HtbDataType.FloatArray;
        return HtbDataType.String;
    }

    private static void WritePlannedColumnValue(
        IDataRecord reader,
        int ordinal,
        DataReaderColumnKind kind,
        HtbStreamWriter writer,
        HtbColumn column,
        ref byte maskByte)
    {
        switch (column.DataType, kind)
        {
            case (HtbDataType.Int32, DataReaderColumnKind.Int32):
                writer.WriteInt32(reader.GetInt32(ordinal));
                return;
            case (HtbDataType.Int64, DataReaderColumnKind.Int64):
                writer.WriteInt64(reader.GetInt64(ordinal));
                return;
            case (HtbDataType.Int16, DataReaderColumnKind.Int16):
                writer.WriteInt16(reader.GetInt16(ordinal));
                return;
            case (HtbDataType.Byte, DataReaderColumnKind.Byte):
                writer.WriteByte(reader.GetByte(ordinal));
                return;
            case (HtbDataType.Float, DataReaderColumnKind.Single):
                writer.WriteFloat(reader.GetFloat(ordinal));
                return;
            case (HtbDataType.Double, DataReaderColumnKind.Double):
                writer.WriteDouble(reader.GetDouble(ordinal));
                return;
            case (HtbDataType.Decimal, DataReaderColumnKind.Decimal):
                writer.WriteDecimal(reader.GetDecimal(ordinal));
                return;
            case (HtbDataType.Boolean, DataReaderColumnKind.Boolean):
                writer.WriteBoolean(reader.GetBoolean(ordinal));
                return;
            case (HtbDataType.Char, DataReaderColumnKind.Char):
                writer.WriteChar(reader.GetChar(ordinal));
                return;
            case (HtbDataType.DateTime, DataReaderColumnKind.DateTime):
                writer.WriteDateTime(reader.GetDateTime(ordinal));
                return;
            case (HtbDataType.Guid, DataReaderColumnKind.Guid):
                writer.WriteGuid(reader.GetGuid(ordinal));
                return;
            case (HtbDataType.String, DataReaderColumnKind.String):
                writer.WriteString(reader.GetString(ordinal));
                return;
            default:
                object? val = reader.GetValue(ordinal);
                if (val is null or DBNull)
                {
                    if (!column.IsNullable)
                    {
                        throw new HtbException(
                            HtbErrorCode.SerializationError,
                            $"Column '{column.Name}' is not nullable but DataReader returned null.");
                    }
                    maskByte |= (byte)(1 << (ordinal % 8));
                    return;
                }
                WriteColumnValue(writer, column.DataType, val);
                return;
        }
    }

    private static async ValueTask WritePlannedColumnValueAsync(
        DbDataReader reader,
        int ordinal,
        DataReaderColumnKind kind,
        HtbStreamWriter writer,
        HtbColumn column,
        byte[] mask,
        CancellationToken cancellationToken)
    {
        switch (column.DataType, kind)
        {
            case (HtbDataType.Int32, DataReaderColumnKind.Int32):
                writer.WriteInt32(await reader.GetFieldValueAsync<int>(ordinal, cancellationToken).ConfigureAwait(false));
                return;
            case (HtbDataType.Int64, DataReaderColumnKind.Int64):
                writer.WriteInt64(await reader.GetFieldValueAsync<long>(ordinal, cancellationToken).ConfigureAwait(false));
                return;
            case (HtbDataType.Int16, DataReaderColumnKind.Int16):
                writer.WriteInt16(await reader.GetFieldValueAsync<short>(ordinal, cancellationToken).ConfigureAwait(false));
                return;
            case (HtbDataType.Byte, DataReaderColumnKind.Byte):
                writer.WriteByte(await reader.GetFieldValueAsync<byte>(ordinal, cancellationToken).ConfigureAwait(false));
                return;
            case (HtbDataType.Float, DataReaderColumnKind.Single):
                writer.WriteFloat(await reader.GetFieldValueAsync<float>(ordinal, cancellationToken).ConfigureAwait(false));
                return;
            case (HtbDataType.Double, DataReaderColumnKind.Double):
                writer.WriteDouble(await reader.GetFieldValueAsync<double>(ordinal, cancellationToken).ConfigureAwait(false));
                return;
            case (HtbDataType.Decimal, DataReaderColumnKind.Decimal):
                writer.WriteDecimal(await reader.GetFieldValueAsync<decimal>(ordinal, cancellationToken).ConfigureAwait(false));
                return;
            case (HtbDataType.Boolean, DataReaderColumnKind.Boolean):
                writer.WriteBoolean(await reader.GetFieldValueAsync<bool>(ordinal, cancellationToken).ConfigureAwait(false));
                return;
            case (HtbDataType.Char, DataReaderColumnKind.Char):
                writer.WriteChar(await reader.GetFieldValueAsync<char>(ordinal, cancellationToken).ConfigureAwait(false));
                return;
            case (HtbDataType.DateTime, DataReaderColumnKind.DateTime):
                writer.WriteDateTime(await reader.GetFieldValueAsync<DateTime>(ordinal, cancellationToken).ConfigureAwait(false));
                return;
            case (HtbDataType.Guid, DataReaderColumnKind.Guid):
                writer.WriteGuid(await reader.GetFieldValueAsync<Guid>(ordinal, cancellationToken).ConfigureAwait(false));
                return;
            case (HtbDataType.String, DataReaderColumnKind.String):
                {
                    string? s = await reader.GetFieldValueAsync<string?>(ordinal, cancellationToken).ConfigureAwait(false);
                    if (s is null)
                    {
                        if (!column.IsNullable)
                        {
                            throw new HtbException(
                                HtbErrorCode.SerializationError,
                                $"Column '{column.Name}' is not nullable but DataReader returned null.");
                        }
                        mask[ordinal / 8] |= (byte)(1 << (ordinal % 8));
                        return;
                    }
                    writer.WriteString(s);
                }
                return;
            default:
                object? val = await reader.GetFieldValueAsync<object?>(ordinal, cancellationToken).ConfigureAwait(false);
                if (val is null or DBNull)
                {
                    if (!column.IsNullable)
                    {
                        throw new HtbException(
                            HtbErrorCode.SerializationError,
                            $"Column '{column.Name}' is not nullable but DataReader returned null.");
                    }
                    mask[ordinal / 8] |= (byte)(1 << (ordinal % 8));
                    return;
                }
                WriteColumnValue(writer, column.DataType, val);
                return;
        }
    }

    private static void WriteColumnValue(HtbStreamWriter writer, HtbDataType dataType, object val)
    {
        switch (dataType)
        {
            case HtbDataType.Int32:
                writer.WriteInt32(val is int i ? i : Convert.ToInt32(val, CultureInfo.InvariantCulture));
                break;
            case HtbDataType.Int64:
                writer.WriteInt64(val is long l ? l : Convert.ToInt64(val, CultureInfo.InvariantCulture));
                break;
            case HtbDataType.Float:
                writer.WriteFloat(val is float f ? f : Convert.ToSingle(val, CultureInfo.InvariantCulture));
                break;
            case HtbDataType.Double:
                writer.WriteDouble(val is double d ? d : Convert.ToDouble(val, CultureInfo.InvariantCulture));
                break;
            case HtbDataType.Decimal:
                writer.WriteDecimal(val is decimal dec ? dec : Convert.ToDecimal(val, CultureInfo.InvariantCulture));
                break;
            case HtbDataType.Boolean:
                writer.WriteBoolean(val is bool b ? b : Convert.ToBoolean(val, CultureInfo.InvariantCulture));
                break;
            case HtbDataType.DateTime:
                writer.WriteDateTime(val is DateTime dt ? dt : Convert.ToDateTime(val, CultureInfo.InvariantCulture));
                break;
            case HtbDataType.DateTimeOffset:
                writer.WriteDateTimeOffset(val switch
                {
                    DateTimeOffset dto => dto,
                    DateTime dtVal => new DateTimeOffset(dtVal),
                    _ => DateTimeOffset.Parse(Convert.ToString(val, CultureInfo.InvariantCulture) ?? string.Empty, CultureInfo.InvariantCulture)
                });
                break;
            case HtbDataType.DateOnly:
                writer.WriteDateOnly(val switch
                {
                    DateOnly dOnly => dOnly,
                    DateTime dtVal => DateOnly.FromDateTime(dtVal),
                    _ => DateOnly.Parse(Convert.ToString(val, CultureInfo.InvariantCulture) ?? string.Empty, CultureInfo.InvariantCulture)
                });
                break;
            case HtbDataType.TimeOnly:
                writer.WriteTimeOnly(val switch
                {
                    TimeOnly tOnly => tOnly,
                    DateTime dtVal => TimeOnly.FromDateTime(dtVal),
                    TimeSpan tsVal => TimeOnly.FromTimeSpan(tsVal),
                    _ => TimeOnly.Parse(Convert.ToString(val, CultureInfo.InvariantCulture) ?? string.Empty, CultureInfo.InvariantCulture)
                });
                break;
            case HtbDataType.TimeSpan:
                writer.WriteTimeSpan(val is TimeSpan ts ? ts : TimeSpan.Parse(Convert.ToString(val, CultureInfo.InvariantCulture) ?? string.Empty, CultureInfo.InvariantCulture));
                break;
            case HtbDataType.Int16:
                writer.WriteInt16(val is short i16 ? i16 : Convert.ToInt16(val, CultureInfo.InvariantCulture));
                break;
            case HtbDataType.UInt16:
                writer.WriteUInt16(val is ushort u16 ? u16 : Convert.ToUInt16(val, CultureInfo.InvariantCulture));
                break;
            case HtbDataType.Byte:
                writer.WriteByte(val is byte u8 ? u8 : Convert.ToByte(val, CultureInfo.InvariantCulture));
                break;
            case HtbDataType.SByte:
                writer.WriteSByte(val is sbyte i8 ? i8 : Convert.ToSByte(val, CultureInfo.InvariantCulture));
                break;
            case HtbDataType.UInt32:
                writer.WriteUInt32(val is uint u32 ? u32 : Convert.ToUInt32(val, CultureInfo.InvariantCulture));
                break;
            case HtbDataType.UInt64:
                writer.WriteUInt64(val is ulong u64 ? u64 : Convert.ToUInt64(val, CultureInfo.InvariantCulture));
                break;
            case HtbDataType.Char:
                writer.WriteChar(val is char c ? c : Convert.ToChar(val, CultureInfo.InvariantCulture));
                break;
            case HtbDataType.Half:
                writer.WriteHalf(val is Half h ? h : (Half)Convert.ToDouble(val, CultureInfo.InvariantCulture));
                break;
            case HtbDataType.Int128:
                writer.WriteInt128(val is Int128 i128 ? i128 : Int128.Parse(Convert.ToString(val, CultureInfo.InvariantCulture) ?? string.Empty, CultureInfo.InvariantCulture));
                break;
            case HtbDataType.UInt128:
                writer.WriteUInt128(val is UInt128 u128 ? u128 : UInt128.Parse(Convert.ToString(val, CultureInfo.InvariantCulture) ?? string.Empty, CultureInfo.InvariantCulture));
                break;
            case HtbDataType.ByteArray:
                if (val is byte[] byteArr)
                {
                    writer.WriteByteArray(byteArr);
                }
                else
                {
                    string text = Convert.ToString(val, CultureInfo.InvariantCulture) ?? string.Empty;
                    if (!Utf8SpanParser.TryParseBase64Chars(text.AsSpan(), out byte[]? parsedBytes))
                        throw new FormatException($"Invalid base64 string '{text}'.");
                    writer.WriteByteArray(parsedBytes);
                }
                break;
            case HtbDataType.BigInteger:
                writer.WriteBigInteger(val is BigInteger bi ? bi : BigInteger.Parse(Convert.ToString(val, CultureInfo.InvariantCulture) ?? string.Empty, CultureInfo.InvariantCulture));
                break;
            case HtbDataType.Uri:
                writer.WriteUri(val is Uri uri ? uri : new Uri(Convert.ToString(val, CultureInfo.InvariantCulture) ?? string.Empty, UriKind.RelativeOrAbsolute));
                break;
            case HtbDataType.Guid:
                writer.WriteGuid(val is Guid g ? g : Guid.Parse(Convert.ToString(val, CultureInfo.InvariantCulture) ?? string.Empty));
                break;
            case HtbDataType.String:
                string str = val switch
                {
                    string s => s,
                    Uri uriVal => uriVal.OriginalString,
                    byte[] raw => raw.Length == 0 ? Utf8SpanParser.EMPTY_BASE64_TOKEN : Convert.ToBase64String(raw),
                    IFormattable fmt => fmt.ToString(null, CultureInfo.InvariantCulture),
                    _ => val.ToString() ?? string.Empty
                };
                writer.WriteString(str);
                break;
            case HtbDataType.FloatArray:
                if (val is float[] arr)
                    writer.WriteFloatArray(arr);
                else
                    throw new HtbException(HtbErrorCode.SerializationError, $"Expected float[] for FloatArray column, got {val.GetType().Name}.");
                break;
            case HtbDataType.Unknown:
            default:
                throw new HtbException(HtbErrorCode.SerializationError, $"Unsupported column type: {dataType}");
        }
    }
}
