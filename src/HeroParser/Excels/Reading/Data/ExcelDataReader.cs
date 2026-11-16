using System.Data;
using HeroParser.Excels.Core;
using HeroParser.Excels.Xlsx;

namespace HeroParser.Excels.Reading.Data;

/// <summary>
/// Streams Excel worksheet data through <see cref="IDataReader"/> for database bulk loading.
/// All values are exposed as strings.
/// </summary>
public sealed class ExcelDataReader : TabularDataReader
{
    private readonly XlsxReader xlsxReader;
    private readonly XlsxSheetReader sheetReader;
    private readonly bool hasHeaderRow;
    private readonly int skipRows;
    private readonly bool caseSensitiveHeaders;
    private readonly bool allowMissingColumns;
    private readonly HashSet<string>? nullValues;
    private readonly IReadOnlyList<string>? columnNameOverrides;

    private string[]? currentRow;

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

    internal ExcelDataReader(
        XlsxReader xlsxReader,
        XlsxSheetReader sheetReader,
        ExcelDataReaderOptions options)
    {
        this.xlsxReader = xlsxReader;
        this.sheetReader = sheetReader;
        hasHeaderRow = options.HasHeaderRow;
        skipRows = options.SkipRows;
        caseSensitiveHeaders = options.CaseSensitiveHeaders;
        allowMissingColumns = options.AllowMissingColumns;
        nullValues = options.NullValues is { Count: > 0 }
            ? new HashSet<string>(options.NullValues, StringComparer.Ordinal)
            : null;
        columnNameOverrides = options.ColumnNames;
    }

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
    public override object GetValue(int ordinal)
    {
        EnsureCurrentRow();
        ValidateOrdinal(ordinal);

        if (currentRow is null || ordinal >= currentRow.Length)
        {
            if (!allowMissingColumns && currentRow is not null && ordinal >= currentRow.Length)
                throw new ExcelException($"Row has only {currentRow.Length} columns but requested index {ordinal}.");
            return DBNull.Value;
        }

        var value = currentRow[ordinal];
        if (string.IsNullOrEmpty(value))
            return DBNull.Value;

        if (nullValues is not null && nullValues.Contains(value))
            return DBNull.Value;

        return value;
    }

    /// <inheritdoc />
    public override bool IsDBNull(int ordinal)
    {
        EnsureCurrentRow();
        ValidateOrdinal(ordinal);

        if (currentRow is null || ordinal >= currentRow.Length)
        {
            if (!allowMissingColumns && currentRow is not null && ordinal >= currentRow.Length)
                throw new ExcelException($"Row has only {currentRow.Length} columns but requested index {ordinal}.");
            return true;
        }

        var value = currentRow[ordinal];
        if (string.IsNullOrEmpty(value))
            return true;

        if (nullValues is not null && nullValues.Contains(value))
            return true;

        return false;
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

    private protected override void EnsureInitialized()
    {
        if (initialized)
            return;

        initialized = true;
        var headerComparer = caseSensitiveHeaders ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

        // Skip configured rows
        for (int i = 0; i < skipRows; i++)
        {
            if (sheetReader.ReadNextRow() is null)
            {
                fieldCount = 0;
                BuildOrdinalMap(headerComparer);
                return;
            }
        }

        if (hasHeaderRow)
        {
            var headerRow = sheetReader.ReadNextRow();
            if (headerRow is null)
            {
                fieldCount = columnNames.Length;
                BuildOrdinalMap(headerComparer);
                return;
            }

            fieldCount = headerRow.Length;
            columnNames = new string[fieldCount];
            for (int i = 0; i < fieldCount; i++)
            {
                columnNames[i] = headerRow[i];
            }
        }

        // Apply column name overrides if provided
        if (columnNameOverrides is { Count: > 0 })
        {
            if (fieldCount == 0)
                fieldCount = columnNameOverrides.Count;

            var overrideNames = new string[fieldCount];
            for (int i = 0; i < fieldCount; i++)
            {
                overrideNames[i] = i < columnNameOverrides.Count ? columnNameOverrides[i] : columnNames[i];
            }
            columnNames = overrideNames;
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

        BuildOrdinalMap(headerComparer);
    }
}
