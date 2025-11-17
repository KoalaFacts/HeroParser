using HeroParser.Simd;
using System.Runtime.CompilerServices;

namespace HeroParser;

/// <summary>
/// Represents a single CSV row with lazy or eager zero-allocation column access.
/// Ref struct ensures stack-only allocation - no GC pressure.
/// Columns are parsed lazily (on first access) or eagerly (in MoveNext) based on options.
/// Uses shared buffers from CsvReader - no per-row allocation.
/// </summary>
public ref struct CsvRow
{
    private readonly ReadOnlySpan<char> _line;
    private readonly char _delimiter;
    private readonly char _quote;
    private readonly ISimdParser _parser;

    // Shared buffers provided by CsvReader (not owned by this row)
    private readonly Span<int> _columnStartsBuffer;
    private readonly Span<int> _columnLengthsBuffer;

    // Column metadata (populated lazily or eagerly)
    private ReadOnlySpan<int> _columnStarts;
    private ReadOnlySpan<int> _columnLengths;
    private int _columnCount;
    private bool _isParsed;

    // Lazy parsing constructor
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal CsvRow(
        ReadOnlySpan<char> line,
        char delimiter,
        char quote,
        Span<int> columnStartsBuffer,
        Span<int> columnLengthsBuffer,
        ISimdParser parser)
    {
        _line = line;
        _delimiter = delimiter;
        _quote = quote;
        _columnStartsBuffer = columnStartsBuffer;
        _columnLengthsBuffer = columnLengthsBuffer;
        _parser = parser;
        _columnStarts = default;
        _columnLengths = default;
        _columnCount = 0;
        _isParsed = false;
    }

    // Eager parsing constructor - columns already parsed
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal CsvRow(
        ReadOnlySpan<char> line,
        char delimiter,
        char quote,
        Span<int> columnStartsBuffer,
        Span<int> columnLengthsBuffer,
        ISimdParser parser,
        int columnCount)
    {
        _line = line;
        _delimiter = delimiter;
        _quote = quote;
        _columnStartsBuffer = columnStartsBuffer;
        _columnLengthsBuffer = columnLengthsBuffer;
        _parser = parser;
        _columnStarts = columnStartsBuffer[..columnCount];
        _columnLengths = columnLengthsBuffer[..columnCount];
        _columnCount = columnCount;
        _isParsed = true;
    }

    /// <summary>
    /// Number of columns in this row (triggers parsing on first access).
    /// </summary>
    public int ColumnCount
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            EnsureParsed();
            return _columnCount;
        }
    }

    /// <summary>
    /// Access a column by index (triggers parsing on first access).
    /// Zero-allocation via span slice.
    /// </summary>
    /// <param name="index">Column index (0-based)</param>
    /// <returns>Column value as CsvCol</returns>
    public CsvColumn this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            EnsureParsed();

#if DEBUG
            // Bounds check in debug mode only
            if ((uint)index >= (uint)_columnCount)
                throw new IndexOutOfRangeException($"Column index {index} out of range (0-{_columnCount - 1})");
#endif
            var start = _columnStarts[index];
            var length = _columnLengths[index];
            var span = _line.Slice(start, length);
            return new CsvColumn(span);
        }
    }

    /// <summary>
    /// Parse columns lazily - only called when first column is accessed (if not already parsed).
    /// Uses shared buffers from reader - ZERO allocation per row.
    /// Hybrid strategy: scalar for short rows (less than 100 chars), SIMD for longer rows.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureParsed()
    {
        if (_isParsed)
            return;

        // Hybrid strategy: avoid SIMD overhead on short rows
        // Threshold of 100 chars balances SIMD setup cost vs throughput gain
        const int SimdThreshold = 100;

        if (_line.Length < SimdThreshold)
        {
            // Short row: use scalar parser (no SIMD overhead)
            _columnCount = ScalarParser.Instance.ParseColumns(
                _line,
                _delimiter,
                _quote,
                _columnStartsBuffer,
                _columnLengthsBuffer,
                _columnStartsBuffer.Length);
        }
        else
        {
            // Long row: use SIMD parser (amortizes setup cost)
            _columnCount = _parser.ParseColumns(
                _line,
                _delimiter,
                _quote,
                _columnStartsBuffer,
                _columnLengthsBuffer,
                _columnStartsBuffer.Length);
        }

        // Slice to actual count
        _columnStarts = _columnStartsBuffer[.._columnCount];
        _columnLengths = _columnLengthsBuffer[.._columnCount];

        _isParsed = true;
    }

    /// <summary>
    /// Convert row to string array (allocates and triggers parsing).
    /// Use only when materialization is required.
    /// </summary>
    public string[] ToStringArray()
    {
        EnsureParsed();
        var result = new string[_columnCount];
        for (int i = 0; i < _columnCount; i++)
        {
            result[i] = this[i].ToString();
        }
        return result;
    }
}
