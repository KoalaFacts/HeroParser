using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using HeroParser.SeparatedValues.Core;

// CLMUL-based branchless quote masking (Phase 1 optimization)
// Uses PCLMULQDQ instruction to compute prefix XOR in O(1), avoiding per-quote iteration

namespace HeroParser.SeparatedValues.Reading.Shared;

/// <summary>
/// Unified row parser for both UTF-16 (char) and UTF-8 (byte) spans.
/// Uses generic specialization for zero-overhead abstraction.
/// </summary>
/// <remarks>
/// <para>
/// SIMD parsing techniques inspired by Sep (https://github.com/nietras/Sep) by nietras,
/// which pioneered bitmask-based quote-aware SIMD parsing for CSV.
/// </para>
/// <para>
/// <strong>Implementation Note:</strong> The SIMD parsing methods (TrySimdParseUtf8, TrySimdParseUtf16)
/// contain similar parsing state machine logic with SIMD-specific vector operations.
/// Future refactoring could extract the common state machine into a shared method using delegates or source
/// generation to reduce maintenance burden, though this must be balanced against performance impact.
/// </para>
/// </remarks>
internal static class CsvRowParser
{
    /// <summary>
    /// Parses a single row from CSV data (convenience overload with runtime boolean).
    /// For best performance, use the generic overload with TTrack type parameter directly.
    /// </summary>
    /// <remarks>
    /// Uses Ends-only storage: columnEnds[0] = -1, columnEnds[1..N] = delimiter positions.
    /// Column start = columnEnds[index] + 1, length = columnEnds[index+1] - columnEnds[index] - 1.
    /// </remarks>
    public static CsvRowParseResult ParseRow<T>(
        ReadOnlySpan<T> data,
        CsvReadOptions options,
        Span<int> columnEnds,
        bool trackLineNumbers)
        where T : unmanaged, IEquatable<T>
    {
        return trackLineNumbers
            ? ParseRow<T, TrackLineNumbers>(data, options, columnEnds)
            : ParseRow<T, NoTrackLineNumbers>(data, options, columnEnds);
    }

    /// <summary>
    /// Parses a single row from CSV data with compile-time line tracking specialization.
    /// TTrack should be either TrackLineNumbers or NoTrackLineNumbers.
    /// </summary>
    /// <remarks>
    /// Uses Ends-only storage: columnEnds[0] = -1, columnEnds[1..N] = delimiter positions.
    /// Column start = columnEnds[index] + 1, length = columnEnds[index+1] - columnEnds[index] - 1.
    /// </remarks>
    public static CsvRowParseResult ParseRow<T, TTrack>(
        ReadOnlySpan<T> data,
        CsvReadOptions options,
        Span<int> columnEnds)
        where T : unmanaged, IEquatable<T>
        where TTrack : struct
    {
        // Dispatch to compile-time specialized version based on EnableQuotedFields
        return options.EnableQuotedFields
            ? ParseRow<T, TTrack, QuotesEnabled>(data, options, columnEnds)
            : ParseRow<T, TTrack, QuotesDisabled>(data, options, columnEnds);
    }

    /// <summary>
    /// Parses a single row from CSV data with compile-time line tracking and quote handling specialization.
    /// TTrack should be either TrackLineNumbers or NoTrackLineNumbers.
    /// TQuotePolicy should be either QuotesEnabled or QuotesDisabled.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Uses Ends-only storage: columnEnds[0] = -1, columnEnds[1..N] = delimiter positions.
    /// Column start = columnEnds[index] + 1, length = columnEnds[index+1] - columnEnds[index] - 1.
    /// </para>
    /// <para>
    /// The TQuotePolicy generic parameter enables JIT constant folding. When TQuotePolicy is QuotesDisabled,
    /// the JIT compiler eliminates all quote-handling branches, producing optimal machine code for unquoted CSV.
    /// </para>
    /// </remarks>
    public static CsvRowParseResult ParseRow<T, TTrack, TQuotePolicy>(
        ReadOnlySpan<T> data,
        CsvReadOptions options,
        Span<int> columnEnds)
        where T : unmanaged, IEquatable<T>
        where TTrack : struct
        where TQuotePolicy : struct
    {
        if (data.IsEmpty)
            return new CsvRowParseResult(0, 0, 0, 0);

        ref readonly T dataRef = ref MemoryMarshal.GetReference(data);
        // Safety: Unsafe.Add requires ref T, not ref readonly T. This reference is only used
        // for reading via Unsafe.Add - no writes occur through mutableRef.
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

                        bool isCr = lineEnd.Equals(cr);
                        bool isLf = lineEnd.Equals(lf);

                        if (isCr && skipPos + 1 < data.Length && Unsafe.Add(ref mutableRef, skipPos + 1).Equals(lf))
                        {
                            consumed++;
                            if (typeof(TTrack) == typeof(TrackLineNumbers))
                                commentNewlines++;
                        }
                        else if (typeof(TTrack) == typeof(TrackLineNumbers) && (isLf || isCr))
                        {
                            commentNewlines++;
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
        int newlineCount = 0; // Track number of line endings encountered
        bool rowEnded = false;
        int quoteStartPosition = -1; // Track where the opening quote was found
        bool pendingCrInQuotes = false;

        // columnEnds[0] = -1 (virtual position before first column)
        columnEnds[0] = -1;

        // SIMD fast path (if enabled and no escape character - escape handling requires sequential processing)
        if (options.UseSimdIfAvailable && !hasEscapeChar)
        {
            TrySimdParse<T, TTrack, TQuotePolicy>(
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
                ref pendingCrInQuotes,
                ref rowEnded,
                ref quoteStartPosition,
                columnEnds,
                options.MaxColumnCount,
                options.AllowNewlinesInsideQuotes,
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

                // JIT eliminates this entire block when TQuotePolicy is QuotesDisabled
                if (typeof(TQuotePolicy) == typeof(QuotesEnabled) && c.Equals(quote))
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
                    if (typeof(TTrack) == typeof(TrackLineNumbers))
                        pendingCrInQuotes = false;
                    continue;
                }

                // JIT eliminates this entire block when TQuotePolicy is QuotesDisabled
                if (typeof(TQuotePolicy) == typeof(QuotesEnabled) && inQuotes && !options.AllowNewlinesInsideQuotes &&
                    (c.Equals(lf) || c.Equals(cr)))
                {
                    throw new CsvException(
                        CsvErrorCode.ParseError,
                        "Newlines inside quoted fields are disabled. Enable AllowNewlinesInsideQuotes to parse them.");
                }

                // JIT eliminates this entire block when TQuotePolicy is QuotesDisabled
                if (typeof(TQuotePolicy) == typeof(QuotesEnabled) && inQuotes)
                {
                    UpdateNewlineCountInQuotes<T, TTrack>(c, lf, cr, ref pendingCrInQuotes, ref newlineCount);
                    continue;
                }

                if (c.Equals(delimiter))
                {
                    AppendColumn(i, ref columnCount, ref currentStart,
                        columnEnds, options.MaxColumnCount, options.MaxFieldSize);
                }
                else if (c.Equals(lf) || c.Equals(cr))
                {
                    CompleteRowAtLineEnding<T, TTrack>(
                        ref mutableRef,
                        data.Length,
                        i,
                        lf,
                        cr,
                        c,
                        ref rowLength,
                        ref charsConsumed,
                        ref newlineCount,
                        ref rowEnded);
                    break;
                }
            }
        }

        if (!rowEnded)
        {
            rowLength = data.Length;
            charsConsumed = rowLength;
        }

        // JIT eliminates this entire block when TQuotePolicy is QuotesDisabled
        if (typeof(TQuotePolicy) == typeof(QuotesEnabled) && inQuotes)
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
            columnEnds, options.MaxColumnCount, options.MaxFieldSize);

