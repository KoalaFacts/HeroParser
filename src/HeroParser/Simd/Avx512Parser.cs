using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace HeroParser.Simd;

/// <summary>
/// Ultra-high-performance AVX-512 CSV parser with RFC 4180 quote handling.
/// Processes 64 characters per iteration using 512-bit SIMD registers.
/// Uses bitmask technique inspired by Sep library for quote-aware parsing.
/// Uses safe MemoryMarshal APIs - NO unsafe code.
/// </summary>
internal sealed class Avx512Parser : ISimdParser
{
    public static readonly Avx512Parser Instance = new();

    private const int CharsPerIteration = 64; // Process 64 chars at once

    private Avx512Parser() { }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public int ParseColumns(
        ReadOnlySpan<char> line,
        char delimiter,
        char quote,
        Span<int> columnStarts,
        Span<int> columnLengths,
        int maxColumns)
    {
        if (line.IsEmpty)
            return 0;

        int columnCount = 0;
        int currentStart = 0;
        int position = 0;
        int quoteCount = 0; // Track quote parity: odd = inside quotes, even = outside quotes

        // Get reference to start of span for safe SIMD access
        ref readonly char lineStart = ref MemoryMarshal.GetReference(line);

        // Process 64-char chunks with AVX-512
        while (position + CharsPerIteration <= line.Length)
        {
            // Safe load using MemoryMarshal + Unsafe - no unsafe keyword!
            ref readonly char pos0 = ref Unsafe.Add(ref Unsafe.AsRef(in lineStart), position);
            ref readonly char pos32 = ref Unsafe.Add(ref Unsafe.AsRef(in lineStart), position + 32);

            // Load two 512-bit vectors = 64 UTF-16 chars (safe!)
            var vec0 = Vector512.LoadUnsafe(ref Unsafe.As<char, ushort>(ref Unsafe.AsRef(in pos0)));
            var vec1 = Vector512.LoadUnsafe(ref Unsafe.As<char, ushort>(ref Unsafe.AsRef(in pos32)));

            // Convert UTF-16 to bytes using saturated conversion
            // This is the "AVX-512-to-256" technique - avoids mask register overhead
            var bytes0 = Avx512BW.ConvertToVector256ByteWithSaturation(vec0);
            var bytes1 = Avx512BW.ConvertToVector256ByteWithSaturation(vec1);

            // Create comparison vectors for delimiter and quote
            var delimiterVec = Vector256.Create((byte)delimiter);
            var quoteVec = Vector256.Create((byte)quote);

            // Compare against delimiter and quote
            var delimCmp0 = Avx2.CompareEqual(bytes0, delimiterVec);
            var delimCmp1 = Avx2.CompareEqual(bytes1, delimiterVec);
            var quoteCmp0 = Avx2.CompareEqual(bytes0, quoteVec);
            var quoteCmp1 = Avx2.CompareEqual(bytes1, quoteVec);

            // Combine delimiter and quote masks
            var specialCmp0 = Avx2.Or(delimCmp0, quoteCmp0);
            var specialCmp1 = Avx2.Or(delimCmp1, quoteCmp1);

            // Extract bitmasks
            uint delimMask0 = (uint)Avx2.MoveMask(delimCmp0);
            uint delimMask1 = (uint)Avx2.MoveMask(delimCmp1);
            uint quoteMask0 = (uint)Avx2.MoveMask(quoteCmp0);
            uint quoteMask1 = (uint)Avx2.MoveMask(quoteCmp1);
            uint specialMask0 = (uint)Avx2.MoveMask(specialCmp0);
            uint specialMask1 = (uint)Avx2.MoveMask(specialCmp1);

            // Combine into 64-bit masks
            ulong delimiterMask = delimMask0 | ((ulong)delimMask1 << 32);
            ulong quoteMask = quoteMask0 | ((ulong)quoteMask1 << 32);
            ulong specialMask = specialMask0 | ((ulong)specialMask1 << 32);

            // Process each special character position (delimiters and quotes)
            while (specialMask != 0)
            {
                // Find position of next special character
                int bitPos = BitOperations.TrailingZeroCount(specialMask);
                int charPos = position + bitPos;
                ulong bitMask = 1UL << bitPos;

                // Check if this position is a quote
                if ((quoteMask & bitMask) != 0)
                {
                    // Quote character - toggle quote state
                    quoteCount++;
                }
                // Check if this position is a delimiter AND we're outside quotes
                else if ((delimiterMask & bitMask) != 0 && (quoteCount & 1) == 0)
                {
                    // Delimiter outside quotes - record column
                    if (columnCount >= maxColumns)
                    {
                        throw new CsvException(
                            CsvErrorCode.TooManyColumns,
                            $"Row has more than {maxColumns} columns");
                    }

                    columnStarts[columnCount] = currentStart;
                    columnLengths[columnCount] = charPos - currentStart;
                    columnCount++;

                    currentStart = charPos + 1;
                }

                // Clear the processed bit
                specialMask &= specialMask - 1;
            }

            position += CharsPerIteration;
        }

        // Handle remaining characters (< 64) with scalar processing
        bool inQuotes = (quoteCount & 1) != 0;
        for (int i = position; i < line.Length; i++)
        {
            char c = line[i];

            if (c == quote)
            {
                inQuotes = !inQuotes;
            }
            else if (!inQuotes && c == delimiter)
            {
                if (columnCount >= maxColumns)
                {
                    throw new CsvException(
                        CsvErrorCode.TooManyColumns,
                        $"Row has more than {maxColumns} columns");
                }

                columnStarts[columnCount] = currentStart;
                columnLengths[columnCount] = i - currentStart;
                columnCount++;

                currentStart = i + 1;
            }
        }

        // Last column
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
