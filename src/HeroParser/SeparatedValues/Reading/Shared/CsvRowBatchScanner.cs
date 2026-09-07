using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using HeroParser.SeparatedValues.Core;

namespace HeroParser.SeparatedValues.Reading.Shared;

/// <summary>
/// Scan-ahead row scanner for UTF-8 spans. One SIMD pass records the column ends of as many
/// complete rows as fit in a pooled buffer, so the row reader only pays per-row cost for index
/// arithmetic instead of restarting the parser on every row.
/// </summary>
/// <remarks>
/// <para>
/// Layout of the <c>ends</c> buffer: each row occupies a contiguous run
/// <c>[sentinel, delimiter positions..., rowEnd]</c> of absolute offsets, where the sentinel is
/// <c>rowStart - 1</c>. <c>rowStarts[r]</c> is the index of row <c>r</c>'s sentinel and
/// <c>rowStarts[rowCount]</c> terminates the list, so row <c>r</c> has
/// <c>rowStarts[r + 1] - rowStarts[r] - 1</c> columns. This is the ends-only encoding already used by
/// <see cref="Rows.CsvRow{T}"/>, in absolute rather than row-relative coordinates.
/// </para>
/// <para>
/// Error handling is delegated: when the scanner detects a violation (too many columns, oversize
/// field, disallowed newline inside quotes, unterminated quote at end of data) it stops, reports
/// the offending row's start through <c>errorRowStart</c>, and the reader re-parses that row with
/// <see cref="CsvRowParser.ParseRow{T, TTrack, TQuotePolicy}"/>, which throws exactly the exception
/// the per-row path always has.
/// </para>
/// <para>
/// Only the <see cref="byte"/> element type, no comment or escape character, and an AVX2 or
/// AVX-512BW capable CPU are supported. Callers check <see cref="IsSupported"/> and otherwise stay on
/// the per-row path.
/// </para>
/// </remarks>
internal static class CsvRowBatchScanner
{
    /// <summary>Default <c>ends</c> capacity: 16 KB of ints, comfortably L1-resident, about 160 rows of 25 columns.</summary>
    public const int DEFAULT_ENDS_CAPACITY = 4096;

    private const int MAX_CHUNK = 64;

    /// <summary>
    /// Writes a chunk can add at worst: one entry per event plus one extra per row close, so
    /// <c>2 * chunk</c>, plus the final row close at end of data. Reserved before every chunk.
    /// </summary>
    private const int CHUNK_RESERVE = (2 * MAX_CHUNK) + 2;

    /// <summary>
    /// Smallest <c>ends</c> capacity that guarantees any row within <paramref name="maxColumns"/> fits
    /// in an empty batch: the row's own entries plus the per-chunk reserve.
    /// </summary>
    public static int MinEndsCapacity(int maxColumns) => maxColumns + 2 + CHUNK_RESERVE;

    /// <summary>Rows have at least two entries each, plus the per-chunk reserve of row closes.</summary>
    public static int RowStartsCapacity(int endsCapacity) => (endsCapacity / 2) + MAX_CHUNK + 2;

    public static bool IsSupported(CsvReadOptions options) =>
        options.UseSimdIfAvailable
        && !options.EscapeCharacter.HasValue
        && !options.CommentCharacter.HasValue
        && (HardwareCapabilities.Avx512BWIsSupported || HardwareCapabilities.Avx2IsSupported);

    /// <summary>Mutable scan state shared by the vector front ends and the mask-level state machine.</summary>
    private ref struct ScanState
    {
        public int RowStart;            // absolute start of the row being scanned
        public int CurRowEndsStart;     // index of the current row's sentinel in ends
        public int RowCount;            // complete rows recorded so far
        public int SourceLine;          // 1-based physical line at the scan position (TrackLineNumbers only)
        public int CurRowStartLine;     // SourceLine when the current row started
        public int ErrorRowStart;       // -1, or the start of a row the per-row parser must re-parse
        public bool InQuotes;
        public bool SkipNextQuote;
        public bool PendingCrInQuotes;
        public bool SkipLeadingLf;      // previous chunk closed a row on a CR whose LF is the next chunk's first byte
    }

