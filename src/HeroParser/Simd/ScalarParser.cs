namespace HeroParser.Simd;

/// <summary>
/// Baseline scalar CSV parser with RFC 4180 quote handling.
/// Used as fallback on unsupported hardware, as correctness baseline,
/// and for hybrid optimization on short rows to avoid SIMD overhead.
/// Handles quoted fields, escaped quotes (""), and delimiters within quotes.
/// </summary>
public sealed class ScalarParser : ISimdParser
{
    /// <summary>
    /// Singleton instance of the scalar parser for reuse.
    /// </summary>
    public static readonly ScalarParser Instance = new();

    private ScalarParser() { }

    /// <summary>
    /// Parse columns from a CSV line with RFC 4180 quote handling.
    /// </summary>
    /// <param name="line">The CSV line to parse</param>
    /// <param name="delimiter">Field delimiter character</param>
    /// <param name="quote">Quote character for RFC 4180 compliance</param>
    /// <param name="columnStarts">Output: starting positions of each column</param>
    /// <param name="columnLengths">Output: lengths of each column</param>
    /// <param name="maxColumns">Maximum allowed columns</param>
    /// <returns>Number of columns parsed</returns>
    public int ParseColumns(
        ReadOnlySpan<char> line,
        char delimiter,
        char quote,
        Span<int> columnStarts,
        Span<int> columnLengths,
        int maxColumns)
    {
        if (line.IsEmpty)
        {
            return 0;
        }

        int columnCount = 0;
        int currentStart = 0;
        bool inQuotes = false;
        int i = 0;

        while (i < line.Length)
        {
            char c = line[i];

            if (c == quote)
            {
                if (inQuotes)
                {
                    // Inside quotes - check if this is an escaped quote
                    if (i + 1 < line.Length && line[i + 1] == quote)
                    {
                        // Escaped quote ("") - skip both quotes and continue
                        i += 2;
                        continue;
                    }
                    else
                    {
                        // Closing quote - exit quoted mode
                        inQuotes = false;
                        i++;
                        continue;
                    }
                }
                else
                {
                    // Opening quote - enter quoted mode
                    inQuotes = true;
                    i++;
                    continue;
                }
            }

            if (!inQuotes && c == delimiter)
            {
                // Found delimiter outside quotes - record column
                if (columnCount >= maxColumns)
                {
                    throw new CsvException(
                        CsvErrorCode.TooManyColumns,
                        $"Row has more than {maxColumns} columns");
                }

                columnStarts[columnCount] = currentStart;
                columnLengths[columnCount] = i - currentStart;
                columnCount++;

                currentStart = i + 1; // Next column starts after delimiter
            }

            i++;
        }

        // Last column (after last delimiter or entire line if no delimiters)
        if (columnCount >= maxColumns)
        {
            throw new CsvException(
                CsvErrorCode.TooManyColumns,
                $"Row has more than {maxColumns} columns");
        }

        columnStarts[columnCount] = currentStart;
        columnLengths[columnCount] = line.Length - currentStart;
        columnCount++;

        return columnCount;
    }
}
