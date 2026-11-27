using System.Buffers;
using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using HeroParser.Htbs.Records;

namespace HeroParser.Htbs.Reading;

/// <summary>
/// A non-generic reader providing high-performance, buffered parsing of schema headers and raw column values
/// directly from an input stream in the High-Throughput Tabular Binary (HTB) format.
/// </summary>
public class HtbStreamReader : IDisposable, IAsyncDisposable
{
    private readonly Stream stream;
    private readonly HtbReadOptions options;
    private readonly bool leaveOpen;
    private readonly byte[] buffer;
    private int bufferPos;
    private int bufferLen;
    private long bytesReadTotal;
    private long recordsProcessed;

    /// <summary>
    /// The parsed schema of the HTB stream.
    /// </summary>
    protected HtbSchema? schema;
    private protected CancellationToken activeCancellationToken;
    private bool headerParsed;
    private bool isDisposed;

    /// <summary>
    /// Gets the number of bytes read from the underlying stream so far.
    /// </summary>
    public long BytesRead => bytesReadTotal - (bufferLen - bufferPos);

    /// <summary>
    /// Gets the number of records read so far.
    /// </summary>
    public long RecordsRead { get; private set; }

    /// <summary>
    /// Gets the schema of this stream, if parsed.
    /// </summary>
    public HtbSchema? Schema => schema;

    /// <summary>
    /// Initializes a new instance of <see cref="HtbStreamReader"/>.
    /// </summary>
    public HtbStreamReader(Stream stream, HtbReadOptions? options = null, bool leaveOpen = false)
    {
        this.stream = stream ?? throw new ArgumentNullException(nameof(stream));
        this.options = options ?? HtbReadOptions.Default;
        this.leaveOpen = leaveOpen;
        buffer = ArrayPool<byte>.Shared.Rent(16 * 1024); // 16 KB internal read buffer
    }

    /// <summary>
    /// Reads and parses the HTB header block synchronously.
    /// </summary>
    public HtbSchema ParseHeader()
    {
        CheckDisposed();
        if (headerParsed)
        {
            return schema!;
        }

        EnsureBuffer(4 + 4); // Magic (4) + ColumnCount (4)
        if (bufferLen - bufferPos < 8)
        {
            throw new HtbException(HtbErrorCode.InvalidHeader, "Truncated stream while reading magic header.");
        }

        // 1. Magic check: HTB\x01
        if (buffer[bufferPos] != 0x48 || buffer[bufferPos + 1] != 0x54 || buffer[bufferPos + 2] != 0x42 || buffer[bufferPos + 3] != 0x01)
        {
            throw new HtbException(HtbErrorCode.InvalidHeader, "Corrupt or invalid HTB magic bytes.");
        }
        bufferPos += 4;

        // 2. Read Column count
        int colCount = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(bufferPos));
        bufferPos += 4;

        if (colCount <= 0 || colCount > 2048)
        {
            throw new HtbException(HtbErrorCode.InvalidHeader, $"Invalid schema column count: {colCount}");
        }

        var columns = new List<HtbColumn>();
        for (int i = 0; i < colCount; i++)
        {
            EnsureBuffer(1 + 4); // DataType (1) + NameLength (4)
            if (bufferLen - bufferPos < 5)
            {
                throw new HtbException(HtbErrorCode.InvalidHeader, "Truncated stream while reading column schema details.");
            }
            HtbDataType dataType = (HtbDataType)buffer[bufferPos++];
            if (dataType < HtbDataType.Int32 || dataType > HtbDataType.Uri)
            {
                throw new HtbException(HtbErrorCode.InvalidHeader, $"Invalid or unsupported column data type byte value: {(byte)dataType}");
            }
            int nameLen = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(bufferPos));
            bufferPos += 4;

            if (nameLen <= 0 || nameLen > 2048)
            {
                throw new HtbException(HtbErrorCode.InvalidHeader, $"Invalid column name length: {nameLen}");
            }

            EnsureBuffer(nameLen);
            if (bufferLen - bufferPos < nameLen)
            {
                throw new HtbException(HtbErrorCode.InvalidHeader, "Truncated stream while reading column name.");
            }
            string name = Encoding.UTF8.GetString(buffer, bufferPos, nameLen);
            bufferPos += nameLen;

