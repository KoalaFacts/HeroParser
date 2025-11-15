using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;

namespace HeroParser.Simd;

/// <summary>
/// High-performance ARM NEON CSV parser for Apple Silicon and ARM64 CPUs.
/// Processes 64 characters per iteration using 8x 128-bit SIMD registers.
/// Target: 11+ GB/s on Apple M1 (beating Sep's 9.5 GB/s).
/// </summary>
internal sealed class NeonParser : ISimdParser
{
    public static readonly NeonParser Instance = new();

    private const int CharsPerIteration = 64; // Process 64 chars (8x 16-byte vectors)

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
            // Process 64-char chunks with NEON (8x vectors)
            while (position + CharsPerIteration <= line.Length)
            {
                // Load 8x 128-bit vectors = 64 UTF-16 chars
                var vec0 = AdvSimd.LoadVector128((ushort*)(linePtr + position));
                var vec1 = AdvSimd.LoadVector128((ushort*)(linePtr + position + 8));
                var vec2 = AdvSimd.LoadVector128((ushort*)(linePtr + position + 16));
                var vec3 = AdvSimd.LoadVector128((ushort*)(linePtr + position + 24));
                var vec4 = AdvSimd.LoadVector128((ushort*)(linePtr + position + 32));
                var vec5 = AdvSimd.LoadVector128((ushort*)(linePtr + position + 40));
                var vec6 = AdvSimd.LoadVector128((ushort*)(linePtr + position + 48));
                var vec7 = AdvSimd.LoadVector128((ushort*)(linePtr + position + 56));

                // Narrow UTF-16 to bytes (saturating conversion)
                var bytes0 = AdvSimd.ExtractNarrowingSaturateUpper(
                    AdvSimd.ExtractNarrowingSaturateLower(vec0), vec1);
                var bytes1 = AdvSimd.ExtractNarrowingSaturateUpper(
                    AdvSimd.ExtractNarrowingSaturateLower(vec2), vec3);
                var bytes2 = AdvSimd.ExtractNarrowingSaturateUpper(
                    AdvSimd.ExtractNarrowingSaturateLower(vec4), vec5);
                var bytes3 = AdvSimd.ExtractNarrowingSaturateUpper(
                    AdvSimd.ExtractNarrowingSaturateLower(vec6), vec7);

                // Create comparison vector for delimiter
                var delimiterVec = Vector128.Create((byte)delimiter);

                // Compare against delimiter
                var cmp0 = AdvSimd.CompareEqual(bytes0, delimiterVec);
                var cmp1 = AdvSimd.CompareEqual(bytes1, delimiterVec);
                var cmp2 = AdvSimd.CompareEqual(bytes2, delimiterVec);
                var cmp3 = AdvSimd.CompareEqual(bytes3, delimiterVec);

                // Extract bitmasks (NEON doesn't have MoveMask, so we use a different approach)
                ulong mask0 = ExtractMask(cmp0);
                ulong mask1 = ExtractMask(cmp1);
                ulong mask2 = ExtractMask(cmp2);
                ulong mask3 = ExtractMask(cmp3);

                // Combine into full mask
                // Process each 16-bit segment
                ProcessMask(mask0, position, 0, ref columnCount, ref currentStart, columnStarts, columnLengths);
                ProcessMask(mask1, position, 16, ref columnCount, ref currentStart, columnStarts, columnLengths);
                ProcessMask(mask2, position, 32, ref columnCount, ref currentStart, columnStarts, columnLengths);
                ProcessMask(mask3, position, 48, ref columnCount, ref currentStart, columnStarts, columnLengths);

                position += CharsPerIteration;
            }
        }

        // Handle remaining characters with scalar fallback
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong ExtractMask(Vector128<byte> comparison)
    {
        // NEON doesn't have direct MoveMask equivalent
        // Use bit manipulation to extract mask from comparison result
        // Each byte is 0xFF if equal, 0x00 if not equal

        // Simplified approach: read as uint64 pairs and check high bits
        ulong mask = 0;
        var span = MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<Vector128<byte>, byte>(ref comparison), 16);

        for (int i = 0; i < 16; i++)
        {
            if (span[i] != 0)
                mask |= 1UL << i;
        }

        return mask;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ProcessMask(
        ulong mask,
        int basePosition,
        int offset,
        ref int columnCount,
        ref int currentStart,
        Span<int> columnStarts,
        Span<int> columnLengths)
    {
        if (mask == 0)
            return;

        while (mask != 0)
        {
            int bitPos = BitOperations.TrailingZeroCount(mask);
            int delimiterPos = basePosition + offset + bitPos;

            if (columnCount < columnStarts.Length)
            {
                columnStarts[columnCount] = currentStart;
                columnLengths[columnCount] = delimiterPos - currentStart;
                columnCount++;
            }

            currentStart = delimiterPos + 1;
            mask &= mask - 1;
        }
    }
}
