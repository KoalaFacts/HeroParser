using HeroParser.SeparatedValues.Core;

namespace HeroParser.SeparatedValues.Reading.Rows;

/// <summary>
/// Extension methods for <see cref="CsvRow{T}"/> to enable named field access.
/// </summary>
public static class ExtensionsToCsvRow
{
    /// <summary>
    /// Gets a column by name using a header index (char-based).
    /// </summary>
    /// <param name="row">The CSV row.</param>
    /// <param name="columnName">The name of the column to retrieve.</param>
    /// <param name="headers">The header index mapping column names to indices.</param>
    /// <returns>The column value.</returns>
    /// <exception cref="KeyNotFoundException">Thrown when the column name is not found in the header index.</exception>
    public static CsvColumn<char> GetField(this CsvRow<char> row, string columnName, CsvHeaderIndex headers)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(columnName);

        int index = headers[columnName];
        return row[index];
    }

    /// <summary>
    /// Tries to get a column by name using a header index (char-based).
    /// </summary>
    /// <param name="row">The CSV row.</param>
    /// <param name="columnName">The name of the column to retrieve.</param>
    /// <param name="headers">The header index mapping column names to indices.</param>
    /// <param name="column">When successful, contains the column value.</param>
    /// <returns>True if the column was found and retrieved; otherwise, false.</returns>
    public static bool TryGetField(this CsvRow<char> row, string columnName, CsvHeaderIndex headers, out CsvColumn<char> column)
    {
        ArgumentNullException.ThrowIfNull(headers);

        if (columnName is null || !headers.TryGetIndex(columnName, out int index) || index >= row.ColumnCount)
        {
            column = default;
            return false;
        }

        column = row[index];
        return true;
    }

    /// <summary>
    /// Gets a column by name using a header index (byte-based).
    /// </summary>
    /// <param name="row">The CSV row.</param>
    /// <param name="columnName">The name of the column to retrieve.</param>
    /// <param name="headers">The header index mapping column names to indices.</param>
    /// <returns>The column value.</returns>
    /// <exception cref="KeyNotFoundException">Thrown when the column name is not found in the header index.</exception>
    public static CsvColumn<byte> GetField(this CsvRow<byte> row, string columnName, CsvHeaderIndex headers)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(columnName);

        int index = headers[columnName];
        return row[index];
    }

    /// <summary>
    /// Tries to get a column by name using a header index (byte-based).
    /// </summary>
    /// <param name="row">The CSV row.</param>
    /// <param name="columnName">The name of the column to retrieve.</param>
    /// <param name="headers">The header index mapping column names to indices.</param>
    /// <param name="column">When successful, contains the column value.</param>
    /// <returns>True if the column was found and retrieved; otherwise, false.</returns>
    public static bool TryGetField(this CsvRow<byte> row, string columnName, CsvHeaderIndex headers, out CsvColumn<byte> column)
    {
        ArgumentNullException.ThrowIfNull(headers);

        if (columnName is null || !headers.TryGetIndex(columnName, out int index) || index >= row.ColumnCount)
        {
            column = default;
            return false;
        }

        column = row[index];
        return true;
    }
}
