using System.Buffers;
using System.Buffers.Binary;
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
    public async Task WriteHeaderAsync(HtbSchema schema)
    {
        CheckDisposed();
        ArgumentNullException.ThrowIfNull(schema);
        if (headerWritten)
        {
            throw new HtbException(HtbErrorCode.SerializationError, "Header was already written.");
        }

        Schema = schema;
        await EnsureBufferAsync(4 + 4);

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
            await EnsureBufferAsync(1);
            buffer[bufferPos++] = (byte)col.DataType;

            int nameByteCount = Encoding.UTF8.GetByteCount(col.Name);
            await EnsureBufferAsync(4);
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(bufferPos), nameByteCount);
            bufferPos += 4;

            if (nameByteCount <= buffer.Length)
            {
                await EnsureBufferAsync(nameByteCount);
                Encoding.UTF8.GetBytes(col.Name, buffer.AsSpan(bufferPos, nameByteCount));
                bufferPos += nameByteCount;
            }
            else
            {
                await FlushAsync();
                byte[] temp = ArrayPool<byte>.Shared.Rent(nameByteCount);
                try
                {
                    Encoding.UTF8.GetBytes(col.Name, 0, col.Name.Length, temp, 0);
                    await stream.WriteAsync(temp.AsMemory(0, nameByteCount));
                    bytesWrittenTotal += nameByteCount;
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
    public async ValueTask WriteMaskAsync(ReadOnlyMemory<byte> mask)
    {
        await EnsureBufferAsync(mask.Length);
        mask.CopyTo(buffer.AsMemory(bufferPos, mask.Length));
        bufferPos += mask.Length;
    }

    #region Strongly-Typed Non-Boxing Write Methods

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
        }
    }

    #endregion

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

            if (options.MaxOutputSize.HasValue && bytesWrittenTotal > options.MaxOutputSize.Value)
            {
                throw new HtbException(HtbErrorCode.LimitExceeded, "MaxOutputSize limit exceeded.");
            }
        }
    }

    /// <summary>
    /// Flushes all buffered writes out to the underlying stream asynchronously.
    /// </summary>
    public async Task FlushAsync()
    {
        if (bufferPos > 0)
        {
            await stream.WriteAsync(buffer.AsMemory(0, bufferPos));
            bytesWrittenTotal += bufferPos;
            bufferPos = 0;

            if (options.MaxOutputSize.HasValue && bytesWrittenTotal > options.MaxOutputSize.Value)
            {
                throw new HtbException(HtbErrorCode.LimitExceeded, "MaxOutputSize limit exceeded.");
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureBuffer(int count)
    {
        if (buffer.Length - bufferPos >= count) return;
        Flush();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private async ValueTask EnsureBufferAsync(int count)
    {
        if (buffer.Length - bufferPos >= count) return;
        await FlushAsync();
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
            await FlushAsync().ConfigureAwait(false);
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
