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
internal static class CsvStreamingParser
{
    public static CsvRowParseResult ParseRow<T>(
        ReadOnlySpan<T> data,
        CsvParserOptions options,
        Span<int> columnStarts,
        Span<int> columnLengths)
        where T : unmanaged, IEquatable<T>
    {
        if (data.IsEmpty)
            return new CsvRowParseResult(0, 0, 0, 0);

        ref readonly T dataRef = ref MemoryMarshal.GetReference(data);
        ref T mutableRef = ref Unsafe.AsRef(in dataRef);

        T delimiter = CastFromChar<T>(options.Delimiter);
        T quote = CastFromChar<T>(options.Quote);
        T lf = CastFromChar<T>('\n');
        T cr = CastFromChar<T>('\r');
        T space = CastFromChar<T>(' ');
        T tab = CastFromChar<T>('\t');
        T escape = options.EscapeCharacter.HasValue ? CastFromChar<T>(options.EscapeCharacter.Value) : default;
        bool hasEscapeChar = options.EscapeCharacter.HasValue;

        // Check for comment line
        if (options.CommentCharacter.HasValue)
        {
            T comment = CastFromChar<T>(options.CommentCharacter.Value);
            int checkPos = 0;

            // Skip leading whitespace to find comment character
            while (checkPos < data.Length)
            {
                T c = Unsafe.Add(ref mutableRef, checkPos);
                if (c.Equals(comment))
                {
                    // This is a comment line, skip to end of line
                    int skipPos = checkPos;
                    int commentNewlines = 0;
                    while (skipPos < data.Length && !Unsafe.Add(ref mutableRef, skipPos).Equals(lf) && !Unsafe.Add(ref mutableRef, skipPos).Equals(cr))
                    {
                        skipPos++;
                    }

                    int consumed = skipPos;
                    if (skipPos < data.Length)
                    {
                        T lineEnd = Unsafe.Add(ref mutableRef, skipPos);
                        consumed++;
                        if (lineEnd.Equals(lf))
                            commentNewlines++;
                        if (lineEnd.Equals(cr) && skipPos + 1 < data.Length && Unsafe.Add(ref mutableRef, skipPos + 1).Equals(lf))
                        {
                            consumed++;
                            commentNewlines++; // Count the LF in CRLF
                        }
                    }

                    return new CsvRowParseResult(0, 0, consumed, commentNewlines);
                }
                else if (!c.Equals(space) && !c.Equals(tab))
                {
                    // Non-whitespace character found before comment, not a comment line
                    break;
                }
                checkPos++;
            }
        }

        int position = 0;
        bool inQuotes = false;
        bool skipNextQuote = false;
        int columnCount = 0;
        int currentStart = 0;
        int rowLength = 0;
        int charsConsumed = 0;
        int newlineCount = 0; // Track number of \n characters encountered
        bool rowEnded = false;
        bool enableQuotes = options.EnableQuotedFields;
        int quoteStartPosition = -1; // Track where the opening quote was found

        // SIMD fast path (if enabled and no escape character - escape handling requires sequential processing)
        if (options.UseSimdIfAvailable && !hasEscapeChar)
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
                ref newlineCount,
                ref rowEnded,
                ref quoteStartPosition,
                columnStarts,
                columnLengths,
                options.MaxColumnCount,
                options.AllowNewlinesInsideQuotes,
                enableQuotes,
                options.MaxFieldSize);
        }

        if (!rowEnded)
        {
            bool skipNextChar = false;
            for (int i = position; i < data.Length; i++)
            {
                T c = Unsafe.Add(ref mutableRef, i);

                // Handle escape character - skip the next character
                if (skipNextChar)
                {
                    skipNextChar = false;
                    continue;
                }

                // Check for escape character (e.g., backslash)
                if (hasEscapeChar && c.Equals(escape) && i + 1 < data.Length)
                {
                    skipNextChar = true;
                    continue;
                }

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

                    if (!inQuotes)
                    {
                        quoteStartPosition = i; // Track where the quote opened
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
                {
                    // Count newlines inside quoted fields
                    if (c.Equals(lf))
                        newlineCount++;
                    continue;
                }

                if (c.Equals(delimiter))
                {
                    AppendColumn(i, ref columnCount, ref currentStart,
                        columnStarts, columnLengths, options.MaxColumnCount, options.MaxFieldSize);
                }
                else if (c.Equals(lf) || c.Equals(cr))
                {
                    rowLength = i;
                    charsConsumed = i + 1;
                    if (c.Equals(lf))
                        newlineCount++;
                    if (c.Equals(cr) && i + 1 < data.Length && Unsafe.Add(ref mutableRef, i + 1).Equals(lf))
                    {
                        charsConsumed++;
                        newlineCount++; // Count the LF in CRLF
                    }
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
            if (quoteStartPosition >= 0)
            {
                throw CsvException.UnterminatedQuote(
                    "Unterminated quoted field detected while parsing CSV data.",
                    1, // Row number is not tracked in ParseRow, will be wrapped by caller
                    quoteStartPosition);
            }
            throw new CsvException(
                CsvErrorCode.ParseError,
                "Unterminated quoted field detected while parsing CSV data.");
        }

        AppendFinalColumn(rowLength, ref columnCount, ref currentStart,
            columnStarts, columnLengths, options.MaxColumnCount, options.MaxFieldSize);

        // Apply trimming if enabled (only for unquoted fields)
        if (options.TrimFields)
        {
            ApplyTrimming(
                ref mutableRef,
                columnStarts,
                columnLengths,
                columnCount,
                quote,
                space,
                tab);
        }

        return new CsvRowParseResult(columnCount, rowLength, charsConsumed, newlineCount);
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
        ref int newlineCount,
        ref bool rowEnded,
        ref int quoteStartPosition,
        Span<int> columnStarts,
        Span<int> columnLengths,
        int maxColumns,
        bool allowNewlinesInsideQuotes,
        bool enableQuotedFields,
        int? maxFieldLength)
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
                ref columnCount, ref currentStart, ref rowLength, ref charsConsumed, ref newlineCount, ref rowEnded, ref quoteStartPosition,
                columnStarts, columnLengths, maxColumns, allowNewlinesInsideQuotes, enableQuotedFields, maxFieldLength);
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
                ref columnCount, ref currentStart, ref rowLength, ref charsConsumed, ref newlineCount, ref rowEnded, ref quoteStartPosition,
                columnStarts, columnLengths, maxColumns, allowNewlinesInsideQuotes, enableQuotedFields, maxFieldLength);
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
#pragma warning disable IDE0060 // Remove unused parameter - maxFieldLength is checked in scalar fallback
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
        ref int newlineCount,
        ref bool rowEnded,
        ref int quoteStartPosition,
        Span<int> columnStarts,
        Span<int> columnLengths,
        int maxColumns,
        bool allowNewlinesInsideQuotes,
        bool enableQuotedFields,
        int? maxFieldLength)
