using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace HeroParser.Simd;

/// <summary>
/// Ultra-high-performance AVX-512 CSV parser targeting 30+ GB/s throughput.
/// Processes 64 characters per iteration using 512-bit SIMD registers.
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
        Span<int> columnStarts,
        Span<int> columnLengths,
        int maxColumns)
    {
        if (line.IsEmpty)
            return 0;

        int columnCount = 0;
        int currentStart = 0;
        int position = 0;

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

            // Create comparison vectors for delimiter
            var delimiterVec = Vector256.Create((byte)delimiter);

            // Compare against delimiter
            var cmp0 = Avx2.CompareEqual(bytes0, delimiterVec);
            var cmp1 = Avx2.CompareEqual(bytes1, delimiterVec);

            // Extract bitmasks
            uint mask0 = (uint)Avx2.MoveMask(cmp0);
            uint mask1 = (uint)Avx2.MoveMask(cmp1);

            // Combine into 64-bit mask
            ulong combinedMask = mask0 | ((ulong)mask1 << 32);

            // Process each set bit (delimiter position)
            while (combinedMask != 0)
            {
                // Check limit before adding column
                if (columnCount >= maxColumns)
                {
                    throw new CsvException(
                        CsvErrorCode.TooManyColumns,
                        $"Row has more than {maxColumns} columns");
                }

                // Find position of next delimiter
                int bitPos = BitOperations.TrailingZeroCount(combinedMask);
                int delimiterPos = position + bitPos;

                // Record column
                columnStarts[columnCount] = currentStart;
                columnLengths[columnCount] = delimiterPos - currentStart;
                columnCount++;

                currentStart = delimiterPos + 1;

                // Clear the processed bit
                combinedMask &= combinedMask - 1;
            }

            position += CharsPerIteration;
        }

        // Handle remaining characters (< 64) with scalar fallback
        for (int i = position; i < line.Length; i++)
        {
            if (line[i] == delimiter)
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
