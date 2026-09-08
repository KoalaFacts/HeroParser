using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using HeroParser.SeparatedValues.Core;

namespace HeroParser.SeparatedValues.Reading.Shared;

/// <summary>
/// Scan-ahead row scanner for UTF-8 (<see cref="byte"/>) and UTF-16 (<see cref="char"/>) spans. One
/// SIMD pass records the column ends of as many complete rows as fit in a pooled buffer, so the row
/// reader only pays per-row cost for index arithmetic instead of restarting the parser on every row.
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
/// One generic front end (<c>ScanCore</c>) serves both element types and both vector widths. The
/// width is a type parameter implementing <see cref="ISimdLanes{TVec}"/> with static abstract members,
/// which the JIT resolves at compile time exactly like <c>TQuotePolicy</c>. UTF-16 chunks are packed to
/// bytes with saturation on load, so after the load everything is byte-vector work. The front end
/// reduces every chunk to bit masks (delimiter, line ending, LF, quote) and hands chunks with line
/// endings, or quote activity the inline quoted path cannot take, to one mask-level state machine.
/// Chunks holding nothing but delimiters take a bare append loop on true locals.
/// </para>
/// <para>
/// Error handling is delegated: when the scanner detects a violation (too many columns, oversize
/// field, disallowed newline inside quotes, unterminated quote at end of data) it stops, reports
/// the offending row's start through <c>errorRowStart</c>, and the reader re-parses that row with
/// <see cref="CsvRowParser.ParseRow{T, TTrack, TQuotePolicy}"/>, which throws exactly the exception
/// the per-row path always has.
/// </para>
/// <para>
/// Requires an ASCII delimiter and quote, no comment or escape character, and an AVX2 or AVX-512BW
/// capable CPU. Callers check <see cref="IsSupported"/> and otherwise stay on the per-row path.
/// </para>
/// </remarks>
internal static class CsvRowBatchScanner
{
    /// <summary>Default <c>ends</c> capacity: 16 KB of ints, comfortably L1-resident, about 160 rows of 25 columns.</summary>
    public const int DEFAULT_ENDS_CAPACITY = 4096;

    /// <summary>Largest chunk any width produces (64 bytes or 64 chars).</summary>
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

    /// <summary>
    /// The scanner handles the default configuration space: ASCII delimiter and quote (the UTF-16 path
    /// packs chars to bytes with saturation, so a non-ASCII special could alias a saturated char), no
    /// comment or escape character, SIMD on and available.
    /// </summary>
    public static bool IsSupported(CsvReadOptions options) =>
        options.UseSimdIfAvailable
        && options.Delimiter < 0x80
        && options.Quote < 0x80
        && !options.EscapeCharacter.HasValue
        && !options.CommentCharacter.HasValue
        && (HardwareCapabilities.Avx512BWIsSupported || HardwareCapabilities.Avx2IsSupported);

    // ---------------------------------------------------------------------------------------------
    // Scan context: everything one scan needs, passed by ref so the mask-level routines take a
    // handful of parameters. The hot loop in ScanCore keeps its own locals (position, endsCount) and
    // only touches the context for state that changes per row.
    // ---------------------------------------------------------------------------------------------

    private ref struct ScanContext<T> where T : unmanaged, IEquatable<T>
    {
        public ReadOnlySpan<T> Data;
        public Span<int> Ends;
        public Span<int> RowStarts;
        public Span<int> SourceLines;
        public CsvReadOptions Options;
        public T Delimiter;
        public T Quote;
        public T Lf;
        public T Cr;

        public int RowStart;            // absolute start of the row being scanned
        public int CurRowEndsStart;     // index of the current row's sentinel in Ends
        public int RowCount;            // complete rows recorded so far
        public int SourceLine;          // 1-based physical line at the scan position (TrackLineNumbers only)
        public int CurRowStartLine;     // SourceLine when the current row started
        public int ErrorRowStart;       // -1, or the start of a row the per-row parser must re-parse
        public bool InQuotes;
        public bool SkipNextQuote;
        public bool PendingCrInQuotes;
        public bool SkipLeadingLf;      // previous chunk closed a row on a CR whose LF is the next chunk's first element
    }

