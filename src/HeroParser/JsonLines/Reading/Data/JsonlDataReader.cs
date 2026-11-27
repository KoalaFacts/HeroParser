using System.Collections;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;

namespace HeroParser.JsonLines.Reading.Data;

/// <summary>
/// Exposes JSONL data as a forward-only <see cref="DbDataReader"/> suitable for bulk-loading into databases
/// (for example <c>SqlBulkCopy</c>).
/// </summary>
/// <remarks>
/// Two schema modes are supported: explicit <see cref="JsonlColumnDefinition"/> list (with JSONPath-like
/// extraction such as <c>messages[0].content</c>), or first-line inference where each top-level JSON key
/// becomes a column. Inferred columns surface as <see cref="string"/>; missing keys surface as <see cref="DBNull"/>.
/// </remarks>
public sealed class JsonlDataReader : DbDataReader
{
    private readonly JsonlLineReader lineReader;
    private readonly JsonlReadOptions readOptions;
    private readonly JsonlDataReaderOptions readerOptions;
    private List<JsonlColumnDefinition> columns;
    private JsonDocument? currentDoc;
    private JsonDocument? peekedDoc;
    private object?[] currentValues;
    private bool initialized;
    private bool hasRows;
    private bool isClosed;
    private int skipped;
    private long lineNumber;
    private long recordsRead;

    /// <summary>
    /// Initializes a new <see cref="JsonlDataReader"/>.
    /// </summary>
    public JsonlDataReader(Stream stream, JsonlReadOptions? readOptions = null, JsonlDataReaderOptions? readerOptions = null, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(stream);
        this.readOptions = readOptions ?? JsonlReadOptions.Default;
        this.readerOptions = readerOptions ?? JsonlDataReaderOptions.Default;
        this.readOptions.Validate();
        if (this.readerOptions.Columns is { Count: > 0 } explicitColumns)
        {
            foreach (JsonlColumnDefinition col in explicitColumns)
            {
                if (!IsSupportedColumnType(col.DataType))
                {
                    throw new ArgumentException(
                        $"Unsupported JsonlColumnDefinition DataType '{col.DataType.FullName}' for column '{col.Name}'.",
                        nameof(readerOptions));
                }
            }
            columns = [.. explicitColumns];
        }
        else
        {
            columns = [];
        }
        lineReader = new JsonlLineReader(stream, this.readOptions, leaveOpen);
        currentValues = [];
    }

    /// <inheritdoc/>
    public override int FieldCount
    {
        get
        {
            EnsureNotClosed();
            if (!initialized && columns.Count == 0 && readerOptions.InferSchemaFromFirstLine)
                EnsureInitialized();
            return columns.Count;
        }
    }

    /// <inheritdoc/>
    public override int Depth => 0;

    /// <inheritdoc/>
    public override bool HasRows
    {
        get
        {
            EnsureNotClosed();
            EnsureInitialized();
            return hasRows;
        }
    }

    /// <inheritdoc/>
    public override bool IsClosed => isClosed;
    /// <inheritdoc/>
    public override int RecordsAffected => -1;

