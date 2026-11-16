using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

namespace HeroParser;

/// <summary>
/// Base <see cref="DbDataReader"/> implementation shared by HeroParser's tabular data readers
/// (CSV, fixed-width, and Excel).
/// </summary>
public abstract class TabularDataReader : DbDataReader
{
    private protected string[] columnNames = [];
    private protected Dictionary<string, int>? ordinals;
    private protected int fieldCount;
    private protected bool initialized;
    private protected bool closed;
    private protected bool hasPendingRow;
    private protected bool hasCurrentRow;
    private protected bool hasAnyRow;

    /// <summary>Initializes a new instance of the <see cref="TabularDataReader"/> class.</summary>
    private protected TabularDataReader()
    {
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
    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)]
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
        var bytes = ByteEncoding.GetBytes(GetString(ordinal));
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
            row[SchemaTableColumn.ColumnSize] = GetSchemaColumnSize(i);
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

    /// <summary>Ensures header metadata and initial state have been loaded.</summary>
    private protected abstract void EnsureInitialized();

    /// <summary>Gets the encoding used by <see cref="GetBytes"/>.</summary>
    private protected virtual Encoding ByteEncoding => Encoding.UTF8;

    /// <summary>Gets the schema column size reported by <see cref="GetSchemaTable"/>.</summary>
    private protected virtual int GetSchemaColumnSize(int ordinal) => -1;

    /// <summary>Validates that the reader is open and positioned on a current row.</summary>
    private protected void EnsureCurrentRow()
    {
        EnsureNotClosed();

        if (!hasCurrentRow)
            throw new InvalidOperationException("Read must be called before accessing values.");
    }

    /// <summary>Validates that the reader has not been closed.</summary>
    private protected void EnsureNotClosed()
    {
        if (closed)
            throw new InvalidOperationException("The data reader is closed.");
    }

    /// <summary>Validates that a column ordinal is within bounds.</summary>
    private protected void ValidateOrdinal(int ordinal)
    {
        if ((uint)ordinal >= (uint)fieldCount)
        {
            throw new IndexOutOfRangeException(
                $"Column index {ordinal} is out of range. Column count is {fieldCount}.");
        }
    }

    /// <summary>Builds the column-name-to-ordinal lookup map.</summary>
    private protected void BuildOrdinalMap(StringComparer comparer)
    {
        if (columnNames.Length == 0)
        {
            ordinals = new Dictionary<string, int>(comparer);
            return;
        }

        ordinals = new Dictionary<string, int>(columnNames.Length, comparer);
        for (int i = 0; i < columnNames.Length; i++)
        {
            if (!ordinals.ContainsKey(columnNames[i]))
            {
                ordinals[columnNames[i]] = i;
            }
        }
    }

    /// <summary>Creates default column names (<c>Column1</c>, <c>Column2</c>, ...).</summary>
    private protected static string[] CreateDefaultNames(int count)
    {
        var names = new string[count];
        for (int i = 0; i < count; i++)
        {
            names[i] = $"Column{i + 1}";
        }
        return names;
    }
}
