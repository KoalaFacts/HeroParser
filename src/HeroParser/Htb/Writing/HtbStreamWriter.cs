using System.Buffers;
using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using HeroParser.Htbs.Records;

namespace HeroParser.Htbs.Writing;

/// <summary>
/// A non-generic writer providing buffered binary serialization of schema headers and raw column values
/// directly into an output stream for the High-Throughput Tabular Binary (HTB) format.
/// </summary>
public class HtbStreamWriter : IDisposable, IAsyncDisposable
{
    private readonly Stream stream;
    private readonly HtbWriteOptions options;
    private readonly bool leaveOpen;
    private readonly byte[] buffer;
    private int bufferPos;
    private long bytesWrittenTotal;

    private bool headerWritten;
    private bool isDisposed;

    /// <summary>
    /// Gets the number of bytes written to the underlying stream so far.
    /// </summary>
    public long BytesWritten => bytesWrittenTotal + bufferPos;

    /// <summary>
    /// Gets the number of records written so far.
    /// </summary>
    public long RecordsWritten { get; private set; }

    /// <summary>
    /// Gets the schema of this writer, if written.
    /// </summary>
    public HtbSchema? Schema { get; private set; }

    /// <summary>
    /// Initializes a new instance of <see cref="HtbStreamWriter"/>.
    /// </summary>
    public HtbStreamWriter(Stream stream, HtbWriteOptions? options = null, bool leaveOpen = false)
    {
        this.stream = stream ?? throw new ArgumentNullException(nameof(stream));
        this.options = options ?? HtbWriteOptions.Default;
        this.leaveOpen = leaveOpen;
        buffer = ArrayPool<byte>.Shared.Rent(16 * 1024); // 16 KB internal write buffer
    }

    /// <summary>
    /// Writes the HTB header block synchronously.
    /// </summary>
    public void WriteHeader(HtbSchema schema)
    {
        CheckDisposed();
        ArgumentNullException.ThrowIfNull(schema);
        if (headerWritten)
        {
            throw new HtbException(HtbErrorCode.SerializationError, "Header was already written.");
        }

        Schema = schema;
        EnsureBuffer(4 + 4); // Magic (4) + ColumnCount (4)

        // 1. Magic: HTB\x01
        buffer[bufferPos] = 0x48;
        buffer[bufferPos + 1] = 0x54;
        buffer[bufferPos + 2] = 0x42;
        buffer[bufferPos + 3] = 0x01;
        bufferPos += 4;

        // 2. Column count
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(bufferPos), schema.Columns.Count);
        bufferPos += 4;

        foreach (var col in schema.Columns)
        {
            EnsureBuffer(1);
            buffer[bufferPos++] = (byte)col.DataType;

            int nameByteCount = Encoding.UTF8.GetByteCount(col.Name);
            EnsureBuffer(4);
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(bufferPos), nameByteCount);
            bufferPos += 4;