    /// <inheritdoc/>
    public override bool Read()
    {
        EnsureNotClosed();

        currentDoc?.Dispose();
        currentDoc = null;

        if (!initialized)
            EnsureInitialized();

        if (peekedDoc is not null)
        {
            JsonDocument doc = peekedDoc;
            peekedDoc = null;
            ExtractValuesAndAssignCurrent(doc);
            return true;
        }

        while (lineReader.TryReadLine(out ReadOnlyMemory<byte> lineMemory, out long ln))
        {
            if (TryConsumeLineToDocument(lineMemory, ln, out JsonDocument? doc))
            {
                ExtractValuesAndAssignCurrent(doc!);
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc/>
    public override async Task<bool> ReadAsync(CancellationToken cancellationToken)
    {
        EnsureNotClosed();
        cancellationToken.ThrowIfCancellationRequested();

        currentDoc?.Dispose();
        currentDoc = null;

        if (!initialized)
            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        if (peekedDoc is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            JsonDocument doc = peekedDoc;
            peekedDoc = null;
            ExtractValuesAndAssignCurrent(doc);
            return true;
        }

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (hasLine, lineMemory, ln) = await lineReader.TryReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (!hasLine)
                return false;

            if (TryConsumeLineToDocument(lineMemory, ln, out JsonDocument? doc))
            {
                ExtractValuesAndAssignCurrent(doc!);
                return true;
            }
        }
    }

    private void ExtractValuesAndAssignCurrent(JsonDocument doc)
    {
        try
        {
            ExtractValues(doc.RootElement);
            currentDoc = doc;
        }
        catch
        {
            doc.Dispose();
            throw;
        }
    }

    private void EnsureInitialized()
    {
        if (initialized)
            return;

        while (lineReader.TryReadLine(out ReadOnlyMemory<byte> lineMemory, out long ln))
        {
            if (TryConsumeLineToDocument(lineMemory, ln, out JsonDocument? doc))
            {
                InitializeFromFirstDocument(doc!);
                return;
            }
        }

        currentValues = new object?[columns.Count];
        hasRows = false;
        initialized = true;
    }

    private async ValueTask EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (initialized)
            return;

        while (true)
        {
            var (hasLine, lineMemory, ln) = await lineReader.TryReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (!hasLine)
                break;

            if (TryConsumeLineToDocument(lineMemory, ln, out JsonDocument? doc))
            {
                InitializeFromFirstDocument(doc!);
                return;
            }
        }

        currentValues = new object?[columns.Count];
        hasRows = false;
        initialized = true;
    }

    private bool TryConsumeLineToDocument(ReadOnlyMemory<byte> lineMemory, long ln, out JsonDocument? doc)
    {
        var line = lineMemory.Span;
        lineNumber = ln;

        if (readOptions.SkipEmptyLines && IsAllWhitespace(line))
        {
            doc = null;
            return false;
        }

        if (skipped < readerOptions.SkipRows + readOptions.SkipRows)
        {
            skipped++;
            doc = null;
            return false;
        }

        if (recordsRead >= readOptions.MaxRowCount)
        {
            throw new JsonlException(
                JsonlErrorCode.TooManyRows,
                string.Create(CultureInfo.InvariantCulture, $"Number of records exceeds the configured MaxRowCount of {readOptions.MaxRowCount:N0}."),
                ln);
        }

        try
        {
            doc = JsonDocument.Parse(lineMemory);
            recordsRead++;
            return true;
        }
        catch (JsonException ex)
        {
            throw new JsonlException(JsonlErrorCode.DeserializeError, $"Failed to parse JSON: {ex.Message}", ln, ex);
        }
    }

    private void InitializeFromFirstDocument(JsonDocument doc)
    {
        try
        {
            if (columns.Count == 0 && readerOptions.InferSchemaFromFirstLine)
                InferSchemaFrom(doc.RootElement);
            currentValues = new object?[columns.Count];
            peekedDoc = doc;
            hasRows = true;
            initialized = true;
        }
        catch
        {
            doc.Dispose();
            throw;
        }
    }

    private void InferSchemaFrom(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonlException(
                JsonlErrorCode.DeserializeError,
                "Cannot infer schema: first non-empty line must be a JSON object.",
                lineNumber);
        }

        var list = new List<JsonlColumnDefinition>();
        foreach (JsonProperty prop in root.EnumerateObject())
        {
            list.Add(new JsonlColumnDefinition(prop.Name, prop.Name, typeof(string))
            {
                IsLiteralTopLevelKey = true
            });
        }
        columns = list;
    }

    private void ExtractValues(JsonElement root)
    {
        for (int i = 0; i < columns.Count; i++)
        {
            JsonlColumnDefinition col = columns[i];
            JsonElement value = default;
            bool found = col.IsLiteralTopLevelKey
                ? root.ValueKind == JsonValueKind.Object && root.TryGetProperty(col.JsonPath, out value)
                : TryNavigate(root, col.JsonPath, out value);
            currentValues[i] = found
                ? ConvertValue(value, col.DataType)
                : DBNull.Value;
        }
    }

