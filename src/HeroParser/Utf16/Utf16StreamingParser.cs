using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace HeroParser.Utf16;

internal static class Utf16StreamingParser
{
    public static Utf16RowParseResult ParseRow(
        ReadOnlySpan<char> data,
        char delimiter,
        char quote,
        Span<int> columnStarts,
        Span<int> columnLengths,
        int maxColumns)
    {
        if (data.IsEmpty)
            return new Utf16RowParseResult(0, 0, 0);

        ref readonly char dataRef = ref MemoryMarshal.GetReference(data);
        ref char mutableRef = ref Unsafe.AsRef(in dataRef);
        ref ushort ushortRef = ref Unsafe.As<char, ushort>(ref mutableRef);

        int position = 0;
        bool inQuotes = false;
        bool skipNextQuote = false;
        int columnCount = 0;
        int currentStart = 0;
        int rowLength = 0;
        int charsConsumed = 0;
        bool rowEnded = false;

        if (Vector256.IsHardwareAccelerated)
        {
            var delimiterVec = Vector256.Create((ushort)delimiter);
            var quoteVec = Vector256.Create((ushort)quote);
            var lfVec = Vector256.Create((ushort)'\n');
            var crVec = Vector256.Create((ushort)'\r');

            while (position + Vector256<ushort>.Count <= data.Length)
            {
                var chunk = Vector256.LoadUnsafe(ref Unsafe.Add(ref ushortRef, position));
                var specials = Vector256.BitwiseOr(
                    Vector256.BitwiseOr(Vector256.Equals(chunk, delimiterVec), Vector256.Equals(chunk, quoteVec)),
                    Vector256.BitwiseOr(Vector256.Equals(chunk, lfVec), Vector256.Equals(chunk, crVec)));

                uint mask = specials.ExtractMostSignificantBits();

                while (mask != 0)
                {
                    int bit = BitOperations.TrailingZeroCount(mask);
                    mask &= mask - 1;
                    int absolute = position + bit;
                    char c = Unsafe.Add(ref mutableRef, absolute);

                    if (c == quote)
                    {
                        if (skipNextQuote)
                        {
                            skipNextQuote = false;
                            continue;
                        }

                        if (inQuotes && absolute + 1 < data.Length && Unsafe.Add(ref mutableRef, absolute + 1) == quote)
                        {
                            skipNextQuote = true;
                            continue;
                        }

                        inQuotes = !inQuotes;
                        continue;
                    }

                    if (inQuotes)
                        continue;

                    if (c == delimiter)
                    {
                        AppendColumn(absolute, ref columnCount, ref currentStart,
                            columnStarts, columnLengths, maxColumns);
                        continue;
                    }

                    if (c == '\n' || c == '\r')
                    {
                        rowLength = absolute;
                        charsConsumed = absolute + 1;
                        if (c == '\r' && absolute + 1 < data.Length && Unsafe.Add(ref mutableRef, absolute + 1) == '\n')
                            charsConsumed++;
                        rowEnded = true;
                        goto ScalarTail;
                    }
                }

                position += Vector256<ushort>.Count;
            }
        }

ScalarTail:
        for (int i = position; i < data.Length && !rowEnded; i++)
        {
            char c = Unsafe.Add(ref mutableRef, i);

            if (c == quote)
            {
                if (skipNextQuote)
                {
                    skipNextQuote = false;
                    continue;
                }

                if (inQuotes && i + 1 < data.Length && Unsafe.Add(ref mutableRef, i + 1) == quote)
                {
                    skipNextQuote = true;
                    continue;
                }

                inQuotes = !inQuotes;
                continue;
            }

            if (inQuotes)
                continue;

            if (c == delimiter)
            {
                AppendColumn(i, ref columnCount, ref currentStart,
                    columnStarts, columnLengths, maxColumns);
            }
            else if (c == '\n' || c == '\r')
            {
                rowLength = i;
                charsConsumed = i + 1;
                if (c == '\r' && i + 1 < data.Length && Unsafe.Add(ref mutableRef, i + 1) == '\n')
                    charsConsumed++;
                rowEnded = true;
                break;
            }
        }

        if (!rowEnded)
        {
            rowLength = data.Length;
            charsConsumed = rowLength;
        }

        AppendFinalColumn(rowLength, ref columnCount, ref currentStart,
            columnStarts, columnLengths, maxColumns);

        return new Utf16RowParseResult(columnCount, rowLength, charsConsumed);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AppendColumn(
        int delimiterIndex,
        ref int columnCount,
        ref int currentStart,
        Span<int> starts,
        Span<int> lengths,
        int maxColumns)
    {
        if (columnCount >= maxColumns)
            ThrowTooManyColumns(maxColumns);

        starts[columnCount] = currentStart;
        lengths[columnCount] = delimiterIndex - currentStart;
        columnCount++;
        currentStart = delimiterIndex + 1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AppendFinalColumn(
        int rowLength,
        ref int columnCount,
        ref int currentStart,
        Span<int> starts,
        Span<int> lengths,
        int maxColumns)
    {
        if (columnCount >= maxColumns)
            ThrowTooManyColumns(maxColumns);

        starts[columnCount] = currentStart;
        lengths[columnCount] = rowLength - currentStart;
        columnCount++;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowTooManyColumns(int maxColumns)
    {
        throw new CsvException(
            CsvErrorCode.TooManyColumns,
            $"Row has more than {maxColumns} columns");
    }
}