            if (nameByteCount <= buffer.Length)
            {
                EnsureBuffer(nameByteCount);
                Encoding.UTF8.GetBytes(col.Name, buffer.AsSpan(bufferPos, nameByteCount));
                bufferPos += nameByteCount;
            }
            else
            {
                Flush();
                byte[] temp = ArrayPool<byte>.Shared.Rent(nameByteCount);
                try
                {
                    Encoding.UTF8.GetBytes(col.Name, 0, col.Name.Length, temp, 0);
                    stream.Write(temp, 0, nameByteCount);
                    bytesWrittenTotal += nameByteCount;
                    CheckMaxOutputSize();
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(temp);
                }
            }
        }

        headerWritten = true;
    }

    /// <summary>
    /// Writes the HTB header block asynchronously.
    /// </summary>
    public Task WriteHeaderAsync(HtbSchema schema)
        => WriteHeaderAsync(schema, CancellationToken.None).AsTask();

    internal async ValueTask WriteHeaderAsync(HtbSchema schema, CancellationToken cancellationToken)
    {
        CheckDisposed();
        ArgumentNullException.ThrowIfNull(schema);
        if (headerWritten)
        {
            throw new HtbException(HtbErrorCode.SerializationError, "Header was already written.");
        }

        Schema = schema;
        await EnsureBufferAsync(4 + 4, cancellationToken).ConfigureAwait(false);

        // 1. Magic: HTB\x01
        buffer[bufferPos] = 0x48;
        buffer[bufferPos + 1] = 0x54;
        buffer[bufferPos + 2] = 0x42;
        buffer[bufferPos + 3] = 0x01;
        bufferPos += 4;

        // 2. Column count
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(bufferPos), schema.Columns.Count);
        bufferPos += 4;

        foreach (var col in schema.Columns)
        {
            await EnsureBufferAsync(1, cancellationToken).ConfigureAwait(false);
            buffer[bufferPos++] = (byte)col.DataType;

            int nameByteCount = Encoding.UTF8.GetByteCount(col.Name);
            await EnsureBufferAsync(4, cancellationToken).ConfigureAwait(false);
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(bufferPos), nameByteCount);
            bufferPos += 4;

            if (nameByteCount <= buffer.Length)
            {
                await EnsureBufferAsync(nameByteCount, cancellationToken).ConfigureAwait(false);
                Encoding.UTF8.GetBytes(col.Name, buffer.AsSpan(bufferPos, nameByteCount));
                bufferPos += nameByteCount;
            }
            else
            {
                await FlushInternalAsync(cancellationToken).ConfigureAwait(false);
                byte[] temp = ArrayPool<byte>.Shared.Rent(nameByteCount);
                try
                {
                    Encoding.UTF8.GetBytes(col.Name, 0, col.Name.Length, temp, 0);
                    await stream.WriteAsync(temp.AsMemory(0, nameByteCount), cancellationToken).ConfigureAwait(false);
                    bytesWrittenTotal += nameByteCount;
                    CheckMaxOutputSize();
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(temp);
                }
            }
        }

        headerWritten = true;
    }

    /// <summary>
    /// Increments the record count and performs limit and progress validations.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void IncrementRecordCount()
    {
        if (options.MaxRowCount.HasValue && RecordsWritten >= options.MaxRowCount.Value)
        {
            throw new HtbException(
                HtbErrorCode.LimitExceeded,
                $"MaxRowCount limit of {options.MaxRowCount.Value} was exceeded.");
        }

        RecordsWritten++;

        // Report progress
        if (options.Progress != null && RecordsWritten % options.ProgressIntervalRows == 0)
        {
            options.Progress.Report(new HtbWriteProgress
            {
                BytesWritten = bytesWrittenTotal + bufferPos,
                RecordsWritten = RecordsWritten
            });
        }
    }

    /// <summary>
    /// Writes the null bitmask directly.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteMask(ReadOnlySpan<byte> mask)
    {
        EnsureBuffer(mask.Length);
        mask.CopyTo(buffer.AsSpan(bufferPos));
        bufferPos += mask.Length;
    }

    /// <summary>
    /// Writes the null bitmask asynchronously.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask WriteMaskAsync(ReadOnlyMemory<byte> mask)
        => WriteMaskAsync(mask, CancellationToken.None);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal async ValueTask WriteMaskAsync(ReadOnlyMemory<byte> mask, CancellationToken cancellationToken)
    {
        await EnsureBufferAsync(mask.Length, cancellationToken).ConfigureAwait(false);
        mask.CopyTo(buffer.AsMemory(bufferPos, mask.Length));
        bufferPos += mask.Length;
    }

    /// <summary>Writes an Int32 directly into the stream buffer.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteInt32(int val)
    {
        EnsureBuffer(4);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(bufferPos), val);
        bufferPos += 4;
    }

    /// <summary>Writes an Int64 directly into the stream buffer.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteInt64(long val)
    {
        EnsureBuffer(8);
        BinaryPrimitives.WriteInt64LittleEndian(buffer.AsSpan(bufferPos), val);
        bufferPos += 8;
    }

    /// <summary>Writes a Single directly into the stream buffer.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteFloat(float val)
    {
        EnsureBuffer(4);
        BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(bufferPos), val);
        bufferPos += 4;
    }

    /// <summary>Writes a Double directly into the stream buffer.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteDouble(double val)
    {
        EnsureBuffer(8);
        BinaryPrimitives.WriteDoubleLittleEndian(buffer.AsSpan(bufferPos), val);
        bufferPos += 8;
    }

    /// <summary>Writes a Decimal directly into the stream buffer without heap allocations.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteDecimal(decimal val)
    {
        EnsureBuffer(16);
        Span<int> decimalBits = stackalloc int[4];
        decimal.GetBits(val, decimalBits);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(bufferPos), decimalBits[0]);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(bufferPos + 4), decimalBits[1]);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(bufferPos + 8), decimalBits[2]);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(bufferPos + 12), decimalBits[3]);
        bufferPos += 16;
    }

    /// <summary>Writes a Boolean directly into the stream buffer.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteBoolean(bool val)
    {
        EnsureBuffer(1);
        buffer[bufferPos++] = (byte)(val ? 1 : 0);
    }

    /// <summary>Writes a DateTime directly into the stream buffer.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteDateTime(DateTime val)
    {
        EnsureBuffer(8);
        BinaryPrimitives.WriteInt64LittleEndian(buffer.AsSpan(bufferPos), val.Ticks);
        bufferPos += 8;
    }

    /// <summary>Writes a Guid directly into the stream buffer.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteGuid(Guid val)
    {
        EnsureBuffer(16);
        bool written = val.TryWriteBytes(buffer.AsSpan(bufferPos, 16));
        if (!written)
        {
            throw new HtbException(HtbErrorCode.SerializationError, "Failed to write Guid bytes.");
        }
        bufferPos += 16;
    }

    /// <summary>Writes a String directly into the stream buffer.</summary>
    public void WriteString(string val)
    {
        ArgumentNullException.ThrowIfNull(val);
        int byteCount = Encoding.UTF8.GetByteCount(val);
        EnsureBuffer(4);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(bufferPos), byteCount);
        bufferPos += 4;

        if (byteCount <= buffer.Length)
        {
            EnsureBuffer(byteCount);
            Encoding.UTF8.GetBytes(val, buffer.AsSpan(bufferPos, byteCount));
            bufferPos += byteCount;
        }
        else
        {
            Flush();
            byte[] temp = ArrayPool<byte>.Shared.Rent(byteCount);
            try
            {
                Encoding.UTF8.GetBytes(val, 0, val.Length, temp, 0);
                stream.Write(temp, 0, byteCount);
                bytesWrittenTotal += byteCount;
                CheckMaxOutputSize();
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(temp);
            }
        }
    }

    internal async ValueTask WriteStringAsync(string val, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(val);
        int byteCount = Encoding.UTF8.GetByteCount(val);
        await EnsureBufferAsync(4, cancellationToken).ConfigureAwait(false);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(bufferPos), byteCount);
        bufferPos += 4;

        if (byteCount <= buffer.Length)
        {
            await EnsureBufferAsync(byteCount, cancellationToken).ConfigureAwait(false);
            Encoding.UTF8.GetBytes(val, buffer.AsSpan(bufferPos, byteCount));
            bufferPos += byteCount;
        }
        else
        {
            await FlushInternalAsync(cancellationToken).ConfigureAwait(false);
            byte[] temp = ArrayPool<byte>.Shared.Rent(byteCount);
            try
            {
                Encoding.UTF8.GetBytes(val, 0, val.Length, temp, 0);
                await stream.WriteAsync(temp.AsMemory(0, byteCount), cancellationToken).ConfigureAwait(false);
                bytesWrittenTotal += byteCount;
                CheckMaxOutputSize();
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(temp);
            }
        }
    }

    /// <summary>Writes a FloatArray directly into the stream buffer.</summary>
    public void WriteFloatArray(float[] val)
    {
        ArgumentNullException.ThrowIfNull(val);
        EnsureBuffer(4);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(bufferPos), val.Length);
        bufferPos += 4;

        int byteLen = val.Length * 4;
        if (!BitConverter.IsLittleEndian)
        {
            if (val.Length > 0)
            {
                float[] temp = ArrayPool<float>.Shared.Rent(val.Length);
                try
                {
                    val.AsSpan().CopyTo(temp);
                    var intSpan = System.Runtime.InteropServices.MemoryMarshal.Cast<float, int>(temp.AsSpan(0, val.Length));
                    for (int i = 0; i < intSpan.Length; i++)
                    {
                        intSpan[i] = BinaryPrimitives.ReverseEndianness(intSpan[i]);
                    }

                    var byteSpan = System.Runtime.InteropServices.MemoryMarshal.AsBytes(temp.AsSpan(0, val.Length));
                    if (byteLen <= buffer.Length)
                    {
                        EnsureBuffer(byteLen);
                        byteSpan.CopyTo(buffer.AsSpan(bufferPos, byteLen));
                        bufferPos += byteLen;
                    }
                    else
                    {
                        Flush();
                        stream.Write(byteSpan);
                        bytesWrittenTotal += byteLen;
                    }
                }
                finally
                {
                    ArrayPool<float>.Shared.Return(temp);
                }
            }
            return;
        }

        if (byteLen <= buffer.Length)
        {
            EnsureBuffer(byteLen);
            System.Runtime.InteropServices.MemoryMarshal.AsBytes(val.AsSpan()).CopyTo(buffer.AsSpan(bufferPos, byteLen));
            bufferPos += byteLen;
        }
        else
        {
            Flush();
            var floatBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(val.AsSpan());
            stream.Write(floatBytes);
            bytesWrittenTotal += byteLen;
            CheckMaxOutputSize();
        }
    }

    internal async ValueTask WriteFloatArrayAsync(float[] val, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(val);
        await EnsureBufferAsync(4, cancellationToken).ConfigureAwait(false);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(bufferPos), val.Length);
        bufferPos += 4;

        if (val.Length == 0)
            return;

        int byteLen = val.Length * 4;
        if (byteLen <= buffer.Length)
        {
            await EnsureBufferAsync(byteLen, cancellationToken).ConfigureAwait(false);
            CopyFloatBytesLittleEndian(val, buffer.AsSpan(bufferPos, byteLen));
            bufferPos += byteLen;
        }
        else
        {
            await FlushInternalAsync(cancellationToken).ConfigureAwait(false);
            byte[] temp = ArrayPool<byte>.Shared.Rent(byteLen);
            try
            {
                CopyFloatBytesLittleEndian(val, temp.AsSpan(0, byteLen));
                await stream.WriteAsync(temp.AsMemory(0, byteLen), cancellationToken).ConfigureAwait(false);
                bytesWrittenTotal += byteLen;
                CheckMaxOutputSize();
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(temp);
            }
        }
    }

    private static void CopyFloatBytesLittleEndian(float[] val, Span<byte> destination)
    {
        if (BitConverter.IsLittleEndian)
        {
            System.Runtime.InteropServices.MemoryMarshal.AsBytes(val.AsSpan()).CopyTo(destination);
            return;
        }

        var intDest = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, int>(destination);
        var intSrc = System.Runtime.InteropServices.MemoryMarshal.Cast<float, int>(val.AsSpan());
        for (int i = 0; i < intSrc.Length; i++)
        {
            intDest[i] = BinaryPrimitives.ReverseEndianness(intSrc[i]);
        }
    }

    /// <summary>Writes a DateTimeOffset directly into the stream buffer.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteDateTimeOffset(DateTimeOffset val)
    {
        EnsureBuffer(10);
        BinaryPrimitives.WriteInt64LittleEndian(buffer.AsSpan(bufferPos), val.Ticks);
        BinaryPrimitives.WriteInt16LittleEndian(buffer.AsSpan(bufferPos + 8), (short)val.Offset.TotalMinutes);
        bufferPos += 10;
    }

    /// <summary>Writes a DateOnly directly into the stream buffer.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteDateOnly(DateOnly val)
    {
        EnsureBuffer(4);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(bufferPos), val.DayNumber);
        bufferPos += 4;
    }

    /// <summary>Writes a TimeOnly directly into the stream buffer.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteTimeOnly(TimeOnly val)
    {
        EnsureBuffer(8);
        BinaryPrimitives.WriteInt64LittleEndian(buffer.AsSpan(bufferPos), val.Ticks);
        bufferPos += 8;
    }

    /// <summary>Writes a TimeSpan directly into the stream buffer.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteTimeSpan(TimeSpan val)
    {
        EnsureBuffer(8);
        BinaryPrimitives.WriteInt64LittleEndian(buffer.AsSpan(bufferPos), val.Ticks);
        bufferPos += 8;
    }

    /// <summary>Writes an Int16 directly into the stream buffer.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteInt16(short val)
    {
        EnsureBuffer(2);
        BinaryPrimitives.WriteInt16LittleEndian(buffer.AsSpan(bufferPos), val);
        bufferPos += 2;
    }

    /// <summary>Writes a UInt16 directly into the stream buffer.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteUInt16(ushort val)
    {
        EnsureBuffer(2);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(bufferPos), val);
        bufferPos += 2;
    }

    /// <summary>Writes a Byte directly into the stream buffer.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteByte(byte val)
    {
        EnsureBuffer(1);
        buffer[bufferPos++] = val;
    }

    /// <summary>Writes an SByte directly into the stream buffer.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteSByte(sbyte val)
    {
        EnsureBuffer(1);
        buffer[bufferPos++] = unchecked((byte)val);
    }

    /// <summary>Writes a UInt32 directly into the stream buffer.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteUInt32(uint val)
    {
        EnsureBuffer(4);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(bufferPos), val);
        bufferPos += 4;
    }

    /// <summary>Writes a UInt64 directly into the stream buffer.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteUInt64(ulong val)
    {
        EnsureBuffer(8);
        BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(bufferPos), val);
        bufferPos += 8;
    }

    /// <summary>Writes a Char directly into the stream buffer.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteChar(char val)
    {
        EnsureBuffer(2);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(bufferPos), val);
        bufferPos += 2;
    }

    /// <summary>Writes a Half directly into the stream buffer.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteHalf(Half val)
    {
        EnsureBuffer(2);
        BinaryPrimitives.WriteHalfLittleEndian(buffer.AsSpan(bufferPos), val);
        bufferPos += 2;
    }

    /// <summary>Writes an Int128 directly into the stream buffer.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteInt128(Int128 val)
    {
        EnsureBuffer(16);
        BinaryPrimitives.WriteInt128LittleEndian(buffer.AsSpan(bufferPos), val);
        bufferPos += 16;
    }

    /// <summary>Writes a UInt128 directly into the stream buffer.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteUInt128(UInt128 val)
    {
        EnsureBuffer(16);
        BinaryPrimitives.WriteUInt128LittleEndian(buffer.AsSpan(bufferPos), val);
        bufferPos += 16;
    }

    /// <summary>Writes a byte array directly into the stream buffer.</summary>
    public void WriteByteArray(byte[] val)
    {
        ArgumentNullException.ThrowIfNull(val);
        WriteLengthPrefixedBytes(val.AsSpan());
    }

    /// <summary>Writes a BigInteger directly into the stream buffer.</summary>
    public void WriteBigInteger(BigInteger val)
    {
        int byteCount = val.GetByteCount(isUnsigned: false);
        byte[]? rented = byteCount > 256 ? ArrayPool<byte>.Shared.Rent(byteCount) : null;
        Span<byte> scratch = rented is null ? stackalloc byte[256] : (Span<byte>)rented;
        try
        {
            if (!val.TryWriteBytes(scratch[..byteCount], out int bytesWritten, isUnsigned: false, isBigEndian: false))
            {
                throw new HtbException(HtbErrorCode.SerializationError, "Failed to write BigInteger bytes.");
            }
            WriteLengthPrefixedBytes(scratch[..bytesWritten]);
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }

    /// <summary>Writes a Uri directly into the stream buffer.</summary>
    public void WriteUri(Uri val)
    {
        ArgumentNullException.ThrowIfNull(val);
        WriteString(val.OriginalString);
    }

    internal ValueTask WriteByteArrayAsync(byte[] val, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(val);
        return WriteLengthPrefixedBytesAsync(val.AsMemory(), cancellationToken);
    }

    internal async ValueTask WriteBigIntegerAsync(BigInteger val, CancellationToken cancellationToken)
    {
        int byteCount = val.GetByteCount(isUnsigned: false);
        byte[] rented = ArrayPool<byte>.Shared.Rent(Math.Max(1, byteCount));
        try
        {
            if (!val.TryWriteBytes(rented.AsSpan(0, byteCount), out int bytesWritten, isUnsigned: false, isBigEndian: false))
            {
                throw new HtbException(HtbErrorCode.SerializationError, "Failed to write BigInteger bytes.");
            }
            await WriteLengthPrefixedBytesAsync(rented.AsMemory(0, bytesWritten), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    internal ValueTask WriteUriAsync(Uri val, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(val);
        return WriteStringAsync(val.OriginalString, cancellationToken);
    }

    private async ValueTask WriteLengthPrefixedBytesAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
    {
        await EnsureBufferAsync(4, cancellationToken).ConfigureAwait(false);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(bufferPos), bytes.Length);
        bufferPos += 4;

        if (bytes.Length == 0)
            return;

        if (bytes.Length <= buffer.Length)
        {
            await EnsureBufferAsync(bytes.Length, cancellationToken).ConfigureAwait(false);
            bytes.Span.CopyTo(buffer.AsSpan(bufferPos));
            bufferPos += bytes.Length;
        }
        else
        {
            await FlushInternalAsync(cancellationToken).ConfigureAwait(false);
            await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            bytesWrittenTotal += bytes.Length;
            CheckMaxOutputSize();
        }
    }

    private void WriteLengthPrefixedBytes(ReadOnlySpan<byte> bytes)
    {
        EnsureBuffer(4);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(bufferPos), bytes.Length);
        bufferPos += 4;

        if (bytes.Length == 0)
            return;

        if (bytes.Length <= buffer.Length)
        {
            EnsureBuffer(bytes.Length);
            bytes.CopyTo(buffer.AsSpan(bufferPos));
            bufferPos += bytes.Length;
        }
        else
        {
            Flush();
            stream.Write(bytes);
            bytesWrittenTotal += bytes.Length;
            CheckMaxOutputSize();
        }
    }

    /// <summary>
    /// Flushes all buffered writes out to the underlying stream.
    /// </summary>
    public void Flush()
    {
        if (bufferPos > 0)
        {
            stream.Write(buffer, 0, bufferPos);
            bytesWrittenTotal += bufferPos;
            bufferPos = 0;
            CheckMaxOutputSize();
        }
    }

    /// <summary>
    /// Flushes all buffered writes out to the underlying stream asynchronously.
    /// </summary>
    public Task FlushAsync()
        => FlushInternalAsync(CancellationToken.None).AsTask();

    internal async ValueTask FlushInternalAsync(CancellationToken cancellationToken)
    {
        if (bufferPos > 0)
        {
            await stream.WriteAsync(buffer.AsMemory(0, bufferPos), cancellationToken).ConfigureAwait(false);
            bytesWrittenTotal += bufferPos;
            bufferPos = 0;
            CheckMaxOutputSize();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool HasBufferSpace(int count)
        => buffer.Length - bufferPos >= count;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureBuffer(int count)
    {
        if (buffer.Length - bufferPos >= count) return;
        Flush();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ValueTask EnsureBufferAsync(int count, CancellationToken cancellationToken)
    {
        if (buffer.Length - bufferPos >= count) return default;
        return FlushInternalAsync(cancellationToken);
    }

    private void CheckMaxOutputSize()
    {
        if (options.MaxOutputSize.HasValue && bytesWrittenTotal > options.MaxOutputSize.Value)
        {
            throw new HtbException(HtbErrorCode.LimitExceeded, "MaxOutputSize limit exceeded.");
        }
    }

    private void CheckDisposed()
    {
        if (isDisposed)
            throw new ObjectDisposedException("HtbStreamWriter");
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (isDisposed) return;
        isDisposed = true;

        try
        {
            Flush();
        }
        catch
        {
            // Suppress exceptions in finalizers/disposes
        }

        ArrayPool<byte>.Shared.Return(buffer);
        if (!leaveOpen)
        {
            stream.Dispose();
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (isDisposed) return;
        isDisposed = true;

        try
        {
            await FlushInternalAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // Suppress exceptions in finalizers/disposes
        }

        ArrayPool<byte>.Shared.Return(buffer);
        if (!leaveOpen)
        {
            await stream.DisposeAsync().ConfigureAwait(false);
        }
    }
}
