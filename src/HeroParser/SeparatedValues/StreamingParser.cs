using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace HeroParser.SeparatedValues;

/// <summary>
/// Unified streaming parser for both UTF-16 (char) and UTF-8 (byte) data.
/// Uses generic specialization for zero-overhead abstraction.
///
/// SIMD parsing techniques inspired by Sep (https://github.com/nietras/Sep) by nietras,
/// which pioneered bitmask-based quote-aware SIMD parsing for CSV.
/// </summary>
internal static class StreamingParser
{
    public static RowParseResult ParseRow<T>(
        ReadOnlySpan<T> data,
        CsvParserOptions options,
        Span<int> columnStarts,
        Span<int> columnLengths)
        where T : unmanaged, IEquatable<T>
    {
        if (data.IsEmpty)
            return new RowParseResult(0, 0, 0);

        ref readonly T dataRef = ref MemoryMarshal.GetReference(data);
        ref T mutableRef = ref Unsafe.AsRef(in dataRef);

        int position = 0;
        bool inQuotes = false;
        bool skipNextQuote = false;
        int columnCount = 0;
        int currentStart = 0;
        int rowLength = 0;
        int charsConsumed = 0;
        bool rowEnded = false;
        bool enableQuotes = options.EnableQuotedFields;

        T delimiter = CastFromChar<T>(options.Delimiter);
        T quote = CastFromChar<T>(options.Quote);
        T lf = CastFromChar<T>('\n');
        T cr = CastFromChar<T>('\r');

        // SIMD fast path (if enabled)
        if (options.UseSimdIfAvailable)
        {
            TrySimdParse(
                ref mutableRef,
                data.Length,
                delimiter,
                quote,
                lf,
                cr,
                ref position,
                ref inQuotes,
                ref skipNextQuote,
                ref columnCount,
                ref currentStart,
                ref rowLength,
                ref charsConsumed,
                ref rowEnded,
                columnStarts,
                columnLengths,
                options.MaxColumns,
                options.AllowNewlinesInsideQuotes,
                enableQuotes);
        }

        if (!rowEnded)
        {
            for (int i = position; i < data.Length; i++)
            {
                T c = Unsafe.Add(ref mutableRef, i);

                if (enableQuotes && c.Equals(quote))
                {
                    if (skipNextQuote)
                    {
                        skipNextQuote = false;
                        continue;
                    }

                    if (inQuotes && i + 1 < data.Length && Unsafe.Add(ref mutableRef, i + 1).Equals(quote))
                    {
                        skipNextQuote = true;
                        continue;
                    }

                    inQuotes = !inQuotes;
                    continue;
                }

                if (enableQuotes && inQuotes && !options.AllowNewlinesInsideQuotes &&
                    (c.Equals(lf) || c.Equals(cr)))
                {
                    throw new CsvException(
                        CsvErrorCode.ParseError,
                        "Newlines inside quoted fields are disabled. Enable AllowNewlinesInsideQuotes to parse them.");
                }

                if (enableQuotes && inQuotes)
                    continue;

                if (c.Equals(delimiter))
                {
                    AppendColumn(i, ref columnCount, ref currentStart,
                        columnStarts, columnLengths, options.MaxColumns);
                }
                else if (c.Equals(lf) || c.Equals(cr))
                {
                    rowLength = i;
                    charsConsumed = i + 1;
                    if (c.Equals(cr) && i + 1 < data.Length && Unsafe.Add(ref mutableRef, i + 1).Equals(lf))
                        charsConsumed++;
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

        if (enableQuotes && inQuotes)
        {
            throw new CsvException(
                CsvErrorCode.ParseError,
                "Unterminated quoted field detected while parsing CSV data.");
        }

        AppendFinalColumn(rowLength, ref columnCount, ref currentStart,
            columnStarts, columnLengths, options.MaxColumns);

        return new RowParseResult(columnCount, rowLength, charsConsumed);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TrySimdParse<T>(
        ref T mutableRef,
        int dataLength,
        T delimiter,
        T quote,
        T lf,
        T cr,
        ref int position,
        ref bool inQuotes,
        ref bool skipNextQuote,
        ref int columnCount,
        ref int currentStart,
        ref int rowLength,
        ref int charsConsumed,
        ref bool rowEnded,
        Span<int> columnStarts,
        Span<int> columnLengths,
        int maxColumns,
        bool allowNewlinesInsideQuotes,
        bool enableQuotedFields)
        where T : unmanaged, IEquatable<T>
    {
        if (typeof(T) == typeof(byte))
        {
            return TrySimdParseUtf8(
                ref Unsafe.As<T, byte>(ref mutableRef),
                dataLength,
                Unsafe.As<T, byte>(ref delimiter),
                Unsafe.As<T, byte>(ref quote),
                Unsafe.As<T, byte>(ref lf),
                Unsafe.As<T, byte>(ref cr),
                ref position, ref inQuotes, ref skipNextQuote,
                ref columnCount, ref currentStart, ref rowLength, ref charsConsumed, ref rowEnded,
                columnStarts, columnLengths, maxColumns, allowNewlinesInsideQuotes, enableQuotedFields);
        }
        else if (typeof(T) == typeof(char))
        {
            return TrySimdParseUtf16(
                ref Unsafe.As<T, char>(ref mutableRef),
                dataLength,
                Unsafe.As<T, char>(ref delimiter),
                Unsafe.As<T, char>(ref quote),
                Unsafe.As<T, char>(ref lf),
                Unsafe.As<T, char>(ref cr),
                ref position, ref inQuotes, ref skipNextQuote,
                ref columnCount, ref currentStart, ref rowLength, ref charsConsumed, ref rowEnded,
                columnStarts, columnLengths, maxColumns, allowNewlinesInsideQuotes, enableQuotedFields);
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TrySimdParseUtf8(
        ref byte mutableRef,
        int dataLength,
        byte delimiter,
        byte quote,
        byte lf,
        byte cr,
        ref int position,
        ref bool inQuotes,
        ref bool skipNextQuote,
        ref int columnCount,
        ref int currentStart,
        ref int rowLength,
        ref int charsConsumed,
        ref bool rowEnded,
        Span<int> columnStarts,
        Span<int> columnLengths,
        int maxColumns,
        bool allowNewlinesInsideQuotes,
        bool enableQuotedFields)
    {
        if (!Avx2.IsSupported)
            return false;

        var delimiterVec = Vector256.Create(delimiter);
        var quoteVec = Vector256.Create(quote);
        var lfVec = Vector256.Create(lf);
        var crVec = Vector256.Create(cr);

        while (position + Vector256<byte>.Count <= dataLength)
        {
            var chunk = Vector256.LoadUnsafe(ref Unsafe.Add(ref mutableRef, position));
            var specials = enableQuotedFields
                ? Avx2.Or(
                    Avx2.Or(Avx2.CompareEqual(chunk, delimiterVec), Avx2.CompareEqual(chunk, quoteVec)),
                    Avx2.Or(Avx2.CompareEqual(chunk, lfVec), Avx2.CompareEqual(chunk, crVec)))
                : Avx2.Or(
                    Avx2.CompareEqual(chunk, delimiterVec),
                    Avx2.Or(Avx2.CompareEqual(chunk, lfVec), Avx2.CompareEqual(chunk, crVec)));

            uint mask = (uint)Avx2.MoveMask(specials);

            while (mask != 0)
            {
                int bit = BitOperations.TrailingZeroCount(mask);
                mask &= mask - 1;

                // Check for integer overflow
                if (position > int.MaxValue - bit)
                    throw new CsvException(CsvErrorCode.ParseError, "CSV data is too large to process");

                int absolute = position + bit;
                byte c = Unsafe.Add(ref mutableRef, absolute);

                if (enableQuotedFields && c == quote)
                {
                    if (skipNextQuote)
                    {
                        skipNextQuote = false;
                        continue;
                    }

                    if (inQuotes && absolute + 1 < dataLength && Unsafe.Add(ref mutableRef, absolute + 1) == quote)
                    {
                        skipNextQuote = true;
                        continue;
                    }

                    inQuotes = !inQuotes;
                    continue;
                }

                if (enableQuotedFields && inQuotes && !allowNewlinesInsideQuotes && (c == lf || c == cr))
                {
                    throw new CsvException(
                        CsvErrorCode.ParseError,
                        "Newlines inside quoted fields are disabled. Enable AllowNewlinesInsideQuotes to parse them.");
                }

                if (enableQuotedFields && inQuotes)
                    continue;

                if (c == delimiter)
                {
                    AppendColumn(absolute, ref columnCount, ref currentStart,
                        columnStarts, columnLengths, maxColumns);
                    continue;
                }

                if (c == lf || c == cr)
                {
                    rowLength = absolute;
                    charsConsumed = absolute + 1;
                    if (c == cr && absolute + 1 < dataLength && Unsafe.Add(ref mutableRef, absolute + 1) == lf)
                        charsConsumed++;
                    rowEnded = true;
                    return true;
                }
            }

            position += Vector256<byte>.Count;
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TrySimdParseUtf16(
        ref char mutableRef,
        int dataLength,
        char delimiter,
        char quote,
        char lf,
        char cr,
        ref int position,
        ref bool inQuotes,
        ref bool skipNextQuote,
        ref int columnCount,
        ref int currentStart,
        ref int rowLength,
        ref int charsConsumed,
        ref bool rowEnded,
        Span<int> columnStarts,
        Span<int> columnLengths,
        int maxColumns,
        bool allowNewlinesInsideQuotes,
        bool enableQuotedFields)
    {
        if (!Vector256.IsHardwareAccelerated)
            return false;

        ref ushort ushortRef = ref Unsafe.As<char, ushort>(ref mutableRef);

        var delimiterVec = Vector256.Create((ushort)delimiter);
        var quoteVec = Vector256.Create((ushort)quote);
        var lfVec = Vector256.Create((ushort)lf);
        var crVec = Vector256.Create((ushort)cr);

        while (position + Vector256<ushort>.Count <= dataLength)
        {
            var chunk = Vector256.LoadUnsafe(ref Unsafe.Add(ref ushortRef, position));
            var specials = enableQuotedFields
                ? Vector256.BitwiseOr(
                    Vector256.BitwiseOr(Vector256.Equals(chunk, delimiterVec), Vector256.Equals(chunk, quoteVec)),
                    Vector256.BitwiseOr(Vector256.Equals(chunk, lfVec), Vector256.Equals(chunk, crVec)))
                : Vector256.BitwiseOr(
                    Vector256.Equals(chunk, delimiterVec),
                    Vector256.BitwiseOr(Vector256.Equals(chunk, lfVec), Vector256.Equals(chunk, crVec)));

            uint mask = specials.ExtractMostSignificantBits();

            while (mask != 0)
            {
                int bit = BitOperations.TrailingZeroCount(mask);
                mask &= mask - 1;

                // Check for integer overflow
                if (position > int.MaxValue - bit)
                    throw new CsvException(CsvErrorCode.ParseError, "CSV data is too large to process");

                int absolute = position + bit;
                char c = Unsafe.Add(ref mutableRef, absolute);

                if (enableQuotedFields && c == quote)
                {
                    if (skipNextQuote)
                    {
                        skipNextQuote = false;
                        continue;
                    }

                    if (inQuotes && absolute + 1 < dataLength && Unsafe.Add(ref mutableRef, absolute + 1) == quote)
                    {
                        skipNextQuote = true;
                        continue;
                    }

                    inQuotes = !inQuotes;
                    continue;
                }

                if (enableQuotedFields && inQuotes && !allowNewlinesInsideQuotes && (c == lf || c == cr))
                {
                    throw new CsvException(
                        CsvErrorCode.ParseError,
                        "Newlines inside quoted fields are disabled. Enable AllowNewlinesInsideQuotes to parse them.");
                }

                if (enableQuotedFields && inQuotes)
                    continue;

                if (c == delimiter)
                {
                    AppendColumn(absolute, ref columnCount, ref currentStart,
                        columnStarts, columnLengths, maxColumns);
                    continue;
                }

                if (c == lf || c == cr)
                {
                    rowLength = absolute;
                    charsConsumed = absolute + 1;
                    if (c == cr && absolute + 1 < dataLength && Unsafe.Add(ref mutableRef, absolute + 1) == lf)
                        charsConsumed++;
                    rowEnded = true;
                    return true;
                }
            }

            position += Vector256<ushort>.Count;
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static T CastFromChar<T>(char c) where T : unmanaged
    {
        if (typeof(T) == typeof(byte))
            return (T)(object)(byte)c;
        if (typeof(T) == typeof(char))
            return (T)(object)c;
        throw new NotSupportedException($"Type {typeof(T)} not supported");
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
        if (columnCount + 1 > maxColumns)
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
        if (columnCount + 1 > maxColumns)
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
