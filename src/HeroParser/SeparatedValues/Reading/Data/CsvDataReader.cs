using System;
using System.Collections;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Reading.Rows;
using HeroParser.SeparatedValues.Reading.Streaming;

namespace HeroParser.SeparatedValues.Reading.Data;

/// <summary>
/// Streams CSV data through <see cref="IDataReader"/> for database bulk loading.
/// </summary>
public sealed class CsvDataReader : DbDataReader
{
    private readonly CsvAsyncStreamReader reader;
    private readonly CsvDataReaderOptions options;
    private readonly byte[][]? nullValueBytes;
    private readonly StringComparer headerComparer;

    private string[] columnNames = [];
    private Dictionary<string, int>? ordinals;
    private int fieldCount;
    private bool initialized;
    private bool closed;
    private bool hasPendingRow;
    private bool hasCurrentRow;
    private bool hasAnyRow;

    internal CsvDataReader(CsvAsyncStreamReader reader, CsvDataReaderOptions options)
    {
        this.reader = reader ?? throw new ArgumentNullException(nameof(reader));
        this.options = options ?? CsvDataReaderOptions.Default;
        headerComparer = this.options.HeaderComparer;
        nullValueBytes = PrepareNullValues(this.options.NullValues);

        if (this.options.ColumnNames is { Count: > 0 })
        {
            columnNames = [.. this.options.ColumnNames];
        }
    }

    /// <inheritdoc />
    public override int FieldCount
    {
        get
        {
            EnsureInitialized();
            return fieldCount;
        }
    }

    /// <inheritdoc />
    public override bool HasRows
    {
        get
        {
            EnsureInitialized();
            return hasAnyRow;
        }
    }

    /// <inheritdoc />
    public override bool IsClosed => closed;

    /// <inheritdoc />
    public override int RecordsAffected => -1;

    /// <inheritdoc />
    public override int Depth => 0;

    /// <inheritdoc />
    public override object this[int ordinal] => GetValue(ordinal);

    /// <inheritdoc />
    public override object this[string name] => GetValue(GetOrdinal(name));

    /// <inheritdoc />
    public override bool Read()
    {
        EnsureNotClosed();
        EnsureInitialized();

        if (hasPendingRow)
        {
            hasPendingRow = false;
            hasCurrentRow = true;
            return true;
        }

        if (!Advance())
        {
            hasCurrentRow = false;
            return false;
        }

        ValidateRow(reader.Current);
        hasCurrentRow = true;
        return true;
    }

    /// <inheritdoc />
    public override bool NextResult() => false;

    /// <inheritdoc />
    public override string GetName(int ordinal)
    {
        EnsureInitialized();
        ValidateOrdinal(ordinal);
        return columnNames[ordinal];
    }

    /// <inheritdoc />
    public override int GetOrdinal(string name)
    {
        EnsureInitialized();
        if (ordinals is not null && ordinals.TryGetValue(name, out var ordinal))
        {
            return ordinal;
        }

        throw new IndexOutOfRangeException($"Column '{name}' does not exist.");
    }

    /// <inheritdoc />
    public override Type GetFieldType(int ordinal)
    {
        EnsureInitialized();
        ValidateOrdinal(ordinal);
        return typeof(string);
    }

    /// <inheritdoc />
    public override string GetDataTypeName(int ordinal)
        => GetFieldType(ordinal).Name;

    /// <inheritdoc />
    public override object GetValue(int ordinal)
    {
        EnsureCurrentRow();
        ValidateOrdinal(ordinal);

        var row = reader.Current;
        if (!row.TryGetColumnSpan(ordinal, out var span))
        {
            if (options.AllowMissingColumns)
                return DBNull.Value;

            throw new CsvException(
                CsvErrorCode.ParseError,
                $"Row has only {row.ColumnCount} columns but requested index {ordinal}.",
                row.LineNumber,
                ordinal + 1);
        }

        if (IsNullValue(span))
            return DBNull.Value;

        return row.GetString(ordinal);
    }

    /// <inheritdoc />
    public override string GetString(int ordinal)
    {
        var value = GetValue(ordinal);
        if (value is DBNull)
            throw new InvalidCastException("Column contains null data.");
        return (string)value;
    }

