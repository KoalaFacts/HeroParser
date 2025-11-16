using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace HeroParser.Simd;

/// <summary>
/// High-performance AVX2 CSV parser for CPUs without AVX-512.
/// Processes 32 characters per iteration using 256-bit SIMD registers.
/// Fallback for older Intel/AMD CPUs (2013+).
/// </summary>
internal sealed class Avx2Parser : ISimdParser
{
    public static readonly Avx2Parser Instance = new();

    private const int CharsPerIteration = 32; // Process 32 chars at once

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
            // Process 32-char chunks with AVX2
            while (position + CharsPerIteration <= line.Length)
            {
                // Load two 256-bit vectors = 32 UTF-16 chars
                var vec0 = Avx.LoadVector256((ushort*)(linePtr + position));
                var vec1 = Avx.LoadVector256((ushort*)(linePtr + position + 16));

                // Pack UTF-16 to bytes using unsigned saturation
                var packed = Avx2.PackUnsignedSaturate(
                    vec0.AsInt16(),
                    vec1.AsInt16());

                // Permute to correct order (PackUnsignedSaturate doesn't preserve order)
                var permuted = Avx2.Permute4x64(packed.AsInt64(), 0b11_01_10_00).AsByte();

                // Create comparison vector for delimiter
                var delimiterVec = Vector256.Create((byte)delimiter);

                // Compare against delimiter
                var cmp = Avx2.CompareEqual(permuted, delimiterVec);

                // Extract bitmask (32 bits for 32 bytes)
                uint mask = (uint)Avx2.MoveMask(cmp);

                // Process each set bit (delimiter position)
                if (mask != 0)
                {
                    while (mask != 0)
                    {
                        // Find position of next delimiter
                        int bitPos = BitOperations.TrailingZeroCount(mask);
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
                        mask &= mask - 1;
                    }
                }

                position += CharsPerIteration;
            }
        }

        // Handle remaining characters (< 32) with scalar fallback
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
}
