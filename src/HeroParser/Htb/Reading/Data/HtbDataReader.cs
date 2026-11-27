using System.Buffers;
using System.Collections;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using HeroParser.Htbs.Records;

namespace HeroParser.Htbs.Reading.Data;

/// <summary>
/// Exposes a High-Throughput Tabular Binary (HTB) stream as a forward-only <see cref="DbDataReader"/>
/// suitable for bulk loading and streaming queries without reflection or NativeAOT metadata.
/// </summary>
public sealed class HtbDataReader : DbDataReader
{
    private readonly HtbStreamReader streamReader;
    private readonly HtbReadOptions options;
    private readonly HtbSchema? schemaOverride;
    private HtbSchema? schema;
    private Dictionary<string, int>? exactOrdinals;
    private Dictionary<string, int>? caseInsensitiveOrdinals;
    private RowCell[] currentCells = [];
    private byte[] maskBuffer = [];
    private bool headerInitialized;
    private bool hasRowsKnown;
    private bool hasRows;
    private bool hasPeekedRow;
    private bool hasCurrentRow;
    private bool isClosed;

    /// <summary>
    /// Initializes a new <see cref="HtbDataReader"/> over an HTB stream.
    /// </summary>
    /// <param name="stream">Readable stream containing HTB data.</param>
    /// <param name="options">Optional HTB read options; defaults to <see cref="HtbReadOptions.Default"/>.</param>
    /// <param name="leaveOpen">When <see langword="true"/>, leaves <paramref name="stream"/> open when disposed.</param>
    public HtbDataReader(Stream stream, HtbReadOptions? options = null, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(stream);
        this.options = options ?? HtbReadOptions.Default;
        this.options.Validate();
        streamReader = new HtbStreamReader(stream, this.options, leaveOpen);
    }

    internal HtbDataReader(Stream stream, HtbSchema? schemaOverride, HtbReadOptions? options = null, bool leaveOpen = false)
        : this(stream, options, leaveOpen)
    {
        this.schemaOverride = schemaOverride;
    }

    /// <inheritdoc />
    public override int FieldCount
    {
        get
        {
            EnsureNotClosed();
            EnsureHeaderInitialized();
            return schema!.Columns.Count;
        }
    }

    /// <inheritdoc />
    public override int Depth => 0;

    /// <inheritdoc />
    public override bool HasRows
    {
        get
        {
            EnsureNotClosed();
            EnsureHeaderInitialized();
            if (!hasRowsKnown)
            {
                if (TryReadNextRow())
                {
                    hasPeekedRow = true;
                    hasRows = true;
                }
                hasRowsKnown = true;
            }
            return hasRows;
        }
    }

    /// <inheritdoc />
    public override bool IsClosed => isClosed;

    /// <inheritdoc />
    public override int RecordsAffected => -1;

    /// <inheritdoc />
    public override object this[int ordinal] => GetValue(ordinal);

    /// <inheritdoc />
    public override object this[string name] => GetValue(GetOrdinal(name));

    /// <inheritdoc />
    public override bool Read()
    {
        EnsureNotClosed();
        if (!headerInitialized)
            EnsureHeaderInitialized();

        if (hasPeekedRow)
        {
            hasPeekedRow = false;
            hasCurrentRow = true;
            return true;
        }

        if (TryReadNextRow())
        {
            hasRows = true;
            hasRowsKnown = true;
            hasCurrentRow = true;
            return true;
        }

        hasRowsKnown = true;
        hasCurrentRow = false;
        Array.Clear(currentCells);
        return false;
    }

    /// <inheritdoc />
    public override async Task<bool> ReadAsync(CancellationToken cancellationToken)
    {
        EnsureNotClosed();
        cancellationToken.ThrowIfCancellationRequested();

        if (!headerInitialized)
            await EnsureHeaderInitializedAsync(cancellationToken).ConfigureAwait(false);

        if (hasPeekedRow)
        {
            hasPeekedRow = false;
            hasCurrentRow = true;
            return true;
        }

        if (await TryReadNextRowAsync(cancellationToken).ConfigureAwait(false))
        {
            hasRows = true;
            hasRowsKnown = true;
            hasCurrentRow = true;
            return true;
        }

        hasRowsKnown = true;
        hasCurrentRow = false;
        Array.Clear(currentCells);
        return false;
    }

    /// <inheritdoc />
    public override string GetName(int ordinal)
    {
        EnsureNotClosed();
        EnsureHeaderInitialized();
        ValidateOrdinal(ordinal);
        return schema!.Columns[ordinal].Name;
    }

