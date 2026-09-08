using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using HeroParser.SeparatedValues.Core;

namespace HeroParser.SeparatedValues.Reading.Shared;

/// <summary>
/// Per-row parser for UTF-16 (char) and UTF-8 (byte) spans: parses exactly one row and reports how much
/// input it consumed.
/// </summary>
/// <remarks>
/// <para>
/// SIMD work is delegated to <see cref="CsvRowBatchScanner"/> in its single-row mode, so the library has
/// one SIMD front end. The scalar loop here serves configurations that front end declines (an escape
/// character, a non-ASCII delimiter or quote, SIMD off or unavailable) and re-parses any row the scanner
/// flagged, so every exception, message and position comes from one place.
/// </para>
/// <para>
/// SIMD parsing techniques inspired by Sep (https://github.com/nietras/Sep) by nietras, which pioneered
/// bitmask-based quote-aware SIMD parsing for CSV.
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
    /// <para>
    /// The SIMD path needs <paramref name="columnEnds"/> to hold at least
    /// <see cref="CsvRowBatchScanner.MinEndsCapacity"/> entries for <see cref="CsvReadOptions.MaxColumnCount"/>;
    /// a smaller buffer takes the scalar loop.
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

        // columnEnds[0] = -1 (virtual position before first column)
        columnEnds[0] = -1;

        // SIMD path: one single-row scan. A flagged row (limit violation, unterminated quote) falls through
        // to the scalar loop, which throws the exception with its message and position.
        if (CsvRowBatchScanner.IsSupportedForRow(options)
            && columnEnds.Length >= CsvRowBatchScanner.MinEndsCapacity(options.MaxColumnCount)
            && TryScanRow<T, TTrack, TQuotePolicy>(data, options, columnEnds, out var scanned))
        {
            return scanned;
        }

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
        bool skipNextChar = false;

        for (int i = 0; i < data.Length; i++)
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
    /// One single-row scan by <see cref="CsvRowBatchScanner"/> over the whole span. Returns false when
    /// the scanner flagged the row, so the caller's scalar loop re-parses it for its exception.
    /// </summary>
    /// <remarks>
    /// The scanner writes the same ends-only encoding this parser returns: <c>columnEnds[0] = -1</c>, then
    /// one entry per delimiter, then the row end. A blank line records no row; the per-row contract for it
    /// is one empty column with the terminator consumed, which is produced here.
    /// </remarks>
    private static bool TryScanRow<T, TTrack, TQuotePolicy>(
        ReadOnlySpan<T> data,
        CsvReadOptions options,
        Span<int> columnEnds,
        out CsvRowParseResult result)
        where T : unmanaged, IEquatable<T>
        where TTrack : struct
        where TQuotePolicy : struct
    {
        Span<int> rowStarts = stackalloc int[2];
        Span<int> sourceLines = stackalloc int[1];

        int rows = CsvRowBatchScanner.Scan<T, TTrack, TQuotePolicy>(
            data,
            start: 0,
            startSourceLine: 0,
            isFinalBlock: true,
            singleRow: true,
            options,
            columnEnds,
            rowStarts,
            sourceLines,
            out int consumed,
            out int newlineCount,
            out int errorRowStart);

        if (errorRowStart >= 0 || consumed == 0)
        {
            result = default;
            return false;
        }

        if (rows == 1)
        {
            int columnCount = rowStarts[1] - 1;
            result = new CsvRowParseResult(columnCount, columnEnds[columnCount], consumed, newlineCount);
            return true;
        }

        // Blank line. The scanner moved the sentinel past the terminator; restore the per-row encoding.
        columnEnds[0] = -1;
        columnEnds[1] = 0;
        result = new CsvRowParseResult(1, 0, consumed, newlineCount);
        return true;
    }

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
    internal static T CastFromChar<T>(char c) where T : unmanaged
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