#pragma warning restore IDE0060
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

                    if (!inQuotes)
                    {
                        quoteStartPosition = absolute;
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
                {
                    // Count newlines inside quoted fields
                    if (c == lf)
                        newlineCount++;
                    continue;
                }

                if (c == delimiter)
                {
                    AppendColumn(absolute, ref columnCount, ref currentStart,
                        columnStarts, columnLengths, maxColumns, maxFieldLength);
                    continue;
                }

                if (c == lf || c == cr)
                {
                    rowLength = absolute;
                    charsConsumed = absolute + 1;
                    if (c == lf)
                        newlineCount++;
                    if (c == cr && absolute + 1 < dataLength && Unsafe.Add(ref mutableRef, absolute + 1) == lf)
                    {
                        charsConsumed++;
                        newlineCount++; // Count the LF in CRLF
                    }
                    rowEnded = true;
                    return true;
                }
            }

            position += Vector256<byte>.Count;
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
#pragma warning disable IDE0060 // Remove unused parameter - maxFieldLength is checked in scalar fallback
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
        ref int newlineCount,
        ref bool rowEnded,
        ref int quoteStartPosition,
        Span<int> columnStarts,
        Span<int> columnLengths,
        int maxColumns,
        bool allowNewlinesInsideQuotes,
        bool enableQuotedFields,
        int? maxFieldLength)
