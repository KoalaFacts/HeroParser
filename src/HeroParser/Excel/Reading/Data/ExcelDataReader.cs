using System.Collections;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text;
using HeroParser.Excels.Core;
using HeroParser.Excels.Xlsx;

namespace HeroParser.Excels.Reading.Data;

/// <summary>
/// Streams Excel worksheet data through <see cref="IDataReader"/> for database bulk loading.
/// All values are exposed as strings.
/// </summary>
public sealed class ExcelDataReader : DbDataReader
{
    private readonly XlsxReader xlsxReader;
    private readonly XlsxSheetReader sheetReader;
    private readonly bool hasHeaderRow;
    private readonly int skipRows;

    private string[] columnNames = [];
    private Dictionary<string, int>? ordinals;
    private int fieldCount;
    private string[]? currentRow;
    private bool initialized;
    private bool closed;
    private bool hasCurrentRow;
    private bool hasAnyRow;
    private bool hasPendingRow;

    internal ExcelDataReader(
        XlsxReader xlsxReader,
        XlsxSheetReader sheetReader,
        bool hasHeaderRow = true,
        int skipRows = 0)
    {
        this.xlsxReader = xlsxReader;
        this.sheetReader = sheetReader;
        this.hasHeaderRow = hasHeaderRow;
        this.skipRows = skipRows;
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

        var row = sheetReader.ReadNextRow();
        if (row is null)
        {
            hasCurrentRow = false;
            currentRow = null;
            return false;
        }

        currentRow = row;
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
            return ordinal;

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

        if (currentRow is null || ordinal >= currentRow.Length)
            return DBNull.Value;

        var value = currentRow[ordinal];
        if (string.IsNullOrEmpty(value))
            return DBNull.Value;

        return value;
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

        if (currentRow is null || ordinal >= currentRow.Length)
            return true;

        return string.IsNullOrEmpty(currentRow[ordinal]);
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
            sheetReader.Dispose();
            xlsxReader.Dispose();
        }

        base.Dispose(disposing);
    }

    private void EnsureInitialized()
    {
        if (initialized)
            return;

        initialized = true;

        // Skip configured rows
        for (int i = 0; i < skipRows; i++)
        {
            if (sheetReader.ReadNextRow() is null)
            {
                fieldCount = 0;
                BuildOrdinalMap();
                return;
            }
        }

        if (hasHeaderRow)
        {
            var headerRow = sheetReader.ReadNextRow();
            if (headerRow is null)
            {
                fieldCount = columnNames.Length;
                BuildOrdinalMap();
                return;
            }

            fieldCount = headerRow.Length;
            columnNames = new string[fieldCount];
            for (int i = 0; i < fieldCount; i++)
            {
                columnNames[i] = headerRow[i];
            }
        }

        // Peek at first data row to detect HasRows
        var firstRow = sheetReader.ReadNextRow();
        if (firstRow is not null)
        {
            currentRow = firstRow;
            hasPendingRow = true;
            hasAnyRow = true;

            // If no header, derive fieldCount from first row
            if (!hasHeaderRow && fieldCount == 0)
            {
                fieldCount = firstRow.Length;
                columnNames = CreateDefaultNames(fieldCount);
            }
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
            ordinals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            return;
        }

        ordinals = new Dictionary<string, int>(columnNames.Length, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < columnNames.Length; i++)
        {
            if (!ordinals.ContainsKey(columnNames[i]))
                ordinals[columnNames[i]] = i;
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
}
