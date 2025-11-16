using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace HeroParser.Simd;

/// <summary>
/// High-performance AVX2 CSV parser with RFC 4180 quote handling.
/// Processes 32 characters per iteration using 256-bit SIMD registers.
/// Fallback for older Intel/AMD CPUs (2013+).
/// Uses bitmask technique inspired by Sep library for quote-aware parsing.
/// Uses safe MemoryMarshal APIs - NO unsafe code.
/// </summary>
internal sealed class Avx2Parser : ISimdParser
{
    public static readonly Avx2Parser Instance = new();

    private const int CharsPerIteration = 32; // Process 32 chars at once

    private Avx2Parser() { }

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

        // Process 32-char chunks with AVX2
        while (position + CharsPerIteration <= line.Length)
        {
            // Safe load using MemoryMarshal + Unsafe - no unsafe keyword!
            ref readonly char pos0 = ref Unsafe.Add(ref Unsafe.AsRef(in lineStart), position);
            ref readonly char pos16 = ref Unsafe.Add(ref Unsafe.AsRef(in lineStart), position + 16);

            // Load two 256-bit vectors = 32 UTF-16 chars (safe!)
            var vec0 = Vector256.LoadUnsafe(ref Unsafe.As<char, ushort>(ref Unsafe.AsRef(in pos0)));
            var vec1 = Vector256.LoadUnsafe(ref Unsafe.As<char, ushort>(ref Unsafe.AsRef(in pos16)));

            // Pack UTF-16 to bytes using unsigned saturation
            var packed = Avx2.PackUnsignedSaturate(vec0.AsInt16(), vec1.AsInt16());

            // Permute to correct order (PackUnsignedSaturate doesn't preserve order)
            var permuted = Avx2.Permute4x64(packed.AsInt64(), 0b11_01_10_00).AsByte();

            // Create comparison vectors for delimiter and quote
            var delimiterVec = Vector256.Create((byte)delimiter);
            var quoteVec = Vector256.Create((byte)quote);

            // Compare against delimiter and quote
            var delimCmp = Avx2.CompareEqual(permuted, delimiterVec);
            var quoteCmp = Avx2.CompareEqual(permuted, quoteVec);

            // Combine delimiter and quote masks
            var specialCmp = Avx2.Or(delimCmp, quoteCmp);

            // Extract bitmasks (32 bits for 32 bytes)
            uint delimiterMask = (uint)Avx2.MoveMask(delimCmp);
            uint quoteMask = (uint)Avx2.MoveMask(quoteCmp);
            uint specialMask = (uint)Avx2.MoveMask(specialCmp);

            // Process each special character position (delimiters and quotes)
            while (specialMask != 0)
            {
                // Find position of next special character
                int bitPos = BitOperations.TrailingZeroCount(specialMask);
                int charPos = position + bitPos;
                uint bitMask = 1U << bitPos;

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

        // Handle remaining characters (< 32) with scalar processing
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