    private static bool IsSupportedColumnType(Type type)
    {
        Type underlying = Nullable.GetUnderlyingType(type) ?? type;
        return underlying == typeof(string)
            || underlying == typeof(object)
            || underlying == typeof(bool)
            || underlying == typeof(byte)
            || underlying == typeof(sbyte)
            || underlying == typeof(short)
            || underlying == typeof(ushort)
            || underlying == typeof(int)
            || underlying == typeof(uint)
            || underlying == typeof(long)
            || underlying == typeof(ulong)
            || underlying == typeof(float)
            || underlying == typeof(double)
            || underlying == typeof(decimal)
            || underlying == typeof(char)
            || underlying == typeof(DateTime)
            || underlying == typeof(DateTimeOffset)
            || underlying == typeof(DateOnly)
            || underlying == typeof(TimeOnly)
            || underlying == typeof(TimeSpan)
            || underlying == typeof(Guid)
            || underlying == typeof(byte[]);
    }

    private static object ConvertValue(JsonElement value, Type targetType)
    {
        if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return DBNull.Value;

        Type underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (underlying == typeof(string))
            return value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText();

        if (underlying == typeof(object))
        {
            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString()!,
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Number => ConvertObjectNumber(value),
                _ => value.GetRawText()
            };
        }

        try
        {
            if (underlying == typeof(bool))
                return value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : DBNull.Value;
            if (underlying == typeof(byte)) return value.GetByte();
            if (underlying == typeof(sbyte)) return value.GetSByte();
            if (underlying == typeof(short)) return value.GetInt16();
            if (underlying == typeof(ushort)) return value.GetUInt16();
            if (underlying == typeof(int)) return value.GetInt32();
            if (underlying == typeof(uint)) return value.GetUInt32();
            if (underlying == typeof(long)) return value.GetInt64();
            if (underlying == typeof(ulong)) return value.GetUInt64();
            if (underlying == typeof(float)) return value.GetSingle();
            if (underlying == typeof(double)) return value.GetDouble();
            if (underlying == typeof(decimal)) return value.GetDecimal();
            if (underlying == typeof(char))
                return value.ValueKind == JsonValueKind.String && value.GetString() is { Length: 1 } s ? s[0] : DBNull.Value;
            if (underlying == typeof(DateTime)) return value.GetDateTime();
            if (underlying == typeof(DateTimeOffset)) return value.GetDateTimeOffset();
            if (underlying == typeof(DateOnly))
                return value.ValueKind == JsonValueKind.String && DateOnly.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly dOnly) ? dOnly : DBNull.Value;
            if (underlying == typeof(TimeOnly))
                return value.ValueKind == JsonValueKind.String && TimeOnly.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out TimeOnly tOnly) ? tOnly : DBNull.Value;
            if (underlying == typeof(TimeSpan))
                return value.ValueKind == JsonValueKind.String && TimeSpan.TryParse(value.GetString(), CultureInfo.InvariantCulture, out TimeSpan ts) ? ts : DBNull.Value;
            if (underlying == typeof(Guid)) return value.GetGuid();
            if (underlying == typeof(byte[]))
                return value.ValueKind == JsonValueKind.String ? value.GetBytesFromBase64() : DBNull.Value;
        }
        catch (InvalidOperationException)
        {
            return DBNull.Value;
        }
        catch (FormatException)
        {
            return DBNull.Value;
        }
        catch (OverflowException)
        {
            return DBNull.Value;
        }

        return DBNull.Value;
    }

    private static object ConvertObjectNumber(JsonElement value)
    {
        if (value.TryGetInt64(out long l))
            return l;

        string rawText = value.GetRawText();
        return TryGetExactDecimal(value, rawText, out decimal dec) ? dec : rawText;
    }

    private static bool TryGetExactDecimal(JsonElement value, string rawText, out decimal result)
    {
        if (!value.TryGetDecimal(out result))
            return false;

        ReadOnlySpan<char> text = rawText.AsSpan();
        int i = 0;
        bool rawNeg = false;
        if (i < text.Length && text[i] is '-' or '+')
        {
            rawNeg = text[i] == '-';
            i++;
        }

        UInt128 rawMantissa = 0;
        int sigDigits = 0;
        int fracDigits = 0;
        int trailingZeros = 0;
        bool seenDot = false;
        bool seenNonZero = false;

        for (; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '.')
            {
                if (seenDot)
                    return false;
                seenDot = true;
                continue;
            }

            if (c is 'E' or 'e')
                break;

            if ((uint)(c - '0') > 9)
                return false;

            if (seenDot)
                fracDigits++;

            if (c == '0')
            {
                if (seenNonZero)
                    trailingZeros++;
            }
            else
            {
                sigDigits += trailingZeros + 1;
                if (sigDigits > 29)
                    return false;

                while (trailingZeros > 0)
                {
                    rawMantissa *= 10;
                    trailingZeros--;
                }

                rawMantissa = (rawMantissa * 10) + (uint)(c - '0');
                seenNonZero = true;
            }
        }

        if (rawMantissa == 0)
            return result == 0m;

        int exp = 0;
        if (i < text.Length && text[i] is 'E' or 'e')
        {
            i++;
            if (!int.TryParse(text[i..], NumberStyles.Integer, CultureInfo.InvariantCulture, out exp))
                return false;
        }

        long exp10 = (long)exp - fracDigits + trailingZeros;
        if (exp10 < -28)
            return false;

        Span<int> bits = stackalloc int[4];
        decimal.GetBits(result, bits);
        UInt128 mMantissa = new((uint)bits[2], ((ulong)(uint)bits[1] << 32) | (uint)bits[0]);
        if (mMantissa == 0)
            return false;

        bool mNeg = bits[3] < 0;
        long mExp10 = -((bits[3] >> 16) & 0x7F);
        while (mMantissa % 10 == 0)
        {
            mMantissa /= 10;
            mExp10++;
        }

        return rawNeg == mNeg && rawMantissa == mMantissa && exp10 == mExp10;
    }

    private static bool TryNavigate(JsonElement root, string path, out JsonElement value)
    {
        value = root;
        ReadOnlySpan<char> remaining = path.AsSpan();

        while (!remaining.IsEmpty)
        {
            if (remaining[0] == '[')
            {
                int close = remaining.IndexOf(']');
                if (close < 0) { value = default; return false; }
                if (value.ValueKind != JsonValueKind.Array) { value = default; return false; }
                if (!int.TryParse(remaining[1..close], NumberStyles.Integer, CultureInfo.InvariantCulture, out int idx))
                {
                    value = default;
                    return false;
                }
                int len = value.GetArrayLength();
                if (idx < 0 || idx >= len) { value = default; return false; }
                value = value[idx];
                remaining = remaining[(close + 1)..];
                if (!remaining.IsEmpty && remaining[0] == '.') remaining = remaining[1..];
                continue;
            }

            int nextSep = -1;
            for (int i = 0; i < remaining.Length; i++)
            {
                if (remaining[i] is '.' or '[') { nextSep = i; break; }
            }
            ReadOnlySpan<char> segment = nextSep < 0 ? remaining : remaining[..nextSep];
            if (value.ValueKind != JsonValueKind.Object) { value = default; return false; }
            if (!value.TryGetProperty(segment, out JsonElement next)) { value = default; return false; }
            value = next;
            if (nextSep < 0) return true;
            remaining = remaining[nextSep] == '.' ? remaining[(nextSep + 1)..] : remaining[nextSep..];
        }
        return true;
    }

    private static bool IsAllWhitespace(ReadOnlySpan<byte> span)
    {
        for (int i = 0; i < span.Length; i++)
        {
            byte b = span[i];
            if (b is not ((byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n'))
                return false;
        }
        return true;
    }

    private void EnsureNotClosed()
    {
        if (isClosed)
            throw new InvalidOperationException("The data reader is closed.");
    }

    private void ValidateOrdinal(int ordinal)
    {
        if ((uint)ordinal >= (uint)columns.Count)
        {
            throw new IndexOutOfRangeException(
                $"Column index {ordinal} is out of range. Column count is {columns.Count}.");
        }
    }

    private void ThrowIfBeforeRead()
    {
        EnsureNotClosed();
        if (currentDoc is null)
            throw new InvalidOperationException("Call Read() before accessing fields.");
    }

    /// <inheritdoc/>
    public override string GetName(int ordinal)
    {
        EnsureNotClosed();
        if (!initialized && columns.Count == 0 && readerOptions.InferSchemaFromFirstLine)
            EnsureInitialized();
        ValidateOrdinal(ordinal);
        return columns[ordinal].Name;
    }

    /// <inheritdoc/>
    public override int GetOrdinal(string name)
    {
        EnsureNotClosed();
        if (!initialized && columns.Count == 0 && readerOptions.InferSchemaFromFirstLine)
            EnsureInitialized();
        for (int i = 0; i < columns.Count; i++)
            if (string.Equals(columns[i].Name, name, StringComparison.OrdinalIgnoreCase))
                return i;
        throw new IndexOutOfRangeException(name);
    }

    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)]
    private static Type ResolveFieldType(Type type)
    {
        Type underlying = Nullable.GetUnderlyingType(type) ?? type;
        if (underlying == typeof(string)) return typeof(string);
        if (underlying == typeof(object)) return typeof(object);
        if (underlying == typeof(bool)) return typeof(bool);
        if (underlying == typeof(byte)) return typeof(byte);
        if (underlying == typeof(sbyte)) return typeof(sbyte);
        if (underlying == typeof(short)) return typeof(short);
        if (underlying == typeof(ushort)) return typeof(ushort);
        if (underlying == typeof(int)) return typeof(int);
        if (underlying == typeof(uint)) return typeof(uint);
        if (underlying == typeof(long)) return typeof(long);
        if (underlying == typeof(ulong)) return typeof(ulong);
        if (underlying == typeof(float)) return typeof(float);
        if (underlying == typeof(double)) return typeof(double);
        if (underlying == typeof(decimal)) return typeof(decimal);
        if (underlying == typeof(char)) return typeof(char);
        if (underlying == typeof(DateTime)) return typeof(DateTime);
        if (underlying == typeof(DateTimeOffset)) return typeof(DateTimeOffset);
        if (underlying == typeof(DateOnly)) return typeof(DateOnly);
        if (underlying == typeof(TimeOnly)) return typeof(TimeOnly);
        if (underlying == typeof(TimeSpan)) return typeof(TimeSpan);
        if (underlying == typeof(Guid)) return typeof(Guid);
        if (underlying == typeof(byte[])) return typeof(byte[]);
        return typeof(object);
    }

    /// <inheritdoc/>
    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)]
    public override Type GetFieldType(int ordinal)
    {
        EnsureNotClosed();
        if (!initialized && columns.Count == 0 && readerOptions.InferSchemaFromFirstLine)
            EnsureInitialized();
        ValidateOrdinal(ordinal);
        return ResolveFieldType(columns[ordinal].DataType);
    }

    /// <inheritdoc/>
    public override string GetDataTypeName(int ordinal) => GetFieldType(ordinal).Name;

    /// <inheritdoc/>
    public override object GetValue(int ordinal)
    {
        ThrowIfBeforeRead();
        ValidateOrdinal(ordinal);
        return currentValues[ordinal] ?? DBNull.Value;
    }

    /// <inheritdoc/>
    public override int GetValues(object[] values)
    {
        ThrowIfBeforeRead();
        ArgumentNullException.ThrowIfNull(values);
        int count = Math.Min(values.Length, columns.Count);
        for (int i = 0; i < count; i++)
            values[i] = currentValues[i] ?? DBNull.Value;
        return count;
    }

    /// <inheritdoc/>
    public override bool IsDBNull(int ordinal)
    {
        ThrowIfBeforeRead();
        ValidateOrdinal(ordinal);
        return currentValues[ordinal] is null or DBNull;
    }

    /// <inheritdoc/>
    public override object this[int ordinal] => GetValue(ordinal);
    /// <inheritdoc/>
    public override object this[string name] => GetValue(GetOrdinal(name));

    /// <inheritdoc/>
    public override bool GetBoolean(int ordinal) => (bool)GetValue(ordinal);
    /// <inheritdoc/>
    public override byte GetByte(int ordinal) => Convert.ToByte(GetValue(ordinal), CultureInfo.InvariantCulture);
    /// <inheritdoc/>
    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length)
    {
        ThrowIfBeforeRead();
        ValidateOrdinal(ordinal);
        object? value = currentValues[ordinal];
        if (value is null or DBNull)
            throw new InvalidCastException($"Column {ordinal} contains null data.");
        if (value is not byte[] bytes)
            throw new InvalidCastException($"Cannot cast column {ordinal} value to byte[].");
        if (dataOffset < 0)
            throw new ArgumentOutOfRangeException(nameof(dataOffset), "Data offset cannot be negative.");

        if (buffer is null)
            return bytes.LongLength;

        if (bufferOffset < 0)
            throw new ArgumentOutOfRangeException(nameof(bufferOffset), "Buffer offset cannot be negative.");
        if (length < 0)
            throw new ArgumentOutOfRangeException(nameof(length), "Length cannot be negative.");
        if ((long)bufferOffset + length > buffer.Length)
            throw new ArgumentException("Buffer is not large enough for the requested copy length.", nameof(buffer));

        if (dataOffset >= bytes.Length || length == 0)
            return 0;

        int available = (int)Math.Min(length, bytes.Length - dataOffset);
        bytes.AsSpan((int)dataOffset, available).CopyTo(buffer.AsSpan(bufferOffset));
        return available;
    }
    /// <inheritdoc/>
    public override char GetChar(int ordinal) => Convert.ToChar(GetValue(ordinal), CultureInfo.InvariantCulture);
    /// <inheritdoc/>
    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length)
    {
        ThrowIfBeforeRead();
        ValidateOrdinal(ordinal);
        throw new NotSupportedException("Char arrays are not supported by JsonlDataReader.");
    }
    /// <inheritdoc/>
    public override DateTime GetDateTime(int ordinal) => Convert.ToDateTime(GetValue(ordinal), CultureInfo.InvariantCulture);
    /// <inheritdoc/>
    public override decimal GetDecimal(int ordinal) => Convert.ToDecimal(GetValue(ordinal), CultureInfo.InvariantCulture);
    /// <inheritdoc/>
    public override double GetDouble(int ordinal) => Convert.ToDouble(GetValue(ordinal), CultureInfo.InvariantCulture);
    /// <inheritdoc/>
    public override float GetFloat(int ordinal) => Convert.ToSingle(GetValue(ordinal), CultureInfo.InvariantCulture);
    /// <inheritdoc/>
    public override Guid GetGuid(int ordinal) => (Guid)GetValue(ordinal);
    /// <inheritdoc/>
    public override short GetInt16(int ordinal) => Convert.ToInt16(GetValue(ordinal), CultureInfo.InvariantCulture);
    /// <inheritdoc/>
    public override int GetInt32(int ordinal) => Convert.ToInt32(GetValue(ordinal), CultureInfo.InvariantCulture);
    /// <inheritdoc/>
    public override long GetInt64(int ordinal) => Convert.ToInt64(GetValue(ordinal), CultureInfo.InvariantCulture);
    /// <inheritdoc/>
    public override string GetString(int ordinal)
    {
        object value = GetValue(ordinal);
        if (value is DBNull) return string.Empty;
        return value as string ?? Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
    }

    /// <inheritdoc/>
    public override bool NextResult() => false;
    /// <inheritdoc/>
    public override IEnumerator GetEnumerator() => new DbEnumerator(this, closeReader: false);

    /// <inheritdoc/>
    public override DataTable GetSchemaTable()
    {
        EnsureNotClosed();
        if (!initialized && columns.Count == 0 && readerOptions.InferSchemaFromFirstLine)
            EnsureInitialized();

        var table = new DataTable("SchemaTable");
        table.Columns.Add("ColumnName", typeof(string));
        table.Columns.Add("ColumnOrdinal", typeof(int));
        table.Columns.Add("DataType", typeof(Type));
        table.Columns.Add("AllowDBNull", typeof(bool));

        for (int i = 0; i < columns.Count; i++)
        {
            DataRow row = table.NewRow();
            row["ColumnName"] = columns[i].Name;
            row["ColumnOrdinal"] = i;
            row["DataType"] = ResolveFieldType(columns[i].DataType);
            row["AllowDBNull"] = true;
            table.Rows.Add(row);
        }
        return table;
    }

    /// <inheritdoc/>
    public override void Close()
    {
        if (isClosed) return;
        isClosed = true;
        peekedDoc?.Dispose();
        peekedDoc = null;
        currentDoc?.Dispose();
        currentDoc = null;
        lineReader.Dispose();
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing) Close();
        base.Dispose(disposing);
    }
}
