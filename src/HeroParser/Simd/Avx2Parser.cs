using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace HeroParser.Simd;

/// <summary>
/// High-performance AVX2 CSV parser for CPUs without AVX-512.
/// Processes 32 characters per iteration using 256-bit SIMD registers.
/// Fallback for older Intel/AMD CPUs (2013+).
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

        // Fast path: if line contains quotes, delegate to scalar parser for RFC 4180 compliance
        if (line.Contains(quote))
        {
            return ScalarParser.Instance.ParseColumns(line, delimiter, quote, columnStarts, columnLengths, maxColumns);
        }

        int columnCount = 0;
        int currentStart = 0;
        int position = 0;

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

            // Create comparison vector for delimiter
            var delimiterVec = Vector256.Create((byte)delimiter);

            // Compare against delimiter
            var cmp = Avx2.CompareEqual(permuted, delimiterVec);

            // Extract bitmask (32 bits for 32 bytes)
            uint mask = (uint)Avx2.MoveMask(cmp);

            // Process each set bit (delimiter position)
            while (mask != 0)
            {
                // Check limit before adding column
                if (columnCount >= maxColumns)
                {
                    throw new CsvException(
                        CsvErrorCode.TooManyColumns,
                        $"Row has more than {maxColumns} columns");
                }

                // Find position of next delimiter
                int bitPos = BitOperations.TrailingZeroCount(mask);
                int delimiterPos = position + bitPos;

                // Record column
                columnStarts[columnCount] = currentStart;
                columnLengths[columnCount] = delimiterPos - currentStart;
                columnCount++;

                currentStart = delimiterPos + 1;

                // Clear the processed bit
                mask &= mask - 1;
            }

            position += CharsPerIteration;
        }

        // Handle remaining characters (< 32) with scalar fallback
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
