using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text;
using HeroParser.FixedWidths;
using HeroParser.FixedWidths.Streaming;

namespace HeroParser.FixedWidths.Reading.Data;

/// <summary>
/// Streams fixed-width data through <see cref="IDataReader"/> for database bulk loading.
/// </summary>
public sealed class FixedWidthDataReader : DbDataReader
{
    private readonly FixedWidthAsyncStreamReader reader;
    private readonly FixedWidthParserOptions parserOptions;
    private readonly FixedWidthDataReaderOptions options;
    private readonly string[]? nullValues;
    private readonly StringComparer headerComparer;
    private readonly int[] columnStarts;
    private readonly int[] columnLengths;
    private readonly char[] columnPadChars;
    private readonly FieldAlignment[] columnAlignments;
    private readonly int requiredRecordLength;

    private readonly string[] columnNames;
    private Dictionary<string, int>? ordinals;
    private readonly int fieldCount;
    private bool initialized;
    private bool closed;
    private bool hasPendingRow;
    private bool hasCurrentRow;
    private bool hasAnyRow;

    internal FixedWidthDataReader(
        FixedWidthAsyncStreamReader reader,
        FixedWidthParserOptions parserOptions,
        FixedWidthDataReaderOptions options)
    {
        this.reader = reader ?? throw new ArgumentNullException(nameof(reader));
        this.parserOptions = parserOptions ?? FixedWidthParserOptions.Default;
        this.options = options ?? FixedWidthDataReaderOptions.Default;
        headerComparer = this.options.HeaderComparer;
        nullValues = PrepareNullValues(this.options.NullValues);

        if (this.options.Columns is not { Count: > 0 })
        {
            throw new FixedWidthException(
                FixedWidthErrorCode.InvalidOptions,
                "Fixed-width data reader requires at least one column definition.");
        }

        var columnCount = this.options.Columns.Count;
        columnNames = new string[columnCount];
        columnStarts = new int[columnCount];
        columnLengths = new int[columnCount];
        columnPadChars = new char[columnCount];
        columnAlignments = new FieldAlignment[columnCount];

        var maxEnd = 0;
        for (int i = 0; i < columnCount; i++)
        {
            var column = this.options.Columns[i]
                ?? throw new FixedWidthException(
                    FixedWidthErrorCode.InvalidOptions,
                    "Fixed-width column definitions cannot be null.");

            if (column.Start < 0)
            {
                throw new FixedWidthException(
                    FixedWidthErrorCode.InvalidOptions,
                    $"Column start must be non-negative. Value: {column.Start}.");
            }

            if (column.Length <= 0)
            {
                throw new FixedWidthException(
                    FixedWidthErrorCode.InvalidOptions,
                    $"Column length must be positive. Value: {column.Length}.");
            }

            var end = (long)column.Start + column.Length;
            if (end > int.MaxValue)
            {
                throw new FixedWidthException(
                    FixedWidthErrorCode.InvalidOptions,
                    "Column start/length overflowed the allowed range.");
            }

            if (end > maxEnd)
                maxEnd = (int)end;

            columnStarts[i] = column.Start;
            columnLengths[i] = column.Length;
            columnPadChars[i] = column.PadChar ?? this.parserOptions.DefaultPadChar;
            columnAlignments[i] = column.Alignment ?? this.parserOptions.DefaultAlignment;

            var name = column.Name;
            columnNames[i] = string.IsNullOrWhiteSpace(name) ? string.Empty : name;
        }

        requiredRecordLength = maxEnd;
        fieldCount = columnCount;
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
        if (!TryGetColumnSpan(row, ordinal, out var span))
            return DBNull.Value;

        if (IsNullValue(span))
            return DBNull.Value;

        return new string(span);
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
        if (!TryGetColumnSpan(row, ordinal, out var span))
            return true;

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
            row[SchemaTableColumn.ColumnSize] = columnLengths[i];
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
                FillDefaultNames();
                BuildOrdinalMap();
                return;
            }

            var headerRow = reader.Current;
            for (int i = 0; i < fieldCount; i++)
            {
                if (!string.IsNullOrEmpty(columnNames[i]))
                    continue;

                columnNames[i] = TryGetColumnSpan(headerRow, i, out var span) && !span.IsEmpty
                    ? new string(span)
                    : $"Column{i + 1}";
            }
        }
        else
        {
            FillDefaultNames();
        }

        if (Advance())
        {
            ValidateRow(reader.Current);
            hasPendingRow = true;
            hasAnyRow = true;
        }

        BuildOrdinalMap();
    }

    private void FillDefaultNames()
    {
        for (int i = 0; i < columnNames.Length; i++)
        {
            if (string.IsNullOrEmpty(columnNames[i]))
            {
                columnNames[i] = $"Column{i + 1}";
            }
        }
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

    private void ValidateRow(FixedWidthCharSpanRow row)
    {
        if (options.AllowMissingColumns || parserOptions.AllowShortRows)
            return;

        if (row.RawRecord.Length < requiredRecordLength)
        {
            throw new FixedWidthException(
                FixedWidthErrorCode.FieldOutOfBounds,
                $"Row has length {row.RawRecord.Length} but schema expects at least {requiredRecordLength} characters.",
                row.RecordNumber,
                row.SourceLineNumber);
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
        ordinals = new Dictionary<string, int>(fieldCount, headerComparer);
        for (int i = 0; i < columnNames.Length; i++)
        {
            if (!ordinals.ContainsKey(columnNames[i]))
            {
                ordinals[columnNames[i]] = i;
            }
        }
    }

    private bool TryGetColumnSpan(FixedWidthCharSpanRow row, int ordinal, out ReadOnlySpan<char> span)
    {
        var start = columnStarts[ordinal];
        var length = columnLengths[ordinal];
        var record = row.RawRecord;
        var end = start + length;

        if (end > record.Length)
        {
            if (options.AllowMissingColumns)
            {
                span = default;
                return false;
            }

            if (parserOptions.AllowShortRows)
            {
                if (start >= record.Length)
                {
                    span = [];
                    return true;
                }

                span = record[start..];
                span = ApplyTrim(span, columnPadChars[ordinal], columnAlignments[ordinal]);
                return true;
            }

            span = GetColumnSpan(row, ordinal);
            return true;
        }

        span = GetColumnSpan(row, ordinal);
        return true;
    }

    private ReadOnlySpan<char> GetColumnSpan(FixedWidthCharSpanRow row, int ordinal)
        => row.GetField(
            columnStarts[ordinal],
            columnLengths[ordinal],
            columnPadChars[ordinal],
            columnAlignments[ordinal]).CharSpan;

    private static ReadOnlySpan<char> ApplyTrim(ReadOnlySpan<char> span, char padChar, FieldAlignment alignment)
        => alignment switch
        {
            FieldAlignment.Left => span.TrimEnd(padChar),
            FieldAlignment.Right => span.TrimStart(padChar),
            FieldAlignment.None => span,
            _ => span
        };

    private bool IsNullValue(ReadOnlySpan<char> span)
    {
        if (nullValues is null)
            return false;

        foreach (var nullValue in nullValues)
        {
            if (span.SequenceEqual(nullValue.AsSpan()))
                return true;
        }
        return false;
    }

    private static string[]? PrepareNullValues(IReadOnlyList<string>? nullValues)
    {
        if (nullValues is not { Count: > 0 })
            return null;

        return [.. nullValues];
    }
}
