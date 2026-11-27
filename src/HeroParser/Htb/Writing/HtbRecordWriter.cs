#pragma warning disable IDE0010 // Populate switch
#pragma warning disable IDE0066 // Use 'switch' expression

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using HeroParser.Htbs.Records;

namespace HeroParser.Htbs.Writing;

/// <summary>
/// Streams and serializes C# records into the High-Throughput Tabular Binary (HTB) format.
/// </summary>
public sealed class HtbRecordWriter<T> : HtbStreamWriter where T : new()
{
    private HtbRecordBinder<T>? binder;
    private IHtbWriter<T>? generatedWriter;
    private bool recordHeaderWritten;

    /// <summary>
    /// Initializes a new instance of <see cref="HtbRecordWriter{T}"/>.
    /// </summary>
    public HtbRecordWriter(Stream stream, HtbWriteOptions? options = null, bool leaveOpen = false)
        : base(stream, options, leaveOpen)
    {
    }

    /// <summary>
    /// Writes all records from the enumerable source synchronously.
    /// </summary>
    [RequiresUnreferencedCode("Reflection-based HTB serialization requires dynamic binding which is not Native AOT-safe.")]
    public void WriteRecords(IEnumerable<T> records)
    {
        ArgumentNullException.ThrowIfNull(records);

        if (!recordHeaderWritten)
        {
            WriteRecordHeader();
        }

        foreach (var record in records)
        {
            WriteSingleRecord(record);
        }

        Flush();
    }

    /// <summary>
    /// Writes all records from the enumerable source asynchronously.
    /// </summary>
    [RequiresUnreferencedCode("Reflection-based HTB serialization requires dynamic binding which is not Native AOT-safe.")]
    public Task WriteRecordsAsync(IEnumerable<T> records)
        => WriteRecordsAsync(records, CancellationToken.None);