            columns.Add(new HtbColumn(name, dataType, isNullable: true));
        }

        schema = new HtbSchema(columns);
        headerParsed = true;
        return schema;
    }

    /// <summary>
    /// Reads and parses the HTB header block asynchronously.
    /// </summary>
    public ValueTask<HtbSchema> ParseHeaderAsync()
        => ParseHeaderAsync(activeCancellationToken);

    internal async ValueTask<HtbSchema> ParseHeaderAsync(CancellationToken cancellationToken)
    {
        CheckDisposed();
        if (headerParsed)
        {
            return schema!;
        }

        await EnsureBufferAsync(4 + 4, cancellationToken).ConfigureAwait(false);
        if (bufferLen - bufferPos < 8)
        {
            throw new HtbException(HtbErrorCode.InvalidHeader, "Truncated stream while reading magic header.");
        }

        if (buffer[bufferPos] != 0x48 || buffer[bufferPos + 1] != 0x54 || buffer[bufferPos + 2] != 0x42 || buffer[bufferPos + 3] != 0x01)
        {
            throw new HtbException(HtbErrorCode.InvalidHeader, "Corrupt or invalid HTB magic bytes.");
        }
        bufferPos += 4;

        int colCount = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(bufferPos));
        bufferPos += 4;

        if (colCount <= 0 || colCount > 2048)
        {
            throw new HtbException(HtbErrorCode.InvalidHeader, $"Invalid schema column count: {colCount}");
        }

        var columns = new List<HtbColumn>();
        for (int i = 0; i < colCount; i++)
        {
            await EnsureBufferAsync(1 + 4, cancellationToken).ConfigureAwait(false);
            if (bufferLen - bufferPos < 5)
            {
                throw new HtbException(HtbErrorCode.InvalidHeader, "Truncated stream while reading column schema details.");
            }
            HtbDataType dataType = (HtbDataType)buffer[bufferPos++];
            if (dataType < HtbDataType.Int32 || dataType > HtbDataType.Uri)
            {
                throw new HtbException(HtbErrorCode.InvalidHeader, $"Invalid or unsupported column data type byte value: {(byte)dataType}");
            }
            int nameLen = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(bufferPos));
            bufferPos += 4;

            if (nameLen <= 0 || nameLen > 2048)
            {
                throw new HtbException(HtbErrorCode.InvalidHeader, $"Invalid column name length: {nameLen}");
            }

            await EnsureBufferAsync(nameLen, cancellationToken).ConfigureAwait(false);
            if (bufferLen - bufferPos < nameLen)
            {
                throw new HtbException(HtbErrorCode.InvalidHeader, "Truncated stream while reading column name.");
            }
            string name = Encoding.UTF8.GetString(buffer, bufferPos, nameLen);
            bufferPos += nameLen;

            columns.Add(new HtbColumn(name, dataType, isNullable: true));
        }

        schema = new HtbSchema(columns);
        headerParsed = true;
        return schema;
    }

    /// <summary>
    /// Checks if the end of the input stream has been reached.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsEndOfStream()
    {
        if (bufferPos < bufferLen) return false;
        bufferPos = 0;
        bufferLen = stream.Read(buffer, 0, buffer.Length);
        if (bufferLen <= 0) return true;
        bytesReadTotal += bufferLen;
        return false;
    }

    /// <summary>
    /// Checks if the end of the input stream has been reached asynchronously.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<bool> IsEndOfStreamAsync()
        => IsEndOfStreamAsync(activeCancellationToken);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal async ValueTask<bool> IsEndOfStreamAsync(CancellationToken cancellationToken)
    {
        if (bufferPos < bufferLen) return false;
        cancellationToken.ThrowIfCancellationRequested();
        bufferPos = 0;
        bufferLen = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
        if (bufferLen <= 0) return true;
        bytesReadTotal += bufferLen;
        return false;
    }

    /// <summary>
    /// Increments the record count and performs limit and progress validations.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void IncrementRecordCount()
    {
        recordsProcessed++;
        if (recordsProcessed > options.MaxRowCount)
        {
            throw new HtbException(
                HtbErrorCode.LimitExceeded,
                $"MaxRowCount limit of {options.MaxRowCount} was exceeded.");
        }

        RecordsRead++;

        // Report progress
        if (options.Progress != null && RecordsRead % options.ProgressIntervalRows == 0)
        {
            options.Progress.Report(new HtbProgress
            {
                BytesRead = BytesRead,
                RecordsRead = RecordsRead
            });
        }
    }

    /// <summary>
    /// Reads and copies the null bitmask.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ReadMask(Span<byte> mask)
    {
        EnsureBuffer(mask.Length);
        if (bufferLen - bufferPos < mask.Length)
        {
            throw new HtbException(HtbErrorCode.CorruptData, "Unexpected end of stream while reading null mask.");
        }
        buffer.AsSpan(bufferPos, mask.Length).CopyTo(mask);
        bufferPos += mask.Length;
    }

    /// <summary>
    /// Reads and copies the null bitmask asynchronously.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask ReadMaskAsync(Memory<byte> mask)
        => ReadMaskAsync(mask, activeCancellationToken);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal async ValueTask ReadMaskAsync(Memory<byte> mask, CancellationToken cancellationToken)
    {
        await EnsureBufferAsync(mask.Length, cancellationToken).ConfigureAwait(false);
        if (bufferLen - bufferPos < mask.Length)
        {
            throw new HtbException(HtbErrorCode.CorruptData, "Unexpected end of stream while reading null mask.");
        }
        buffer.AsMemory(bufferPos, mask.Length).CopyTo(mask);
        bufferPos += mask.Length;
    }

    /// <summary>Reads an Int32 directly from the stream buffer.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int ReadInt32()
    {
        EnsureBuffer(4);
        if (bufferLen - bufferPos < 4)
        {
            throw new HtbException(HtbErrorCode.CorruptData, "Unexpected end of stream while reading Int32.");
        }
        int val = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(bufferPos));
        bufferPos += 4;
        return val;
    }

    /// <summary>Reads an Int32 asynchronously from the stream buffer.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<int> ReadInt32Async()
        => ReadInt32Async(activeCancellationToken);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal async ValueTask<int> ReadInt32Async(CancellationToken cancellationToken)
    {
        await EnsureBufferAsync(4, cancellationToken).ConfigureAwait(false);
        if (bufferLen - bufferPos < 4)
        {
            throw new HtbException(HtbErrorCode.CorruptData, "Unexpected end of stream while reading Int32.");
        }
        int val = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(bufferPos));
        bufferPos += 4;
        return val;
    }

    /// <summary>Reads an Int64 directly from the stream buffer.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public long ReadInt64()
    {
        EnsureBuffer(8);
        if (bufferLen - bufferPos < 8)
        {
            throw new HtbException(HtbErrorCode.CorruptData, "Unexpected end of stream while reading Int64.");
        }
        long val = BinaryPrimitives.ReadInt64LittleEndian(buffer.AsSpan(bufferPos));
        bufferPos += 8;
        return val;
    }

    /// <summary>Reads an Int64 asynchronously from the stream buffer.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<long> ReadInt64Async()
        => ReadInt64Async(activeCancellationToken);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal async ValueTask<long> ReadInt64Async(CancellationToken cancellationToken)
    {
        await EnsureBufferAsync(8, cancellationToken).ConfigureAwait(false);
        if (bufferLen - bufferPos < 8)
        {
            throw new HtbException(HtbErrorCode.CorruptData, "Unexpected end of stream while reading Int64.");
        }
        long val = BinaryPrimitives.ReadInt64LittleEndian(buffer.AsSpan(bufferPos));
        bufferPos += 8;
        return val;
    }

    /// <summary>Reads a Single directly from the stream buffer.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float ReadFloat()
    {
        EnsureBuffer(4);
        if (bufferLen - bufferPos < 4)
        {
            throw new HtbException(HtbErrorCode.CorruptData, "Unexpected end of stream while reading Float.");
        }
        float val = BinaryPrimitives.ReadSingleLittleEndian(buffer.AsSpan(bufferPos));
        bufferPos += 4;
        return val;
    }

    /// <summary>Reads a Single asynchronously from the stream buffer.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<float> ReadFloatAsync()
        => ReadFloatAsync(activeCancellationToken);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal async ValueTask<float> ReadFloatAsync(CancellationToken cancellationToken)
    {
        await EnsureBufferAsync(4, cancellationToken).ConfigureAwait(false);
        if (bufferLen - bufferPos < 4)
        {
            throw new HtbException(HtbErrorCode.CorruptData, "Unexpected end of stream while reading Float.");
        }
        float val = BinaryPrimitives.ReadSingleLittleEndian(buffer.AsSpan(bufferPos));
        bufferPos += 4;
        return val;
    }

    /// <summary>Reads a Double directly from the stream buffer.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double ReadDouble()
    {
        EnsureBuffer(8);
        if (bufferLen - bufferPos < 8)
        {
            throw new HtbException(HtbErrorCode.CorruptData, "Unexpected end of stream while reading Double.");
        }
        double val = BinaryPrimitives.ReadDoubleLittleEndian(buffer.AsSpan(bufferPos));
        bufferPos += 8;
        return val;
    }

    /// <summary>Reads a Double asynchronously from the stream buffer.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<double> ReadDoubleAsync()
        => ReadDoubleAsync(activeCancellationToken);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal async ValueTask<double> ReadDoubleAsync(CancellationToken cancellationToken)
    {
        await EnsureBufferAsync(8, cancellationToken).ConfigureAwait(false);
        if (bufferLen - bufferPos < 8)
        {
            throw new HtbException(HtbErrorCode.CorruptData, "Unexpected end of stream while reading Double.");
        }
        double val = BinaryPrimitives.ReadDoubleLittleEndian(buffer.AsSpan(bufferPos));
        bufferPos += 8;
        return val;
    }

    /// <summary>Reads a Decimal directly from the stream buffer without heap allocations.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public decimal ReadDecimal()
    {
        EnsureBuffer(16);
        if (bufferLen - bufferPos < 16)
        {
            throw new HtbException(HtbErrorCode.CorruptData, "Unexpected end of stream while reading Decimal.");
        }
        Span<int> decimalBits = [
            BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(bufferPos)),
            BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(bufferPos + 4)),
            BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(bufferPos + 8)),
            BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(bufferPos + 12))
        ];
        bufferPos += 16;
        return new decimal(decimalBits);
    }

    /// <summary>Reads a Decimal asynchronously from the stream buffer without heap allocations.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<decimal> ReadDecimalAsync()
        => ReadDecimalAsync(activeCancellationToken);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal async ValueTask<decimal> ReadDecimalAsync(CancellationToken cancellationToken)
    {
        await EnsureBufferAsync(16, cancellationToken).ConfigureAwait(false);
        if (bufferLen - bufferPos < 16)
        {
            throw new HtbException(HtbErrorCode.CorruptData, "Unexpected end of stream while reading Decimal.");
        }
        Span<int> decimalBits = [
            BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(bufferPos)),
            BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(bufferPos + 4)),
            BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(bufferPos + 8)),
            BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(bufferPos + 12))
        ];
        bufferPos += 16;
        return new decimal(decimalBits);
    }

    /// <summary>Reads a Boolean directly from the stream buffer.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ReadBoolean()
    {
        EnsureBuffer(1);
        if (bufferLen - bufferPos < 1)
        {
            throw new HtbException(HtbErrorCode.CorruptData, "Unexpected end of stream while reading Boolean.");
        }
        return buffer[bufferPos++] != 0;
    }

    /// <summary>Reads a Boolean asynchronously from the stream buffer.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<bool> ReadBooleanAsync()
        => ReadBooleanAsync(activeCancellationToken);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal async ValueTask<bool> ReadBooleanAsync(CancellationToken cancellationToken)
    {
        await EnsureBufferAsync(1, cancellationToken).ConfigureAwait(false);
        if (bufferLen - bufferPos < 1)
        {
            throw new HtbException(HtbErrorCode.CorruptData, "Unexpected end of stream while reading Boolean.");
        }
        return buffer[bufferPos++] != 0;
    }

    /// <summary>Reads a DateTime directly from the stream buffer.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public DateTime ReadDateTime()
    {
        EnsureBuffer(8);
        if (bufferLen - bufferPos < 8)
        {
            throw new HtbException(HtbErrorCode.CorruptData, "Unexpected end of stream while reading DateTime.");
        }
        long ticks = BinaryPrimitives.ReadInt64LittleEndian(buffer.AsSpan(bufferPos));
        bufferPos += 8;
        return new DateTime(ticks);
    }

    /// <summary>Reads a DateTime asynchronously from the stream buffer.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<DateTime> ReadDateTimeAsync()
        => ReadDateTimeAsync(activeCancellationToken);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal async ValueTask<DateTime> ReadDateTimeAsync(CancellationToken cancellationToken)
    {
        await EnsureBufferAsync(8, cancellationToken).ConfigureAwait(false);
        if (bufferLen - bufferPos < 8)
        {
            throw new HtbException(HtbErrorCode.CorruptData, "Unexpected end of stream while reading DateTime.");
        }
        long ticks = BinaryPrimitives.ReadInt64LittleEndian(buffer.AsSpan(bufferPos));
        bufferPos += 8;
        return new DateTime(ticks);
    }

    /// <summary>Reads a Guid directly from the stream buffer.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Guid ReadGuid()
    {
        EnsureBuffer(16);
        if (bufferLen - bufferPos < 16)
        {
            throw new HtbException(HtbErrorCode.CorruptData, "Unexpected end of stream while reading Guid.");
        }
        var val = new Guid(buffer.AsSpan(bufferPos, 16));
        bufferPos += 16;
        return val;
    }

    /// <summary>Reads a Guid asynchronously from the stream buffer.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<Guid> ReadGuidAsync()
        => ReadGuidAsync(activeCancellationToken);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal async ValueTask<Guid> ReadGuidAsync(CancellationToken cancellationToken)
    {
        await EnsureBufferAsync(16, cancellationToken).ConfigureAwait(false);
        if (bufferLen - bufferPos < 16)
        {
            throw new HtbException(HtbErrorCode.CorruptData, "Unexpected end of stream while reading Guid.");
        }
        var val = new Guid(buffer.AsSpan(bufferPos, 16));
        bufferPos += 16;
        return val;
    }

    /// <summary>Reads a String directly from the stream buffer.</summary>
    public string ReadString()
    {
        EnsureBuffer(4);
        if (bufferLen - bufferPos < 4)
        {
            throw new HtbException(HtbErrorCode.CorruptData, "Unexpected end of stream while reading string length.");
        }
        int strLen = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(bufferPos));
        bufferPos += 4;
        if (strLen < 0 || strLen > HtbSchema.MAX_PAYLOAD_BYTES)
        {
            throw new HtbException(HtbErrorCode.CorruptData, $"Invalid string length: {strLen}");
        }
        if (strLen == 0) return string.Empty;

        if (strLen <= buffer.Length)
        {
            EnsureBuffer(strLen);
            if (bufferLen - bufferPos < strLen)
            {
                throw new HtbException(HtbErrorCode.CorruptData, "Unexpected end of stream while reading string value.");
            }
            string str = Encoding.UTF8.GetString(buffer, bufferPos, strLen);
            bufferPos += strLen;
            return str;
        }
        else
        {
            int available = bufferLen - bufferPos;
            byte[] temp = ArrayPool<byte>.Shared.Rent(strLen);
            try
            {
                if (available > 0)
                {
                    Array.Copy(buffer, bufferPos, temp, 0, available);
                }
                bufferPos = 0;
                bufferLen = 0;

                int readTotal = available;
                while (readTotal < strLen)
                {
                    int read = stream.Read(temp, readTotal, strLen - readTotal);
                    if (read <= 0)
                    {
                        throw new HtbException(HtbErrorCode.CorruptData, "Unexpected end of stream while reading large binary string.");
                    }
                    readTotal += read;
                    bytesReadTotal += read;
                }

                return Encoding.UTF8.GetString(temp, 0, strLen);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(temp);
            }
        }
    }

    /// <summary>Reads a String asynchronously from the stream buffer.</summary>
    public ValueTask<string> ReadStringAsync()
        => ReadStringAsync(activeCancellationToken);

    internal async ValueTask<string> ReadStringAsync(CancellationToken cancellationToken)
    {
        await EnsureBufferAsync(4, cancellationToken).ConfigureAwait(false);
        if (bufferLen - bufferPos < 4)
        {
            throw new HtbException(HtbErrorCode.CorruptData, "Unexpected end of stream while reading string length.");
        }
        int strLen = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(bufferPos));
        bufferPos += 4;
        if (strLen < 0 || strLen > HtbSchema.MAX_PAYLOAD_BYTES)
        {
            throw new HtbException(HtbErrorCode.CorruptData, $"Invalid string length: {strLen}");
        }
        if (strLen == 0) return string.Empty;

        if (strLen <= buffer.Length)
        {
            await EnsureBufferAsync(strLen, cancellationToken).ConfigureAwait(false);
            if (bufferLen - bufferPos < strLen)
            {
                throw new HtbException(HtbErrorCode.CorruptData, "Unexpected end of stream while reading string value.");
            }
            string str = Encoding.UTF8.GetString(buffer, bufferPos, strLen);
            bufferPos += strLen;
            return str;
        }
        else
        {
            int available = bufferLen - bufferPos;
            byte[] temp = ArrayPool<byte>.Shared.Rent(strLen);
            try
            {
                if (available > 0)
                {
                    Array.Copy(buffer, bufferPos, temp, 0, available);
                }
                bufferPos = 0;
                bufferLen = 0;

                int readTotal = available;
                while (readTotal < strLen)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int read = await stream.ReadAsync(temp.AsMemory(readTotal, strLen - readTotal), cancellationToken).ConfigureAwait(false);
                    if (read <= 0)
                    {
                        throw new HtbException(HtbErrorCode.CorruptData, "Unexpected end of stream while reading large binary string.");
                    }
                    readTotal += read;
                    bytesReadTotal += read;
                }

                return Encoding.UTF8.GetString(temp, 0, strLen);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(temp);
            }
        }
    }

    /// <summary>Reads a FloatArray directly from the stream buffer.</summary>
    public float[] ReadFloatArray()
    {
        EnsureBuffer(4);
        if (bufferLen - bufferPos < 4)
        {
            throw new HtbException(HtbErrorCode.CorruptData, "Unexpected end of stream while reading float array length.");
        }
        int arrLen = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(bufferPos));
        bufferPos += 4;
        if (arrLen < 0 || arrLen > HtbSchema.MAX_FLOAT_ARRAY_LENGTH)
        {
            throw new HtbException(HtbErrorCode.CorruptData, $"Invalid float array length: {arrLen}");
        }
        if (arrLen == 0) return [];

        float[] arr = new float[arrLen];
        int byteLen = arrLen * 4;

        if (byteLen <= buffer.Length)
        {
            EnsureBuffer(byteLen);
            if (bufferLen - bufferPos < byteLen)
            {
                throw new HtbException(HtbErrorCode.CorruptData, "Unexpected end of stream while reading float array values.");
            }
            buffer.AsSpan(bufferPos, byteLen).CopyTo(System.Runtime.InteropServices.MemoryMarshal.AsBytes(arr.AsSpan()));
            bufferPos += byteLen;
        }
        else
        {
            int available = bufferLen - bufferPos;
            Span<byte> destBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(arr.AsSpan());
            if (available > 0)
            {
                buffer.AsSpan(bufferPos, available).CopyTo(destBytes);
            }
            bufferPos = 0;
            bufferLen = 0;

            int readTotal = available;
            while (readTotal < byteLen)
            {
                int read = stream.Read(destBytes[readTotal..]);
                if (read <= 0)
                {
                    throw new HtbException(HtbErrorCode.CorruptData, "Unexpected end of stream while reading large float array.");
                }
                readTotal += read;
                bytesReadTotal += read;
            }
        }

        if (!BitConverter.IsLittleEndian)
        {
            var intSpan = System.Runtime.InteropServices.MemoryMarshal.Cast<float, int>(arr.AsSpan());
            for (int i = 0; i < intSpan.Length; i++)
            {
                intSpan[i] = BinaryPrimitives.ReverseEndianness(intSpan[i]);
            }
        }

        return arr;
    }

    /// <summary>Reads a FloatArray asynchronously from the stream buffer.</summary>
    public ValueTask<float[]> ReadFloatArrayAsync()
        => ReadFloatArrayAsync(activeCancellationToken);

    internal async ValueTask<float[]> ReadFloatArrayAsync(CancellationToken cancellationToken)
    {
        await EnsureBufferAsync(4, cancellationToken).ConfigureAwait(false);
        if (bufferLen - bufferPos < 4)
        {
            throw new HtbException(HtbErrorCode.CorruptData, "Unexpected end of stream while reading float array length.");
        }
        int arrLen = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(bufferPos));
        bufferPos += 4;
        if (arrLen < 0 || arrLen > HtbSchema.MAX_FLOAT_ARRAY_LENGTH)
        {
            throw new HtbException(HtbErrorCode.CorruptData, $"Invalid float array length: {arrLen}");
        }
        if (arrLen == 0) return [];

        float[] arr = new float[arrLen];
        int byteLen = arrLen * 4;

        if (byteLen <= buffer.Length)
        {
            await EnsureBufferAsync(byteLen, cancellationToken).ConfigureAwait(false);
            if (bufferLen - bufferPos < byteLen)
            {
                throw new HtbException(HtbErrorCode.CorruptData, "Unexpected end of stream while reading float array values.");
            }
            buffer.AsSpan(bufferPos, byteLen).CopyTo(System.Runtime.InteropServices.MemoryMarshal.AsBytes(arr.AsSpan()));
            bufferPos += byteLen;
        }
        else
        {
            int available = bufferLen - bufferPos;
            byte[] temp = ArrayPool<byte>.Shared.Rent(byteLen);
            try
            {
                if (available > 0)
                {
                    Array.Copy(buffer, bufferPos, temp, 0, available);
                }
                bufferPos = 0;
                bufferLen = 0;

                int readTotal = available;
                while (readTotal < byteLen)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int read = await stream.ReadAsync(temp.AsMemory(readTotal, byteLen - readTotal), cancellationToken).ConfigureAwait(false);
                    if (read <= 0)
                    {
                        throw new HtbException(HtbErrorCode.CorruptData, "Unexpected end of stream while reading large float array.");
                    }
                    readTotal += read;
                    bytesReadTotal += read;
                }

                Buffer.BlockCopy(temp, 0, arr, 0, byteLen);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(temp);
            }
        }

        if (!BitConverter.IsLittleEndian)
        {
            var intSpan = System.Runtime.InteropServices.MemoryMarshal.Cast<float, int>(arr.AsSpan());
            for (int i = 0; i < intSpan.Length; i++)
            {
                intSpan[i] = BinaryPrimitives.ReverseEndianness(intSpan[i]);
            }
        }

        return arr;
    }

    /// <summary>Reads a DateTimeOffset directly from the stream buffer.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public DateTimeOffset ReadDateTimeOffset()
    {
        EnsureBuffer(10);
        if (bufferLen - bufferPos < 10)
        {
            throw new HtbException(HtbErrorCode.CorruptData, "Unexpected end of stream while reading DateTimeOffset.");
        }
        long ticks = BinaryPrimitives.ReadInt64LittleEndian(buffer.AsSpan(bufferPos));
        short offsetMinutes = BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(bufferPos + 8));
        bufferPos += 10;
        return CreateDateTimeOffset(ticks, offsetMinutes);
    }

    /// <summary>Reads a DateTimeOffset asynchronously from the stream buffer.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<DateTimeOffset> ReadDateTimeOffsetAsync()
        => ReadDateTimeOffsetAsync(activeCancellationToken);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal async ValueTask<DateTimeOffset> ReadDateTimeOffsetAsync(CancellationToken cancellationToken)
    {
        await EnsureBufferAsync(10, cancellationToken).ConfigureAwait(false);
        if (bufferLen - bufferPos < 10)
        {
            throw new HtbException(HtbErrorCode.CorruptData, "Unexpected end of stream while reading DateTimeOffset.");
        }
        long ticks = BinaryPrimitives.ReadInt64LittleEndian(buffer.AsSpan(bufferPos));
        short offsetMinutes = BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(bufferPos + 8));
        bufferPos += 10;
        return CreateDateTimeOffset(ticks, offsetMinutes);
    }

    /// <summary>Reads a DateOnly directly from the stream buffer.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public DateOnly ReadDateOnly()
    {
        EnsureBuffer(4);
        if (bufferLen - bufferPos < 4)
        {
            throw new HtbException(HtbErrorCode.CorruptData, "Unexpected end of stream while reading DateOnly.");
        }
        int dayNumber = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(bufferPos));
        bufferPos += 4;
        return CreateDateOnly(dayNumber);
    }

    /// <summary>Reads a DateOnly asynchronously from the stream buffer.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<DateOnly> ReadDateOnlyAsync()
        => ReadDateOnlyAsync(activeCancellationToken);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal async ValueTask<DateOnly> ReadDateOnlyAsync(CancellationToken cancellationToken)
    {
        await EnsureBufferAsync(4, cancellationToken).ConfigureAwait(false);
        if (bufferLen - bufferPos < 4)
        {
            throw new HtbException(HtbErrorCode.CorruptData, "Unexpected end of stream while reading DateOnly.");
        }
        int dayNumber = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(bufferPos));
        bufferPos += 4;
        return CreateDateOnly(dayNumber);
    }

    /// <summary>Reads a TimeOnly directly from the stream buffer.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TimeOnly ReadTimeOnly()
    {
        EnsureBuffer(8);
        if (bufferLen - bufferPos < 8)
        {
            throw new HtbException(HtbErrorCode.CorruptData, "Unexpected end of stream while reading TimeOnly.");
        }
        long ticks = BinaryPrimitives.ReadInt64LittleEndian(buffer.AsSpan(bufferPos));
        bufferPos += 8;
        return CreateTimeOnly(ticks);
    }

    /// <summary>Reads a TimeOnly asynchronously from the stream buffer.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<TimeOnly> ReadTimeOnlyAsync()
        => ReadTimeOnlyAsync(activeCancellationToken);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal async ValueTask<TimeOnly> ReadTimeOnlyAsync(CancellationToken cancellationToken)
    {
        await EnsureBufferAsync(8, cancellationToken).ConfigureAwait(false);
        if (bufferLen - bufferPos < 8)
        {
            throw new HtbException(HtbErrorCode.CorruptData, "Unexpected end of stream while reading TimeOnly.");
        }
        long ticks = BinaryPrimitives.ReadInt64LittleEndian(buffer.AsSpan(bufferPos));
        bufferPos += 8;
        return CreateTimeOnly(ticks);
    }

    /// <summary>Reads a TimeSpan directly from the stream buffer.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TimeSpan ReadTimeSpan() => new(ReadInt64());

    /// <summary>Reads a TimeSpan asynchronously from the stream buffer.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<TimeSpan> ReadTimeSpanAsync() => ReadTimeSpanAsync(activeCancellationToken);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal async ValueTask<TimeSpan> ReadTimeSpanAsync(CancellationToken cancellationToken)
        => new(await ReadInt64Async(cancellationToken).ConfigureAwait(false));

    /// <summary>Reads an Int16 directly from the stream buffer.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public short ReadInt16()
    {
        EnsureBuffer(2);
        if (bufferLen - bufferPos < 2)
        {
            throw new HtbException(HtbErrorCode.CorruptData, "Unexpected end of stream while reading Int16.");
        }
        short val = BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(bufferPos));
        bufferPos += 2;
        return val;
    }

    /// <summary>Reads an Int16 asynchronously from the stream buffer.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<short> ReadInt16Async()
        => ReadInt16Async(activeCancellationToken);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal async ValueTask<short> ReadInt16Async(CancellationToken cancellationToken)
    {
        await EnsureBufferAsync(2, cancellationToken).ConfigureAwait(false);
        if (bufferLen - bufferPos < 2)
        {
            throw new HtbException(HtbErrorCode.CorruptData, "Unexpected end of stream while reading Int16.");
        }
        short val = BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(bufferPos));
        bufferPos += 2;
        return val;
    }

    /// <summary>Reads a UInt16 directly from the stream buffer.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ushort ReadUInt16()
    {
        EnsureBuffer(2);
        if (bufferLen - bufferPos < 2)
        {
            throw new HtbException(HtbErrorCode.CorruptData, "Unexpected end of stream while reading UInt16.");
        }
        ushort val = BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(bufferPos));
        bufferPos += 2;
        return val;
    }

    /// <summary>Reads a UInt16 asynchronously from the stream buffer.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<ushort> ReadUInt16Async()
        => ReadUInt16Async(activeCancellationToken);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal async ValueTask<ushort> ReadUInt16Async(CancellationToken cancellationToken)
    {
        await EnsureBufferAsync(2, cancellationToken).ConfigureAwait(false);
        if (bufferLen - bufferPos < 2)
        {
            throw new HtbException(HtbErrorCode.CorruptData, "Unexpected end of stream while reading UInt16.");
        }
        ushort val = BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(bufferPos));
        bufferPos += 2;
        return val;
    }

    /// <summary>Reads a Byte directly from the stream buffer.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte ReadByte()
    {
        EnsureBuffer(1);
        if (bufferLen - bufferPos < 1)
        {
            throw new HtbException(HtbErrorCode.CorruptData, "Unexpected end of stream while reading Byte.");
        }
        return buffer[bufferPos++];
    }

    /// <summary>Reads a Byte asynchronously from the stream buffer.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<byte> ReadByteAsync()
        => ReadByteAsync(activeCancellationToken);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal async ValueTask<byte> ReadByteAsync(CancellationToken cancellationToken)
    {
        await EnsureBufferAsync(1, cancellationToken).ConfigureAwait(false);
        if (bufferLen - bufferPos < 1)
        {
            throw new HtbException(HtbErrorCode.CorruptData, "Unexpected end of stream while reading Byte.");
        }
        return buffer[bufferPos++];
    }

    /// <summary>Reads an SByte directly from the stream buffer.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public sbyte ReadSByte() => unchecked((sbyte)ReadByte());

    /// <summary>Reads an SByte asynchronously from the stream buffer.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<sbyte> ReadSByteAsync() => ReadSByteAsync(activeCancellationToken);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal async ValueTask<sbyte> ReadSByteAsync(CancellationToken cancellationToken)
        => unchecked((sbyte)await ReadByteAsync(cancellationToken).ConfigureAwait(false));

    /// <summary>Reads a UInt32 directly from the stream buffer.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint ReadUInt32()
    {
        EnsureBuffer(4);
        if (bufferLen - bufferPos < 4)
        {
            throw new HtbException(HtbErrorCode.CorruptData, "Unexpected end of stream while reading UInt32.");
        }
        uint val = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(bufferPos));
        bufferPos += 4;
        return val;
    }

    /// <summary>Reads a UInt32 asynchronously from the stream buffer.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<uint> ReadUInt32Async()
        => ReadUInt32Async(activeCancellationToken);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal async ValueTask<uint> ReadUInt32Async(CancellationToken cancellationToken)
    {
        await EnsureBufferAsync(4, cancellationToken).ConfigureAwait(false);
        if (bufferLen - bufferPos < 4)
        {
            throw new HtbException(HtbErrorCode.CorruptData, "Unexpected end of stream while reading UInt32.");
        }
        uint val = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(bufferPos));
        bufferPos += 4;
        return val;
    }

    /// <summary>Reads a UInt64 directly from the stream buffer.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ulong ReadUInt64()
    {
        EnsureBuffer(8);
        if (bufferLen - bufferPos < 8)
        {
            throw new HtbException(HtbErrorCode.CorruptData, "Unexpected end of stream while reading UInt64.");
        }
        ulong val = BinaryPrimitives.ReadUInt64LittleEndian(buffer.AsSpan(bufferPos));
        bufferPos += 8;
        return val;
    }

    /// <summary>Reads a UInt64 asynchronously from the stream buffer.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<ulong> ReadUInt64Async()
        => ReadUInt64Async(activeCancellationToken);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal async ValueTask<ulong> ReadUInt64Async(CancellationToken cancellationToken)
    {
        await EnsureBufferAsync(8, cancellationToken).ConfigureAwait(false);
        if (bufferLen - bufferPos < 8)
        {
            throw new HtbException(HtbErrorCode.CorruptData, "Unexpected end of stream while reading UInt64.");
        }
        ulong val = BinaryPrimitives.ReadUInt64LittleEndian(buffer.AsSpan(bufferPos));
        bufferPos += 8;
        return val;
    }

    /// <summary>Reads a Char directly from the stream buffer.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public char ReadChar() => (char)ReadUInt16();

    /// <summary>Reads a Char asynchronously from the stream buffer.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<char> ReadCharAsync() => ReadCharAsync(activeCancellationToken);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal async ValueTask<char> ReadCharAsync(CancellationToken cancellationToken)
        => (char)await ReadUInt16Async(cancellationToken).ConfigureAwait(false);

    /// <summary>Reads a Half directly from the stream buffer.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Half ReadHalf()
    {
        EnsureBuffer(2);
        if (bufferLen - bufferPos < 2)
        {
            throw new HtbException(HtbErrorCode.CorruptData, "Unexpected end of stream while reading Half.");
        }
        Half val = BinaryPrimitives.ReadHalfLittleEndian(buffer.AsSpan(bufferPos));
        bufferPos += 2;
        return val;
    }

    /// <summary>Reads a Half asynchronously from the stream buffer.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<Half> ReadHalfAsync()
        => ReadHalfAsync(activeCancellationToken);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal async ValueTask<Half> ReadHalfAsync(CancellationToken cancellationToken)
    {
        await EnsureBufferAsync(2, cancellationToken).ConfigureAwait(false);
        if (bufferLen - bufferPos < 2)
        {
            throw new HtbException(HtbErrorCode.CorruptData, "Unexpected end of stream while reading Half.");
        }
        Half val = BinaryPrimitives.ReadHalfLittleEndian(buffer.AsSpan(bufferPos));
        bufferPos += 2;
        return val;
    }

    /// <summary>Reads an Int128 directly from the stream buffer.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Int128 ReadInt128()
    {
        EnsureBuffer(16);
        if (bufferLen - bufferPos < 16)
        {
            throw new HtbException(HtbErrorCode.CorruptData, "Unexpected end of stream while reading Int128.");
        }
        Int128 val = BinaryPrimitives.ReadInt128LittleEndian(buffer.AsSpan(bufferPos));
        bufferPos += 16;
        return val;
    }

    /// <summary>Reads an Int128 asynchronously from the stream buffer.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<Int128> ReadInt128Async()
        => ReadInt128Async(activeCancellationToken);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal async ValueTask<Int128> ReadInt128Async(CancellationToken cancellationToken)
    {
        await EnsureBufferAsync(16, cancellationToken).ConfigureAwait(false);
        if (bufferLen - bufferPos < 16)
        {
            throw new HtbException(HtbErrorCode.CorruptData, "Unexpected end of stream while reading Int128.");
        }
        Int128 val = BinaryPrimitives.ReadInt128LittleEndian(buffer.AsSpan(bufferPos));
        bufferPos += 16;
        return val;
    }

    /// <summary>Reads a UInt128 directly from the stream buffer.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public UInt128 ReadUInt128()
    {
        EnsureBuffer(16);
        if (bufferLen - bufferPos < 16)
        {
            throw new HtbException(HtbErrorCode.CorruptData, "Unexpected end of stream while reading UInt128.");
        }
        UInt128 val = BinaryPrimitives.ReadUInt128LittleEndian(buffer.AsSpan(bufferPos));
        bufferPos += 16;
        return val;
    }

    /// <summary>Reads a UInt128 asynchronously from the stream buffer.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<UInt128> ReadUInt128Async()
        => ReadUInt128Async(activeCancellationToken);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal async ValueTask<UInt128> ReadUInt128Async(CancellationToken cancellationToken)
    {
        await EnsureBufferAsync(16, cancellationToken).ConfigureAwait(false);
        if (bufferLen - bufferPos < 16)
        {
            throw new HtbException(HtbErrorCode.CorruptData, "Unexpected end of stream while reading UInt128.");
        }
        UInt128 val = BinaryPrimitives.ReadUInt128LittleEndian(buffer.AsSpan(bufferPos));
        bufferPos += 16;
        return val;
    }

    /// <summary>Reads a byte array directly from the stream buffer.</summary>
    public byte[] ReadByteArray() => ReadLengthPrefixedBytes("byte array");

    /// <summary>Reads a byte array asynchronously from the stream buffer.</summary>
    public ValueTask<byte[]> ReadByteArrayAsync() => ReadByteArrayAsync(activeCancellationToken);

    internal ValueTask<byte[]> ReadByteArrayAsync(CancellationToken cancellationToken)
        => ReadLengthPrefixedBytesAsync("byte array", cancellationToken);

    /// <summary>Reads a BigInteger directly from the stream buffer without intermediate byte array allocations.</summary>
    public BigInteger ReadBigInteger()
    {
        EnsureBuffer(4);
        if (bufferLen - bufferPos < 4)
        {
            throw new HtbException(HtbErrorCode.CorruptData, "Unexpected end of stream while reading BigInteger length.");
        }
        int len = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(bufferPos));
        bufferPos += 4;
        if (len < 0 || len > HtbSchema.MAX_PAYLOAD_BYTES)
        {
            throw new HtbException(HtbErrorCode.CorruptData, $"Invalid BigInteger length: {len}");
        }
        if (len == 0) return BigInteger.Zero;

        if (len <= buffer.Length)
        {
            EnsureBuffer(len);
            if (bufferLen - bufferPos < len)
            {
                throw new HtbException(HtbErrorCode.CorruptData, "Unexpected end of stream while reading BigInteger value.");
            }
            var val = new BigInteger(buffer.AsSpan(bufferPos, len), isUnsigned: false, isBigEndian: false);
            bufferPos += len;
            return val;
        }

        byte[] rented = ArrayPool<byte>.Shared.Rent(len);
        try
        {
            int available = bufferLen - bufferPos;
            if (available > 0)
            {
                Buffer.BlockCopy(buffer, bufferPos, rented, 0, available);
            }
            bufferPos = 0;
            bufferLen = 0;

            int readTotal = available;
            while (readTotal < len)
            {
                int read = stream.Read(rented, readTotal, len - readTotal);
                if (read <= 0)
                {
                    throw new HtbException(HtbErrorCode.CorruptData, "Unexpected end of stream while reading large BigInteger.");
                }
                readTotal += read;
                bytesReadTotal += read;
            }

            return new BigInteger(rented.AsSpan(0, len), isUnsigned: false, isBigEndian: false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    /// <summary>Reads a BigInteger asynchronously from the stream buffer without intermediate byte array allocations.</summary>
    public ValueTask<BigInteger> ReadBigIntegerAsync()
        => ReadBigIntegerAsync(activeCancellationToken);

    internal async ValueTask<BigInteger> ReadBigIntegerAsync(CancellationToken cancellationToken)
    {
        await EnsureBufferAsync(4, cancellationToken).ConfigureAwait(false);
        if (bufferLen - bufferPos < 4)
        {
            throw new HtbException(HtbErrorCode.CorruptData, "Unexpected end of stream while reading BigInteger length.");
        }
        int len = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(bufferPos));
        bufferPos += 4;
        if (len < 0 || len > HtbSchema.MAX_PAYLOAD_BYTES)
        {
            throw new HtbException(HtbErrorCode.CorruptData, $"Invalid BigInteger length: {len}");
        }
        if (len == 0) return BigInteger.Zero;

        if (len <= buffer.Length)
        {
            await EnsureBufferAsync(len, cancellationToken).ConfigureAwait(false);
            if (bufferLen - bufferPos < len)
            {
                throw new HtbException(HtbErrorCode.CorruptData, "Unexpected end of stream while reading BigInteger value.");
            }
            var val = new BigInteger(buffer.AsSpan(bufferPos, len), isUnsigned: false, isBigEndian: false);
            bufferPos += len;
            return val;
        }

        byte[] rented = ArrayPool<byte>.Shared.Rent(len);
        try
        {
            int available = bufferLen - bufferPos;
            if (available > 0)
            {
                Buffer.BlockCopy(buffer, bufferPos, rented, 0, available);
            }
            bufferPos = 0;
            bufferLen = 0;

            int readTotal = available;
            while (readTotal < len)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int read = await stream.ReadAsync(rented.AsMemory(readTotal, len - readTotal), cancellationToken).ConfigureAwait(false);
                if (read <= 0)
                {
                    throw new HtbException(HtbErrorCode.CorruptData, "Unexpected end of stream while reading large BigInteger.");
                }
                readTotal += read;
                bytesReadTotal += read;
            }

            return new BigInteger(rented.AsSpan(0, len), isUnsigned: false, isBigEndian: false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    /// <summary>Reads a Uri directly from the stream buffer.</summary>
    public Uri ReadUri() => CreateUri(ReadString());

    /// <summary>Reads a Uri asynchronously from the stream buffer.</summary>
    public ValueTask<Uri> ReadUriAsync() => ReadUriAsync(activeCancellationToken);

    internal async ValueTask<Uri> ReadUriAsync(CancellationToken cancellationToken)
        => CreateUri(await ReadStringAsync(cancellationToken).ConfigureAwait(false));

    private static DateTimeOffset CreateDateTimeOffset(long ticks, short offsetMinutes)
    {
        if (offsetMinutes is < -840 or > 840)
            throw new HtbException(HtbErrorCode.CorruptData, $"Invalid DateTimeOffset offset minutes: {offsetMinutes}");

        var offset = TimeSpan.FromMinutes(offsetMinutes);
        long utcTicks = ticks - offset.Ticks;
        if ((ulong)ticks > (ulong)DateTime.MaxValue.Ticks || (ulong)utcTicks > (ulong)DateTime.MaxValue.Ticks)
            throw new HtbException(HtbErrorCode.CorruptData, $"Invalid DateTimeOffset ticks: {ticks}");

        return new DateTimeOffset(ticks, offset);
    }

    private static DateOnly CreateDateOnly(int dayNumber)
    {
        if ((uint)dayNumber > (uint)DateOnly.MaxValue.DayNumber)
            throw new HtbException(HtbErrorCode.CorruptData, $"Invalid DateOnly day number: {dayNumber}");

        return DateOnly.FromDayNumber(dayNumber);
    }

    private static TimeOnly CreateTimeOnly(long ticks)
    {
        if ((ulong)ticks > (ulong)TimeOnly.MaxValue.Ticks)
            throw new HtbException(HtbErrorCode.CorruptData, $"Invalid TimeOnly ticks: {ticks}");

        return new TimeOnly(ticks);
    }

    private static Uri CreateUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.RelativeOrAbsolute, out var uri))
            throw new HtbException(HtbErrorCode.CorruptData, $"Invalid URI value: '{value}'.");

        return uri;
    }

    private byte[] ReadLengthPrefixedBytes(string typeLabel)
    {
        EnsureBuffer(4);
        if (bufferLen - bufferPos < 4)
        {
            throw new HtbException(HtbErrorCode.CorruptData, $"Unexpected end of stream while reading {typeLabel} length.");
        }
        int len = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(bufferPos));
        bufferPos += 4;
        if (len < 0 || len > HtbSchema.MAX_PAYLOAD_BYTES)
        {
            throw new HtbException(HtbErrorCode.CorruptData, $"Invalid {typeLabel} length: {len}");
        }
        if (len == 0) return [];

        byte[] result = new byte[len];
        if (len <= buffer.Length)
        {
            EnsureBuffer(len);
            if (bufferLen - bufferPos < len)
            {
                throw new HtbException(HtbErrorCode.CorruptData, $"Unexpected end of stream while reading {typeLabel} value.");
            }
            Buffer.BlockCopy(buffer, bufferPos, result, 0, len);
            bufferPos += len;
        }
        else
        {
            int available = bufferLen - bufferPos;
            if (available > 0)
            {
                Buffer.BlockCopy(buffer, bufferPos, result, 0, available);
            }
            bufferPos = 0;
            bufferLen = 0;

            int readTotal = available;
            while (readTotal < len)
            {
                int read = stream.Read(result, readTotal, len - readTotal);
                if (read <= 0)
                {
                    throw new HtbException(HtbErrorCode.CorruptData, $"Unexpected end of stream while reading large {typeLabel}.");
                }
                readTotal += read;
                bytesReadTotal += read;
            }
        }

        return result;
    }

    private async ValueTask<byte[]> ReadLengthPrefixedBytesAsync(string typeLabel, CancellationToken cancellationToken)
    {
        await EnsureBufferAsync(4, cancellationToken).ConfigureAwait(false);
        if (bufferLen - bufferPos < 4)
        {
            throw new HtbException(HtbErrorCode.CorruptData, $"Unexpected end of stream while reading {typeLabel} length.");
        }
        int len = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(bufferPos));
        bufferPos += 4;
        if (len < 0 || len > HtbSchema.MAX_PAYLOAD_BYTES)
        {
            throw new HtbException(HtbErrorCode.CorruptData, $"Invalid {typeLabel} length: {len}");
        }
        if (len == 0) return [];

        byte[] result = new byte[len];
        if (len <= buffer.Length)
        {
            await EnsureBufferAsync(len, cancellationToken).ConfigureAwait(false);
            if (bufferLen - bufferPos < len)
            {
                throw new HtbException(HtbErrorCode.CorruptData, $"Unexpected end of stream while reading {typeLabel} value.");
            }
            Buffer.BlockCopy(buffer, bufferPos, result, 0, len);
            bufferPos += len;
        }
        else
        {
            int available = bufferLen - bufferPos;
            if (available > 0)
            {
                Buffer.BlockCopy(buffer, bufferPos, result, 0, available);
            }
            bufferPos = 0;
            bufferLen = 0;

            int readTotal = available;
            while (readTotal < len)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int read = await stream.ReadAsync(result.AsMemory(readTotal, len - readTotal), cancellationToken).ConfigureAwait(false);
                if (read <= 0)
                {
                    throw new HtbException(HtbErrorCode.CorruptData, $"Unexpected end of stream while reading large {typeLabel}.");
                }
                readTotal += read;
                bytesReadTotal += read;
            }
        }

        return result;
    }

    /// <summary>Skips column bytes sequentially without materialization.</summary>
    public void SkipColumnValue(HtbDataType type)
    {
        switch (type)
        {
            case HtbDataType.Boolean:
            case HtbDataType.Byte:
            case HtbDataType.SByte:
                SkipFixedBytes(1);
                break;

            case HtbDataType.Int16:
            case HtbDataType.UInt16:
            case HtbDataType.Char:
            case HtbDataType.Half:
                SkipFixedBytes(2);
                break;

            case HtbDataType.Int32:
            case HtbDataType.Float:
            case HtbDataType.DateOnly:
            case HtbDataType.UInt32:
                SkipFixedBytes(4);
                break;

            case HtbDataType.Int64:
            case HtbDataType.Double:
            case HtbDataType.DateTime:
            case HtbDataType.TimeOnly:
            case HtbDataType.TimeSpan:
            case HtbDataType.UInt64:
                SkipFixedBytes(8);
                break;

            case HtbDataType.DateTimeOffset:
                SkipFixedBytes(10);
                break;

            case HtbDataType.Decimal:
            case HtbDataType.Guid:
            case HtbDataType.Int128:
            case HtbDataType.UInt128:
                SkipFixedBytes(16);
                break;

            case HtbDataType.String:
                SkipLengthPrefixedBytes("string");
                break;

            case HtbDataType.ByteArray:
            case HtbDataType.BigInteger:
            case HtbDataType.Uri:
                SkipLengthPrefixedBytes("value");
                break;

            case HtbDataType.FloatArray:
                EnsureBuffer(4);
                if (bufferLen - bufferPos < 4)
                    throw new HtbException(HtbErrorCode.CorruptData, "Truncated stream while skipping float array length.");
                int arrLen = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(bufferPos));
                bufferPos += 4;
                if (arrLen < 0 || arrLen > HtbSchema.MAX_FLOAT_ARRAY_LENGTH)
                {
                    throw new HtbException(HtbErrorCode.CorruptData, $"Invalid float array length: {arrLen}");
                }
                if (arrLen > 0)
                {
                    int byteLen = arrLen * 4;
                    int remaining = bufferLen - bufferPos;
                    if (byteLen <= remaining)
                    {
                        bufferPos += byteLen;
                    }
                    else
                    {
                        int toSkip = byteLen - remaining;
                        bufferPos = 0;
                        bufferLen = 0;
                        SkipFromStream(toSkip);
                    }
                }
                break;

            case HtbDataType.Unknown:
            default:
                throw new HtbException(HtbErrorCode.CorruptData, $"Invalid or unsupported column data type during skipping: {type}");
        }
    }

    /// <summary>Skips column bytes asynchronously without materialization.</summary>
    public ValueTask SkipColumnValueAsync(HtbDataType type)
        => SkipColumnValueAsync(type, activeCancellationToken);

    internal async ValueTask SkipColumnValueAsync(HtbDataType type, CancellationToken cancellationToken)
    {
        switch (type)
        {
            case HtbDataType.Boolean:
            case HtbDataType.Byte:
            case HtbDataType.SByte:
                await SkipFixedBytesAsync(1, cancellationToken).ConfigureAwait(false);
                break;

            case HtbDataType.Int16:
            case HtbDataType.UInt16:
            case HtbDataType.Char:
            case HtbDataType.Half:
                await SkipFixedBytesAsync(2, cancellationToken).ConfigureAwait(false);
                break;

            case HtbDataType.Int32:
            case HtbDataType.Float:
            case HtbDataType.DateOnly:
            case HtbDataType.UInt32:
                await SkipFixedBytesAsync(4, cancellationToken).ConfigureAwait(false);
                break;

            case HtbDataType.Int64:
            case HtbDataType.Double:
            case HtbDataType.DateTime:
            case HtbDataType.TimeOnly:
            case HtbDataType.TimeSpan:
            case HtbDataType.UInt64:
                await SkipFixedBytesAsync(8, cancellationToken).ConfigureAwait(false);
                break;

            case HtbDataType.DateTimeOffset:
                await SkipFixedBytesAsync(10, cancellationToken).ConfigureAwait(false);
                break;

            case HtbDataType.Decimal:
            case HtbDataType.Guid:
            case HtbDataType.Int128:
            case HtbDataType.UInt128:
                await SkipFixedBytesAsync(16, cancellationToken).ConfigureAwait(false);
                break;

            case HtbDataType.String:
                await SkipLengthPrefixedBytesAsync("string", cancellationToken).ConfigureAwait(false);
                break;

            case HtbDataType.ByteArray:
            case HtbDataType.BigInteger:
            case HtbDataType.Uri:
                await SkipLengthPrefixedBytesAsync("value", cancellationToken).ConfigureAwait(false);
                break;

            case HtbDataType.FloatArray:
                await EnsureBufferAsync(4, cancellationToken).ConfigureAwait(false);
                if (bufferLen - bufferPos < 4)
                    throw new HtbException(HtbErrorCode.CorruptData, "Truncated stream while skipping float array length.");
                int arrLen = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(bufferPos));
                bufferPos += 4;
                if (arrLen < 0 || arrLen > HtbSchema.MAX_FLOAT_ARRAY_LENGTH)
                {
                    throw new HtbException(HtbErrorCode.CorruptData, $"Invalid float array length: {arrLen}");
                }
                if (arrLen > 0)
                {
                    int byteLen = arrLen * 4;
                    int remaining = bufferLen - bufferPos;
                    if (byteLen <= remaining)
                    {
                        bufferPos += byteLen;
                    }
                    else
                    {
                        int toSkip = byteLen - remaining;
                        bufferPos = 0;
                        bufferLen = 0;
                        await SkipFromStreamAsync(toSkip, cancellationToken).ConfigureAwait(false);
                    }
                }
                break;

            case HtbDataType.Unknown:
            default:
                throw new HtbException(HtbErrorCode.CorruptData, $"Invalid or unsupported column data type during skipping: {type}");
        }
    }

    private void SkipFixedBytes(int count)
    {
        EnsureBuffer(count);
        if (bufferLen - bufferPos < count)
            throw new HtbException(HtbErrorCode.CorruptData, "Truncated stream while skipping value.");
        bufferPos += count;
    }

    private async ValueTask SkipFixedBytesAsync(int count, CancellationToken cancellationToken)
    {
        await EnsureBufferAsync(count, cancellationToken).ConfigureAwait(false);
        if (bufferLen - bufferPos < count)
            throw new HtbException(HtbErrorCode.CorruptData, "Truncated stream while skipping value.");
        bufferPos += count;
    }

    private void SkipLengthPrefixedBytes(string label)
    {
        EnsureBuffer(4);
        if (bufferLen - bufferPos < 4)
            throw new HtbException(HtbErrorCode.CorruptData, $"Truncated stream while skipping {label} length.");
        int len = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(bufferPos));
        bufferPos += 4;
        if (len < 0 || len > HtbSchema.MAX_PAYLOAD_BYTES)
        {
            throw new HtbException(HtbErrorCode.CorruptData, $"Invalid {label} length: {len}");
        }
        if (len > 0)
        {
            int remaining = bufferLen - bufferPos;
            if (len <= remaining)
            {
                bufferPos += len;
            }
            else
            {
                int toSkip = len - remaining;
                bufferPos = 0;
                bufferLen = 0;
                SkipFromStream(toSkip);
            }
        }
    }

    private async ValueTask SkipLengthPrefixedBytesAsync(string label, CancellationToken cancellationToken)
    {
        await EnsureBufferAsync(4, cancellationToken).ConfigureAwait(false);
        if (bufferLen - bufferPos < 4)
            throw new HtbException(HtbErrorCode.CorruptData, $"Truncated stream while skipping {label} length.");
        int len = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(bufferPos));
        bufferPos += 4;
        if (len < 0 || len > HtbSchema.MAX_PAYLOAD_BYTES)
        {
            throw new HtbException(HtbErrorCode.CorruptData, $"Invalid {label} length: {len}");
        }
        if (len > 0)
        {
            int remaining = bufferLen - bufferPos;
            if (len <= remaining)
            {
                bufferPos += len;
            }
            else
            {
                int toSkip = len - remaining;
                bufferPos = 0;
                bufferLen = 0;
                await SkipFromStreamAsync(toSkip, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private void SkipFromStream(int count)
    {
        if (stream.CanSeek)
        {
            long remaining = stream.Length - stream.Position;
            if (count < 0 || count > remaining)
                throw new HtbException(HtbErrorCode.CorruptData, "Unexpected end of stream while skipping data.");

            stream.Seek(count, SeekOrigin.Current);
            bytesReadTotal += count;
        }
        else
        {
            byte[] temp = ArrayPool<byte>.Shared.Rent(Math.Min(count, 8192));
            try
            {
                int remaining = count;
                while (remaining > 0)
                {
                    int toRead = Math.Min(remaining, temp.Length);
                    int read = stream.Read(temp, 0, toRead);
                    if (read <= 0)
                    {
                        throw new HtbException(HtbErrorCode.CorruptData, "Unexpected end of stream while skipping data.");
                    }
                    remaining -= read;
                    bytesReadTotal += read;
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(temp);
            }
        }
    }

    private async ValueTask SkipFromStreamAsync(int count, CancellationToken cancellationToken)
    {
        if (stream.CanSeek)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long remaining = stream.Length - stream.Position;
            if (count < 0 || count > remaining)
                throw new HtbException(HtbErrorCode.CorruptData, "Unexpected end of stream while skipping data.");

            stream.Seek(count, SeekOrigin.Current);
            bytesReadTotal += count;
        }
        else
        {
            byte[] temp = ArrayPool<byte>.Shared.Rent(Math.Min(count, 8192));
            try
            {
                int remaining = count;
                while (remaining > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int toRead = Math.Min(remaining, temp.Length);
                    int read = await stream.ReadAsync(temp.AsMemory(0, toRead), cancellationToken).ConfigureAwait(false);
                    if (read <= 0)
                    {
                        throw new HtbException(HtbErrorCode.CorruptData, "Unexpected end of stream while skipping data.");
                    }
                    remaining -= read;
                    bytesReadTotal += read;
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(temp);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureBuffer(int count)
    {
        if (bufferLen - bufferPos >= count) return;

        int remaining = bufferLen - bufferPos;
        if (remaining > 0)
        {
            Array.Copy(buffer, bufferPos, buffer, 0, remaining);
        }
        bufferPos = 0;
        bufferLen = remaining;

        while (bufferLen < count && bufferLen < buffer.Length)
        {
            int read = stream.Read(buffer, bufferLen, buffer.Length - bufferLen);
            if (read <= 0)
            {
                break;
            }
            bufferLen += read;
            bytesReadTotal += read;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private async ValueTask EnsureBufferAsync(int count, CancellationToken cancellationToken)
    {
        if (bufferLen - bufferPos >= count) return;

        int remaining = bufferLen - bufferPos;
        if (remaining > 0)
        {
            Array.Copy(buffer, bufferPos, buffer, 0, remaining);
        }
        bufferPos = 0;
        bufferLen = remaining;

        while (bufferLen < count && bufferLen < buffer.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int read = await stream.ReadAsync(buffer.AsMemory(bufferLen, buffer.Length - bufferLen), cancellationToken).ConfigureAwait(false);
            if (read <= 0)
            {
                break;
            }
            bufferLen += read;
            bytesReadTotal += read;
        }
    }

    private void CheckDisposed()
    {
        if (isDisposed)
            throw new ObjectDisposedException("HtbStreamReader");
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (isDisposed) return;
        isDisposed = true;

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

        ArrayPool<byte>.Shared.Return(buffer);
        if (!leaveOpen)
        {
            await stream.DisposeAsync().ConfigureAwait(false);
        }
    }
}
