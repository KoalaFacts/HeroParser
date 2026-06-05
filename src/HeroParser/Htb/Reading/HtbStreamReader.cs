using System.Buffers;
using System.Buffers.Binary;
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
            if (dataType < HtbDataType.Int32 || dataType > HtbDataType.FloatArray)
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
    public async ValueTask<HtbSchema> ParseHeaderAsync()
    {
        CheckDisposed();
        if (headerParsed)
        {
            return schema!;
        }

        await EnsureBufferAsync(4 + 4);
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
            await EnsureBufferAsync(1 + 4);
            if (bufferLen - bufferPos < 5)
            {
                throw new HtbException(HtbErrorCode.InvalidHeader, "Truncated stream while reading column schema details.");
            }
            HtbDataType dataType = (HtbDataType)buffer[bufferPos++];
            if (dataType < HtbDataType.Int32 || dataType > HtbDataType.FloatArray)
            {
                throw new HtbException(HtbErrorCode.InvalidHeader, $"Invalid or unsupported column data type byte value: {(byte)dataType}");
            }
            int nameLen = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(bufferPos));
            bufferPos += 4;

            if (nameLen <= 0 || nameLen > 2048)
            {
                throw new HtbException(HtbErrorCode.InvalidHeader, $"Invalid column name length: {nameLen}");
            }

            await EnsureBufferAsync(nameLen);
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
    public async ValueTask<bool> IsEndOfStreamAsync()
    {
        if (bufferPos < bufferLen) return false;
        bufferPos = 0;
        bufferLen = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length)).ConfigureAwait(false);
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
    public async ValueTask ReadMaskAsync(Memory<byte> mask)
    {
        await EnsureBufferAsync(mask.Length).ConfigureAwait(false);
        if (bufferLen - bufferPos < mask.Length)
        {
            throw new HtbException(HtbErrorCode.CorruptData, "Unexpected end of stream while reading null mask.");
        }
        buffer.AsMemory(bufferPos, mask.Length).CopyTo(mask);
        bufferPos += mask.Length;
    }

    #region Strongly-Typed Non-Boxing Read Methods

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
    public async ValueTask<int> ReadInt32Async()
    {
        await EnsureBufferAsync(4).ConfigureAwait(false);
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
    public async ValueTask<long> ReadInt64Async()
    {
        await EnsureBufferAsync(8).ConfigureAwait(false);
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
    public async ValueTask<float> ReadFloatAsync()
    {
        await EnsureBufferAsync(4).ConfigureAwait(false);
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
    public async ValueTask<double> ReadDoubleAsync()
    {
        await EnsureBufferAsync(8).ConfigureAwait(false);
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
    public async ValueTask<decimal> ReadDecimalAsync()
    {
        await EnsureBufferAsync(16).ConfigureAwait(false);
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
    public async ValueTask<bool> ReadBooleanAsync()
    {
        await EnsureBufferAsync(1).ConfigureAwait(false);
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
    public async ValueTask<DateTime> ReadDateTimeAsync()
    {
        await EnsureBufferAsync(8).ConfigureAwait(false);
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
    public async ValueTask<Guid> ReadGuidAsync()
    {
        await EnsureBufferAsync(16).ConfigureAwait(false);
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
        if (strLen < 0 || strLen > 64 * 1024 * 1024)
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
    public async ValueTask<string> ReadStringAsync()
    {
        await EnsureBufferAsync(4).ConfigureAwait(false);
        if (bufferLen - bufferPos < 4)
        {
            throw new HtbException(HtbErrorCode.CorruptData, "Unexpected end of stream while reading string length.");
        }
        int strLen = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(bufferPos));
        bufferPos += 4;
        if (strLen < 0 || strLen > 64 * 1024 * 1024)
        {
            throw new HtbException(HtbErrorCode.CorruptData, $"Invalid string length: {strLen}");
        }
        if (strLen == 0) return string.Empty;

        if (strLen <= buffer.Length)
        {
            await EnsureBufferAsync(strLen).ConfigureAwait(false);
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
                    int read = await stream.ReadAsync(temp.AsMemory(readTotal, strLen - readTotal)).ConfigureAwait(false);
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
        if (arrLen < 0 || arrLen > 16 * 1024 * 1024)
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
    public async ValueTask<float[]> ReadFloatArrayAsync()
    {
        await EnsureBufferAsync(4).ConfigureAwait(false);
        if (bufferLen - bufferPos < 4)
        {
            throw new HtbException(HtbErrorCode.CorruptData, "Unexpected end of stream while reading float array length.");
        }
        int arrLen = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(bufferPos));
        bufferPos += 4;
        if (arrLen < 0 || arrLen > 16 * 1024 * 1024)
        {
            throw new HtbException(HtbErrorCode.CorruptData, $"Invalid float array length: {arrLen}");
        }
        if (arrLen == 0) return [];

        float[] arr = new float[arrLen];
        int byteLen = arrLen * 4;

        if (byteLen <= buffer.Length)
        {
            await EnsureBufferAsync(byteLen).ConfigureAwait(false);
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
                    int read = await stream.ReadAsync(temp.AsMemory(readTotal, byteLen - readTotal)).ConfigureAwait(false);
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

    #endregion

    #region Skipping Methods for Unbound Columns

    /// <summary>Skips column bytes sequentially without materialization.</summary>
    public void SkipColumnValue(HtbDataType type)
    {
        switch (type)
        {
            case HtbDataType.Int32:
            case HtbDataType.Float:
                EnsureBuffer(4);
                if (bufferLen - bufferPos < 4)
                    throw new HtbException(HtbErrorCode.CorruptData, "Truncated stream while skipping value.");
                bufferPos += 4;
                break;

            case HtbDataType.Int64:
            case HtbDataType.Double:
            case HtbDataType.DateTime:
                EnsureBuffer(8);
                if (bufferLen - bufferPos < 8)
                    throw new HtbException(HtbErrorCode.CorruptData, "Truncated stream while skipping value.");
                bufferPos += 8;
                break;

            case HtbDataType.Decimal:
            case HtbDataType.Guid:
                EnsureBuffer(16);
                if (bufferLen - bufferPos < 16)
                    throw new HtbException(HtbErrorCode.CorruptData, "Truncated stream while skipping value.");
                bufferPos += 16;
                break;

            case HtbDataType.Boolean:
                EnsureBuffer(1);
                if (bufferLen - bufferPos < 1)
                    throw new HtbException(HtbErrorCode.CorruptData, "Truncated stream while skipping value.");
                bufferPos += 1;
                break;

            case HtbDataType.String:
                EnsureBuffer(4);
                if (bufferLen - bufferPos < 4)
                    throw new HtbException(HtbErrorCode.CorruptData, "Truncated stream while skipping string length.");
                int strLen = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(bufferPos));
                bufferPos += 4;
                if (strLen < 0 || strLen > 64 * 1024 * 1024)
                {
                    throw new HtbException(HtbErrorCode.CorruptData, $"Invalid string length: {strLen}");
                }
                if (strLen > 0)
                {
                    int remaining = bufferLen - bufferPos;
                    if (strLen <= remaining)
                    {
                        bufferPos += strLen;
                    }
                    else
                    {
                        int toSkip = strLen - remaining;
                        bufferPos = 0;
                        bufferLen = 0;
                        SkipFromStream(toSkip);
                    }
                }
                break;

            case HtbDataType.FloatArray:
                EnsureBuffer(4);
                if (bufferLen - bufferPos < 4)
                    throw new HtbException(HtbErrorCode.CorruptData, "Truncated stream while skipping float array length.");
                int arrLen = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(bufferPos));
                bufferPos += 4;
                if (arrLen < 0 || arrLen > 16 * 1024 * 1024)
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
    public async ValueTask SkipColumnValueAsync(HtbDataType type)
    {
        switch (type)
        {
            case HtbDataType.Int32:
            case HtbDataType.Float:
                await EnsureBufferAsync(4).ConfigureAwait(false);
                if (bufferLen - bufferPos < 4)
                    throw new HtbException(HtbErrorCode.CorruptData, "Truncated stream while skipping value.");
                bufferPos += 4;
                break;

            case HtbDataType.Int64:
            case HtbDataType.Double:
            case HtbDataType.DateTime:
                await EnsureBufferAsync(8).ConfigureAwait(false);
                if (bufferLen - bufferPos < 8)
                    throw new HtbException(HtbErrorCode.CorruptData, "Truncated stream while skipping value.");
                bufferPos += 8;
                break;

            case HtbDataType.Decimal:
            case HtbDataType.Guid:
                await EnsureBufferAsync(16).ConfigureAwait(false);
                if (bufferLen - bufferPos < 16)
                    throw new HtbException(HtbErrorCode.CorruptData, "Truncated stream while skipping value.");
                bufferPos += 16;
                break;

            case HtbDataType.Boolean:
                await EnsureBufferAsync(1).ConfigureAwait(false);
                if (bufferLen - bufferPos < 1)
                    throw new HtbException(HtbErrorCode.CorruptData, "Truncated stream while skipping value.");
                bufferPos += 1;
                break;

            case HtbDataType.String:
                await EnsureBufferAsync(4).ConfigureAwait(false);
                if (bufferLen - bufferPos < 4)
                    throw new HtbException(HtbErrorCode.CorruptData, "Truncated stream while skipping string length.");
                int strLen = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(bufferPos));
                bufferPos += 4;
                if (strLen < 0 || strLen > 64 * 1024 * 1024)
                {
                    throw new HtbException(HtbErrorCode.CorruptData, $"Invalid string length: {strLen}");
                }
                if (strLen > 0)
                {
                    int remaining = bufferLen - bufferPos;
                    if (strLen <= remaining)
                    {
                        bufferPos += strLen;
                    }
                    else
                    {
                        int toSkip = strLen - remaining;
                        bufferPos = 0;
                        bufferLen = 0;
                        await SkipFromStreamAsync(toSkip).ConfigureAwait(false);
                    }
                }
                break;

            case HtbDataType.FloatArray:
                await EnsureBufferAsync(4).ConfigureAwait(false);
                if (bufferLen - bufferPos < 4)
                    throw new HtbException(HtbErrorCode.CorruptData, "Truncated stream while skipping float array length.");
                int arrLen = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(bufferPos));
                bufferPos += 4;
                if (arrLen < 0 || arrLen > 16 * 1024 * 1024)
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
                        await SkipFromStreamAsync(toSkip).ConfigureAwait(false);
                    }
                }
                break;

            case HtbDataType.Unknown:
            default:
                throw new HtbException(HtbErrorCode.CorruptData, $"Invalid or unsupported column data type during skipping: {type}");
        }
    }

    #endregion

    private void SkipFromStream(int count)
    {
        if (stream.CanSeek)
        {
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

    private async ValueTask SkipFromStreamAsync(int count)
    {
        if (stream.CanSeek)
        {
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
                    int read = await stream.ReadAsync(temp.AsMemory(0, toRead)).ConfigureAwait(false);
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
    private async ValueTask EnsureBufferAsync(int count)
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
            int read = await stream.ReadAsync(buffer.AsMemory(bufferLen, buffer.Length - bufferLen)).ConfigureAwait(false);
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