    /// <summary>
    /// Writes all records from the enumerable source asynchronously with cancellation support.
    /// </summary>
    /// <param name="records">The records to write.</param>
    /// <param name="cancellationToken">Token used to cancel async I/O.</param>
    [RequiresUnreferencedCode("Reflection-based HTB serialization requires dynamic binding which is not Native AOT-safe.")]
    public async Task WriteRecordsAsync(IEnumerable<T> records, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(records);
        cancellationToken.ThrowIfCancellationRequested();

        activeCancellationToken = cancellationToken;
        try
        {
            if (!recordHeaderWritten)
            {
                await WriteRecordHeaderAsync(cancellationToken).ConfigureAwait(false);
            }

            foreach (var record in records)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await WriteSingleRecordAsync(record, cancellationToken).ConfigureAwait(false);
            }

            await FlushInternalAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            activeCancellationToken = default;
        }
    }

    /// <summary>
    /// Writes all records from the async enumerable source asynchronously.
    /// </summary>
    [RequiresUnreferencedCode("Reflection-based HTB serialization requires dynamic binding which is not Native AOT-safe.")]
    public Task WriteRecordsAsync(IAsyncEnumerable<T> records)
        => WriteRecordsAsync(records, CancellationToken.None);

    /// <summary>
    /// Writes all records from the async enumerable source asynchronously with cancellation support.
    /// </summary>
    /// <param name="records">The async enumerable records to write.</param>
    /// <param name="cancellationToken">Token used to cancel enumeration and async I/O.</param>
    [RequiresUnreferencedCode("Reflection-based HTB serialization requires dynamic binding which is not Native AOT-safe.")]
    public async Task WriteRecordsAsync(IAsyncEnumerable<T> records, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(records);
        cancellationToken.ThrowIfCancellationRequested();

        activeCancellationToken = cancellationToken;
        try
        {
            if (!recordHeaderWritten)
            {
                await WriteRecordHeaderAsync(cancellationToken).ConfigureAwait(false);
            }

            await foreach (var record in records.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await WriteSingleRecordAsync(record, cancellationToken).ConfigureAwait(false);
            }

            await FlushInternalAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            activeCancellationToken = default;
        }
    }

    [RequiresUnreferencedCode("Reflection schema extraction is not safe for Native AOT.")]
    private void WriteRecordHeader()
    {
        generatedWriter = HtbRecordWriterFactory.GetWriter<T>();
        var targetSchema = HtbSchema.FromType<T>();
        WriteHeader(targetSchema);
        if (generatedWriter == null)
        {
            binder = new HtbRecordBinder<T>(targetSchema);
        }
        recordHeaderWritten = true;
    }

    [RequiresUnreferencedCode("Reflection schema extraction is not safe for Native AOT.")]
    private async Task WriteRecordHeaderAsync(CancellationToken cancellationToken)
    {
        generatedWriter = HtbRecordWriterFactory.GetWriter<T>();
        var targetSchema = HtbSchema.FromType<T>();
        await WriteHeaderAsync(targetSchema, cancellationToken).ConfigureAwait(false);
        if (generatedWriter == null)
        {
            binder = new HtbRecordBinder<T>(targetSchema);
        }
        recordHeaderWritten = true;
    }

    [RequiresUnreferencedCode("Reflection properties mapping is not Native AOT-safe.")]
    private void WriteSingleRecord(T record)
    {
        IncrementRecordCount();

        // If a generated writer is available, delegate entirely to avoid boxing!
        if (generatedWriter != null)
        {
            generatedWriter.WriteRecord(this, record);
            return;
        }

        int colCount = Schema!.Columns.Count;
        int maskLen = (colCount + 7) / 8;

        byte[]? rented = null;
        Span<byte> mask = maskLen <= 128
            ? stackalloc byte[maskLen]
            : (rented = ArrayPool<byte>.Shared.Rent(maskLen)).AsSpan(0, maskLen);
        try
        {
            mask.Clear();

            // 1. Compute null bitmask
            for (int i = 0; i < colCount; i++)
            {
                object? val = binder!.GetValue(record, i);
                if (val == null)
                {
                    mask[i / 8] |= (byte)(1 << (i % 8));
                }
            }

            // 2. Write null bitmask
            WriteMask(mask);

            // 3. Write non-null column values
            for (int i = 0; i < colCount; i++)
            {
                object? val = binder!.GetValue(record, i);
                if (val != null)
                {
                    WriteColumnValue(Schema.Columns[i].DataType, val);
                }
            }
        }
        finally
        {
            if (rented != null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }

    [RequiresUnreferencedCode("Reflection properties mapping is not Native AOT-safe.")]
    private async Task WriteSingleRecordAsync(T record, CancellationToken cancellationToken)
    {
        IncrementRecordCount();

        if (generatedWriter != null)
        {
            await generatedWriter.WriteRecordAsync(this, record).ConfigureAwait(false);
            return;
        }

        int colCount = Schema!.Columns.Count;
        int maskLen = (colCount + 7) / 8;

        byte[] rented = ArrayPool<byte>.Shared.Rent(maskLen);
        try
        {
            Array.Clear(rented, 0, rented.Length);
            for (int i = 0; i < colCount; i++)
            {
                object? val = binder!.GetValue(record, i);
                if (val == null)
                {
                    rented[i / 8] |= (byte)(1 << (i % 8));
                }
            }

            await WriteMaskAsync(rented.AsMemory(0, maskLen), cancellationToken).ConfigureAwait(false);

            for (int i = 0; i < colCount; i++)
            {
                object? val = binder!.GetValue(record, i);
                if (val != null)
                {
                    await WriteColumnValueAsync(Schema.Columns[i].DataType, val, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private async ValueTask WriteColumnValueAsync(HtbDataType type, object val, CancellationToken cancellationToken)
    {
        switch (type)
        {
            case HtbDataType.String:
                await WriteStringAsync((string)val, cancellationToken).ConfigureAwait(false);
                return;
            case HtbDataType.FloatArray:
                await WriteFloatArrayAsync((float[])val, cancellationToken).ConfigureAwait(false);
                return;
            case HtbDataType.ByteArray:
                await WriteByteArrayAsync((byte[])val, cancellationToken).ConfigureAwait(false);
                return;
            case HtbDataType.BigInteger:
                await WriteBigIntegerAsync((BigInteger)val, cancellationToken).ConfigureAwait(false);
                return;
            case HtbDataType.Uri:
                await WriteUriAsync((Uri)val, cancellationToken).ConfigureAwait(false);
                return;
            default:
                if (!HasBufferSpace(16))
                    await FlushInternalAsync(cancellationToken).ConfigureAwait(false);
                WriteColumnValue(type, val);
                return;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteColumnValue(HtbDataType type, object val)
    {
        switch (type)
        {
            case HtbDataType.Int32: WriteInt32((int)val); break;
            case HtbDataType.Int64: WriteInt64((long)val); break;
            case HtbDataType.Float: WriteFloat((float)val); break;
            case HtbDataType.Double: WriteDouble((double)val); break;
            case HtbDataType.Decimal: WriteDecimal((decimal)val); break;
            case HtbDataType.Boolean: WriteBoolean((bool)val); break;
            case HtbDataType.DateTime: WriteDateTime((DateTime)val); break;
            case HtbDataType.Guid: WriteGuid((Guid)val); break;
            case HtbDataType.String: WriteString((string)val); break;
            case HtbDataType.FloatArray: WriteFloatArray((float[])val); break;
            case HtbDataType.DateTimeOffset: WriteDateTimeOffset((DateTimeOffset)val); break;
            case HtbDataType.DateOnly: WriteDateOnly((DateOnly)val); break;
            case HtbDataType.TimeOnly: WriteTimeOnly((TimeOnly)val); break;
            case HtbDataType.TimeSpan: WriteTimeSpan((TimeSpan)val); break;
            case HtbDataType.Int16: WriteInt16((short)val); break;
            case HtbDataType.UInt16: WriteUInt16((ushort)val); break;
            case HtbDataType.Byte: WriteByte((byte)val); break;
            case HtbDataType.SByte: WriteSByte((sbyte)val); break;
            case HtbDataType.UInt32: WriteUInt32((uint)val); break;
            case HtbDataType.UInt64: WriteUInt64((ulong)val); break;
            case HtbDataType.Char: WriteChar((char)val); break;
            case HtbDataType.Half: WriteHalf((Half)val); break;
            case HtbDataType.Int128: WriteInt128((Int128)val); break;
            case HtbDataType.UInt128: WriteUInt128((UInt128)val); break;
            case HtbDataType.ByteArray: WriteByteArray((byte[])val); break;
            case HtbDataType.BigInteger: WriteBigInteger((BigInteger)val); break;
            case HtbDataType.Uri: WriteUri((Uri)val); break;
            default:
                throw new HtbException(HtbErrorCode.SerializationError, $"Unsupported column type: {type}");
        }
    }
}