        // Note: TrimFields is handled at read time in the row types.
        // This is because ends-only storage cannot independently adjust column starts
        // without affecting adjacent columns.

        return new CsvRowParseResult(columnCount, rowLength, charsConsumed, newlineCount);
    }

    /// <summary>
    /// Computes the "inside quotes" mask using CLMUL (carry-less multiplication).
    /// </summary>
    /// <remarks>
    /// <para>
    /// This method uses the PCLMULQDQ instruction to compute prefix XOR in O(1),
    /// avoiding per-quote iteration. The carry-less multiplication with all-ones
    /// produces a running XOR that toggles at each quote position.
    /// </para>
    /// <para>
    /// Example: quoteMask = 0b01010000 (quotes at positions 4 and 6)
    /// Result after CLMUL and shift: 0b00111100 (inside quotes at positions 4,5,6)
    /// </para>
    /// </remarks>
    /// <param name="quoteMask">Bitmask where 1 indicates a quote character at that position in the chunk.</param>
    /// <param name="prevInQuotes">Whether parsing was inside a quoted field at the start of this chunk.</param>
    /// <returns>Bitmask where 1 = position is inside quotes, 0 = position is outside quotes.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint ComputeInQuotesMaskClmul(uint quoteMask, bool prevInQuotes)
    {
        // CLMUL(quoteMask, 0xFFFFFFFFFFFFFFFF) computes the prefix XOR
        // Result: c[i] = XOR of quoteMask[0..i] (inclusive)
        // We want: inQuotes[i] = parity of quotes BEFORE position i = c[i-1]
        // So we shift left by 1 after CLMUL
        var quoteMaskVec = Vector128.CreateScalarUnsafe((long)quoteMask);
        var allOnes = Vector128.CreateScalarUnsafe(-1L);

        var prefixXor = Pclmulqdq.CarrylessMultiply(quoteMaskVec, allOnes, 0);
        ulong clmulResult = (ulong)prefixXor.GetElement(0);

        // Shift left by 1: new[0] = 0, new[i] = c[i-1] for i > 0
        // This gives us the parity of quotes BEFORE each position
        uint inQuotesMask = (uint)(clmulResult << 1);

        // If we started inside quotes, flip the entire mask
        // This handles: inQuotes[i] = prevInQuotes XOR c[i-1]
        if (prevInQuotes)
            inQuotesMask = ~inQuotesMask;

        return inQuotesMask;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int CountLineEndingsInQuotes(uint lfInsideQuotes, uint crInsideQuotes, ref bool pendingCrInQuotes, uint lastBitMask)
    {
        int count = BitOperations.PopCount(lfInsideQuotes) + BitOperations.PopCount(crInsideQuotes);
        uint crlfMask = crInsideQuotes & (lfInsideQuotes >> 1);
        count -= BitOperations.PopCount(crlfMask);

        if (pendingCrInQuotes && (lfInsideQuotes & 1u) != 0)
            count--;

        pendingCrInQuotes = (crInsideQuotes & lastBitMask) != 0;
        return count;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void UpdateNewlineCountInQuotes<T, TTrack>(
        T current,
        T lf,
        T cr,
        ref bool pendingCrInQuotes,
        ref int newlineCount)
        where T : unmanaged, IEquatable<T>
        where TTrack : struct
    {
        if (typeof(TTrack) != typeof(TrackLineNumbers))
            return;

        if (pendingCrInQuotes)
        {
            pendingCrInQuotes = false;
            if (current.Equals(lf))
                return;
        }

        if (current.Equals(cr))
        {
            newlineCount++;
            pendingCrInQuotes = true;
        }
        else if (current.Equals(lf))
        {
            newlineCount++;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void CompleteRowAtLineEnding<T, TTrack>(
        ref T mutableRef,
        int dataLength,
        int absolute,
        T lf,
        T cr,
        T lineEndChar,
        ref int rowLength,
        ref int charsConsumed,
        ref int newlineCount,
        ref bool rowEnded)
        where T : unmanaged, IEquatable<T>
        where TTrack : struct
    {
        rowLength = absolute;
        charsConsumed = absolute + 1;
        if (lineEndChar.Equals(cr) && absolute + 1 < dataLength && Unsafe.Add(ref mutableRef, absolute + 1).Equals(lf))
        {
            charsConsumed++;
            if (typeof(TTrack) == typeof(TrackLineNumbers))
                newlineCount++;
        }
        else if (typeof(TTrack) == typeof(TrackLineNumbers))
        {
            newlineCount++;
        }
        rowEnded = true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TrySimdParse<T, TTrack, TQuotePolicy>(
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
        ref bool pendingCrInQuotes,
        ref bool rowEnded,
        ref int quoteStartPosition,
        Span<int> columnEnds,
        int maxColumns,
        bool allowNewlinesInsideQuotes,
        int? maxFieldLength)
        where T : unmanaged, IEquatable<T>
        where TTrack : struct
        where TQuotePolicy : struct
    {
        if (typeof(T) == typeof(byte))
        {
            return TrySimdParseUtf8<TTrack, TQuotePolicy>(
                ref Unsafe.As<T, byte>(ref mutableRef),
                dataLength,
                Unsafe.As<T, byte>(ref delimiter),
                Unsafe.As<T, byte>(ref quote),
                Unsafe.As<T, byte>(ref lf),
                Unsafe.As<T, byte>(ref cr),
                ref position, ref inQuotes, ref skipNextQuote,
                ref columnCount, ref currentStart, ref rowLength, ref charsConsumed, ref newlineCount, ref pendingCrInQuotes, ref rowEnded, ref quoteStartPosition,
                columnEnds, maxColumns, allowNewlinesInsideQuotes, maxFieldLength);
        }
        if (typeof(T) == typeof(char))
        {
            return TrySimdParseUtf16<TTrack, TQuotePolicy>(
                ref Unsafe.As<T, char>(ref mutableRef),
                dataLength,
                Unsafe.As<T, char>(ref delimiter),
                Unsafe.As<T, char>(ref quote),
                Unsafe.As<T, char>(ref lf),
                Unsafe.As<T, char>(ref cr),
                ref position, ref inQuotes, ref skipNextQuote,
                ref columnCount, ref currentStart, ref rowLength, ref charsConsumed, ref newlineCount, ref pendingCrInQuotes, ref rowEnded, ref quoteStartPosition,
                columnEnds, maxColumns, allowNewlinesInsideQuotes, maxFieldLength);
        }
        return false;
    }

    /// <summary>
    /// SIMD-accelerated UTF-8 CSV row parser using AVX2 instructions.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Processes 32 bytes per iteration using AVX2 vector operations. Uses bitmask-based
    /// detection of delimiters, quotes, and line endings. When quotes are enabled and
    /// PCLMULQDQ is available, uses carry-less multiplication for O(1) quote state tracking.
    /// </para>
    /// <para>
    /// Fast paths:
    /// <list type="bullet">
    /// <item>Unquoted rows with only delimiters: batch delimiter processing</item>
    /// <item>Rows with delimiters + line endings: process up to line ending</item>
    /// <item>Quoted rows without doubled quotes: CLMUL-based quote masking</item>
    /// </list>
    /// Falls back to sequential processing for doubled quotes (escaped quotes).
    /// </para>
    /// </remarks>
    /// <typeparam name="TTrack">TrackLineNumbers or NoTrackLineNumbers for compile-time specialization.</typeparam>
    /// <typeparam name="TQuotePolicy">QuotesEnabled or QuotesDisabled for compile-time specialization.</typeparam>
    /// <returns>True if SIMD processing was attempted, false if AVX2 is not supported.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TrySimdParseUtf8<TTrack, TQuotePolicy>(
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
        ref bool pendingCrInQuotes,
        ref bool rowEnded,
        ref int quoteStartPosition,
        Span<int> columnEnds,
        int maxColumns,
        bool allowNewlinesInsideQuotes,
        int? maxFieldLength)
        where TTrack : struct
        where TQuotePolicy : struct
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
            var delimMatch = Avx2.CompareEqual(chunk, delimiterVec);
            var lfMatch = Avx2.CompareEqual(chunk, lfVec);
            var crMatch = Avx2.CompareEqual(chunk, crVec);
            var lineEndingMatch = Avx2.Or(lfMatch, crMatch);

            Vector256<byte> specials;
            Vector256<byte> quoteMatch = default;
            // JIT eliminates the else branch when TQuotePolicy is QuotesEnabled
            // JIT eliminates the if branch when TQuotePolicy is QuotesDisabled
            if (typeof(TQuotePolicy) == typeof(QuotesEnabled))
            {
                quoteMatch = Avx2.CompareEqual(chunk, quoteVec);
                specials = Avx2.Or(Avx2.Or(delimMatch, quoteMatch), lineEndingMatch);
            }
            else
            {
                specials = Avx2.Or(delimMatch, lineEndingMatch);
            }

            uint mask = (uint)Avx2.MoveMask(specials);

            // Fast paths when quotes are disabled - JIT eliminates this entire block when TQuotePolicy is QuotesEnabled
            if (typeof(TQuotePolicy) == typeof(QuotesDisabled))
            {
                uint delimMask = (uint)Avx2.MoveMask(delimMatch);
                uint lineEndingMask = (uint)Avx2.MoveMask(lineEndingMatch);

                // FAST PATH 1: Only separators, no line endings
                if (delimMask == mask)
                {
                    int startColCountFast = columnCount;

                    while (delimMask != 0)
                    {
                        int bit = BitOperations.TrailingZeroCount(delimMask);
                        delimMask &= delimMask - 1;
                        int absolute = position + bit;
                        AppendColumnUnchecked(absolute, ref columnCount, ref currentStart, columnEnds);
                    }

                    // Validate once per chunk instead of per delimiter
                    if (columnCount > maxColumns)
                        ThrowTooManyColumns(maxColumns);

                    if (maxFieldLength.HasValue)
                    {
                        // Check all fields added in this chunk
                        // Ends-only format: fieldLength = end - previousEnd - 1
                        for (int i = startColCountFast; i < columnCount; i++)
                        {
                            int fieldLength = columnEnds[i + 1] - columnEnds[i] - 1;
                            if (fieldLength > maxFieldLength.Value)
                                ThrowFieldTooLong(maxFieldLength.Value, fieldLength);
                        }
                    }

                    position += Vector256<byte>.Count;
                    continue;
                }

                // FAST PATH 2: Separators + line endings
                if ((delimMask | lineEndingMask) == mask)
                {
                    // Find first line ending position - only process delimiters before it
                    int lineEndBit = lineEndingMask != 0 ? BitOperations.TrailingZeroCount(lineEndingMask) : Vector256<byte>.Count;

                    int startColCountFast = columnCount;

                    // Process delimiters that come BEFORE the first line ending
                    uint delimsToProcess = delimMask;
                    while (delimsToProcess != 0)
                    {
                        int bit = BitOperations.TrailingZeroCount(delimsToProcess);
                        if (bit >= lineEndBit) break; // Stop at line ending
                        delimsToProcess &= delimsToProcess - 1;
                        int absolute = position + bit;
                        AppendColumnUnchecked(absolute, ref columnCount, ref currentStart, columnEnds);
                    }

                    // Validate once per chunk/segment instead of per delimiter
                    if (columnCount > maxColumns)
                        ThrowTooManyColumns(maxColumns);

                    if (maxFieldLength.HasValue)
                    {
                        // Check all fields added in this segment
                        for (int i = startColCountFast; i < columnCount; i++)
                        {
                            int fieldLength = columnEnds[i + 1] - columnEnds[i] - 1;
                            if (fieldLength > maxFieldLength.Value)
                                ThrowFieldTooLong(maxFieldLength.Value, fieldLength);
                        }
                    }

                    // Check if there's a line ending in this chunk
                    if (lineEndingMask != 0)
                    {
                        int absolute = position + lineEndBit;
                        byte c = Unsafe.Add(ref mutableRef, absolute);
                        CompleteRowAtLineEnding<byte, TTrack>(
                            ref mutableRef,
                            dataLength,
                            absolute,
                            lf,
                            cr,
                            c,
                            ref rowLength,
                            ref charsConsumed,
                            ref newlineCount,
                            ref rowEnded);
                        return true;
                    }

                    position += Vector256<byte>.Count;
                    continue;
                }
            }

            // CLMUL fast path for quoted fields (when PCLMULQDQ is available)
            // JIT eliminates this entire block when TQuotePolicy is QuotesDisabled
            if (typeof(TQuotePolicy) == typeof(QuotesEnabled) && Pclmulqdq.IsSupported)
            {
                uint quoteMask = (uint)Avx2.MoveMask(quoteMatch);
                uint delimMask = (uint)Avx2.MoveMask(delimMatch);
                uint lineEndingMask = (uint)Avx2.MoveMask(lineEndingMatch);

                // CLMUL FAST PATH: No doubled quotes in this chunk
                // Doubled quotes (escaped) require sequential processing, but are rare
                bool hasDoubledQuotes = (quoteMask & (quoteMask >> 1)) != 0;

                // Also check for doubled quote at chunk boundary (last quote + first char of next chunk)
                if (!hasDoubledQuotes && quoteMask != 0 && (quoteMask & 0x80000000u) != 0)
                {
                    int nextPos = position + 32;
                    if (nextPos < dataLength && Unsafe.Add(ref mutableRef, nextPos) == quote)
                    {
                        // Potential doubled quote spanning chunks - use slow path
                        hasDoubledQuotes = true;
                    }
                }

                if (!hasDoubledQuotes && !skipNextQuote)
                {
                    // Compute "inside quotes" mask using CLMUL prefix XOR
                    uint inQuotesMask = quoteMask != 0
                        ? ComputeInQuotesMaskClmul(quoteMask, inQuotes)
                        : (inQuotes ? 0xFFFFFFFF : 0);

                    // Filter: only process delimiters/line endings OUTSIDE quotes
                    uint filteredDelimMask = delimMask & ~inQuotesMask;
                    uint filteredLineEndMask = lineEndingMask & ~inQuotesMask;

                    // Check for disallowed newlines inside quotes
                    if (!allowNewlinesInsideQuotes && (lineEndingMask & inQuotesMask) != 0)
                    {
                        throw new CsvException(
                            CsvErrorCode.ParseError,
                            "Newlines inside quoted fields are disabled. Enable AllowNewlinesInsideQuotes to parse them.");
                    }

                    // Count newlines inside quotes (if tracking line numbers)
                    if (typeof(TTrack) == typeof(TrackLineNumbers))
                    {
                        uint lfMask = (uint)Avx2.MoveMask(lfMatch);
                        uint crMask = (uint)Avx2.MoveMask(crMatch);
                        uint lfInsideQuotes = lfMask & inQuotesMask;
                        uint crInsideQuotes = crMask & inQuotesMask;
                        newlineCount += CountLineEndingsInQuotes(
                            lfInsideQuotes,
                            crInsideQuotes,
                            ref pendingCrInQuotes,
                            1u << (Vector256<byte>.Count - 1));
                    }

                    // FAST PATH 1: Only delimiters outside quotes, no line endings
                    if (filteredLineEndMask == 0)
                    {
                        // Track quote start position for error reporting
                        if (quoteMask != 0 && !inQuotes)
                        {
                            int firstQuoteBit = BitOperations.TrailingZeroCount(quoteMask);
                            quoteStartPosition = position + firstQuoteBit;
                        }

                        // Update inQuotes state for next chunk (odd number of quotes toggles state)
                        if ((BitOperations.PopCount(quoteMask) & 1) != 0)
                            inQuotes = !inQuotes;

                        while (filteredDelimMask != 0)
                        {
                            int bit = BitOperations.TrailingZeroCount(filteredDelimMask);
                            filteredDelimMask &= filteredDelimMask - 1;
                            int absolute = position + bit;
                            AppendColumn(absolute, ref columnCount, ref currentStart,
                                columnEnds, maxColumns, maxFieldLength);
                        }
                        position += Vector256<byte>.Count;
                        continue;
                    }

                    // FAST PATH 2: Delimiters + line endings outside quotes
                    int lineEndBit = BitOperations.TrailingZeroCount(filteredLineEndMask);

                    // Only count quotes BEFORE the line ending (quotes after belong to next row)
                    uint quotesInThisRow = quoteMask & ((1u << lineEndBit) - 1);

                    // Track quote start position for error reporting
                    if (quotesInThisRow != 0 && !inQuotes)
                    {
                        int firstQuoteBit = BitOperations.TrailingZeroCount(quotesInThisRow);
                        quoteStartPosition = position + firstQuoteBit;
                    }

                    // Update inQuotes state based only on quotes in this row
                    if ((BitOperations.PopCount(quotesInThisRow) & 1) != 0)
                        inQuotes = !inQuotes;

                    // Process delimiters before line ending
                    while (filteredDelimMask != 0)
                    {
                        int bit = BitOperations.TrailingZeroCount(filteredDelimMask);
                        if (bit >= lineEndBit) break;
                        filteredDelimMask &= filteredDelimMask - 1;
                        int absolute = position + bit;
                        AppendColumn(absolute, ref columnCount, ref currentStart,
                            columnEnds, maxColumns, maxFieldLength);
                    }

                    // Handle line ending
                    int lineEndAbsolute = position + lineEndBit;
                    byte lineEndChar = Unsafe.Add(ref mutableRef, lineEndAbsolute);
                    CompleteRowAtLineEnding<byte, TTrack>(
                        ref mutableRef,
                        dataLength,
                        lineEndAbsolute,
                        lf,
                        cr,
                        lineEndChar,
                        ref rowLength,
                        ref charsConsumed,
                        ref newlineCount,
                        ref rowEnded);
                    return true;
                }
            }

            // Slow path: handle quotes, line endings, and other special cases
            int startColCountSlow = columnCount;
            while (mask != 0)
            {
                int bit = BitOperations.TrailingZeroCount(mask);
                mask &= mask - 1;

                int absolute = position + bit;
                byte c = Unsafe.Add(ref mutableRef, absolute);

                // JIT eliminates this entire block when TQuotePolicy is QuotesDisabled
                if (typeof(TQuotePolicy) == typeof(QuotesEnabled) && c == quote)
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
                    if (typeof(TTrack) == typeof(TrackLineNumbers))
                        pendingCrInQuotes = false;
                    continue;
                }

                // JIT eliminates this entire block when TQuotePolicy is QuotesDisabled
                if (typeof(TQuotePolicy) == typeof(QuotesEnabled) && inQuotes && !allowNewlinesInsideQuotes && (c == lf || c == cr))
                {
                    throw new CsvException(
                        CsvErrorCode.ParseError,
                        "Newlines inside quoted fields are disabled. Enable AllowNewlinesInsideQuotes to parse them.");
                }

                // JIT eliminates this entire block when TQuotePolicy is QuotesDisabled
                if (typeof(TQuotePolicy) == typeof(QuotesEnabled) && inQuotes)
                {
                    UpdateNewlineCountInQuotes<byte, TTrack>(c, lf, cr, ref pendingCrInQuotes, ref newlineCount);
                    continue;
                }

                if (c == delimiter)
                {
                    AppendColumnUnchecked(absolute, ref columnCount, ref currentStart, columnEnds);
                    continue;
                }

                if (c == lf || c == cr)
                {
                    // Validate columns before returning
                    if (columnCount > maxColumns)
                        ThrowTooManyColumns(maxColumns);

                    if (maxFieldLength.HasValue)
                    {
                        for (int i = startColCountSlow; i < columnCount; i++)
                        {
                            int fieldLength = columnEnds[i + 1] - columnEnds[i] - 1;
                            if (fieldLength > maxFieldLength.Value)
                                ThrowFieldTooLong(maxFieldLength.Value, fieldLength);
                        }
                    }

                    CompleteRowAtLineEnding<byte, TTrack>(
                        ref mutableRef,
                        dataLength,
                        absolute,
                        lf,
                        cr,
                        c,
                        ref rowLength,
                        ref charsConsumed,
                        ref newlineCount,
                        ref rowEnded);
                    return true;
                }
            }

            // Validate columns after loop
            if (columnCount > maxColumns)
                ThrowTooManyColumns(maxColumns);

            if (maxFieldLength.HasValue)
            {
                for (int i = startColCountSlow; i < columnCount; i++)
                {
                    int fieldLength = columnEnds[i + 1] - columnEnds[i] - 1;
                    if (fieldLength > maxFieldLength.Value)
                        ThrowFieldTooLong(maxFieldLength.Value, fieldLength);
                }
            }

            position += Vector256<byte>.Count;
        }

        return true;
    }

    /// <summary>
    /// SIMD-accelerated UTF-16 CSV row parser using AVX-512 instructions.
    /// </summary>
    /// <remarks>
    /// Processes 32 chars per iteration using AVX-512 vector operations. Mirrors the AVX2 SIMD
    /// state machine but operates on 512-bit vectors.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TrySimdParseUtf16Avx512<TTrack, TQuotePolicy>(
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
        ref bool pendingCrInQuotes,
        ref bool rowEnded,
        ref int quoteStartPosition,
        Span<int> columnEnds,
        int maxColumns,
        bool allowNewlinesInsideQuotes,
        int? maxFieldLength)
        where TTrack : struct
        where TQuotePolicy : struct
    {
        if (!Avx512BW.IsSupported)
            return false;

        var delimiterVec = Vector512.Create((ushort)delimiter);
        var quoteVec = Vector512.Create((ushort)quote);
        var lfVec = Vector512.Create((ushort)lf);
        var crVec = Vector512.Create((ushort)cr);

        while (position + Vector512<ushort>.Count <= dataLength)
        {
            var chunk = Vector512.LoadUnsafe(ref Unsafe.As<char, ushort>(ref Unsafe.Add(ref mutableRef, position)));
            var delimMatch = Avx512BW.CompareEqual(chunk, delimiterVec);
            var lfMatch = Avx512BW.CompareEqual(chunk, lfVec);
            var crMatch = Avx512BW.CompareEqual(chunk, crVec);
            var lineEndingMatch = Avx512F.Or(lfMatch, crMatch);

            Vector512<ushort> specials;
            Vector512<ushort> quoteMatch = default;
            if (typeof(TQuotePolicy) == typeof(QuotesEnabled))
            {
                quoteMatch = Avx512BW.CompareEqual(chunk, quoteVec);
                specials = Avx512F.Or(Avx512F.Or(delimMatch, quoteMatch), lineEndingMatch);
            }
            else
            {
                specials = Avx512F.Or(delimMatch, lineEndingMatch);
            }

            uint mask = (uint)specials.ExtractMostSignificantBits();

            if (typeof(TQuotePolicy) == typeof(QuotesDisabled))
            {
                uint delimMask = (uint)delimMatch.ExtractMostSignificantBits();
                uint lineEndingMask = (uint)lineEndingMatch.ExtractMostSignificantBits();

                if (delimMask == mask)
                {
                    int startColCountFast = columnCount;

                    while (delimMask != 0)
                    {
                        int bit = BitOperations.TrailingZeroCount(delimMask);
                        delimMask &= delimMask - 1;
                        int absolute = position + bit;
                        AppendColumnUnchecked(absolute, ref columnCount, ref currentStart, columnEnds);
                    }

                    if (columnCount > maxColumns)
                        ThrowTooManyColumns(maxColumns);

                    if (maxFieldLength.HasValue)
                    {
                        for (int i = startColCountFast; i < columnCount; i++)
                        {
                            int fieldLength = columnEnds[i + 1] - columnEnds[i] - 1;
                            if (fieldLength > maxFieldLength.Value)
                                ThrowFieldTooLong(maxFieldLength.Value, fieldLength);
                        }
                    }

                    position += Vector512<ushort>.Count;
                    continue;
                }

                if ((delimMask | lineEndingMask) == mask)
                {
                    int lineEndBit = lineEndingMask != 0
                        ? BitOperations.TrailingZeroCount(lineEndingMask)
                        : Vector512<ushort>.Count;

                    int startColCountFast = columnCount;

                    uint delimsToProcess = delimMask;
                    while (delimsToProcess != 0)
                    {
                        int bit = BitOperations.TrailingZeroCount(delimsToProcess);
                        if (bit >= lineEndBit) break;
                        delimsToProcess &= delimsToProcess - 1;
                        int absolute = position + bit;
                        AppendColumnUnchecked(absolute, ref columnCount, ref currentStart, columnEnds);
                    }

                    if (columnCount > maxColumns)
                        ThrowTooManyColumns(maxColumns);

                    if (maxFieldLength.HasValue)
                    {
                        for (int i = startColCountFast; i < columnCount; i++)
                        {
                            int fieldLength = columnEnds[i + 1] - columnEnds[i] - 1;
                            if (fieldLength > maxFieldLength.Value)
                                ThrowFieldTooLong(maxFieldLength.Value, fieldLength);
                        }
                    }

                    if (lineEndingMask != 0)
                    {
                        int absolute = position + lineEndBit;
                        char c = Unsafe.Add(ref mutableRef, absolute);
                        CompleteRowAtLineEnding<char, TTrack>(
                            ref mutableRef,
                            dataLength,
                            absolute,
                            lf,
                            cr,
                            c,
                            ref rowLength,
                            ref charsConsumed,
                            ref newlineCount,
                            ref rowEnded);
                        return true;
                    }

                    position += Vector512<ushort>.Count;
                    continue;
                }
            }

            if (typeof(TQuotePolicy) == typeof(QuotesEnabled) && Pclmulqdq.IsSupported)
            {
                uint quoteMask = (uint)quoteMatch.ExtractMostSignificantBits();
                uint delimMask = (uint)delimMatch.ExtractMostSignificantBits();
                uint lineEndingMask = (uint)lineEndingMatch.ExtractMostSignificantBits();

                bool hasDoubledQuotes = (quoteMask & (quoteMask >> 1)) != 0;
                if (!hasDoubledQuotes && quoteMask != 0 && (quoteMask & 0x80000000u) != 0)
                {
                    int nextPos = position + Vector512<ushort>.Count;
                    if (nextPos < dataLength && Unsafe.Add(ref mutableRef, nextPos) == quote)
                    {
                        hasDoubledQuotes = true;
                    }
                }

                if (!hasDoubledQuotes && !skipNextQuote)
                {
                    uint inQuotesMask = quoteMask != 0
                        ? ComputeInQuotesMaskClmul(quoteMask, inQuotes)
                        : (inQuotes ? 0xFFFFFFFFu : 0u);

                    uint filteredDelimMask = delimMask & ~inQuotesMask;
                    uint filteredLineEndMask = lineEndingMask & ~inQuotesMask;

                    if (!allowNewlinesInsideQuotes && (lineEndingMask & inQuotesMask) != 0)
                    {
                        throw new CsvException(
                            CsvErrorCode.ParseError,
                            "Newlines inside quoted fields are disabled. Enable AllowNewlinesInsideQuotes to parse them.");
                    }

                    if (typeof(TTrack) == typeof(TrackLineNumbers))
                    {
                        uint lfMask = (uint)lfMatch.ExtractMostSignificantBits();
                        uint crMask = (uint)crMatch.ExtractMostSignificantBits();
                        uint lfInsideQuotes = lfMask & inQuotesMask;
                        uint crInsideQuotes = crMask & inQuotesMask;
                        newlineCount += CountLineEndingsInQuotes(
                            lfInsideQuotes,
                            crInsideQuotes,
                            ref pendingCrInQuotes,
                            1u << (Vector512<ushort>.Count - 1));
                    }

                    if (filteredLineEndMask == 0)
                    {
                        if (quoteMask != 0 && !inQuotes)
                        {
                            int firstQuoteBit = BitOperations.TrailingZeroCount(quoteMask);
                            quoteStartPosition = position + firstQuoteBit;
                        }

                        if ((BitOperations.PopCount(quoteMask) & 1) != 0)
                            inQuotes = !inQuotes;

                        int startColumnCount = columnCount;

                        while (filteredDelimMask != 0)
                        {
                            int bit = BitOperations.TrailingZeroCount(filteredDelimMask);
                            filteredDelimMask &= filteredDelimMask - 1;
                            int absolute = position + bit;
                            AppendColumnUnchecked(absolute, ref columnCount, ref currentStart, columnEnds);
                        }

                        if (columnCount > maxColumns)
                            ThrowTooManyColumns(maxColumns);

                        if (maxFieldLength.HasValue)
                        {
                            for (int i = startColumnCount; i < columnCount; i++)
                            {
                                int fieldLength = columnEnds[i + 1] - columnEnds[i] - 1;
                                if (fieldLength > maxFieldLength.Value)
                                    ThrowFieldTooLong(maxFieldLength.Value, fieldLength);
                            }
                        }

                        position += Vector512<ushort>.Count;
                        continue;
                    }

                    int lineEndBit = BitOperations.TrailingZeroCount(filteredLineEndMask);
                    uint quotesInThisRow = quoteMask & ((1u << lineEndBit) - 1);

                    if (quotesInThisRow != 0 && !inQuotes)
                    {
                        int firstQuoteBit = BitOperations.TrailingZeroCount(quotesInThisRow);
                        quoteStartPosition = position + firstQuoteBit;
                    }

                    if ((BitOperations.PopCount(quotesInThisRow) & 1) != 0)
                        inQuotes = !inQuotes;

                    int startColCountClmul = columnCount;

                    while (filteredDelimMask != 0)
                    {
                        int bit = BitOperations.TrailingZeroCount(filteredDelimMask);
                        if (bit >= lineEndBit) break;
                        filteredDelimMask &= filteredDelimMask - 1;
                        int absolute = position + bit;
                        AppendColumnUnchecked(absolute, ref columnCount, ref currentStart, columnEnds);
                    }

                    if (columnCount > maxColumns)
                        ThrowTooManyColumns(maxColumns);

                    if (maxFieldLength.HasValue)
                    {
                        for (int i = startColCountClmul; i < columnCount; i++)
                        {
                            int fieldLength = columnEnds[i + 1] - columnEnds[i] - 1;
                            if (fieldLength > maxFieldLength.Value)
                                ThrowFieldTooLong(maxFieldLength.Value, fieldLength);
                        }
                    }

                    int lineEndAbsolute = position + lineEndBit;
                    char lineEndChar = Unsafe.Add(ref mutableRef, lineEndAbsolute);
                    CompleteRowAtLineEnding<char, TTrack>(
                        ref mutableRef,
                        dataLength,
                        lineEndAbsolute,
                        lf,
                        cr,
                        lineEndChar,
                        ref rowLength,
                        ref charsConsumed,
                        ref newlineCount,
                        ref rowEnded);
                    return true;
                }
            }

            int startColCountSlow = columnCount;
            while (mask != 0)
            {
                int bit = BitOperations.TrailingZeroCount(mask);
                mask &= mask - 1;

                int absolute = position + bit;
                char c = Unsafe.Add(ref mutableRef, absolute);

                if (typeof(TQuotePolicy) == typeof(QuotesEnabled) && c == quote)
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
                    if (typeof(TTrack) == typeof(TrackLineNumbers))
                        pendingCrInQuotes = false;
                    continue;
                }

                if (typeof(TQuotePolicy) == typeof(QuotesEnabled) && inQuotes && !allowNewlinesInsideQuotes && (c == lf || c == cr))
                {
                    throw new CsvException(
                        CsvErrorCode.ParseError,
                        "Newlines inside quoted fields are disabled. Enable AllowNewlinesInsideQuotes to parse them.");
                }

                if (typeof(TQuotePolicy) == typeof(QuotesEnabled) && inQuotes)
                {
                    UpdateNewlineCountInQuotes<char, TTrack>(c, lf, cr, ref pendingCrInQuotes, ref newlineCount);
                    continue;
                }

                if (c == delimiter)
                {
                    AppendColumnUnchecked(absolute, ref columnCount, ref currentStart, columnEnds);
                    continue;
                }

                if (c == lf || c == cr)
                {
                    if (columnCount > maxColumns)
                        ThrowTooManyColumns(maxColumns);

                    if (maxFieldLength.HasValue)
                    {
                        for (int i = startColCountSlow; i < columnCount; i++)
                        {
                            int fieldLength = columnEnds[i + 1] - columnEnds[i] - 1;
                            if (fieldLength > maxFieldLength.Value)
                                ThrowFieldTooLong(maxFieldLength.Value, fieldLength);
                        }
                    }

                    CompleteRowAtLineEnding<char, TTrack>(
                        ref mutableRef,
                        dataLength,
                        absolute,
                        lf,
                        cr,
                        c,
                        ref rowLength,
                        ref charsConsumed,
                        ref newlineCount,
                        ref rowEnded);
                    return true;
                }
            }

            if (columnCount > maxColumns)
                ThrowTooManyColumns(maxColumns);

            if (maxFieldLength.HasValue)
            {
                for (int i = startColCountSlow; i < columnCount; i++)
                {
                    int fieldLength = columnEnds[i + 1] - columnEnds[i] - 1;
                    if (fieldLength > maxFieldLength.Value)
                        ThrowFieldTooLong(maxFieldLength.Value, fieldLength);
                }
            }

            position += Vector512<ushort>.Count;
        }

        return true;
    }

    /// <summary>
    /// SIMD-accelerated UTF-16 CSV row parser using AVX2 instructions.
    /// </summary>
    /// <remarks>
    /// Processes 16 chars per iteration using AVX2 vector operations. Mirrors the UTF-8 SIMD
    /// state machine but operates on UTF-16 code units.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TrySimdParseUtf16<TTrack, TQuotePolicy>(
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
        ref bool pendingCrInQuotes,
        ref bool rowEnded,
        ref int quoteStartPosition,
        Span<int> columnEnds,
        int maxColumns,
        bool allowNewlinesInsideQuotes,
        int? maxFieldLength)
        where TTrack : struct
        where TQuotePolicy : struct
    {
        if (Avx512BW.IsSupported)
        {
            return TrySimdParseUtf16Avx512<TTrack, TQuotePolicy>(
                ref mutableRef,
                dataLength,
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
                ref pendingCrInQuotes,
                ref rowEnded,
                ref quoteStartPosition,
                columnEnds,
                maxColumns,
                allowNewlinesInsideQuotes,
                maxFieldLength);
        }

        if (!Avx2.IsSupported)
            return false;

        var delimiterVec = Vector256.Create((ushort)delimiter);
        var quoteVec = Vector256.Create((ushort)quote);
        var lfVec = Vector256.Create((ushort)lf);
        var crVec = Vector256.Create((ushort)cr);

        while (position + Vector256<ushort>.Count <= dataLength)
        {
            var chunk = Vector256.LoadUnsafe(ref Unsafe.As<char, ushort>(ref Unsafe.Add(ref mutableRef, position)));
            var delimMatch = Avx2.CompareEqual(chunk, delimiterVec);
            var lfMatch = Avx2.CompareEqual(chunk, lfVec);
            var crMatch = Avx2.CompareEqual(chunk, crVec);
            var lineEndingMatch = Avx2.Or(lfMatch, crMatch);

            Vector256<ushort> specials;
            Vector256<ushort> quoteMatch = default;
            if (typeof(TQuotePolicy) == typeof(QuotesEnabled))
            {
                quoteMatch = Avx2.CompareEqual(chunk, quoteVec);
                specials = Avx2.Or(Avx2.Or(delimMatch, quoteMatch), lineEndingMatch);
            }
            else
            {
                specials = Avx2.Or(delimMatch, lineEndingMatch);
            }

            uint mask = specials.ExtractMostSignificantBits();

            // Fast paths when quotes are disabled - JIT eliminates this entire block when TQuotePolicy is QuotesEnabled
            if (typeof(TQuotePolicy) == typeof(QuotesDisabled))
            {
                uint delimMask = delimMatch.ExtractMostSignificantBits();
                uint lineEndingMask = lineEndingMatch.ExtractMostSignificantBits();

                // FAST PATH 1: Only separators, no line endings
                if (delimMask == mask)
                {
                    int startColCountFast = columnCount;

                    while (delimMask != 0)
                    {
                        int bit = BitOperations.TrailingZeroCount(delimMask);
                        delimMask &= delimMask - 1;
                        int absolute = position + bit;
                        AppendColumnUnchecked(absolute, ref columnCount, ref currentStart, columnEnds);
                    }

                    // Validate once per chunk instead of per delimiter
                    if (columnCount > maxColumns)
                        ThrowTooManyColumns(maxColumns);

                    if (maxFieldLength.HasValue)
                    {
                        for (int i = startColCountFast; i < columnCount; i++)
                        {
                            int fieldLength = columnEnds[i + 1] - columnEnds[i] - 1;
                            if (fieldLength > maxFieldLength.Value)
                                ThrowFieldTooLong(maxFieldLength.Value, fieldLength);
                        }
                    }

                    position += Vector256<ushort>.Count;
                    continue;
                }

                // FAST PATH 2: Separators + line endings
                if ((delimMask | lineEndingMask) == mask)
                {
                    int lineEndBit = lineEndingMask != 0
                        ? BitOperations.TrailingZeroCount(lineEndingMask)
                        : Vector256<ushort>.Count;

                    int startColCountFast = columnCount;

                    uint delimsToProcess = delimMask;
                    while (delimsToProcess != 0)
                    {
                        int bit = BitOperations.TrailingZeroCount(delimsToProcess);
                        if (bit >= lineEndBit) break;
                        delimsToProcess &= delimsToProcess - 1;
                        int absolute = position + bit;
                        AppendColumnUnchecked(absolute, ref columnCount, ref currentStart, columnEnds);
                    }

                    if (columnCount > maxColumns)
                        ThrowTooManyColumns(maxColumns);

                    if (maxFieldLength.HasValue)
                    {
                        for (int i = startColCountFast; i < columnCount; i++)
                        {
                            int fieldLength = columnEnds[i + 1] - columnEnds[i] - 1;
                            if (fieldLength > maxFieldLength.Value)
                                ThrowFieldTooLong(maxFieldLength.Value, fieldLength);
                        }
                    }

                    if (lineEndingMask != 0)
                    {
                        int absolute = position + lineEndBit;
                        char c = Unsafe.Add(ref mutableRef, absolute);
                        CompleteRowAtLineEnding<char, TTrack>(
                            ref mutableRef,
                            dataLength,
                            absolute,
                            lf,
                            cr,
                            c,
                            ref rowLength,
                            ref charsConsumed,
                            ref newlineCount,
                            ref rowEnded);
                        return true;
                    }

                    position += Vector256<ushort>.Count;
                    continue;
                }
            }

            // CLMUL fast path for quoted fields (when PCLMULQDQ is available)
            if (typeof(TQuotePolicy) == typeof(QuotesEnabled) && Pclmulqdq.IsSupported)
            {
                uint quoteMask = quoteMatch.ExtractMostSignificantBits();
                uint delimMask = delimMatch.ExtractMostSignificantBits();
                uint lineEndingMask = lineEndingMatch.ExtractMostSignificantBits();

                bool hasDoubledQuotes = (quoteMask & (quoteMask >> 1)) != 0;
                if (!hasDoubledQuotes && quoteMask != 0 && (quoteMask & 0x8000u) != 0)
                {
                    int nextPos = position + Vector256<ushort>.Count;
                    if (nextPos < dataLength && Unsafe.Add(ref mutableRef, nextPos) == quote)
                    {
                        hasDoubledQuotes = true;
                    }
                }

                if (!hasDoubledQuotes && !skipNextQuote)
                {
                    uint inQuotesMask = quoteMask != 0
                        ? ComputeInQuotesMaskClmul(quoteMask, inQuotes)
                        : (inQuotes ? 0xFFFFFFFFu : 0u);

                    uint filteredDelimMask = delimMask & ~inQuotesMask;
                    uint filteredLineEndMask = lineEndingMask & ~inQuotesMask;

                    if (!allowNewlinesInsideQuotes && (lineEndingMask & inQuotesMask) != 0)
                    {
                        throw new CsvException(
                            CsvErrorCode.ParseError,
                            "Newlines inside quoted fields are disabled. Enable AllowNewlinesInsideQuotes to parse them.");
                    }

                    if (typeof(TTrack) == typeof(TrackLineNumbers))
                    {
                        uint lfMask = lfMatch.ExtractMostSignificantBits();
                        uint crMask = crMatch.ExtractMostSignificantBits();
                        uint lfInsideQuotes = lfMask & inQuotesMask;
                        uint crInsideQuotes = crMask & inQuotesMask;
                        newlineCount += CountLineEndingsInQuotes(
                            lfInsideQuotes,
                            crInsideQuotes,
                            ref pendingCrInQuotes,
                            1u << (Vector256<ushort>.Count - 1));
                    }

                    if (filteredLineEndMask == 0)
                    {
                        if (quoteMask != 0 && !inQuotes)
                        {
                            int firstQuoteBit = BitOperations.TrailingZeroCount(quoteMask);
                            quoteStartPosition = position + firstQuoteBit;
                        }

                        if ((BitOperations.PopCount(quoteMask) & 1) != 0)
                            inQuotes = !inQuotes;

                        int startColumnCount = columnCount;

                        while (filteredDelimMask != 0)
                        {
                            int bit = BitOperations.TrailingZeroCount(filteredDelimMask);
                            filteredDelimMask &= filteredDelimMask - 1;
                            int absolute = position + bit;
                            AppendColumnUnchecked(absolute, ref columnCount, ref currentStart, columnEnds);
                        }

                        if (columnCount > maxColumns)
                            ThrowTooManyColumns(maxColumns);

                        if (maxFieldLength.HasValue)
                        {
                            for (int i = startColumnCount; i < columnCount; i++)
                            {
                                int fieldLength = columnEnds[i + 1] - columnEnds[i] - 1;
                                if (fieldLength > maxFieldLength.Value)
                                    ThrowFieldTooLong(maxFieldLength.Value, fieldLength);
                            }
                        }

                        position += Vector256<ushort>.Count;
                        continue;
                    }

                    int lineEndBit = BitOperations.TrailingZeroCount(filteredLineEndMask);
                    uint quotesInThisRow = quoteMask & ((1u << lineEndBit) - 1);

                    if (quotesInThisRow != 0 && !inQuotes)
                    {
                        int firstQuoteBit = BitOperations.TrailingZeroCount(quotesInThisRow);
                        quoteStartPosition = position + firstQuoteBit;
                    }

                    if ((BitOperations.PopCount(quotesInThisRow) & 1) != 0)
                        inQuotes = !inQuotes;

                    int startColCountClmul = columnCount;

                    while (filteredDelimMask != 0)
                    {
                        int bit = BitOperations.TrailingZeroCount(filteredDelimMask);
                        if (bit >= lineEndBit) break;
                        filteredDelimMask &= filteredDelimMask - 1;
                        int absolute = position + bit;
                        AppendColumnUnchecked(absolute, ref columnCount, ref currentStart, columnEnds);
                    }

                    if (columnCount > maxColumns)
                        ThrowTooManyColumns(maxColumns);

                    if (maxFieldLength.HasValue)
                    {
                        for (int i = startColCountClmul; i < columnCount; i++)
                        {
                            int fieldLength = columnEnds[i + 1] - columnEnds[i] - 1;
                            if (fieldLength > maxFieldLength.Value)
                                ThrowFieldTooLong(maxFieldLength.Value, fieldLength);
                        }
                    }

                    int lineEndAbsolute = position + lineEndBit;
                    char lineEndChar = Unsafe.Add(ref mutableRef, lineEndAbsolute);
                    CompleteRowAtLineEnding<char, TTrack>(
                        ref mutableRef,
                        dataLength,
                        lineEndAbsolute,
                        lf,
                        cr,
                        lineEndChar,
                        ref rowLength,
                        ref charsConsumed,
                        ref newlineCount,
                        ref rowEnded);
                    return true;
                }
            }

            int startColCountSlow = columnCount;
            while (mask != 0)
            {
                int bit = BitOperations.TrailingZeroCount(mask);
                mask &= mask - 1;

                int absolute = position + bit;
                char c = Unsafe.Add(ref mutableRef, absolute);

                if (typeof(TQuotePolicy) == typeof(QuotesEnabled) && c == quote)
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
                    if (typeof(TTrack) == typeof(TrackLineNumbers))
                        pendingCrInQuotes = false;
                    continue;
                }

                if (typeof(TQuotePolicy) == typeof(QuotesEnabled) && inQuotes && !allowNewlinesInsideQuotes && (c == lf || c == cr))
                {
                    throw new CsvException(
                        CsvErrorCode.ParseError,
                        "Newlines inside quoted fields are disabled. Enable AllowNewlinesInsideQuotes to parse them.");
                }

                if (typeof(TQuotePolicy) == typeof(QuotesEnabled) && inQuotes)
                {
                    UpdateNewlineCountInQuotes<char, TTrack>(c, lf, cr, ref pendingCrInQuotes, ref newlineCount);
                    continue;
                }

                if (c == delimiter)
                {
                    AppendColumnUnchecked(absolute, ref columnCount, ref currentStart, columnEnds);
                    continue;
                }

                if (c == lf || c == cr)
                {
                    if (columnCount > maxColumns)
                        ThrowTooManyColumns(maxColumns);

                    if (maxFieldLength.HasValue)
                    {
                        for (int i = startColCountSlow; i < columnCount; i++)
                        {
                            int fieldLength = columnEnds[i + 1] - columnEnds[i] - 1;
                            if (fieldLength > maxFieldLength.Value)
                                ThrowFieldTooLong(maxFieldLength.Value, fieldLength);
                        }
                    }

                    CompleteRowAtLineEnding<char, TTrack>(
                        ref mutableRef,
                        dataLength,
                        absolute,
                        lf,
                        cr,
                        c,
                        ref rowLength,
                        ref charsConsumed,
                        ref newlineCount,
                        ref rowEnded);
                    return true;
                }
            }

            if (columnCount > maxColumns)
                ThrowTooManyColumns(maxColumns);

            if (maxFieldLength.HasValue)
            {
                for (int i = startColCountSlow; i < columnCount; i++)
                {
                    int fieldLength = columnEnds[i + 1] - columnEnds[i] - 1;
                    if (fieldLength > maxFieldLength.Value)
                        ThrowFieldTooLong(maxFieldLength.Value, fieldLength);
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

    /// <summary>
    /// Appends a column end position (ends-only storage).
    /// Writes to columnEnds[columnCount + 1] = delimiterIndex.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AppendColumn(
        int delimiterIndex,
        ref int columnCount,
        ref int currentStart,
        Span<int> columnEnds,
        int maxColumns,
        int? maxFieldLength)
    {
        if (columnCount + 1 > maxColumns)
            ThrowTooManyColumns(maxColumns);

        int fieldLength = delimiterIndex - currentStart;
        if (maxFieldLength.HasValue && fieldLength > maxFieldLength.Value)
            ThrowFieldTooLong(maxFieldLength.Value, fieldLength);

        // store only the end position (delimiter index)
        // Column start = columnEnds[columnCount] + 1
        // Column length = columnEnds[columnCount + 1] - columnEnds[columnCount] - 1
        columnEnds[columnCount + 1] = delimiterIndex;
        columnCount++;
        currentStart = delimiterIndex + 1;
    }

    /// <summary>
    /// Appends a column end position without field-length validation (for SIMD fast paths).
    /// Column count is still bounded to avoid buffer overruns.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AppendColumnUnchecked(
        int delimiterIndex,
        ref int columnCount,
        ref int currentStart,
        Span<int> columnEnds)
    {
        if (columnCount + 1 >= columnEnds.Length)
            ThrowTooManyColumns(columnEnds.Length - 1);
        Debug.Assert((uint)(columnCount + 1) < (uint)columnEnds.Length, "Column count exceeds buffer capacity");
        columnEnds[columnCount + 1] = delimiterIndex;
        columnCount++;
        currentStart = delimiterIndex + 1;
    }

    /// <summary>
    /// Appends the final column end position (ends-only storage).
    /// Writes to columnEnds[columnCount + 1] = rowLength.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AppendFinalColumn(
        int rowLength,
        ref int columnCount,
        ref int currentStart,
        Span<int> columnEnds,
        int maxColumns,
        int? maxFieldLength)
    {
        if (columnCount + 1 > maxColumns)
            ThrowTooManyColumns(maxColumns);

        int fieldLength = rowLength - currentStart;
        if (maxFieldLength.HasValue && fieldLength > maxFieldLength.Value)
            ThrowFieldTooLong(maxFieldLength.Value, fieldLength);

        // store only the end position (row length for final column)
        columnEnds[columnCount + 1] = rowLength;
        columnCount++;
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