    /// <summary>
    /// Scans from <paramref name="start"/> and fills <paramref name="ends"/> / <paramref name="rowStarts"/>
    /// with complete rows.
    /// </summary>
    /// <returns>The number of complete rows recorded.</returns>
    public static int Scan<TTrack, TQuotePolicy>(
        ReadOnlySpan<byte> data,
        int start,
        int startSourceLine,
        CsvReadOptions options,
        Span<int> ends,
        Span<int> rowStarts,
        Span<int> sourceLines,
        out int nextPosition,
        out int nextSourceLine,
        out int errorRowStart)
        where TTrack : struct
        where TQuotePolicy : struct
    {
        var st = new ScanState
        {
            RowStart = start,
            CurRowEndsStart = 0,
            RowCount = 0,
            SourceLine = startSourceLine,
            CurRowStartLine = startSourceLine,
            ErrorRowStart = -1
        };
        ends[0] = start - 1;
        int endsCount = 1;

        int position = HardwareCapabilities.Avx512BWIsSupported
            ? ScanAvx512<TTrack, TQuotePolicy>(data, start, options, ends, rowStarts, sourceLines, ref st, ref endsCount)
            : ScanAvx2<TTrack, TQuotePolicy>(data, start, options, ends, rowStarts, sourceLines, ref st, ref endsCount);

        bool reachedEnd = st.ErrorRowStart < 0 && position >= data.Length;
        if (reachedEnd)
        {
            if (typeof(TQuotePolicy) == typeof(QuotesEnabled) && st.InQuotes)
                st.ErrorRowStart = st.RowStart;
            else if (st.RowStart < data.Length)
                CloseRow<TTrack>(data.Length, isCr: false, terminatesLine: false, data, options, ends, rowStarts, sourceLines, ref st, ref endsCount);
        }

        // Drop a partial row (batch full or error row) so the next call re-scans it from its start.
        rowStarts[st.RowCount] = st.CurRowEndsStart;

        errorRowStart = st.ErrorRowStart;
        if (errorRowStart >= 0)
        {
            nextPosition = errorRowStart;
            nextSourceLine = st.CurRowStartLine;
        }
        else if (reachedEnd)
        {
            nextPosition = data.Length;
            nextSourceLine = st.SourceLine;
        }
        else
        {
            nextPosition = st.RowStart;
            nextSourceLine = st.CurRowStartLine;
        }

        return st.RowCount;
    }

    // ---------------------------------------------------------------------------------------------
    // Vector front ends. They own the hot locals (position, endsCount) and only hand off to the
    // shared state machine for chunks that contain line endings or quote activity.
    // ---------------------------------------------------------------------------------------------

