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
        int rowLength = 0;
        int charsConsumed = 0;
        int quoteParity = 0;
        int position = 0;
        bool rowEnded = false;
        int newlineLen = 0;

        if (Avx2.IsSupported)
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

                    rowLength = absolute;
                    newlineLen = 1;
                    if (c == '\r' && absolute + 1 < data.Length && Unsafe.Add(ref mutableRef, absolute + 1) == (byte)'\n')
                    {
                        newlineLen = 2;
                    }

                    charsConsumed = rowLength + newlineLen;
                    rowEnded = true;
                    goto VectorDone;
                }

                position += Vector256<byte>.Count;
            }
        }

VectorDone:

        if (!rowEnded)
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
                    rowLength = i;
                    newlineLen = 1;
                    if (c == '\r' && i + 1 < data.Length && Unsafe.Add(ref mutableRef, i + 1) == (byte)'\n')
                        newlineLen = 2;
                    charsConsumed = rowLength + newlineLen;
                    rowEnded = true;
                    break;
                }
            }
        }

        if (!rowEnded)
        {
            rowLength = data.Length;
            charsConsumed = rowLength;
        }

        // Account for last column (even if empty)
        columnCount++;

        return new RowParseResult(columnCount, rowLength, charsConsumed);
    }

}

internal readonly record struct RowParseResult(int Columns, int RowLength, int CharsConsumed);