    /// <inheritdoc />
    public override int GetOrdinal(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        EnsureNotClosed();
        EnsureHeaderInitialized();
        if (exactOrdinals is not null && exactOrdinals.TryGetValue(name, out int exact))
            return exact;
        if (caseInsensitiveOrdinals is not null && caseInsensitiveOrdinals.TryGetValue(name, out int ordinal))
            return ordinal;

        throw new IndexOutOfRangeException($"Column '{name}' does not exist.");
    }

    /// <inheritdoc />
    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)]
    public override Type GetFieldType(int ordinal)
    {
        EnsureNotClosed();
        EnsureHeaderInitialized();
        ValidateOrdinal(ordinal);
        return MapClrType(schema!.Columns[ordinal].DataType);
    }

    /// <inheritdoc />
    public override string GetDataTypeName(int ordinal) => GetFieldType(ordinal).Name;

    /// <inheritdoc />
    public override object GetValue(int ordinal)
    {
        ThrowIfBeforeRead();
        ValidateOrdinal(ordinal);
        return currentCells[ordinal].IsNull ? DBNull.Value : BoxCell(ordinal);
    }

    /// <inheritdoc />
    public override int GetValues(object[] values)
    {
        ThrowIfBeforeRead();
        ArgumentNullException.ThrowIfNull(values);
        int count = Math.Min(values.Length, schema!.Columns.Count);
        for (int i = 0; i < count; i++)
            values[i] = currentCells[i].IsNull ? DBNull.Value : BoxCell(i);
        return count;
    }

    /// <inheritdoc />
    public override bool IsDBNull(int ordinal)
    {
        ThrowIfBeforeRead();
        ValidateOrdinal(ordinal);
        return currentCells[ordinal].IsNull;
    }

    /// <inheritdoc />
    public override bool GetBoolean(int ordinal)
    {
        ThrowIfBeforeRead();
        ValidateOrdinal(ordinal);
        ref readonly RowCell cell = ref currentCells[ordinal];
        if (!cell.IsNull && schema!.Columns[ordinal].DataType == HtbDataType.Boolean)
            return cell.LowBits != 0;

        object val = cell.IsNull ? DBNull.Value : BoxCell(ordinal);
        return val is bool b ? b : Convert.ToBoolean(val, CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    public override byte GetByte(int ordinal)
    {
        ThrowIfBeforeRead();
        ValidateOrdinal(ordinal);
        ref readonly RowCell cell = ref currentCells[ordinal];
        if (!cell.IsNull && schema!.Columns[ordinal].DataType == HtbDataType.Byte)
            return unchecked((byte)cell.LowBits);

        object val = cell.IsNull ? DBNull.Value : BoxCell(ordinal);
        return val is byte b ? b : Convert.ToByte(val, CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length)
    {
        ThrowIfBeforeRead();
        ValidateOrdinal(ordinal);
        ref readonly RowCell cell = ref currentCells[ordinal];
        if (cell.IsNull)
            throw new InvalidCastException($"Column {ordinal} contains null data.");
        if (cell.Reference is not byte[] bytes)
            throw new InvalidCastException($"Cannot cast column {ordinal} value to byte[].");
        if (dataOffset < 0)
            throw new ArgumentOutOfRangeException(nameof(dataOffset), "Data offset cannot be negative.");

        if (buffer is null)
            return bytes.LongLength;

        if (bufferOffset < 0)
            throw new ArgumentOutOfRangeException(nameof(bufferOffset), "Buffer offset cannot be negative.");
        if (length < 0)
            throw new ArgumentOutOfRangeException(nameof(length), "Length cannot be negative.");
        if (bufferOffset + length > buffer.Length)
            throw new ArgumentException("Buffer is not large enough for the requested copy length.", nameof(buffer));

        if (dataOffset >= bytes.Length)
            return 0;

        int available = (int)Math.Min(length, bytes.Length - dataOffset);
        bytes.AsSpan((int)dataOffset, available).CopyTo(buffer.AsSpan(bufferOffset));
        return available;
    }

    /// <inheritdoc />
    public override char GetChar(int ordinal)
    {
        ThrowIfBeforeRead();
        ValidateOrdinal(ordinal);
        ref readonly RowCell cell = ref currentCells[ordinal];
        if (!cell.IsNull && schema!.Columns[ordinal].DataType == HtbDataType.Char)
            return (char)cell.LowBits;

        object val = cell.IsNull ? DBNull.Value : BoxCell(ordinal);
        return val is char c ? c : Convert.ToChar(val, CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length)
    {
        ThrowIfBeforeRead();
        ValidateOrdinal(ordinal);
        throw new NotSupportedException("Char array streaming is not supported by HtbDataReader.");
    }

    /// <inheritdoc />
    public override DateTime GetDateTime(int ordinal)
    {
        ThrowIfBeforeRead();
        ValidateOrdinal(ordinal);
        ref readonly RowCell cell = ref currentCells[ordinal];
        if (!cell.IsNull && schema!.Columns[ordinal].DataType == HtbDataType.DateTime)
            return DateTime.FromBinary(cell.LowBits);

        object val = cell.IsNull ? DBNull.Value : BoxCell(ordinal);
        return val is DateTime dt ? dt : Convert.ToDateTime(val, CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    public override decimal GetDecimal(int ordinal)
    {
        ThrowIfBeforeRead();
        ValidateOrdinal(ordinal);
        ref readonly RowCell cell = ref currentCells[ordinal];
        if (!cell.IsNull)
        {
            switch (schema!.Columns[ordinal].DataType)
            {
                case HtbDataType.Decimal:
                    return cell.Decimal;
                case HtbDataType.Half:
                    return (decimal)(double)BitConverter.Int16BitsToHalf(unchecked((short)cell.LowBits));
                case HtbDataType.Int128:
                    return (decimal)new Int128(unchecked((ulong)cell.HighBits), unchecked((ulong)cell.LowBits));
                case HtbDataType.UInt128:
                    return (decimal)new UInt128(unchecked((ulong)cell.HighBits), unchecked((ulong)cell.LowBits));
                case HtbDataType.BigInteger:
                    return (decimal)cell.BigInteger;
                case HtbDataType.Unknown:
                case HtbDataType.Int32:
                case HtbDataType.Int64:
                case HtbDataType.Float:
                case HtbDataType.Double:
                case HtbDataType.Boolean:
                case HtbDataType.DateTime:
                case HtbDataType.String:
                case HtbDataType.Guid:
                case HtbDataType.FloatArray:
                case HtbDataType.DateTimeOffset:
                case HtbDataType.DateOnly:
                case HtbDataType.TimeOnly:
                case HtbDataType.TimeSpan:
                case HtbDataType.Int16:
                case HtbDataType.UInt16:
                case HtbDataType.Byte:
                case HtbDataType.SByte:
                case HtbDataType.UInt32:
                case HtbDataType.UInt64:
                case HtbDataType.Char:
                case HtbDataType.ByteArray:
                case HtbDataType.Uri:
                default:
                    break;
            }
        }

        object val = cell.IsNull ? DBNull.Value : BoxCell(ordinal);
        return Convert.ToDecimal(val, CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    public override double GetDouble(int ordinal)
    {
        ThrowIfBeforeRead();
        ValidateOrdinal(ordinal);
        ref readonly RowCell cell = ref currentCells[ordinal];
        if (!cell.IsNull)
        {
            switch (schema!.Columns[ordinal].DataType)
            {
                case HtbDataType.Double:
                    return BitConverter.Int64BitsToDouble(cell.LowBits);
                case HtbDataType.Float:
                    return BitConverter.Int32BitsToSingle(unchecked((int)cell.LowBits));
                case HtbDataType.Half:
                    return (double)BitConverter.Int16BitsToHalf(unchecked((short)cell.LowBits));
                case HtbDataType.Int128:
                    return (double)new Int128(unchecked((ulong)cell.HighBits), unchecked((ulong)cell.LowBits));
                case HtbDataType.UInt128:
                    return (double)new UInt128(unchecked((ulong)cell.HighBits), unchecked((ulong)cell.LowBits));
                case HtbDataType.BigInteger:
                    return (double)cell.BigInteger;
                case HtbDataType.Unknown:
                case HtbDataType.Int32:
                case HtbDataType.Int64:
                case HtbDataType.Decimal:
                case HtbDataType.Boolean:
                case HtbDataType.DateTime:
                case HtbDataType.String:
                case HtbDataType.Guid:
                case HtbDataType.FloatArray:
                case HtbDataType.DateTimeOffset:
                case HtbDataType.DateOnly:
                case HtbDataType.TimeOnly:
                case HtbDataType.TimeSpan:
                case HtbDataType.Int16:
                case HtbDataType.UInt16:
                case HtbDataType.Byte:
                case HtbDataType.SByte:
                case HtbDataType.UInt32:
                case HtbDataType.UInt64:
                case HtbDataType.Char:
                case HtbDataType.ByteArray:
                case HtbDataType.Uri:
                default:
                    break;
            }
        }

        object val = cell.IsNull ? DBNull.Value : BoxCell(ordinal);
        return Convert.ToDouble(val, CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    public override float GetFloat(int ordinal)
    {
        ThrowIfBeforeRead();
        ValidateOrdinal(ordinal);
        ref readonly RowCell cell = ref currentCells[ordinal];
        if (!cell.IsNull && schema!.Columns[ordinal].DataType == HtbDataType.Float)
            return BitConverter.Int32BitsToSingle(unchecked((int)cell.LowBits));

        object val = cell.IsNull ? DBNull.Value : BoxCell(ordinal);
        return val is float f ? f : Convert.ToSingle(val, CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    public override Guid GetGuid(int ordinal)
    {
        ThrowIfBeforeRead();
        ValidateOrdinal(ordinal);
        ref readonly RowCell cell = ref currentCells[ordinal];
        if (!cell.IsNull)
        {
            HtbDataType dataType = schema!.Columns[ordinal].DataType;
            if (dataType == HtbDataType.Guid)
                return cell.Guid;
            if (dataType == HtbDataType.String && cell.Reference is string s && Guid.TryParse(s, out Guid parsed))
                return parsed;
        }

        throw new InvalidCastException($"Cannot cast column {ordinal} value to Guid.");
    }

    /// <inheritdoc />
    public override short GetInt16(int ordinal)
    {
        ThrowIfBeforeRead();
        ValidateOrdinal(ordinal);
        ref readonly RowCell cell = ref currentCells[ordinal];
        if (!cell.IsNull && schema!.Columns[ordinal].DataType == HtbDataType.Int16)
            return unchecked((short)cell.LowBits);

        return Convert.ToInt16(cell.IsNull ? DBNull.Value : BoxCell(ordinal), CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    public override int GetInt32(int ordinal)
    {
        ThrowIfBeforeRead();
        ValidateOrdinal(ordinal);
        ref readonly RowCell cell = ref currentCells[ordinal];
        if (!cell.IsNull && schema!.Columns[ordinal].DataType == HtbDataType.Int32)
            return unchecked((int)cell.LowBits);

        object val = cell.IsNull ? DBNull.Value : BoxCell(ordinal);
        return val is int i ? i : Convert.ToInt32(val, CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    public override long GetInt64(int ordinal)
    {
        ThrowIfBeforeRead();
        ValidateOrdinal(ordinal);
        ref readonly RowCell cell = ref currentCells[ordinal];
        if (!cell.IsNull && schema!.Columns[ordinal].DataType == HtbDataType.Int64)
            return cell.LowBits;

        object val = cell.IsNull ? DBNull.Value : BoxCell(ordinal);
        return val is long l ? l : Convert.ToInt64(val, CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    public override string GetString(int ordinal)
    {
        ThrowIfBeforeRead();
        ValidateOrdinal(ordinal);
        ref readonly RowCell cell = ref currentCells[ordinal];
        if (cell.IsNull)
            throw new InvalidCastException("Column contains null data.");

        return schema!.Columns[ordinal].DataType switch
        {
            HtbDataType.String => (string)cell.Reference!,
            HtbDataType.Uri => ((Uri)cell.Reference!).OriginalString,
            HtbDataType.ByteArray => Convert.ToBase64String((byte[])cell.Reference!),
            _ => Convert.ToString(BoxCell(ordinal), CultureInfo.InvariantCulture) ?? string.Empty
        };
    }

    /// <inheritdoc />
    public override bool NextResult() => false;

    /// <inheritdoc />
    public override IEnumerator GetEnumerator() => new DbEnumerator(this, closeReader: false);

    /// <inheritdoc />
    public override DataTable GetSchemaTable()
    {
        EnsureNotClosed();
        EnsureHeaderInitialized();

        var table = new DataTable("SchemaTable");
        table.Columns.Add(SchemaTableColumn.ColumnName, typeof(string));
        table.Columns.Add(SchemaTableColumn.ColumnOrdinal, typeof(int));
        table.Columns.Add(SchemaTableColumn.DataType, typeof(Type));
        table.Columns.Add(SchemaTableColumn.AllowDBNull, typeof(bool));

        for (int i = 0; i < schema!.Columns.Count; i++)
        {
            DataRow row = table.NewRow();
            row[SchemaTableColumn.ColumnName] = schema.Columns[i].Name;
            row[SchemaTableColumn.ColumnOrdinal] = i;
            row[SchemaTableColumn.DataType] = MapClrType(schema.Columns[i].DataType);
            row[SchemaTableColumn.AllowDBNull] = schema.Columns[i].IsNullable;
            table.Rows.Add(row);
        }

        return table;
    }

    /// <inheritdoc />
    public override void Close()
    {
        if (isClosed)
            return;

        isClosed = true;
        hasCurrentRow = false;
        hasPeekedRow = false;
        Array.Clear(currentCells);
        streamReader.Dispose();
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
            Close();
        base.Dispose(disposing);
    }

    /// <inheritdoc />
    public override async ValueTask DisposeAsync()
    {
        if (!isClosed)
        {
            isClosed = true;
            hasCurrentRow = false;
            hasPeekedRow = false;
            Array.Clear(currentCells);
            await streamReader.DisposeAsync().ConfigureAwait(false);
        }

        await base.DisposeAsync().ConfigureAwait(false);
    }

    private void EnsureHeaderInitialized()
    {
        if (headerInitialized)
            return;

        HtbSchema headerSchema = streamReader.ParseHeader();
        schema = schemaOverride ?? headerSchema;
        BuildOrdinalLookup(schema);
        headerInitialized = true;
    }

    private async Task EnsureHeaderInitializedAsync(CancellationToken cancellationToken)
    {
        if (headerInitialized)
            return;

        cancellationToken.ThrowIfCancellationRequested();
        HtbSchema headerSchema = await streamReader.ParseHeaderAsync(cancellationToken).ConfigureAwait(false);
        schema = schemaOverride ?? headerSchema;
        BuildOrdinalLookup(schema);
        headerInitialized = true;
    }

    private void BuildOrdinalLookup(HtbSchema parsedSchema)
    {
        int colCount = parsedSchema.Columns.Count;
        var exact = new Dictionary<string, int>(colCount, StringComparer.Ordinal);
        var caseInsensitive = new Dictionary<string, int>(colCount, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < colCount; i++)
        {
            string colName = parsedSchema.Columns[i].Name;
            exact.TryAdd(colName, i);
            caseInsensitive.TryAdd(colName, i);
        }
        exactOrdinals = exact;
        caseInsensitiveOrdinals = caseInsensitive;
        currentCells = new RowCell[colCount];
        maskBuffer = new byte[(colCount + 7) / 8];
    }

    private bool TryReadNextRow()
    {
        int colCount = schema!.Columns.Count;
        Span<byte> mask = maskBuffer;

        while (streamReader.RecordsRead < options.MaxRowCount)
        {
            if (streamReader.IsEndOfStream())
                return false;

            streamReader.ReadMask(mask);
            for (int i = 0; i < colCount; i++)
            {
                HtbColumn column = schema.Columns[i];
                bool isNull = (mask[i / 8] & (1 << (i % 8))) != 0;
                if (isNull)
                {
                    if (!column.IsNullable)
                        throw new HtbException(HtbErrorCode.CorruptData, $"Column '{column.Name}' is not nullable but null was read.");
                    currentCells[i] = new RowCell { IsNull = true };
                }
                else
                {
                    ReadColumnCell(i, column.DataType);
                }
            }

            streamReader.IncrementRecordCount();
            if (streamReader.RecordsRead <= options.SkipRows)
                continue;

            return true;
        }

        if (!streamReader.IsEndOfStream())
        {
            throw new HtbException(
                HtbErrorCode.LimitExceeded,
                $"MaxRowCount limit of {options.MaxRowCount} was exceeded.");
        }

        return false;
    }

    private async ValueTask<bool> TryReadNextRowAsync(CancellationToken cancellationToken)
    {
        int colCount = schema!.Columns.Count;

        while (streamReader.RecordsRead < options.MaxRowCount)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await streamReader.IsEndOfStreamAsync(cancellationToken).ConfigureAwait(false))
                return false;

            await streamReader.ReadMaskAsync(maskBuffer, cancellationToken).ConfigureAwait(false);
            for (int i = 0; i < colCount; i++)
            {
                HtbColumn column = schema.Columns[i];
                bool isNull = (maskBuffer[i / 8] & (1 << (i % 8))) != 0;
                if (isNull)
                {
                    if (!column.IsNullable)
                        throw new HtbException(HtbErrorCode.CorruptData, $"Column '{column.Name}' is not nullable but null was read.");
                    currentCells[i] = new RowCell { IsNull = true };
                }
                else
                {
                    await ReadColumnCellAsync(i, column.DataType, cancellationToken).ConfigureAwait(false);
                }
            }

            streamReader.IncrementRecordCount();
            if (streamReader.RecordsRead <= options.SkipRows)
                continue;

            return true;
        }

        if (!await streamReader.IsEndOfStreamAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new HtbException(
                HtbErrorCode.LimitExceeded,
                $"MaxRowCount limit of {options.MaxRowCount} was exceeded.");
        }

        return false;
    }

    private void ReadColumnCell(int ordinal, HtbDataType dataType)
    {
        ref RowCell cell = ref currentCells[ordinal];
        cell = default;

        switch (dataType)
        {
            case HtbDataType.Int32:
                cell.LowBits = streamReader.ReadInt32();
                break;
            case HtbDataType.Int64:
                cell.LowBits = streamReader.ReadInt64();
                break;
            case HtbDataType.Float:
                cell.LowBits = BitConverter.SingleToInt32Bits(streamReader.ReadFloat());
                break;
            case HtbDataType.Double:
                cell.LowBits = BitConverter.DoubleToInt64Bits(streamReader.ReadDouble());
                break;
            case HtbDataType.Decimal:
                cell.Decimal = streamReader.ReadDecimal();
                break;
            case HtbDataType.Boolean:
                cell.LowBits = streamReader.ReadBoolean() ? 1L : 0L;
                break;
            case HtbDataType.DateTime:
                cell.LowBits = streamReader.ReadDateTime().ToBinary();
                break;
            case HtbDataType.String:
                cell.Reference = streamReader.ReadString();
                break;
            case HtbDataType.Guid:
                cell.Guid = streamReader.ReadGuid();
                break;
            case HtbDataType.FloatArray:
                cell.Reference = streamReader.ReadFloatArray();
                break;
            case HtbDataType.DateTimeOffset:
                DateTimeOffset dto = streamReader.ReadDateTimeOffset();
                cell.LowBits = dto.Ticks;
                cell.HighBits = dto.Offset.Ticks;
                break;
            case HtbDataType.DateOnly:
                cell.LowBits = streamReader.ReadDateOnly().DayNumber;
                break;
            case HtbDataType.TimeOnly:
                cell.LowBits = streamReader.ReadTimeOnly().Ticks;
                break;
            case HtbDataType.TimeSpan:
                cell.LowBits = streamReader.ReadTimeSpan().Ticks;
                break;
            case HtbDataType.Int16:
                cell.LowBits = streamReader.ReadInt16();
                break;
            case HtbDataType.UInt16:
                cell.LowBits = streamReader.ReadUInt16();
                break;
            case HtbDataType.Byte:
                cell.LowBits = streamReader.ReadByte();
                break;
            case HtbDataType.SByte:
                cell.LowBits = streamReader.ReadSByte();
                break;
            case HtbDataType.UInt32:
                cell.LowBits = streamReader.ReadUInt32();
                break;
            case HtbDataType.UInt64:
                cell.LowBits = unchecked((long)streamReader.ReadUInt64());
                break;
            case HtbDataType.Char:
                cell.LowBits = streamReader.ReadChar();
                break;
            case HtbDataType.Half:
                cell.LowBits = BitConverter.HalfToInt16Bits(streamReader.ReadHalf());
                break;
            case HtbDataType.Int128:
                Int128 i128Val = streamReader.ReadInt128();
                cell.LowBits = unchecked((long)(ulong)i128Val);
                cell.HighBits = unchecked((long)(ulong)(i128Val >> 64));
                break;
            case HtbDataType.UInt128:
                UInt128 u128Val = streamReader.ReadUInt128();
                cell.LowBits = unchecked((long)(ulong)u128Val);
                cell.HighBits = unchecked((long)(ulong)(u128Val >> 64));
                break;
            case HtbDataType.ByteArray:
                cell.Reference = streamReader.ReadByteArray();
                break;
            case HtbDataType.BigInteger:
                cell.BigInteger = streamReader.ReadBigInteger();
                break;
            case HtbDataType.Uri:
                cell.Reference = streamReader.ReadUri();
                break;
            case HtbDataType.Unknown:
            default:
                throw new HtbException(HtbErrorCode.CorruptData, $"Unsupported column data type: {dataType}");
        }
    }

    private async ValueTask ReadColumnCellAsync(int ordinal, HtbDataType dataType, CancellationToken cancellationToken)
    {
        currentCells[ordinal] = default;

        switch (dataType)
        {
            case HtbDataType.Int32:
                currentCells[ordinal].LowBits = await streamReader.ReadInt32Async(cancellationToken).ConfigureAwait(false);
                break;
            case HtbDataType.Int64:
                currentCells[ordinal].LowBits = await streamReader.ReadInt64Async(cancellationToken).ConfigureAwait(false);
                break;
            case HtbDataType.Float:
                currentCells[ordinal].LowBits = BitConverter.SingleToInt32Bits(await streamReader.ReadFloatAsync(cancellationToken).ConfigureAwait(false));
                break;
            case HtbDataType.Double:
                currentCells[ordinal].LowBits = BitConverter.DoubleToInt64Bits(await streamReader.ReadDoubleAsync(cancellationToken).ConfigureAwait(false));
                break;
            case HtbDataType.Decimal:
                currentCells[ordinal].Decimal = await streamReader.ReadDecimalAsync(cancellationToken).ConfigureAwait(false);
                break;
            case HtbDataType.Boolean:
                currentCells[ordinal].LowBits = await streamReader.ReadBooleanAsync(cancellationToken).ConfigureAwait(false) ? 1L : 0L;
                break;
            case HtbDataType.DateTime:
                currentCells[ordinal].LowBits = (await streamReader.ReadDateTimeAsync(cancellationToken).ConfigureAwait(false)).ToBinary();
                break;
            case HtbDataType.String:
                currentCells[ordinal].Reference = await streamReader.ReadStringAsync(cancellationToken).ConfigureAwait(false);
                break;
            case HtbDataType.Guid:
                currentCells[ordinal].Guid = await streamReader.ReadGuidAsync(cancellationToken).ConfigureAwait(false);
                break;
            case HtbDataType.FloatArray:
                currentCells[ordinal].Reference = await streamReader.ReadFloatArrayAsync(cancellationToken).ConfigureAwait(false);
                break;
            case HtbDataType.DateTimeOffset:
                DateTimeOffset asyncDto = await streamReader.ReadDateTimeOffsetAsync(cancellationToken).ConfigureAwait(false);
                currentCells[ordinal].LowBits = asyncDto.Ticks;
                currentCells[ordinal].HighBits = asyncDto.Offset.Ticks;
                break;
            case HtbDataType.DateOnly:
                currentCells[ordinal].LowBits = (await streamReader.ReadDateOnlyAsync(cancellationToken).ConfigureAwait(false)).DayNumber;
                break;
            case HtbDataType.TimeOnly:
                currentCells[ordinal].LowBits = (await streamReader.ReadTimeOnlyAsync(cancellationToken).ConfigureAwait(false)).Ticks;
                break;
            case HtbDataType.TimeSpan:
                currentCells[ordinal].LowBits = (await streamReader.ReadTimeSpanAsync(cancellationToken).ConfigureAwait(false)).Ticks;
                break;
            case HtbDataType.Int16:
                currentCells[ordinal].LowBits = await streamReader.ReadInt16Async(cancellationToken).ConfigureAwait(false);
                break;
            case HtbDataType.UInt16:
                currentCells[ordinal].LowBits = await streamReader.ReadUInt16Async(cancellationToken).ConfigureAwait(false);
                break;
            case HtbDataType.Byte:
                currentCells[ordinal].LowBits = await streamReader.ReadByteAsync(cancellationToken).ConfigureAwait(false);
                break;
            case HtbDataType.SByte:
                currentCells[ordinal].LowBits = await streamReader.ReadSByteAsync(cancellationToken).ConfigureAwait(false);
                break;
            case HtbDataType.UInt32:
                currentCells[ordinal].LowBits = await streamReader.ReadUInt32Async(cancellationToken).ConfigureAwait(false);
                break;
            case HtbDataType.UInt64:
                currentCells[ordinal].LowBits = unchecked((long)await streamReader.ReadUInt64Async(cancellationToken).ConfigureAwait(false));
                break;
            case HtbDataType.Char:
                currentCells[ordinal].LowBits = await streamReader.ReadCharAsync(cancellationToken).ConfigureAwait(false);
                break;
            case HtbDataType.Half:
                currentCells[ordinal].LowBits = BitConverter.HalfToInt16Bits(await streamReader.ReadHalfAsync(cancellationToken).ConfigureAwait(false));
                break;
            case HtbDataType.Int128:
                Int128 asyncInt128 = await streamReader.ReadInt128Async(cancellationToken).ConfigureAwait(false);
                currentCells[ordinal].LowBits = unchecked((long)(ulong)asyncInt128);
                currentCells[ordinal].HighBits = unchecked((long)(ulong)(asyncInt128 >> 64));
                break;
            case HtbDataType.UInt128:
                UInt128 asyncUInt128 = await streamReader.ReadUInt128Async(cancellationToken).ConfigureAwait(false);
                currentCells[ordinal].LowBits = unchecked((long)(ulong)asyncUInt128);
                currentCells[ordinal].HighBits = unchecked((long)(ulong)(asyncUInt128 >> 64));
                break;
            case HtbDataType.ByteArray:
                currentCells[ordinal].Reference = await streamReader.ReadByteArrayAsync(cancellationToken).ConfigureAwait(false);
                break;
            case HtbDataType.BigInteger:
                currentCells[ordinal].BigInteger = await streamReader.ReadBigIntegerAsync(cancellationToken).ConfigureAwait(false);
                break;
            case HtbDataType.Uri:
                currentCells[ordinal].Reference = await streamReader.ReadUriAsync(cancellationToken).ConfigureAwait(false);
                break;
            case HtbDataType.Unknown:
            default:
                throw new HtbException(HtbErrorCode.CorruptData, $"Unsupported column data type: {dataType}");
        }
    }

    private object BoxCell(int ordinal)
    {
        ref readonly RowCell cell = ref currentCells[ordinal];
        HtbDataType dataType = schema!.Columns[ordinal].DataType;

        return dataType switch
        {
            HtbDataType.Int32 => unchecked((int)cell.LowBits),
            HtbDataType.Int64 => cell.LowBits,
            HtbDataType.Float => BitConverter.Int32BitsToSingle(unchecked((int)cell.LowBits)),
            HtbDataType.Double => BitConverter.Int64BitsToDouble(cell.LowBits),
            HtbDataType.Decimal => cell.Decimal,
            HtbDataType.Boolean => cell.LowBits != 0,
            HtbDataType.DateTime => DateTime.FromBinary(cell.LowBits),
            HtbDataType.String or HtbDataType.FloatArray or HtbDataType.ByteArray or HtbDataType.Uri => cell.Reference!,
            HtbDataType.Guid => cell.Guid,
            HtbDataType.DateTimeOffset => new DateTimeOffset(cell.LowBits, new TimeSpan(cell.HighBits)),
            HtbDataType.DateOnly => DateOnly.FromDayNumber(unchecked((int)cell.LowBits)),
            HtbDataType.TimeOnly => new TimeOnly(cell.LowBits),
            HtbDataType.TimeSpan => new TimeSpan(cell.LowBits),
            HtbDataType.Int16 => unchecked((short)cell.LowBits),
            HtbDataType.UInt16 => unchecked((ushort)cell.LowBits),
            HtbDataType.Byte => unchecked((byte)cell.LowBits),
            HtbDataType.SByte => unchecked((sbyte)cell.LowBits),
            HtbDataType.UInt32 => unchecked((uint)cell.LowBits),
            HtbDataType.UInt64 => unchecked((ulong)cell.LowBits),
            HtbDataType.Char => (char)cell.LowBits,
            HtbDataType.Half => BitConverter.Int16BitsToHalf(unchecked((short)cell.LowBits)),
            HtbDataType.Int128 => new Int128(unchecked((ulong)cell.HighBits), unchecked((ulong)cell.LowBits)),
            HtbDataType.UInt128 => new UInt128(unchecked((ulong)cell.HighBits), unchecked((ulong)cell.LowBits)),
            HtbDataType.BigInteger => cell.BigInteger,
            _ => throw new HtbException(HtbErrorCode.CorruptData, $"Unsupported column data type: {dataType}")
        };
    }

    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)]
    private static Type MapClrType(HtbDataType dataType) => dataType switch
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

    private void EnsureNotClosed()
    {
        if (isClosed)
            throw new InvalidOperationException("The data reader is closed.");
    }

    private void ValidateOrdinal(int ordinal)
    {
        int count = schema?.Columns.Count ?? 0;
        if ((uint)ordinal >= (uint)count)
            throw new IndexOutOfRangeException($"Column index {ordinal} is out of range. Column count is {count}.");
    }

    private void ThrowIfBeforeRead()
    {
        EnsureNotClosed();
        if (!hasCurrentRow)
            throw new InvalidOperationException("No current row is available. Call Read() before accessing fields.");
    }

    private struct RowCell
    {
        public bool IsNull;
        public long LowBits;
        public long HighBits;
        public decimal Decimal;
        public Guid Guid;
        public BigInteger BigInteger;
        public object? Reference;
    }
}