    private static int ScanAvx512<TTrack, TQuotePolicy>(
        ReadOnlySpan<byte> data,
        int start,
        CsvReadOptions options,
        Span<int> ends,
        Span<int> rowStarts,
        Span<int> sourceLines,
        ref ScanState st,
        ref int endsCountRef)
        where TTrack : struct
        where TQuotePolicy : struct
    {
        const int N = 64;
        ref byte dataRef = ref MemoryMarshal.GetReference(data);
        int length = data.Length;
        byte quote = (byte)options.Quote;
        var delimV = Vector512.Create((byte)options.Delimiter);
        var quoteV = Vector512.Create(quote);
        var lfV = Vector512.Create((byte)'\n');
        var crV = Vector512.Create((byte)'\r');

        int position = start;
        int endsCount = endsCountRef;
        int rowStartsLimit = rowStarts.Length - 1;

        while (position + N <= length)
        {
            if (endsCount + CHUNK_RESERVE > ends.Length || st.RowCount + N > rowStartsLimit)
                break;

            // Unquoted 4-vector block: newline-free stretches take the bare path four vectors at a time.
            if (typeof(TQuotePolicy) == typeof(QuotesDisabled)
                && position + (4 * N) <= length
                && endsCount + (4 * CHUNK_RESERVE) <= ends.Length
                && st.RowCount + (4 * N) <= rowStartsLimit)
            {
                ref byte blockRef = ref Unsafe.Add(ref dataRef, position);
                var c0 = Vector512.LoadUnsafe(ref blockRef);
                var c1 = Vector512.LoadUnsafe(ref Unsafe.Add(ref blockRef, N));
                var c2 = Vector512.LoadUnsafe(ref Unsafe.Add(ref blockRef, 2 * N));
                var c3 = Vector512.LoadUnsafe(ref Unsafe.Add(ref blockRef, 3 * N));

                var le0 = Vector512.Equals(c0, lfV) | Vector512.Equals(c0, crV);
                var le1 = Vector512.Equals(c1, lfV) | Vector512.Equals(c1, crV);
                var le2 = Vector512.Equals(c2, lfV) | Vector512.Equals(c2, crV);
                var le3 = Vector512.Equals(c3, lfV) | Vector512.Equals(c3, crV);

                if ((le0 | le1 | le2 | le3).ExtractMostSignificantBits() == 0)
                {
                    AppendDelimiters(Vector512.Equals(c0, delimV).ExtractMostSignificantBits(), position, ends, ref endsCount);
                    AppendDelimiters(Vector512.Equals(c1, delimV).ExtractMostSignificantBits(), position + N, ends, ref endsCount);
                    AppendDelimiters(Vector512.Equals(c2, delimV).ExtractMostSignificantBits(), position + (2 * N), ends, ref endsCount);
                    AppendDelimiters(Vector512.Equals(c3, delimV).ExtractMostSignificantBits(), position + (3 * N), ends, ref endsCount);
                    position += 4 * N;
                    continue;
                }

                // A line ending is somewhere in the block: dispatch each vector in order.
                endsCount = DispatchChunk<TTrack, TQuotePolicy>(c0, le0, delimV, quoteV, lfV, quote, position, N, data, options, ends, rowStarts, sourceLines, ref st, endsCount);
                if (st.ErrorRowStart >= 0) break;
                position += N;
                endsCount = DispatchChunk<TTrack, TQuotePolicy>(c1, le1, delimV, quoteV, lfV, quote, position, N, data, options, ends, rowStarts, sourceLines, ref st, endsCount);
                if (st.ErrorRowStart >= 0) break;
                position += N;
                endsCount = DispatchChunk<TTrack, TQuotePolicy>(c2, le2, delimV, quoteV, lfV, quote, position, N, data, options, ends, rowStarts, sourceLines, ref st, endsCount);
                if (st.ErrorRowStart >= 0) break;
                position += N;
                endsCount = DispatchChunk<TTrack, TQuotePolicy>(c3, le3, delimV, quoteV, lfV, quote, position, N, data, options, ends, rowStarts, sourceLines, ref st, endsCount);
                if (st.ErrorRowStart >= 0) break;
                position += N;
                continue;
            }

            var chunk = Vector512.LoadUnsafe(ref Unsafe.Add(ref dataRef, position));
            var le = Vector512.Equals(chunk, lfV) | Vector512.Equals(chunk, crV);
            endsCount = DispatchChunk<TTrack, TQuotePolicy>(chunk, le, delimV, quoteV, lfV, quote, position, N, data, options, ends, rowStarts, sourceLines, ref st, endsCount);
            if (st.ErrorRowStart >= 0) break;
            position += N;
        }

        if (st.ErrorRowStart < 0 && position + N > length)
            position = ScanTail<TTrack, TQuotePolicy>(data, position, options, ends, rowStarts, sourceLines, ref st, ref endsCount);

        endsCountRef = endsCount;
        return position;
    }