    /// <summary>
    /// Scans from <paramref name="start"/> and fills <paramref name="ends"/> / <paramref name="rowStarts"/>
    /// with complete rows. With <paramref name="isFinalBlock"/> true the data ends the input: a trailing
    /// row without a line ending is emitted and an open quote is an error. With it false the data is a
    /// streaming buffer that more data may follow: the trailing row (open quote or not) is left
    /// unconsumed for the next call and <paramref name="nextPosition"/> stops at its start.
    /// </summary>
    /// <returns>The number of complete rows recorded.</returns>
    public static int Scan<T, TTrack, TQuotePolicy>(
        ReadOnlySpan<T> data,
        int start,
        int startSourceLine,
        bool isFinalBlock,
        CsvReadOptions options,
        Span<int> ends,
        Span<int> rowStarts,
        Span<int> sourceLines,
        out int nextPosition,
        out int nextSourceLine,
        out int errorRowStart)
        where T : unmanaged, IEquatable<T>
        where TTrack : struct
        where TQuotePolicy : struct
    {
        var ctx = new ScanContext<T>
        {
            Data = data,
            Ends = ends,
            RowStarts = rowStarts,
            SourceLines = sourceLines,
            Options = options,
            Delimiter = CastFromChar<T>(options.Delimiter),
            Quote = CastFromChar<T>(options.Quote),
            Lf = CastFromChar<T>('\n'),
            Cr = CastFromChar<T>('\r'),
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
            ? ScanCore<T, Vector512<byte>, Avx512Lanes, TTrack, TQuotePolicy>(start, ref ctx, ref endsCount)
            : ScanCore<T, Vector256<byte>, Avx2Lanes, TTrack, TQuotePolicy>(start, ref ctx, ref endsCount);

        bool reachedEnd = ctx.ErrorRowStart < 0 && position >= data.Length;
        if (reachedEnd && !isFinalBlock)
        {
            // A streaming buffer: whatever follows the last line ending is a partial row until more data
            // (or end of stream) says otherwise, so leave it for the next call.
            reachedEnd = false;

            // A CR as the window's last element may be the first half of a CRLF whose LF arrives with the
            // next read. Closing on the CR now would make that LF look like a blank line (and, with line
            // tracking, count a line that does not exist), so rewind to the CR and let the next call see
            // the pair together.
            if (ctx.RowStart == data.Length && data.Length > start && data[^1].Equals(ctx.Cr))
                RewindTrailingCr<T, TTrack>(data.Length - 1, ref ctx);
        }
        else if (reachedEnd)
        {
            if (typeof(TQuotePolicy) == typeof(QuotesEnabled) && ctx.InQuotes)
                ctx.ErrorRowStart = ctx.RowStart;
            else if (ctx.RowStart < data.Length)
                CloseRow<T, TTrack>(data.Length, isCr: false, terminatesLine: false, ref ctx, ref endsCount);
        }

        // Drop a partial row (batch full or error row) so the next call re-scans it from its start.
        rowStarts[ctx.RowCount] = ctx.CurRowEndsStart;

        errorRowStart = ctx.ErrorRowStart;
        if (errorRowStart >= 0)
        {
            nextPosition = errorRowStart;
            nextSourceLine = ctx.CurRowStartLine;
        }
        else if (reachedEnd)
        {
            nextPosition = data.Length;
            nextSourceLine = ctx.SourceLine;
        }
        else
        {
            nextPosition = ctx.RowStart;
            nextSourceLine = ctx.CurRowStartLine;
        }

        return ctx.RowCount;
    }

    /// <summary>
    /// Undoes the row close performed on the CR at <paramref name="crPosition"/> (the window's last
    /// element) so the scan resumes there. The CR either terminated a recorded row, which is popped, or
    /// a blank line, whose consumption is reverted.
    /// </summary>
    private static void RewindTrailingCr<T, TTrack>(int crPosition, ref ScanContext<T> ctx)
        where T : unmanaged, IEquatable<T>
        where TTrack : struct
    {
        bool recordedRowEndsHere = ctx.RowCount > 0 && ctx.Ends[ctx.CurRowEndsStart - 1] == crPosition;
        if (recordedRowEndsHere)
        {
            ctx.RowCount--;
            ctx.CurRowEndsStart = ctx.RowStarts[ctx.RowCount];
            ctx.RowStart = ctx.Ends[ctx.CurRowEndsStart] + 1;
            if (typeof(TTrack) == typeof(TrackLineNumbers))
            {
                ctx.CurRowStartLine = ctx.SourceLines[ctx.RowCount];
                ctx.SourceLine = ctx.CurRowStartLine;
            }
        }
        else
        {
            ctx.RowStart = crPosition;
            ctx.Ends[ctx.CurRowEndsStart] = crPosition - 1;
            if (typeof(TTrack) == typeof(TrackLineNumbers))
            {
                ctx.SourceLine--;
                ctx.CurRowStartLine = ctx.SourceLine;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static T CastFromChar<T>(char c) where T : unmanaged
    {
        if (typeof(T) == typeof(byte))
        {
            byte b = (byte)c;
            return Unsafe.As<byte, T>(ref b);
        }

        return Unsafe.As<char, T>(ref c);
    }

    // ---------------------------------------------------------------------------------------------
    // SIMD width adapters. Static abstract members on a struct type parameter compile to direct
    // intrinsic calls, so ScanCore is written once and specialised per width by the JIT.
    // ---------------------------------------------------------------------------------------------

    private interface ISimdLanes<TVec> where TVec : struct
    {
        /// <summary>Elements per chunk: bytes for UTF-8, chars for UTF-16 (two half vectors packed).</summary>
        static abstract int Count { get; }
        static abstract TVec Create(byte value);
        static abstract TVec LoadBytes(ref byte source);
        /// <summary>
        /// Loads <see cref="Count"/> chars as two <c>short</c> vectors and packs them to one byte vector
        /// with unsigned saturation: chars 0x0100-0x7FFF become 0xFF, chars at or above 0x8000 (negative
        /// as <c>short</c>) become 0x00; neither can equal an ASCII special. The pack interleaves 64-bit
        /// lanes, so one permute restores source order.
        /// </summary>
        static abstract TVec LoadChars(ref short source);
        static abstract TVec Equals(TVec left, TVec right);
        static abstract TVec Or(TVec left, TVec right);
        static abstract ulong Mask(TVec vector);
    }

    private readonly struct Avx512Lanes : ISimdLanes<Vector512<byte>>
    {
        public static int Count => 64;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector512<byte> Create(byte value) => Vector512.Create(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector512<byte> LoadBytes(ref byte source) => Vector512.LoadUnsafe(ref source);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector512<byte> LoadChars(ref short source)
        {
            var lo = Vector512.LoadUnsafe(ref source);
            var hi = Vector512.LoadUnsafe(ref Unsafe.Add(ref source, 32));
            // PackUnsignedSaturate emits, per 128-bit lane, 8 bytes of lo then 8 bytes of hi; gather the lo
            // qwords first and the hi qwords second to recover source order.
            var laneOrder = Vector512.Create(0L, 2, 4, 6, 1, 3, 5, 7);
            return Avx512F.PermuteVar8x64(Avx512BW.PackUnsignedSaturate(lo, hi).AsInt64(), laneOrder).AsByte();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector512<byte> Equals(Vector512<byte> left, Vector512<byte> right) => Vector512.Equals(left, right);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector512<byte> Or(Vector512<byte> left, Vector512<byte> right) => left | right;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong Mask(Vector512<byte> vector) => vector.ExtractMostSignificantBits();
    }

    private readonly struct Avx2Lanes : ISimdLanes<Vector256<byte>>
    {
        public static int Count => 32;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector256<byte> Create(byte value) => Vector256.Create(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector256<byte> LoadBytes(ref byte source) => Vector256.LoadUnsafe(ref source);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector256<byte> LoadChars(ref short source)
        {
            var lo = Vector256.LoadUnsafe(ref source);
            var hi = Vector256.LoadUnsafe(ref Unsafe.Add(ref source, 16));
            return Avx2.Permute4x64(Avx2.PackUnsignedSaturate(lo, hi).AsInt64(), 0b11_01_10_00).AsByte();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector256<byte> Equals(Vector256<byte> left, Vector256<byte> right) => Vector256.Equals(left, right);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector256<byte> Or(Vector256<byte> left, Vector256<byte> right) => left | right;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong Mask(Vector256<byte> vector) => vector.ExtractMostSignificantBits();
    }

    // ---------------------------------------------------------------------------------------------
    // The one vector front end. It owns the hot locals (position, endsCount, the ends span) and only
    // hands off to the state machine for chunks that contain line endings or quote activity.
    // ---------------------------------------------------------------------------------------------

    /// <summary>Loads one chunk at <paramref name="position"/>, packing chars to bytes for UTF-16.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static TVec Load<T, TVec, TLanes>(ref T dataRef, int position)
        where T : unmanaged
        where TVec : struct
        where TLanes : struct, ISimdLanes<TVec>
    {
        ref T at = ref Unsafe.Add(ref dataRef, position);
        return typeof(T) == typeof(byte)
            ? TLanes.LoadBytes(ref Unsafe.As<T, byte>(ref at))
            : TLanes.LoadChars(ref Unsafe.As<T, short>(ref at));
    }

    private static int ScanCore<T, TVec, TLanes, TTrack, TQuotePolicy>(int start, ref ScanContext<T> ctx, ref int endsCountRef)
        where T : unmanaged, IEquatable<T>
        where TVec : struct
        where TLanes : struct, ISimdLanes<TVec>
        where TTrack : struct
        where TQuotePolicy : struct
    {
        int n = TLanes.Count;
        ref T dataRef = ref MemoryMarshal.GetReference(ctx.Data);
        int length = ctx.Data.Length;
        Span<int> ends = ctx.Ends;
        int endsLength = ends.Length;
        int rowStartsLimit = ctx.RowStarts.Length - 1;
        var delimV = TLanes.Create((byte)ctx.Options.Delimiter);
        var quoteV = TLanes.Create((byte)ctx.Options.Quote);
        var lfV = TLanes.Create((byte)'\n');
        var crV = TLanes.Create((byte)'\r');

        int position = start;
        int endsCount = endsCountRef;

        while (position + n <= length)
        {
            if (endsCount + CHUNK_RESERVE > endsLength || ctx.RowCount + n > rowStartsLimit)
                break;

            // Unquoted 4-chunk block: newline-free stretches take the bare path four chunks at a time.
            if (typeof(TQuotePolicy) == typeof(QuotesDisabled)
                && position + (4 * n) <= length
                && endsCount + (4 * CHUNK_RESERVE) <= endsLength
                && ctx.RowCount + (4 * n) <= rowStartsLimit)
            {
                var c0 = Load<T, TVec, TLanes>(ref dataRef, position);
                var c1 = Load<T, TVec, TLanes>(ref dataRef, position + n);
                var c2 = Load<T, TVec, TLanes>(ref dataRef, position + (2 * n));
                var c3 = Load<T, TVec, TLanes>(ref dataRef, position + (3 * n));

                var le0 = TLanes.Or(TLanes.Equals(c0, lfV), TLanes.Equals(c0, crV));
                var le1 = TLanes.Or(TLanes.Equals(c1, lfV), TLanes.Equals(c1, crV));
                var le2 = TLanes.Or(TLanes.Equals(c2, lfV), TLanes.Equals(c2, crV));
                var le3 = TLanes.Or(TLanes.Equals(c3, lfV), TLanes.Equals(c3, crV));

                if (TLanes.Mask(TLanes.Or(TLanes.Or(le0, le1), TLanes.Or(le2, le3))) == 0)
                {
                    AppendDelimiters(TLanes.Mask(TLanes.Equals(c0, delimV)), position, ends, ref endsCount);
                    AppendDelimiters(TLanes.Mask(TLanes.Equals(c1, delimV)), position + n, ends, ref endsCount);
                    AppendDelimiters(TLanes.Mask(TLanes.Equals(c2, delimV)), position + (2 * n), ends, ref endsCount);
                    AppendDelimiters(TLanes.Mask(TLanes.Equals(c3, delimV)), position + (3 * n), ends, ref endsCount);
                    position += 4 * n;
                    continue;
                }

                // A line ending is somewhere in the block: dispatch each chunk in order.
                endsCount = Dispatch<T, TVec, TLanes, TTrack, TQuotePolicy>(c0, le0, delimV, quoteV, lfV, position, ref ctx, endsCount);
                if (ctx.ErrorRowStart >= 0) break;
                position += n;
                endsCount = Dispatch<T, TVec, TLanes, TTrack, TQuotePolicy>(c1, le1, delimV, quoteV, lfV, position, ref ctx, endsCount);
                if (ctx.ErrorRowStart >= 0) break;
                position += n;
                endsCount = Dispatch<T, TVec, TLanes, TTrack, TQuotePolicy>(c2, le2, delimV, quoteV, lfV, position, ref ctx, endsCount);
                if (ctx.ErrorRowStart >= 0) break;
                position += n;
                endsCount = Dispatch<T, TVec, TLanes, TTrack, TQuotePolicy>(c3, le3, delimV, quoteV, lfV, position, ref ctx, endsCount);
                if (ctx.ErrorRowStart >= 0) break;
                position += n;
                continue;
            }

            var chunk = Load<T, TVec, TLanes>(ref dataRef, position);
            var le = TLanes.Or(TLanes.Equals(chunk, lfV), TLanes.Equals(chunk, crV));
            endsCount = Dispatch<T, TVec, TLanes, TTrack, TQuotePolicy>(chunk, le, delimV, quoteV, lfV, position, ref ctx, endsCount);
            if (ctx.ErrorRowStart >= 0) break;
            position += n;
        }

        if (ctx.ErrorRowStart < 0 && position + n > length)
            position = ScanTail<T, TTrack, TQuotePolicy>(position, ref ctx, ref endsCount);

        endsCountRef = endsCount;
        return position;
    }

    /// <summary>
    /// Processes one chunk: bare delimiter appends when nothing but delimiters is present, the inline
    /// quoted path when quotes appear without a line ending, otherwise the state machine. Returns the
    /// new <c>endsCount</c>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Dispatch<T, TVec, TLanes, TTrack, TQuotePolicy>(
        TVec chunk,
        TVec lineEndings,
        TVec delimV,
        TVec quoteV,
        TVec lfV,
        int chunkBase,
        ref ScanContext<T> ctx,
        int endsCount)
        where T : unmanaged, IEquatable<T>
        where TVec : struct
        where TLanes : struct, ISimdLanes<TVec>
        where TTrack : struct
        where TQuotePolicy : struct
    {
        ulong lem = TLanes.Mask(lineEndings);
        ulong dm = TLanes.Mask(TLanes.Equals(chunk, delimV));
        ulong qm = 0;
        if (typeof(TQuotePolicy) == typeof(QuotesEnabled))
            qm = TLanes.Mask(TLanes.Equals(chunk, quoteV));

        if (lem == 0 && (typeof(TQuotePolicy) == typeof(QuotesDisabled) || (qm == 0 && !ctx.InQuotes && !ctx.SkipNextQuote)))
        {
            AppendDelimiters(dm, chunkBase, ctx.Ends, ref endsCount);
            return endsCount;
        }

        if (typeof(TQuotePolicy) == typeof(QuotesEnabled) && lem == 0
            && TryAppendQuotedNoLineEnding(dm, qm, chunkBase, TLanes.Count, ref ctx, ref endsCount))
        {
            return endsCount;
        }

        ulong lfm = TLanes.Mask(TLanes.Equals(chunk, lfV));
        return ProcessEventChunk<T, TTrack, TQuotePolicy>(dm, lem, qm, lfm, chunkBase, TLanes.Count, ref ctx, endsCount);
    }

    /// <summary>
    /// Scans the final elements that do not fill a chunk by building the same masks scalar-side and
    /// running the state machine once. Returns the position reached.
    /// </summary>
    private static int ScanTail<T, TTrack, TQuotePolicy>(int position, ref ScanContext<T> ctx, ref int endsCount)
        where T : unmanaged, IEquatable<T>
        where TTrack : struct
        where TQuotePolicy : struct
    {
        ReadOnlySpan<T> data = ctx.Data;
        int tail = data.Length - position;
        if (tail <= 0)
            return position;

        if (endsCount + CHUNK_RESERVE > ctx.Ends.Length || ctx.RowCount + MAX_CHUNK > ctx.RowStarts.Length - 1)
            return position;

        ulong dm = 0, lfm = 0, crm = 0, qm = 0;
        for (int i = 0; i < tail; i++)
        {
            T c = data[position + i];
            ulong bit = 1ul << i;
            if (c.Equals(ctx.Delimiter)) dm |= bit;
            else if (c.Equals(ctx.Lf)) lfm |= bit;
            else if (c.Equals(ctx.Cr)) crm |= bit;
            else if (typeof(TQuotePolicy) == typeof(QuotesEnabled) && c.Equals(ctx.Quote)) qm |= bit;
        }

        endsCount = ProcessEventChunk<T, TTrack, TQuotePolicy>(dm, lfm | crm, qm, lfm, position, tail, ref ctx, endsCount);
        return ctx.ErrorRowStart < 0 ? data.Length : position;
    }

    // ---------------------------------------------------------------------------------------------
    // Mask-level state machine.
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
    /// Inline handling for the common quoted shape: a chunk with quote activity but no line ending
    /// (most chunks inside a row of quoted fields). Filters delimiters through the CLMUL in-quotes
    /// mask, appends the survivors bare, and flips the quote parity. Returns false when the chunk
    /// needs the full state machine (doubled quotes, a carried-in skipped quote, no PCLMULQDQ).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryAppendQuotedNoLineEnding<T>(ulong dm, ulong qm, int chunkBase, int chunkSize, ref ScanContext<T> ctx, ref int endsCount)
        where T : unmanaged, IEquatable<T>
    {
        if (ctx.SkipNextQuote || (qm & (qm >> 1)) != 0 || !HardwareCapabilities.PclmulqdqIsSupported)
            return false;

        if ((qm & (1ul << (chunkSize - 1))) != 0)
        {
            int next = chunkBase + chunkSize;
            if (next < ctx.Data.Length && ctx.Data[next].Equals(ctx.Quote))
                return false;
        }

        // No line ending in this chunk, so no CR can be pending at its end.
        ctx.PendingCrInQuotes = false;

        if (qm == 0)
            return true; // entirely inside a quoted field: nothing to record

        ulong inQuotes = ComputeInQuotesMask(qm, ctx.InQuotes);
        AppendDelimiters(dm & ~inQuotes, chunkBase, ctx.Ends, ref endsCount);
        if ((BitOperations.PopCount(qm) & 1) != 0)
            ctx.InQuotes = !ctx.InQuotes;
        return true;
    }

    /// <summary>
    /// Processes one chunk that contains line endings and/or quote activity and returns the new
    /// <c>endsCount</c>. On return, <c>ctx.ErrorRowStart</c> is set if the scan must stop for the
    /// reader to re-parse a row.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int ProcessEventChunk<T, TTrack, TQuotePolicy>(
        ulong dm,
        ulong lem,
        ulong qm,
        ulong lfm,
        int chunkBase,
        int chunkSize,
        ref ScanContext<T> ctx,
        int endsCount)
        where T : unmanaged, IEquatable<T>
        where TTrack : struct
        where TQuotePolicy : struct
    {
        if (ctx.SkipLeadingLf)
        {
            // The LF half of a CRLF that straddled the chunk boundary; the row start already skips it.
            ctx.SkipLeadingLf = false;
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
                if (next < ctx.Data.Length && ctx.Data[next].Equals(ctx.Quote))
                    hasDoubledQuotes = true;
            }

            if (hasDoubledQuotes || ctx.SkipNextQuote || !HardwareCapabilities.PclmulqdqIsSupported)
                return ProcessEventChunkSequential<T, TTrack>(dm | lem | qm, chunkBase, chunkSize, ref ctx, endsCount);

            ulong inQuotes = qm != 0
                ? ComputeInQuotesMask(qm, ctx.InQuotes)
                : (ctx.InQuotes ? ulong.MaxValue : 0ul);

            if (!ctx.Options.AllowNewlinesInsideQuotes && (lem & inQuotes) != 0)
            {
                ctx.ErrorRowStart = ctx.RowStart;
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
                if (ctx.PendingCrInQuotes)
                    quotedNewlines &= ~1ul;
                ctx.PendingCrInQuotes = (crInside & (1ul << (chunkSize - 1))) != 0;
            }

            dm &= ~inQuotes;
            lem &= ~inQuotes;
            crm &= ~inQuotes;

            if ((BitOperations.PopCount(qm) & 1) != 0)
                ctx.InQuotes = !ctx.InQuotes;
        }

        ulong events = dm | lem;
        while (events != 0)
        {
            int bit = BitOperations.TrailingZeroCount(events);
            events &= events - 1;
            int pos = chunkBase + bit;

            if (((lem >> bit) & 1) == 0)
            {
                ctx.Ends[endsCount++] = pos;
                continue;
            }

            if (typeof(TTrack) == typeof(TrackLineNumbers) && quotedNewlines != 0)
            {
                ulong below = quotedNewlines & ((1ul << bit) - 1);
                ctx.SourceLine += BitOperations.PopCount(below);
                quotedNewlines ^= below;
            }

            bool isCr = ((crm >> bit) & 1) != 0;
            if (!CloseRow<T, TTrack>(pos, isCr, terminatesLine: true, ref ctx, ref endsCount))
                return endsCount;

            // CRLF: CloseRow consumed the LF; drop its event bit, or flag it if it is the next chunk's first element.
            if (ctx.RowStart == pos + 2)
            {
                if (bit + 1 < chunkSize)
                    events &= ~(1ul << (bit + 1));
                else
                    ctx.SkipLeadingLf = true;
            }
        }

        // In-quote line endings after the last row close belong to the row still open.
        if (typeof(TTrack) == typeof(TrackLineNumbers))
            ctx.SourceLine += BitOperations.PopCount(quotedNewlines);

        return endsCount;
    }

    /// <summary>
    /// Sequential fallback for chunks with doubled (escaped) quotes, a carried-in skipped quote, or
    /// no PCLMULQDQ. Mirrors the per-row parser's slow path, but closes rows instead of returning.
    /// </summary>
    private static int ProcessEventChunkSequential<T, TTrack>(ulong mask, int chunkBase, int chunkSize, ref ScanContext<T> ctx, int endsCount)
        where T : unmanaged, IEquatable<T>
        where TTrack : struct
    {
        ReadOnlySpan<T> data = ctx.Data;

        while (mask != 0)
        {
            int bit = BitOperations.TrailingZeroCount(mask);
            mask &= mask - 1;
            int pos = chunkBase + bit;
            T c = data[pos];

            if (c.Equals(ctx.Quote))
            {
                if (ctx.SkipNextQuote)
                {
                    ctx.SkipNextQuote = false;
                    continue;
                }

                if (ctx.InQuotes && pos + 1 < data.Length && data[pos + 1].Equals(ctx.Quote))
                {
                    ctx.SkipNextQuote = true;
                    continue;
                }

                ctx.InQuotes = !ctx.InQuotes;
                if (typeof(TTrack) == typeof(TrackLineNumbers))
                    ctx.PendingCrInQuotes = false;
                continue;
            }

            if (ctx.InQuotes)
            {
                bool isLf = c.Equals(ctx.Lf);
                bool isCr = c.Equals(ctx.Cr);
                if (isLf || isCr)
                {
                    if (!ctx.Options.AllowNewlinesInsideQuotes)
                    {
                        ctx.ErrorRowStart = ctx.RowStart;
                        return endsCount;
                    }

                    if (typeof(TTrack) == typeof(TrackLineNumbers))
                        UpdateNewlineCountInQuotes(isLf, isCr, ref ctx.PendingCrInQuotes, ref ctx.SourceLine);
                }
                continue;
            }

            if (c.Equals(ctx.Delimiter))
            {
                ctx.Ends[endsCount++] = pos;
                continue;
            }

            if (!CloseRow<T, TTrack>(pos, c.Equals(ctx.Cr), terminatesLine: true, ref ctx, ref endsCount))
                return endsCount;

            if (ctx.RowStart == pos + 2)
            {
                if (bit + 1 < chunkSize)
                    mask &= ~(1ul << (bit + 1));
                else
                    ctx.SkipLeadingLf = true;
            }
        }

        return endsCount;
    }

    /// <summary>
    /// Closes the current row at <paramref name="rowEnd"/>. Blank rows are dropped. Sets up the next
    /// row's sentinel. Returns false when the row violates a limit and must be re-parsed by the
    /// per-row parser for its exception.
    /// </summary>
    private static bool CloseRow<T, TTrack>(int rowEnd, bool isCr, bool terminatesLine, ref ScanContext<T> ctx, ref int endsCount)
        where T : unmanaged, IEquatable<T>
        where TTrack : struct
    {
        Span<int> ends = ctx.Ends;

        if (rowEnd != ctx.RowStart)
        {
            int delimiterCount = endsCount - ctx.CurRowEndsStart - 1;
            if (delimiterCount + 1 > ctx.Options.MaxColumnCount)
            {
                ctx.ErrorRowStart = ctx.RowStart;
                return false;
            }

            ends[endsCount++] = rowEnd;

            if (ctx.Options.MaxFieldSize is { } maxFieldSize && !FieldLengthsWithinLimit(ends, ctx.CurRowEndsStart, endsCount, maxFieldSize))
            {
                ctx.ErrorRowStart = ctx.RowStart;
                return false;
            }

            ctx.RowStarts[ctx.RowCount] = ctx.CurRowEndsStart;
            if (typeof(TTrack) == typeof(TrackLineNumbers))
                ctx.SourceLines[ctx.RowCount] = ctx.CurRowStartLine;
            ctx.RowCount++;
            ctx.CurRowEndsStart = endsCount;
        }

        int nextRowStart = rowEnd;
        if (terminatesLine)
        {
            nextRowStart++;
            if (isCr && nextRowStart < ctx.Data.Length && ctx.Data[nextRowStart].Equals(ctx.Lf))
                nextRowStart++;

            if (typeof(TTrack) == typeof(TrackLineNumbers))
                ctx.SourceLine++;
        }

        ends[ctx.CurRowEndsStart] = nextRowStart - 1;
        endsCount = ctx.CurRowEndsStart + 1;
        ctx.RowStart = nextRowStart;
        ctx.CurRowStartLine = ctx.SourceLine;
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
    private static void UpdateNewlineCountInQuotes(bool isLf, bool isCr, ref bool pendingCrInQuotes, ref int newlineCount)
    {
        if (pendingCrInQuotes)
        {
            pendingCrInQuotes = false;
            if (isLf)
                return;
        }

        if (isCr)
        {
            newlineCount++;
            pendingCrInQuotes = true;
        }
        else if (isLf)
        {
            newlineCount++;
        }
    }
}
