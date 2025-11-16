using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using HeroParser.Simd;

namespace HeroParser;

/// <summary>
/// Represents a single CSV row with lazy, zero-allocation column access.
/// Ref struct ensures stack-only allocation - no GC pressure.
/// Columns are only parsed when first accessed (lazy evaluation).
/// </summary>
public ref struct CsvRow
{
    private readonly ReadOnlySpan<char> _line;
    private readonly char _delimiter;
    private readonly int _maxColumns;
    private readonly ISimdParser _parser;

    // Lazy-initialized column metadata
    private ReadOnlySpan<int> _columnStarts;
    private ReadOnlySpan<int> _columnLengths;
    private int[]? _startsArray;
    private int[]? _lengthsArray;
    private int _columnCount;
    private bool _isParsed;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal CsvRow(
        ReadOnlySpan<char> line,
        char delimiter,
        int maxColumns,
        ISimdParser parser)
    {
        _line = line;
        _delimiter = delimiter;
        _maxColumns = maxColumns;
        _parser = parser;
        _columnStarts = default;
        _columnLengths = default;
        _startsArray = null;
        _lengthsArray = null;
        _columnCount = 0;
        _isParsed = false;
    }

    /// <summary>
    /// Number of columns in this row (triggers parsing on first access).
    /// </summary>
    public int Count
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
    public CsvCol this[int index]
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
            return new CsvCol(span);
        }
    }

    /// <summary>
    /// Parse columns lazily - only called when first column is accessed.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureParsed()
    {
        if (_isParsed)
            return;

        // Estimate column count for efficient allocation
        int estimatedColumns = EstimateColumnCount();
        int bufferSize = Math.Max(Math.Min(estimatedColumns * 2, _maxColumns), 16);

        // Rent arrays from pool
        _startsArray = ArrayPool<int>.Shared.Rent(bufferSize);
        _lengthsArray = ArrayPool<int>.Shared.Rent(bufferSize);

        try
        {
            _columnStarts = _startsArray.AsSpan();
            _columnLengths = _lengthsArray.AsSpan();

            _columnCount = _parser.ParseColumns(
                _line,
                _delimiter,
                _startsArray,
                _lengthsArray,
                _maxColumns);

            // Slice to actual count
            _columnStarts = _columnStarts.Slice(0, _columnCount);
            _columnLengths = _columnLengths.Slice(0, _columnCount);

            _isParsed = true;
        }
        catch
        {
            // Exception-safe: return arrays on error
            if (_startsArray != null)
                ArrayPool<int>.Shared.Return(_startsArray, clearArray: true);
            if (_lengthsArray != null)
                ArrayPool<int>.Shared.Return(_lengthsArray, clearArray: true);
            _startsArray = null;
            _lengthsArray = null;
            throw;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int EstimateColumnCount()
    {
        // Quick estimation: count delimiters in first 256 chars
        var sample = _line.Length > 256 ? _line.Slice(0, 256) : _line;
        int delimiterCount = 0;

        for (int i = 0; i < sample.Length; i++)
        {
            if (sample[i] == _delimiter)
                delimiterCount++;
        }

        return delimiterCount + 1; // columns = delimiters + 1
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

    /// <summary>
    /// Return pooled arrays to ArrayPool if used.
    /// Must be called to avoid memory leaks.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose()
    {
        if (_startsArray != null)
            ArrayPool<int>.Shared.Return(_startsArray, clearArray: true);

        if (_lengthsArray != null)
            ArrayPool<int>.Shared.Return(_lengthsArray, clearArray: true);

        _startsArray = null;
        _lengthsArray = null;
    }
}