    /// <summary>
    /// Processes one 64-byte vector: bare delimiter appends when nothing but delimiters is present,
    /// otherwise the shared state machine. Returns the new <c>endsCount</c>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int DispatchChunk<TTrack, TQuotePolicy>(
        Vector512<byte> chunk,
        Vector512<byte> lineEndings,
        Vector512<byte> delimV,
        Vector512<byte> quoteV,
        Vector512<byte> lfV,
        byte quote,
        int chunkBase,
        int chunkSize,
        ReadOnlySpan<byte> data,
        CsvReadOptions options,
        Span<int> ends,
        Span<int> rowStarts,
        Span<int> sourceLines,
        ref ScanState st,
        int endsCount)
        where TTrack : struct
        where TQuotePolicy : struct
    {
        ulong lem = lineEndings.ExtractMostSignificantBits();
        ulong dm = Vector512.Equals(chunk, delimV).ExtractMostSignificantBits();
        ulong qm = 0;
        if (typeof(TQuotePolicy) == typeof(QuotesEnabled))
            qm = Vector512.Equals(chunk, quoteV).ExtractMostSignificantBits();

        if (lem == 0 && (typeof(TQuotePolicy) == typeof(QuotesDisabled) || (qm == 0 && !st.InQuotes && !st.SkipNextQuote)))
        {
            AppendDelimiters(dm, chunkBase, ends, ref endsCount);
            return endsCount;
        }

        ulong lfm = Vector512.Equals(chunk, lfV).ExtractMostSignificantBits();
        return ProcessEventChunk<TTrack, TQuotePolicy>(dm, lem, qm, lfm, quote, chunkBase, chunkSize, data, options, ends, rowStarts, sourceLines, ref st, endsCount);
    }

    private static int ScanAvx2<TTrack, TQuotePolicy>(
        ReadOnlySpan<byte> data,
        int start,
        CsvReadOptions options,
        Span<int> ends,
        Span<int> rowStarts,
        Span<int> sourceLines,
        ref ScanState st,
        ref int endsCountRef)
        where TTrack : struct
        where TQuotePolicy : struct
    {
        const int N = 32;
        ref byte dataRef = ref MemoryMarshal.GetReference(data);
        int length = data.Length;
        byte quote = (byte)options.Quote;
        var delimV = Vector256.Create((byte)options.Delimiter);
        var quoteV = Vector256.Create(quote);
        var lfV = Vector256.Create((byte)'\n');
        var crV = Vector256.Create((byte)'\r');

        int position = start;
        int endsCount = endsCountRef;
        int rowStartsLimit = rowStarts.Length - 1;

        while (position + N <= length)
        {
            if (endsCount + CHUNK_RESERVE > ends.Length || st.RowCount + N > rowStartsLimit)
                break;

            if (typeof(TQuotePolicy) == typeof(QuotesDisabled)
                && position + (4 * N) <= length
                && endsCount + (4 * CHUNK_RESERVE) <= ends.Length
                && st.RowCount + (4 * N) <= rowStartsLimit)
            {
                ref byte blockRef = ref Unsafe.Add(ref dataRef, position);
                var c0 = Vector256.LoadUnsafe(ref blockRef);
                var c1 = Vector256.LoadUnsafe(ref Unsafe.Add(ref blockRef, N));
                var c2 = Vector256.LoadUnsafe(ref Unsafe.Add(ref blockRef, 2 * N));
                var c3 = Vector256.LoadUnsafe(ref Unsafe.Add(ref blockRef, 3 * N));

                var le0 = Vector256.Equals(c0, lfV) | Vector256.Equals(c0, crV);
                var le1 = Vector256.Equals(c1, lfV) | Vector256.Equals(c1, crV);
                var le2 = Vector256.Equals(c2, lfV) | Vector256.Equals(c2, crV);
                var le3 = Vector256.Equals(c3, lfV) | Vector256.Equals(c3, crV);

                if ((le0 | le1 | le2 | le3).ExtractMostSignificantBits() == 0)
                {
                    AppendDelimiters(Vector256.Equals(c0, delimV).ExtractMostSignificantBits(), position, ends, ref endsCount);
                    AppendDelimiters(Vector256.Equals(c1, delimV).ExtractMostSignificantBits(), position + N, ends, ref endsCount);
                    AppendDelimiters(Vector256.Equals(c2, delimV).ExtractMostSignificantBits(), position + (2 * N), ends, ref endsCount);
                    AppendDelimiters(Vector256.Equals(c3, delimV).ExtractMostSignificantBits(), position + (3 * N), ends, ref endsCount);
                    position += 4 * N;
                    continue;
                }

                endsCount = DispatchChunk<TTrack, TQuotePolicy>(c0, le0, delimV, quoteV, lfV, quote, position, N, data, options, ends, rowStarts, sourceLines, ref st, endsCount);
                if (st.ErrorRowStart >= 0) break;
                position += N;
                endsCount = DispatchChunk<TTrack, TQuotePolicy>(c1, le1, delimV, quoteV, lfV, quote, position, N, data, options, ends, rowStarts, sourceLines, ref st, endsCount);
                if (st.ErrorRowStart >= 0) break;
                position += N;
                endsCount = DispatchChunk<TTrack, TQuotePolicy>(c2, le2, delimV, quoteV, lfV, quote, position, N, data, options, ends, rowStarts, sourceLines, ref st, endsCount);
                if (st.ErrorRowStart >= 0) break;
                position += N;
                endsCount = DispatchChunk<TTrack, TQuotePolicy>(c3, le3, delimV, quoteV, lfV, quote, position, N, data, options, ends, rowStarts, sourceLines, ref st, endsCount);
                if (st.ErrorRowStart >= 0) break;
                position += N;
                continue;
            }

            var chunk = Vector256.LoadUnsafe(ref Unsafe.Add(ref dataRef, position));
            var le = Vector256.Equals(chunk, lfV) | Vector256.Equals(chunk, crV);
            endsCount = DispatchChunk<TTrack, TQuotePolicy>(chunk, le, delimV, quoteV, lfV, quote, position, N, data, options, ends, rowStarts, sourceLines, ref st, endsCount);
            if (st.ErrorRowStart >= 0) break;
            position += N;
        }

        if (st.ErrorRowStart < 0 && position + N > length)
            position = ScanTail<TTrack, TQuotePolicy>(data, position, options, ends, rowStarts, sourceLines, ref st, ref endsCount);

        endsCountRef = endsCount;
        return position;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int DispatchChunk<TTrack, TQuotePolicy>(
        Vector256<byte> chunk,
        Vector256<byte> lineEndings,
        Vector256<byte> delimV,
        Vector256<byte> quoteV,
        Vector256<byte> lfV,
        byte quote,
        int chunkBase,
        int chunkSize,
        ReadOnlySpan<byte> data,
        CsvReadOptions options,
        Span<int> ends,
        Span<int> rowStarts,
        Span<int> sourceLines,
        ref ScanState st,
        int endsCount)
        where TTrack : struct
        where TQuotePolicy : struct
    {
        ulong lem = lineEndings.ExtractMostSignificantBits();
        ulong dm = Vector256.Equals(chunk, delimV).ExtractMostSignificantBits();
        ulong qm = 0;
        if (typeof(TQuotePolicy) == typeof(QuotesEnabled))
            qm = Vector256.Equals(chunk, quoteV).ExtractMostSignificantBits();

        if (lem == 0 && (typeof(TQuotePolicy) == typeof(QuotesDisabled) || (qm == 0 && !st.InQuotes && !st.SkipNextQuote)))
        {
            AppendDelimiters(dm, chunkBase, ends, ref endsCount);
            return endsCount;
        }

        ulong lfm = Vector256.Equals(chunk, lfV).ExtractMostSignificantBits();
        return ProcessEventChunk<TTrack, TQuotePolicy>(dm, lem, qm, lfm, quote, chunkBase, chunkSize, data, options, ends, rowStarts, sourceLines, ref st, endsCount);
    }

    /// <summary>
    /// Scans the final bytes that do not fill a vector by building the same masks scalar-side and
    /// running the shared state machine once. Returns the position reached.
    /// </summary>
    private static int ScanTail<TTrack, TQuotePolicy>(
        ReadOnlySpan<byte> data,
        int position,
        CsvReadOptions options,
        Span<int> ends,
        Span<int> rowStarts,
        Span<int> sourceLines,
        ref ScanState st,
        ref int endsCount)
        where TTrack : struct
        where TQuotePolicy : struct
    {
        int tail = data.Length - position;
        if (tail <= 0)
            return position;

        if (endsCount + CHUNK_RESERVE > ends.Length || st.RowCount + MAX_CHUNK > rowStarts.Length - 1)
            return position;

        byte delimiter = (byte)options.Delimiter;
        byte quote = (byte)options.Quote;
        ulong dm = 0, lfm = 0, crm = 0, qm = 0;
        for (int i = 0; i < tail; i++)
        {
            byte b = data[position + i];
            ulong bit = 1ul << i;
            if (b == delimiter) dm |= bit;
            else if (b == (byte)'\n') lfm |= bit;
            else if (b == (byte)'\r') crm |= bit;
            else if (typeof(TQuotePolicy) == typeof(QuotesEnabled) && b == quote) qm |= bit;
        }

        endsCount = ProcessEventChunk<TTrack, TQuotePolicy>(dm, lfm | crm, qm, lfm, quote, position, tail, data, options, ends, rowStarts, sourceLines, ref st, endsCount);
        return st.ErrorRowStart < 0 ? data.Length : position;
    }

    // ---------------------------------------------------------------------------------------------
    // Shared mask-level state machine.
    // ---------------------------------------------------------------------------------------------

    /// <summary>Bare append of one column end per set bit; used when a chunk holds nothing but delimiters.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AppendDelimiters(ulong delimiterMask, int chunkBase, Span<int> ends, ref int endsCount)
    {
        while (delimiterMask != 0)
        {
            int bit = BitOperations.TrailingZeroCount(delimiterMask);
            delimiterMask &= delimiterMask - 1;
            ends[endsCount++] = chunkBase + bit;
        }
    }

    /// <summary>
    /// Processes one chunk that contains line endings and/or quote activity and returns the new
    /// <c>endsCount</c>. On return, <c>st.ErrorRowStart</c> is set if the scan must stop for the reader
    /// to re-parse a row.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int ProcessEventChunk<TTrack, TQuotePolicy>(
        ulong dm,
        ulong lem,
        ulong qm,
        ulong lfm,
        byte quote,
        int chunkBase,
        int chunkSize,
        ReadOnlySpan<byte> data,
        CsvReadOptions options,
        Span<int> ends,
        Span<int> rowStarts,
        Span<int> sourceLines,
        ref ScanState st,
        int endsCount)
        where TTrack : struct
        where TQuotePolicy : struct
    {
        if (st.SkipLeadingLf)
        {
            // The LF half of a CRLF that straddled the chunk boundary; the row start already skips it.
            st.SkipLeadingLf = false;
            lem &= ~1ul;
            lfm &= ~1ul;
        }

        ulong crm = lem & ~lfm;
        ulong quotedNewlines = 0;

        if (typeof(TQuotePolicy) == typeof(QuotesEnabled))
        {
            bool hasDoubledQuotes = (qm & (qm >> 1)) != 0;
            if (!hasDoubledQuotes && qm != 0 && (qm & (1ul << (chunkSize - 1))) != 0)
            {
                int next = chunkBase + chunkSize;
                if (next < data.Length && data[next] == quote)
                    hasDoubledQuotes = true;
            }

            if (hasDoubledQuotes || st.SkipNextQuote || !HardwareCapabilities.PclmulqdqIsSupported)
            {
                return ProcessEventChunkSequential<TTrack>(dm | lem | qm, quote, chunkBase, chunkSize, data, options, ends, rowStarts, sourceLines, ref st, endsCount);
            }

            ulong inQuotes = qm != 0
                ? ComputeInQuotesMask(qm, st.InQuotes)
                : (st.InQuotes ? ulong.MaxValue : 0ul);

            if (!options.AllowNewlinesInsideQuotes && (lem & inQuotes) != 0)
            {
                st.ErrorRowStart = st.RowStart;
                return endsCount;
            }

            if (typeof(TTrack) == typeof(TrackLineNumbers))
            {
                // One bit per physical line ending inside quotes: every CR, plus every LF not preceded by
                // a CR (in this chunk, or carried in from the previous one). Credited to the row that
                // contains it as rows close below, so earlier rows in the chunk keep their start line.
                ulong lfInside = lfm & inQuotes;
                ulong crInside = crm & inQuotes;
                quotedNewlines = crInside | (lfInside & ~(crInside << 1));
                if (st.PendingCrInQuotes)
                    quotedNewlines &= ~1ul;
                st.PendingCrInQuotes = (crInside & (1ul << (chunkSize - 1))) != 0;
            }

            dm &= ~inQuotes;
            lem &= ~inQuotes;
            crm &= ~inQuotes;

            if ((BitOperations.PopCount(qm) & 1) != 0)
                st.InQuotes = !st.InQuotes;
        }

        ulong events = dm | lem;
        while (events != 0)
        {
            int bit = BitOperations.TrailingZeroCount(events);
            events &= events - 1;
            int pos = chunkBase + bit;

            if (((lem >> bit) & 1) == 0)
            {
                ends[endsCount++] = pos;
                continue;
            }

            if (typeof(TTrack) == typeof(TrackLineNumbers) && quotedNewlines != 0)
            {
                ulong below = quotedNewlines & ((1ul << bit) - 1);
                st.SourceLine += BitOperations.PopCount(below);
                quotedNewlines ^= below;
            }

            bool isCr = ((crm >> bit) & 1) != 0;
            if (!CloseRow<TTrack>(pos, isCr, terminatesLine: true, data, options, ends, rowStarts, sourceLines, ref st, ref endsCount))
                return endsCount;

            // CRLF: CloseRow consumed the LF; drop its event bit, or flag it if it is the next chunk's first byte.
            if (st.RowStart == pos + 2)
            {
                if (bit + 1 < chunkSize)
                    events &= ~(1ul << (bit + 1));
                else
                    st.SkipLeadingLf = true;
            }
        }

        // In-quote line endings after the last row close belong to the row still open.
        if (typeof(TTrack) == typeof(TrackLineNumbers))
            st.SourceLine += BitOperations.PopCount(quotedNewlines);

        return endsCount;
    }

    /// <summary>
    /// Sequential fallback for chunks with doubled (escaped) quotes, a carried-in skipped quote, or
    /// no PCLMULQDQ. Mirrors the per-row parser's slow path, but closes rows instead of returning.
    /// </summary>
    private static int ProcessEventChunkSequential<TTrack>(
        ulong mask,
        byte quote,
        int chunkBase,
        int chunkSize,
        ReadOnlySpan<byte> data,
        CsvReadOptions options,
        Span<int> ends,
        Span<int> rowStarts,
        Span<int> sourceLines,
        ref ScanState st,
        int endsCount)
        where TTrack : struct
    {
        byte delimiter = (byte)options.Delimiter;
        const byte lf = (byte)'\n';
        const byte cr = (byte)'\r';

        while (mask != 0)
        {
            int bit = BitOperations.TrailingZeroCount(mask);
            mask &= mask - 1;
            int pos = chunkBase + bit;
            byte c = data[pos];

            if (c == quote)
            {
                if (st.SkipNextQuote)
                {
                    st.SkipNextQuote = false;
                    continue;
                }

                if (st.InQuotes && pos + 1 < data.Length && data[pos + 1] == quote)
                {
                    st.SkipNextQuote = true;
                    continue;
                }

                st.InQuotes = !st.InQuotes;
                if (typeof(TTrack) == typeof(TrackLineNumbers))
                    st.PendingCrInQuotes = false;
                continue;
            }

            if (st.InQuotes)
            {
                if (c == lf || c == cr)
                {
                    if (!options.AllowNewlinesInsideQuotes)
                    {
                        st.ErrorRowStart = st.RowStart;
                        return endsCount;
                    }

                    if (typeof(TTrack) == typeof(TrackLineNumbers))
                        UpdateNewlineCountInQuotes(c, lf, cr, ref st.PendingCrInQuotes, ref st.SourceLine);
                }
                continue;
            }

            if (c == delimiter)
            {
                ends[endsCount++] = pos;
                continue;
            }

            if (!CloseRow<TTrack>(pos, c == cr, terminatesLine: true, data, options, ends, rowStarts, sourceLines, ref st, ref endsCount))
                return endsCount;

            if (st.RowStart == pos + 2)
            {
                if (bit + 1 < chunkSize)
                    mask &= ~(1ul << (bit + 1));
                else
                    st.SkipLeadingLf = true;
            }
        }

        return endsCount;
    }

    /// <summary>
    /// Closes the current row at <paramref name="rowEnd"/>. Blank rows are dropped. Sets up the next
    /// row's sentinel. Returns false when the row violates a limit and must be re-parsed by the
    /// per-row parser for its exception.
    /// </summary>
    private static bool CloseRow<TTrack>(
        int rowEnd,
        bool isCr,
        bool terminatesLine,
        ReadOnlySpan<byte> data,
        CsvReadOptions options,
        Span<int> ends,
        Span<int> rowStarts,
        Span<int> sourceLines,
        ref ScanState st,
        ref int endsCount)
        where TTrack : struct
    {
        if (rowEnd != st.RowStart)
        {
            int delimiterCount = endsCount - st.CurRowEndsStart - 1;
            if (delimiterCount + 1 > options.MaxColumnCount)
            {
                st.ErrorRowStart = st.RowStart;
                return false;
            }

            ends[endsCount++] = rowEnd;

            if (options.MaxFieldSize.HasValue && !FieldLengthsWithinLimit(ends, st.CurRowEndsStart, endsCount, options.MaxFieldSize.Value))
            {
                st.ErrorRowStart = st.RowStart;
                return false;
            }

            rowStarts[st.RowCount] = st.CurRowEndsStart;
            if (typeof(TTrack) == typeof(TrackLineNumbers))
                sourceLines[st.RowCount] = st.CurRowStartLine;
            st.RowCount++;
            st.CurRowEndsStart = endsCount;
        }

        int nextRowStart = rowEnd;
        if (terminatesLine)
        {
            nextRowStart++;
            if (isCr && nextRowStart < data.Length && data[nextRowStart] == (byte)'\n')
                nextRowStart++;

            if (typeof(TTrack) == typeof(TrackLineNumbers))
                st.SourceLine++;
        }

        ends[st.CurRowEndsStart] = nextRowStart - 1;
        endsCount = st.CurRowEndsStart + 1;
        st.RowStart = nextRowStart;
        st.CurRowStartLine = st.SourceLine;
        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool FieldLengthsWithinLimit(Span<int> ends, int sentinelIndex, int endsCount, int maxFieldLength)
    {
        for (int i = sentinelIndex; i + 1 < endsCount; i++)
        {
            if (ends[i + 1] - ends[i] - 1 > maxFieldLength)
                return false;
        }
        return true;
    }

    // ---------------------------------------------------------------------------------------------
    // Quote-state helpers (same technique as CsvRowParser).
    // ---------------------------------------------------------------------------------------------

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong ComputeInQuotesMask(ulong quoteMask, bool prevInQuotes)
    {
        var quoteMaskVec = Vector128.CreateScalarUnsafe((long)quoteMask);
        var allOnes = Vector128.CreateScalarUnsafe(-1L);
        var prefixXor = Pclmulqdq.CarrylessMultiply(quoteMaskVec, allOnes, 0);
        ulong inQuotesMask = (ulong)prefixXor.GetElement(0) << 1;
        return prevInQuotes ? ~inQuotesMask : inQuotesMask;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void UpdateNewlineCountInQuotes(byte current, byte lf, byte cr, ref bool pendingCrInQuotes, ref int newlineCount)
    {
        if (pendingCrInQuotes)
        {
            pendingCrInQuotes = false;
            if (current == lf)
                return;
        }

        if (current == cr)
        {
            newlineCount++;
            pendingCrInQuotes = true;
        }
        else if (current == lf)
        {
            newlineCount++;
        }
    }
}
