using System.Collections;
using System.Data;
using System.Data.Common;
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
    private object?[] currentValues;
    private bool initialized;
    private bool isClosed;
    private int skipped;
    private long lineNumber;

    /// <summary>
    /// Initializes a new <see cref="JsonlDataReader"/>.
    /// </summary>
    public JsonlDataReader(Stream stream, JsonlReadOptions? readOptions = null, JsonlDataReaderOptions? readerOptions = null, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(stream);
        this.readOptions = readOptions ?? JsonlReadOptions.Default;
        this.readerOptions = readerOptions ?? JsonlDataReaderOptions.Default;
        this.readOptions.Validate();
        lineReader = new JsonlLineReader(stream, this.readOptions, leaveOpen);
        columns = this.readerOptions.Columns is { Count: > 0 }
            ? [.. this.readerOptions.Columns]
            : [];
        currentValues = [];
    }

    /// <inheritdoc/>
    public override int FieldCount => columns.Count;
    /// <inheritdoc/>
    public override int Depth => 0;
    /// <inheritdoc/>
    public override bool HasRows => true;
    /// <inheritdoc/>
    public override bool IsClosed => isClosed;
    /// <inheritdoc/>
    public override int RecordsAffected => -1;

    /// <inheritdoc/>
    public override bool Read()
    {
        if (isClosed) return false;

        currentDoc?.Dispose();
        currentDoc = null;

        while (lineReader.TryReadLine(out ReadOnlyMemory<byte> lineMemory, out long ln))
        {
            var line = lineMemory.Span;
            lineNumber = ln;

            if (readOptions.SkipEmptyLines && IsAllWhitespace(line))
                continue;

            if (skipped < readerOptions.SkipRows + readOptions.SkipRows)
            {
                skipped++;
                continue;
            }

            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(lineMemory);
            }
            catch (Exception ex)
            {
                throw new JsonlException(JsonlErrorCode.DeserializeError, $"Failed to parse JSON: {ex.Message}", ln, ex);
            }

            if (!initialized)
            {
                if (columns.Count == 0 && readerOptions.InferSchemaFromFirstLine)
                    InferSchemaFrom(doc.RootElement);
                currentValues = new object?[columns.Count];
                initialized = true;
            }

            ExtractValues(doc.RootElement);
            currentDoc = doc;
            return true;
        }

        return false;
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
            list.Add(new JsonlColumnDefinition(prop.Name, prop.Name, typeof(string)));
        columns = list;
    }

    private void ExtractValues(JsonElement root)
    {
        for (int i = 0; i < columns.Count; i++)
        {
            JsonlColumnDefinition col = columns[i];
            currentValues[i] = TryNavigate(root, col.JsonPath, out JsonElement value)
                ? ConvertValue(value, col.DataType)
                : DBNull.Value;
        }
    }

    private static object ConvertValue(JsonElement value, Type targetType)
    {
        if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return DBNull.Value;

        if (targetType == typeof(string))
            return value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText();

        try
        {
            if (targetType == typeof(int)) return value.GetInt32();
            if (targetType == typeof(long)) return value.GetInt64();
            if (targetType == typeof(double)) return value.GetDouble();
            if (targetType == typeof(decimal)) return value.GetDecimal();
            if (targetType == typeof(bool)) return value.GetBoolean();
            if (targetType == typeof(DateTime)) return value.GetDateTime();
            if (targetType == typeof(Guid)) return value.GetGuid();
        }
        catch
        {
            return DBNull.Value;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText();
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

    private void ThrowIfBeforeRead()
    {
        if (currentDoc is null)
            throw new InvalidOperationException("Call Read() before accessing fields.");
    }

    /// <inheritdoc/>
    public override string GetName(int ordinal) => columns[ordinal].Name;
    /// <inheritdoc/>
    public override int GetOrdinal(string name)
    {
        for (int i = 0; i < columns.Count; i++)
            if (string.Equals(columns[i].Name, name, StringComparison.OrdinalIgnoreCase))
                return i;
        throw new IndexOutOfRangeException(name);
    }
    /// <inheritdoc/>
    public override Type GetFieldType(int ordinal) => columns[ordinal].DataType;
    /// <inheritdoc/>
    public override string GetDataTypeName(int ordinal) => GetFieldType(ordinal).Name;

    /// <inheritdoc/>
    public override object GetValue(int ordinal)
    {
        ThrowIfBeforeRead();
        return currentValues[ordinal] ?? DBNull.Value;
    }

    /// <inheritdoc/>
    public override int GetValues(object[] values)
    {
        ThrowIfBeforeRead();
        int count = Math.Min(values.Length, FieldCount);
        for (int i = 0; i < count; i++)
            values[i] = currentValues[i] ?? DBNull.Value;
        return count;
    }

    /// <inheritdoc/>
    public override bool IsDBNull(int ordinal)
    {
        ThrowIfBeforeRead();
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
        => throw new NotSupportedException("Binary fields are not supported by JsonlDataReader.");
    /// <inheritdoc/>
    public override char GetChar(int ordinal) => Convert.ToChar(GetValue(ordinal), CultureInfo.InvariantCulture);
    /// <inheritdoc/>
    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length)
        => throw new NotSupportedException("Char arrays are not supported by JsonlDataReader.");
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
            row["DataType"] = columns[i].DataType;
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
        currentDoc?.Dispose();
        lineReader.Dispose();
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing) Close();
        base.Dispose(disposing);
    }
}
