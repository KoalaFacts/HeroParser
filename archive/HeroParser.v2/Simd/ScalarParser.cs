using System.Runtime.CompilerServices;

namespace HeroParser.Simd;

/// <summary>
/// Baseline scalar CSV parser - no SIMD optimizations.
/// Used as fallback on unsupported hardware and as correctness baseline.
/// </summary>
internal sealed class ScalarParser : ISimdParser
{
    public static readonly ScalarParser Instance = new();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int ParseColumns(
        ReadOnlySpan<char> line,
        char delimiter,
        Span<int> columnStarts,
        Span<int> columnLengths)
    {
        if (line.IsEmpty)
        {
            return 0;
        }

        int columnCount = 0;
        int currentStart = 0;

        // Simple delimiter scan - one character at a time
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == delimiter)
            {
                // Found delimiter - record column
                if (columnCount < columnStarts.Length)
                {
                    columnStarts[columnCount] = currentStart;
                    columnLengths[columnCount] = i - currentStart;
                    columnCount++;
                }

                currentStart = i + 1; // Next column starts after delimiter
            }
        }

        // Last column (after last delimiter or entire line if no delimiters)
        if (columnCount < columnStarts.Length)
        {
            columnStarts[columnCount] = currentStart;
            columnLengths[columnCount] = line.Length - currentStart;
            columnCount++;
        }

        return columnCount;
    }
}
