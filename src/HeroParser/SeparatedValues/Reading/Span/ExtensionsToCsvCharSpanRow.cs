using HeroParser.SeparatedValues.Core;

namespace HeroParser.SeparatedValues.Reading.Span;

/// <summary>
/// Extension methods for <see cref="CsvCharSpanRow"/> to enable named field access.
/// </summary>
public static class ExtensionsToCsvCharSpanRow
{
    /// <summary>
    /// Gets a column by name using a header index.
    /// </summary>
    /// <param name="row">The CSV row.</param>
    /// <param name="columnName">The name of the column to retrieve.</param>
    /// <param name="headers">The header index mapping column names to indices.</param>
    /// <returns>The column value.</returns>
    /// <exception cref="KeyNotFoundException">Thrown when the column name is not found in the header index.</exception>
    public static CsvCharSpanColumn GetField(this CsvCharSpanRow row, string columnName, CsvHeaderIndex headers)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(columnName);

        int index = headers[columnName];
        return row[index];
    }

    /// <summary>
    /// Tries to get a column by name using a header index.
    /// </summary>
    /// <param name="row">The CSV row.</param>
    /// <param name="columnName">The name of the column to retrieve.</param>
    /// <param name="headers">The header index mapping column names to indices.</param>
    /// <param name="column">When successful, contains the column value.</param>
    /// <returns>True if the column was found and retrieved; otherwise, false.</returns>
    public static bool TryGetField(this CsvCharSpanRow row, string columnName, CsvHeaderIndex headers, out CsvCharSpanColumn column)
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

/// <summary>
/// Extension methods for <see cref="CsvByteSpanRow"/> to enable named field access.
/// </summary>
#pragma warning disable IDE0130 // Namespace does not match folder structure
public static class ExtensionsToCsvByteSpanRow
#pragma warning restore IDE0130
{
    /// <summary>
    /// Gets a column by name using a header index.
    /// </summary>
    /// <param name="row">The CSV row.</param>
    /// <param name="columnName">The name of the column to retrieve.</param>
    /// <param name="headers">The header index mapping column names to indices.</param>
    /// <returns>The column value.</returns>
    /// <exception cref="KeyNotFoundException">Thrown when the column name is not found in the header index.</exception>
    public static CsvByteSpanColumn GetField(this CsvByteSpanRow row, string columnName, CsvHeaderIndex headers)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(columnName);

        int index = headers[columnName];
        return row[index];
    }

    /// <summary>
    /// Tries to get a column by name using a header index.
    /// </summary>
    /// <param name="row">The CSV row.</param>
    /// <param name="columnName">The name of the column to retrieve.</param>
    /// <param name="headers">The header index mapping column names to indices.</param>
    /// <param name="column">When successful, contains the column value.</param>
    /// <returns>True if the column was found and retrieved; otherwise, false.</returns>
    public static bool TryGetField(this CsvByteSpanRow row, string columnName, CsvHeaderIndex headers, out CsvByteSpanColumn column)
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
