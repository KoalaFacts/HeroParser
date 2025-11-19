using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace StreamingPrototype;

/// <summary>
/// Experimental AVX2 streaming CSV parser inspired by Sep's mask machinery.
/// Operates on UTF-8 bytes and returns row metadata in a single pass.
/// </summary>
internal static class StreamingParser
{
    public static RowParseResult ParseRow(ReadOnlySpan<byte> data, byte delimiter = (byte)',', byte quote = (byte)'"')
    {
        if (data.IsEmpty)
        {
            return new RowParseResult(Columns: 1, RowLength: 0, CharsConsumed: 0);
        }

        ref readonly byte dataRef = ref MemoryMarshal.GetReference(data);
        ref byte mutableRef = ref Unsafe.AsRef(in dataRef);

        int columnCount = 0;
        int quoteParity = 0;
        int position = 0;

        // Try SIMD-accelerated parsing first
        if (Avx2.IsSupported && TryParseRowSimd(data, ref mutableRef, delimiter, quote, ref columnCount, ref quoteParity, ref position, out var simdResult))
        {
            return simdResult;
        }

        // Fall back to scalar parsing for remainder or when SIMD didn't complete the row
        return ParseRowScalar(data, ref mutableRef, delimiter, quote, columnCount, quoteParity, position);
    }

    private static bool TryParseRowSimd(
        ReadOnlySpan<byte> data,
        ref byte mutableRef,
        byte delimiter,
        byte quote,
        ref int columnCount,
        ref int quoteParity,
        ref int position,
        out RowParseResult result)
    {
        var delimiterVec = Vector256.Create(delimiter);
        var quoteVec = Vector256.Create(quote);
        var lfVec = Vector256.Create((byte)'\n');
        var crVec = Vector256.Create((byte)'\r');

        while (position + Vector256<byte>.Count <= data.Length)
        {
            var chunk = Vector256.LoadUnsafe(ref Unsafe.Add(ref mutableRef, position));

            var delimiterMask = Avx2.CompareEqual(chunk, delimiterVec);
            var quoteMask = Avx2.CompareEqual(chunk, quoteVec);
            var lfMask = Avx2.CompareEqual(chunk, lfVec);
            var crMask = Avx2.CompareEqual(chunk, crVec);

            var specials = Avx2.Or(delimiterMask, Avx2.Or(quoteMask, Avx2.Or(lfMask, crMask)));
            uint mask = (uint)Avx2.MoveMask(specials);

            while (mask != 0)
            {
                int bit = BitOperations.TrailingZeroCount(mask);
                mask &= mask - 1;
                int absolute = position + bit;
                byte c = Unsafe.Add(ref mutableRef, absolute);

                if (c == quote)
                {
                    quoteParity ^= 1;
                    continue;
                }

                if (quoteParity != 0)
                    continue;

                if (c == delimiter)
                {
                    columnCount++;
                    continue;
                }

                // Found newline - row is complete
                int newlineLen = CalculateNewlineLength(data, ref mutableRef, absolute, c);
                int rowLength = absolute;
                columnCount++; // Account for last column
                result = new RowParseResult(columnCount, rowLength, rowLength + newlineLen);
                return true;
            }

            position += Vector256<byte>.Count;
        }

        result = default;
        return false;
    }

    private static RowParseResult ParseRowScalar(
        ReadOnlySpan<byte> data,
        ref byte mutableRef,
        byte delimiter,
        byte quote,
        int columnCount,
        int quoteParity,
        int position)
    {
        for (int i = position; i < data.Length; i++)
        {
            byte c = Unsafe.Add(ref mutableRef, i);

            if (c == quote)
            {
                quoteParity ^= 1;
                continue;
            }

            if (quoteParity != 0)
                continue;

            if (c == delimiter)
            {
                columnCount++;
            }
            else if (c == '\n' || c == '\r')
            {
                int newlineLen = CalculateNewlineLength(data, ref mutableRef, i, c);
                int rowLength = i;
                columnCount++; // Account for last column
                return new RowParseResult(columnCount, rowLength, rowLength + newlineLen);
            }
        }

        // No newline found - entire remaining data is the row
        columnCount++; // Account for last column
        return new RowParseResult(columnCount, data.Length, data.Length);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int CalculateNewlineLength(ReadOnlySpan<byte> data, ref byte mutableRef, int position, byte currentChar)
    {
        if (currentChar == '\r' && position + 1 < data.Length && Unsafe.Add(ref mutableRef, position + 1) == (byte)'\n')
        {
            return 2; // CRLF
        }
        return 1; // LF or CR only
    }

}

internal readonly record struct RowParseResult(int Columns, int RowLength, int CharsConsumed);
