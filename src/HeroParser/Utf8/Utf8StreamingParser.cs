using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace HeroParser.Utf8;

/// <summary>
/// Streaming UTF-8 parser that emits column metadata in a single pass.
/// </summary>
internal static class Utf8StreamingParser
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Utf8RowParseResult ParseRow(
        ReadOnlySpan<byte> data,
        byte delimiter,
        byte quote,
        Span<int> columnStarts,
        Span<int> columnLengths,
        int maxColumns)
    {
        if (data.IsEmpty)
            return new Utf8RowParseResult(0, 0, 0);

        ref readonly byte dataRef = ref MemoryMarshal.GetReference(data);
        ref byte mutableRef = ref Unsafe.AsRef(in dataRef);

        int position = 0;
        bool inQuotes = false;
        bool skipNextQuote = false;
        int columnCount = 0;
        int currentStart = 0;
        int rowLength = 0;
        int charsConsumed = 0;
        bool rowEnded = false;

        if (Avx2.IsSupported)
        {
            var delimiterVec = Vector256.Create(delimiter);
            var quoteVec = Vector256.Create(quote);
            var lfVec = Vector256.Create((byte)'\n');
            var crVec = Vector256.Create((byte)'\r');

            while (position + Vector256<byte>.Count <= data.Length)
            {
                var chunk = Vector256.LoadUnsafe(ref Unsafe.Add(ref mutableRef, position));
                var specials = Avx2.Or(
                    Avx2.Or(Avx2.CompareEqual(chunk, delimiterVec), Avx2.CompareEqual(chunk, quoteVec)),
                    Avx2.Or(Avx2.CompareEqual(chunk, lfVec), Avx2.CompareEqual(chunk, crVec)));

                uint mask = (uint)Avx2.MoveMask(specials);

                while (mask != 0)
                {
                    int bit = BitOperations.TrailingZeroCount(mask);
                    mask &= mask - 1;
                    int absolute = position + bit;
                    byte c = Unsafe.Add(ref mutableRef, absolute);

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
                        if (c == '\r' && absolute + 1 < data.Length && Unsafe.Add(ref mutableRef, absolute + 1) == (byte)'\n')
                            charsConsumed++;
                        rowEnded = true;
                        goto ScalarTail;
                    }
                }

                position += Vector256<byte>.Count;
            }
        }

ScalarTail:
        for (int i = position; i < data.Length && !rowEnded; i++)
        {
            byte c = Unsafe.Add(ref mutableRef, i);

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
                if (c == '\r' && i + 1 < data.Length && Unsafe.Add(ref mutableRef, i + 1) == (byte)'\n')
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

        return new Utf8RowParseResult(columnCount, rowLength, charsConsumed);
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
