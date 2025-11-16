using System;
using System.Runtime.CompilerServices;

namespace HeroParser.Simd;

/// <summary>
/// Baseline scalar CSV parser with RFC 4180 quote handling.
/// Used as fallback on unsupported hardware and as correctness baseline.
/// Handles quoted fields, escaped quotes (""), and delimiters within quotes.
/// </summary>
internal sealed class ScalarParser : ISimdParser
{
    public static readonly ScalarParser Instance = new();

    private ScalarParser() { }

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
