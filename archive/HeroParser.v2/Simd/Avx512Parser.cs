using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace HeroParser.Simd;

/// <summary>
/// Ultra-high-performance AVX-512 CSV parser targeting 30+ GB/s throughput.
/// Processes 64 characters per iteration using 512-bit SIMD registers.
/// Uses AVX-512-to-256 technique to avoid mask register overhead.
/// </summary>
internal sealed class Avx512Parser : ISimdParser
{
    public static readonly Avx512Parser Instance = new();

    private const int CharsPerIteration = 64; // Process 64 chars at once

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public unsafe int ParseColumns(
        ReadOnlySpan<char> line,
        char delimiter,
        Span<int> columnStarts,
        Span<int> columnLengths)
    {
        if (line.IsEmpty)
            return 0;

        int columnCount = 0;
        int currentStart = 0;
        int position = 0;

        fixed (char* linePtr = line)
        {
            // Process 64-char chunks with AVX-512
            while (position + CharsPerIteration <= line.Length)
            {
                // Load two 512-bit vectors = 64 UTF-16 chars
                var vec0 = Avx512F.LoadVector512((ushort*)(linePtr + position));
                var vec1 = Avx512F.LoadVector512((ushort*)(linePtr + position + 32));

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
                if (combinedMask != 0)
                {
                    while (combinedMask != 0)
                    {
                        // Find position of next delimiter
                        int bitPos = BitOperations.TrailingZeroCount(combinedMask);
                        int delimiterPos = position + bitPos;

                        // Record column
                        if (columnCount < columnStarts.Length)
                        {
                            columnStarts[columnCount] = currentStart;
                            columnLengths[columnCount] = delimiterPos - currentStart;
                            columnCount++;
                        }

                        currentStart = delimiterPos + 1;

                        // Clear the processed bit
                        combinedMask &= combinedMask - 1;
                    }
                }

                position += CharsPerIteration;
            }
        }

        // Handle remaining characters (< 64) with scalar fallback
        while (position < line.Length)
        {
            if (line[position] == delimiter)
            {
                if (columnCount < columnStarts.Length)
                {
                    columnStarts[columnCount] = currentStart;
                    columnLengths[columnCount] = position - currentStart;
                    columnCount++;
                }
                currentStart = position + 1;
            }
            position++;
        }

        // Last column
        if (columnCount < columnStarts.Length)
        {
            columnStarts[columnCount] = currentStart;
            columnLengths[columnCount] = line.Length - currentStart;
            columnCount++;
        }

        return columnCount;
    }

    /// <summary>
    /// Optimized version for comma delimiter (compile-time constant).
    /// JIT can optimize constant comparisons better.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public unsafe int ParseColumnsComma(
        ReadOnlySpan<char> line,
        Span<int> columnStarts,
        Span<int> columnLengths)
    {
        const char delimiter = ',';
        return ParseColumns(line, delimiter, columnStarts, columnLengths);
    }
}