#pragma warning restore IDE0060
    {
#if NET8_0_OR_GREATER
        // Try AVX-512BW first (32 chars per iteration with native 16-bit ops)
        if (Avx512BW.IsSupported)
        {
            return TrySimdParseUtf16Avx512(
                ref mutableRef, dataLength, delimiter, quote, lf, cr,
                ref position, ref inQuotes, ref skipNextQuote,
                ref columnCount, ref currentStart, ref rowLength, ref charsConsumed, ref newlineCount, ref rowEnded, ref quoteStartPosition,
                columnStarts, columnLengths, maxColumns, allowNewlinesInsideQuotes, enableQuotedFields, maxFieldLength);
        }
#endif

        // Fall back to AVX2 with optimized intrinsics
        if (!Avx2.IsSupported)
            return false;

        return TrySimdParseUtf16Avx2(
            ref mutableRef, dataLength, delimiter, quote, lf, cr,
            ref position, ref inQuotes, ref skipNextQuote,
            ref columnCount, ref currentStart, ref rowLength, ref charsConsumed, ref newlineCount, ref rowEnded, ref quoteStartPosition,
            columnStarts, columnLengths, maxColumns, allowNewlinesInsideQuotes, enableQuotedFields, maxFieldLength);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TrySimdParseUtf16Avx2(
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
        ref int newlineCount,
        ref bool rowEnded,
        ref int quoteStartPosition,
        Span<int> columnStarts,
        Span<int> columnLengths,
        int maxColumns,
        bool allowNewlinesInsideQuotes,
        bool enableQuotedFields,
        int? maxFieldLength)
    {
        ref ushort ushortRef = ref Unsafe.As<char, ushort>(ref mutableRef);

        // Create vectors for comparison (as 16-bit)
        var delimiterVec = Vector256.Create((ushort)delimiter);
        var quoteVec = Vector256.Create((ushort)quote);
        var lfVec = Vector256.Create((ushort)lf);
        var crVec = Vector256.Create((ushort)cr);

        // Sep-style optimization: process 32 chars at once by packing two 16-bit vectors to one 8-bit vector
        // This doubles throughput by using byte-level MoveMask instead of 16-bit ExtractMostSignificantBits
        const int CharsPerIteration = 32; // Two Vector256<ushort> = 32 chars

        while (position + CharsPerIteration <= dataLength)
        {
            // Load two consecutive 16-char vectors
            var v0 = Vector256.LoadUnsafe(ref Unsafe.Add(ref ushortRef, position));
            var v1 = Vector256.LoadUnsafe(ref Unsafe.Add(ref ushortRef, position + 16));

            // Compare both vectors against special characters
            Vector256<ushort> specials0, specials1;
            if (enableQuotedFields)
            {
                var d0 = Avx2.CompareEqual(v0, delimiterVec);
                var q0 = Avx2.CompareEqual(v0, quoteVec);
                var l0 = Avx2.CompareEqual(v0, lfVec);
                var r0 = Avx2.CompareEqual(v0, crVec);
                specials0 = Avx2.Or(Avx2.Or(d0, q0), Avx2.Or(l0, r0));

                var d1 = Avx2.CompareEqual(v1, delimiterVec);
                var q1 = Avx2.CompareEqual(v1, quoteVec);
                var l1 = Avx2.CompareEqual(v1, lfVec);
                var r1 = Avx2.CompareEqual(v1, crVec);
                specials1 = Avx2.Or(Avx2.Or(d1, q1), Avx2.Or(l1, r1));
            }
            else
            {
                var d0 = Avx2.CompareEqual(v0, delimiterVec);
                var l0 = Avx2.CompareEqual(v0, lfVec);
                var r0 = Avx2.CompareEqual(v0, crVec);
                specials0 = Avx2.Or(d0, Avx2.Or(l0, r0));

                var d1 = Avx2.CompareEqual(v1, delimiterVec);
                var l1 = Avx2.CompareEqual(v1, lfVec);
                var r1 = Avx2.CompareEqual(v1, crVec);
                specials1 = Avx2.Or(d1, Avx2.Or(l1, r1));
            }

            // Pack two 16-bit vectors into one 8-bit vector
            // Use PackSignedSaturate because comparison results are 0xFFFF (-1) for match, 0x0000 (0) for no match
            // PackUnsignedSaturate would saturate -1 to 0, losing the match info!
            // PackSignedSaturate saturates -1 to -128 (0x80), preserving the high bit for MoveMask
            var packed = Avx2.PackSignedSaturate(specials0.AsInt16(), specials1.AsInt16());
            // Permute: PackSignedSaturate interleaves lanes [v0_low,v1_low,v0_high,v1_high]
            // We need [v0_low,v0_high,v1_low,v1_high] = lanes [0,2,1,3]
            var bytes = Avx2.Permute4x64(packed.AsInt64(), 0b_11_01_10_00).AsByte();

            // Extract 32-bit mask (one bit per character)
            uint mask = (uint)Avx2.MoveMask(bytes);

            while (mask != 0)
            {
                int bit = BitOperations.TrailingZeroCount(mask);
                mask &= mask - 1;

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

                    if (!inQuotes)
                    {
                        quoteStartPosition = absolute;
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
                {
                    // Count newlines inside quoted fields
                    if (c == lf)
                        newlineCount++;
                    continue;
                }

                if (c == delimiter)
                {
                    AppendColumn(absolute, ref columnCount, ref currentStart,
                        columnStarts, columnLengths, maxColumns, maxFieldLength);
                    continue;
                }

                if (c == lf || c == cr)
                {
                    rowLength = absolute;
                    charsConsumed = absolute + 1;
                    if (c == lf)
                        newlineCount++;
                    if (c == cr && absolute + 1 < dataLength && Unsafe.Add(ref mutableRef, absolute + 1) == lf)
                    {
                        charsConsumed++;
                        newlineCount++; // Count the LF in CRLF
                    }
                    rowEnded = true;
                    return true;
                }
            }

            position += CharsPerIteration;
        }

        // Handle remaining 16+ chars with single-vector fallback
        while (position + 16 <= dataLength)
        {
            var chunk = Vector256.LoadUnsafe(ref Unsafe.Add(ref ushortRef, position));

            Vector256<ushort> specials;
            if (enableQuotedFields)
            {
                var delimMatch = Avx2.CompareEqual(chunk, delimiterVec);
                var quoteMatch = Avx2.CompareEqual(chunk, quoteVec);
                var lfMatch = Avx2.CompareEqual(chunk, lfVec);
                var crMatch = Avx2.CompareEqual(chunk, crVec);
                specials = Avx2.Or(Avx2.Or(delimMatch, quoteMatch), Avx2.Or(lfMatch, crMatch));
            }
            else
            {
                var delimMatch = Avx2.CompareEqual(chunk, delimiterVec);
                var lfMatch = Avx2.CompareEqual(chunk, lfVec);
                var crMatch = Avx2.CompareEqual(chunk, crVec);
                specials = Avx2.Or(delimMatch, Avx2.Or(lfMatch, crMatch));
            }

            uint mask = specials.ExtractMostSignificantBits();

            while (mask != 0)
            {
                int bit = BitOperations.TrailingZeroCount(mask);
                mask &= mask - 1;

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

                    if (!inQuotes)
                    {
                        quoteStartPosition = absolute;
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
                {
                    // Count newlines inside quoted fields
                    if (c == lf)
                        newlineCount++;
                    continue;
                }

                if (c == delimiter)
                {
                    AppendColumn(absolute, ref columnCount, ref currentStart,
                        columnStarts, columnLengths, maxColumns, maxFieldLength);
                    continue;
                }

                if (c == lf || c == cr)
                {
                    rowLength = absolute;
                    charsConsumed = absolute + 1;
                    if (c == lf)
                        newlineCount++;
                    if (c == cr && absolute + 1 < dataLength && Unsafe.Add(ref mutableRef, absolute + 1) == lf)
                    {
                        charsConsumed++;
                        newlineCount++; // Count the LF in CRLF
                    }
                    rowEnded = true;
                    return true;
                }
            }

            position += 16;
        }

        return true;
    }

#if NET8_0_OR_GREATER
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TrySimdParseUtf16Avx512(
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
        ref int newlineCount,
        ref bool rowEnded,
        ref int quoteStartPosition,
        Span<int> columnStarts,
        Span<int> columnLengths,
        int maxColumns,
        bool allowNewlinesInsideQuotes,
        bool enableQuotedFields,
        int? maxFieldLength)
    {
        ref ushort ushortRef = ref Unsafe.As<char, ushort>(ref mutableRef);

        var delimiterVec = Vector512.Create((ushort)delimiter);
        var quoteVec = Vector512.Create((ushort)quote);
        var lfVec = Vector512.Create((ushort)lf);
        var crVec = Vector512.Create((ushort)cr);

        // Process 64 chars per iteration by packing two 32-char vectors to one 64-byte vector
        const int CharsPerIteration = 64;

        while (position + CharsPerIteration <= dataLength)
        {
            // Load two consecutive 32-char vectors
            var v0 = Vector512.LoadUnsafe(ref Unsafe.Add(ref ushortRef, position));
            var v1 = Vector512.LoadUnsafe(ref Unsafe.Add(ref ushortRef, position + 32));

            // Compare both vectors against special characters
            Vector512<ushort> specials0, specials1;
            if (enableQuotedFields)
            {
                var d0 = Vector512.Equals(v0, delimiterVec);
                var q0 = Vector512.Equals(v0, quoteVec);
                var l0 = Vector512.Equals(v0, lfVec);
                var r0 = Vector512.Equals(v0, crVec);
                specials0 = Vector512.BitwiseOr(Vector512.BitwiseOr(d0, q0), Vector512.BitwiseOr(l0, r0));

                var d1 = Vector512.Equals(v1, delimiterVec);
                var q1 = Vector512.Equals(v1, quoteVec);
                var l1 = Vector512.Equals(v1, lfVec);
                var r1 = Vector512.Equals(v1, crVec);
                specials1 = Vector512.BitwiseOr(Vector512.BitwiseOr(d1, q1), Vector512.BitwiseOr(l1, r1));
            }
            else
            {
                var d0 = Vector512.Equals(v0, delimiterVec);
                var l0 = Vector512.Equals(v0, lfVec);
                var r0 = Vector512.Equals(v0, crVec);
                specials0 = Vector512.BitwiseOr(d0, Vector512.BitwiseOr(l0, r0));

                var d1 = Vector512.Equals(v1, delimiterVec);
                var l1 = Vector512.Equals(v1, lfVec);
                var r1 = Vector512.Equals(v1, crVec);
                specials1 = Vector512.BitwiseOr(d1, Vector512.BitwiseOr(l1, r1));
            }

            // Pack two 16-bit vectors into one 8-bit vector using AVX-512BW
            // Use PackSignedSaturate because comparison results are 0xFFFF (-1) for match
            // PackSignedSaturate saturates -1 to -128 (0x80), preserving the high bit for mask extraction
            var packed = Avx512BW.PackSignedSaturate(specials0.AsInt16(), specials1.AsInt16());
            // Permute to fix interleaved lane order (AVX-512 uses 4 128-bit lanes)
            var bytes = Avx512F.PermuteVar8x64(packed.AsInt64(),
                Vector512.Create(0L, 2, 4, 6, 1, 3, 5, 7)).AsByte();

            // Extract 64-bit mask (one bit per character)
            ulong mask = bytes.ExtractMostSignificantBits();

            while (mask != 0)
            {
                int bit = BitOperations.TrailingZeroCount(mask);
                mask &= mask - 1;

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

                    if (!inQuotes)
                    {
                        quoteStartPosition = absolute;
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
                {
                    // Count newlines inside quoted fields
                    if (c == lf)
                        newlineCount++;
                    continue;
                }

                if (c == delimiter)
                {
                    AppendColumn(absolute, ref columnCount, ref currentStart,
                        columnStarts, columnLengths, maxColumns, maxFieldLength);
                    continue;
                }

                if (c == lf || c == cr)
                {
                    rowLength = absolute;
                    charsConsumed = absolute + 1;
                    if (c == lf)
                        newlineCount++;
                    if (c == cr && absolute + 1 < dataLength && Unsafe.Add(ref mutableRef, absolute + 1) == lf)
                    {
                        charsConsumed++;
                        newlineCount++; // Count the LF in CRLF
                    }
                    rowEnded = true;
                    return true;
                }
            }

            position += CharsPerIteration;
        }

        // Handle remaining with AVX2 path
        return TrySimdParseUtf16Avx2(
            ref mutableRef, dataLength, delimiter, quote, lf, cr,
            ref position, ref inQuotes, ref skipNextQuote,
            ref columnCount, ref currentStart, ref rowLength, ref charsConsumed, ref newlineCount, ref rowEnded, ref quoteStartPosition,
            columnStarts, columnLengths, maxColumns, allowNewlinesInsideQuotes, enableQuotedFields, maxFieldLength);
    }
#endif

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
        int maxColumns,
        int? maxFieldLength)
    {
        if (columnCount + 1 > maxColumns)
            ThrowTooManyColumns(maxColumns);

        int fieldLength = delimiterIndex - currentStart;
        if (maxFieldLength.HasValue && fieldLength > maxFieldLength.Value)
            ThrowFieldTooLong(maxFieldLength.Value, fieldLength);

        starts[columnCount] = currentStart;
        lengths[columnCount] = fieldLength;
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
        int maxColumns,
        int? maxFieldLength)
    {
        if (columnCount + 1 > maxColumns)
            ThrowTooManyColumns(maxColumns);

        int fieldLength = rowLength - currentStart;
        if (maxFieldLength.HasValue && fieldLength > maxFieldLength.Value)
            ThrowFieldTooLong(maxFieldLength.Value, fieldLength);

        starts[columnCount] = currentStart;
        lengths[columnCount] = fieldLength;
        columnCount++;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ApplyTrimming<T>(
        ref T mutableRef,
        Span<int> starts,
        Span<int> lengths,
        int columnCount,
        T quote,
        T space,
        T tab)
        where T : unmanaged, IEquatable<T>
    {
        for (int i = 0; i < columnCount; i++)
        {
            int start = starts[i];
            int length = lengths[i];

            if (length == 0)
                continue;

            // Check if field is quoted - if so, skip trimming
            bool isQuoted = length >= 2 &&
                           Unsafe.Add(ref mutableRef, start).Equals(quote) &&
                           Unsafe.Add(ref mutableRef, start + length - 1).Equals(quote);

            if (isQuoted)
                continue;

            int trimStart = start;
            int trimEnd = start + length;

            // Trim leading whitespace
            while (trimStart < trimEnd)
            {
                T c = Unsafe.Add(ref mutableRef, trimStart);
                if (!c.Equals(space) && !c.Equals(tab))
                    break;
                trimStart++;
            }

            // Trim trailing whitespace
            while (trimEnd > trimStart)
            {
                T c = Unsafe.Add(ref mutableRef, trimEnd - 1);
                if (!c.Equals(space) && !c.Equals(tab))
                    break;
                trimEnd--;
            }

            starts[i] = trimStart;
            lengths[i] = trimEnd - trimStart;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowTooManyColumns(int maxColumns)
    {
        throw new CsvException(
            CsvErrorCode.TooManyColumns,
            $"Row has more than {maxColumns} columns");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowFieldTooLong(int maxFieldLength, int actualLength)
    {
        throw new CsvException(
            CsvErrorCode.ParseError,
            $"Field length {actualLength} exceeds maximum allowed length of {maxFieldLength}");
    }
}