    /// <inheritdoc />
    public override bool GetBoolean(int ordinal)
    {
        var value = GetString(ordinal);
        if (bool.TryParse(value, out var result))
            return result;
        if (value.Length == 1)
        {
            return value[0] switch
            {
                '1' => true,
                '0' => false,
                _ => throw new FormatException($"Value '{value}' is not a valid boolean.")
            };
        }
        throw new FormatException($"Value '{value}' is not a valid boolean.");
    }

    /// <inheritdoc />
    public override byte GetByte(int ordinal)
        => byte.Parse(GetString(ordinal), NumberStyles.Integer, CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public override short GetInt16(int ordinal)
        => short.Parse(GetString(ordinal), NumberStyles.Integer, CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public override int GetInt32(int ordinal)
        => int.Parse(GetString(ordinal), NumberStyles.Integer, CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public override long GetInt64(int ordinal)
        => long.Parse(GetString(ordinal), NumberStyles.Integer, CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public override float GetFloat(int ordinal)
        => float.Parse(GetString(ordinal), NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public override double GetDouble(int ordinal)
        => double.Parse(GetString(ordinal), NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public override decimal GetDecimal(int ordinal)
        => decimal.Parse(GetString(ordinal), NumberStyles.Number, CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public override DateTime GetDateTime(int ordinal)
        => DateTime.Parse(GetString(ordinal), CultureInfo.InvariantCulture, DateTimeStyles.None);

    /// <inheritdoc />
    public override Guid GetGuid(int ordinal)
        => Guid.Parse(GetString(ordinal));

    /// <inheritdoc />
    public override char GetChar(int ordinal)
    {
        var value = GetString(ordinal);
        if (value.Length == 0)
            throw new FormatException("Column is empty.");
        return value[0];
    }

    /// <inheritdoc />
    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length)
    {
        var bytes = Encoding.UTF8.GetBytes(GetString(ordinal));
        if (buffer is null)
            return bytes.Length;

        if (dataOffset < 0 || dataOffset > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(dataOffset));

        var offset = (int)dataOffset;
        if (offset >= bytes.Length)
            return 0;

        var count = Math.Min(length, bytes.Length - offset);
        Array.Copy(bytes, offset, buffer, bufferOffset, count);
        return count;
    }

    /// <inheritdoc />
    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length)
    {
        var text = GetString(ordinal);
        if (buffer is null)
            return text.Length;

        if (dataOffset < 0 || dataOffset > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(dataOffset));

        var offset = (int)dataOffset;
        if (offset >= text.Length)
            return 0;

        var count = Math.Min(length, text.Length - offset);
        text.CopyTo(offset, buffer, bufferOffset, count);
        return count;
    }

    /// <inheritdoc />
    public override int GetValues(object[] values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var count = Math.Min(values.Length, FieldCount);
        for (int i = 0; i < count; i++)
        {
            values[i] = GetValue(i);
        }
        return count;
    }

    /// <inheritdoc />
    public override bool IsDBNull(int ordinal)
    {
        EnsureCurrentRow();
        ValidateOrdinal(ordinal);

        var row = reader.Current;
        if (!row.TryGetColumnSpan(ordinal, out var span))
        {
            if (options.AllowMissingColumns)
                return true;

            throw new CsvException(
                CsvErrorCode.ParseError,
                $"Row has only {row.ColumnCount} columns but requested index {ordinal}.",
                row.LineNumber,
                ordinal + 1);
        }

        return IsNullValue(span);
    }

    /// <inheritdoc />
    public override DataTable GetSchemaTable()
    {
        EnsureInitialized();

        var table = new DataTable("SchemaTable");
        table.Columns.Add(SchemaTableColumn.ColumnName, typeof(string));
        table.Columns.Add(SchemaTableColumn.ColumnOrdinal, typeof(int));
        table.Columns.Add(SchemaTableColumn.ColumnSize, typeof(int));
        table.Columns.Add(SchemaTableColumn.NumericPrecision, typeof(short));
        table.Columns.Add(SchemaTableColumn.NumericScale, typeof(short));
        table.Columns.Add(SchemaTableColumn.DataType, typeof(Type));
        table.Columns.Add(SchemaTableColumn.ProviderType, typeof(int));
        table.Columns.Add(SchemaTableColumn.IsLong, typeof(bool));
        table.Columns.Add(SchemaTableColumn.AllowDBNull, typeof(bool));
        table.Columns.Add(SchemaTableOptionalColumn.IsReadOnly, typeof(bool));
        table.Columns.Add(SchemaTableOptionalColumn.IsRowVersion, typeof(bool));
        table.Columns.Add(SchemaTableColumn.IsUnique, typeof(bool));
        table.Columns.Add(SchemaTableColumn.IsKey, typeof(bool));
        table.Columns.Add(SchemaTableOptionalColumn.IsAutoIncrement, typeof(bool));
        table.Columns.Add(SchemaTableOptionalColumn.BaseCatalogName, typeof(string));
        table.Columns.Add(SchemaTableColumn.BaseSchemaName, typeof(string));
        table.Columns.Add(SchemaTableColumn.BaseTableName, typeof(string));
        table.Columns.Add(SchemaTableColumn.BaseColumnName, typeof(string));
        table.Columns.Add(SchemaTableOptionalColumn.AutoIncrementSeed, typeof(long));
        table.Columns.Add(SchemaTableOptionalColumn.AutoIncrementStep, typeof(long));
        table.Columns.Add(SchemaTableOptionalColumn.DefaultValue, typeof(object));
        table.Columns.Add(SchemaTableOptionalColumn.Expression, typeof(string));
        table.Columns.Add(SchemaTableOptionalColumn.ColumnMapping, typeof(MappingType));
        table.Columns.Add(SchemaTableOptionalColumn.BaseTableNamespace, typeof(string));
        table.Columns.Add(SchemaTableOptionalColumn.BaseColumnNamespace, typeof(string));

        for (int i = 0; i < fieldCount; i++)
        {
            var row = table.NewRow();
            row[SchemaTableColumn.ColumnName] = columnNames[i];
            row[SchemaTableColumn.ColumnOrdinal] = i;
            row[SchemaTableColumn.ColumnSize] = -1;
            row[SchemaTableColumn.NumericPrecision] = DBNull.Value;
            row[SchemaTableColumn.NumericScale] = DBNull.Value;
            row[SchemaTableColumn.DataType] = typeof(string);
            row[SchemaTableColumn.ProviderType] = DBNull.Value;
            row[SchemaTableColumn.IsLong] = false;
            row[SchemaTableColumn.AllowDBNull] = true;
            row[SchemaTableOptionalColumn.IsReadOnly] = false;
            row[SchemaTableOptionalColumn.IsRowVersion] = false;
            row[SchemaTableColumn.IsUnique] = false;
            row[SchemaTableColumn.IsKey] = false;
            row[SchemaTableOptionalColumn.IsAutoIncrement] = false;
            row[SchemaTableOptionalColumn.BaseCatalogName] = DBNull.Value;
            row[SchemaTableColumn.BaseSchemaName] = DBNull.Value;
            row[SchemaTableColumn.BaseTableName] = string.Empty;
            row[SchemaTableColumn.BaseColumnName] = columnNames[i];
            row[SchemaTableOptionalColumn.AutoIncrementSeed] = 0L;
            row[SchemaTableOptionalColumn.AutoIncrementStep] = 1L;
            row[SchemaTableOptionalColumn.DefaultValue] = DBNull.Value;
            row[SchemaTableOptionalColumn.Expression] = DBNull.Value;
            row[SchemaTableOptionalColumn.ColumnMapping] = MappingType.Element;
            row[SchemaTableOptionalColumn.BaseTableNamespace] = string.Empty;
            row[SchemaTableOptionalColumn.BaseColumnNamespace] = string.Empty;
            table.Rows.Add(row);
        }

        return table;
    }

    /// <inheritdoc />
    public override IEnumerator GetEnumerator() => new DbEnumerator(this, closeReader: false);

    /// <inheritdoc />
    public override void Close()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (closed)
            return;

        closed = true;
        if (disposing)
        {
            reader.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        base.Dispose(disposing);
    }

    private void EnsureInitialized()
    {
        if (initialized)
            return;

        initialized = true;

        if (options.HasHeaderRow)
        {
            if (!Advance())
            {
                fieldCount = columnNames.Length;
                BuildOrdinalMap();
                return;
            }

            var headerRow = reader.Current;
            fieldCount = headerRow.ColumnCount;

            if (columnNames.Length == 0)
            {
                columnNames = new string[fieldCount];
                for (int i = 0; i < fieldCount; i++)
                {
                    columnNames[i] = headerRow.GetString(i);
                }
            }
            else if (columnNames.Length != fieldCount)
            {
                throw new CsvException(
                    CsvErrorCode.ParseError,
                    $"Header contains {fieldCount} columns but ColumnNames defines {columnNames.Length}.",
                    headerRow.LineNumber,
                    0);
            }
        }
        else if (columnNames.Length > 0)
        {
            fieldCount = columnNames.Length;
        }

        if (fieldCount == 0)
        {
            if (!Advance())
            {
                BuildOrdinalMap();
                return;
            }

            fieldCount = reader.Current.ColumnCount;
            columnNames = columnNames.Length > 0
                ? columnNames
                : CreateDefaultNames(fieldCount);

            hasPendingRow = true;
            hasAnyRow = true;
            BuildOrdinalMap();
            return;
        }

        if (Advance())
        {
            ValidateRow(reader.Current);
            hasPendingRow = true;
            hasAnyRow = true;
        }

        BuildOrdinalMap();
    }

    private void EnsureCurrentRow()
    {
        EnsureNotClosed();

        if (!hasCurrentRow)
            throw new InvalidOperationException("Read must be called before accessing values.");
    }

    private void EnsureNotClosed()
    {
        if (closed)
            throw new InvalidOperationException("The data reader is closed.");
    }

    private bool Advance()
        => reader.MoveNextAsync().AsTask().GetAwaiter().GetResult();

    private void ValidateRow(CsvRow<byte> row)
    {
        if (row.ColumnCount > fieldCount)
        {
            throw new CsvException(
                CsvErrorCode.ParseError,
                $"Row has {row.ColumnCount} columns but schema expects {fieldCount}.",
                row.LineNumber,
                fieldCount + 1);
        }

        if (!options.AllowMissingColumns && row.ColumnCount < fieldCount)
        {
            throw new CsvException(
                CsvErrorCode.ParseError,
                $"Row has {row.ColumnCount} columns but schema expects {fieldCount}.",
                row.LineNumber,
                row.ColumnCount + 1);
        }
    }

    private void ValidateOrdinal(int ordinal)
    {
        if ((uint)ordinal >= (uint)fieldCount)
        {
            throw new IndexOutOfRangeException(
                $"Column index {ordinal} is out of range. Column count is {fieldCount}.");
        }
    }

    private void BuildOrdinalMap()
    {
        if (columnNames.Length == 0)
        {
            ordinals = new Dictionary<string, int>(headerComparer);
            return;
        }

        ordinals = new Dictionary<string, int>(columnNames.Length, headerComparer);
        for (int i = 0; i < columnNames.Length; i++)
        {
            if (!ordinals.ContainsKey(columnNames[i]))
            {
                ordinals[columnNames[i]] = i;
            }
        }
    }

    private static string[] CreateDefaultNames(int count)
    {
        var names = new string[count];
        for (int i = 0; i < count; i++)
        {
            names[i] = $"Column{i + 1}";
        }
        return names;
    }

    private bool IsNullValue(ReadOnlySpan<byte> span)
    {
        if (nullValueBytes is null)
            return false;

        foreach (var nullValue in nullValueBytes)
        {
            if (span.SequenceEqual(nullValue))
                return true;
        }
        return false;
    }

    private static byte[][]? PrepareNullValues(IReadOnlyList<string>? nullValues)
    {
        if (nullValues is not { Count: > 0 })
            return null;

        var result = new byte[nullValues.Count][];
        for (int i = 0; i < nullValues.Count; i++)
        {
            result[i] = Encoding.UTF8.GetBytes(nullValues[i]);
        }
        return result;
    }
}
