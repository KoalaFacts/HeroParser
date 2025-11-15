using System.Buffers;
using System.Runtime.CompilerServices;

namespace HeroParser;

/// <summary>
/// Represents a single CSV row with zero-allocation column access.
/// Ref struct ensures stack-only allocation - no GC pressure.
/// </summary>
public readonly ref struct CsvRow
{
    private readonly ReadOnlySpan<char> _line;
    private readonly ReadOnlySpan<int> _columnStarts;
    private readonly ReadOnlySpan<int> _columnLengths;

    // Optional pooled arrays (null if stack-allocated)
    private readonly int[]? _startsArray;
    private readonly int[]? _lengthsArray;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal CsvRow(
        ReadOnlySpan<char> line,
        ReadOnlySpan<int> columnStarts,
        ReadOnlySpan<int> columnLengths,
        int[]? startsArray = null,
        int[]? lengthsArray = null)
    {
        _line = line;
        _columnStarts = columnStarts;
        _columnLengths = columnLengths;
        _startsArray = startsArray;
        _lengthsArray = lengthsArray;
    }

    /// <summary>
    /// Number of columns in this row.
    /// </summary>
    public int Count
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _columnStarts.Length;
    }

    /// <summary>
    /// Access a column by index. Zero-allocation via span slice.
    /// </summary>
    /// <param name="index">Column index (0-based)</param>
    /// <returns>Column value as CsvCol</returns>
    public CsvCol this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            // Bounds check removed for maximum performance
            // User must ensure valid index
            var start = _columnStarts[index];
            var length = _columnLengths[index];
            var span = _line.Slice(start, length);
            return new CsvCol(span);
        }
    }

    /// <summary>
    /// Get a range of columns as an enumerable.
    /// </summary>
    public CsvCols this[Range range]
    {
        get
        {
            var (offset, length) = range.GetOffsetAndLength(Count);
            return new CsvCols(this, offset, length);
        }
    }

    /// <summary>
    /// Convert row to string array (allocates).
    /// Use only when materialization is required.
    /// </summary>
    public string[] ToStringArray()
    {
        var result = new string[Count];
        for (int i = 0; i < Count; i++)
        {
            result[i] = this[i].ToString();
        }
        return result;
    }

    /// <summary>
    /// Return pooled arrays to ArrayPool if used.
    /// Automatically called when ref struct goes out of scope in .NET 9+.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose()
    {
        if (_startsArray != null)
            ArrayPool<int>.Shared.Return(_startsArray);

        if (_lengthsArray != null)
            ArrayPool<int>.Shared.Return(_lengthsArray);
    }
}

/// <summary>
/// Enumerator for column ranges (e.g., row[2..5]).
/// </summary>
public ref struct CsvCols
{
    private readonly CsvRow _row;
    private readonly int _start;
    private readonly int _length;
    private int _current;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal CsvCols(CsvRow row, int start, int length)
    {
        _row = row;
        _start = start;
        _length = length;
        _current = -1;
    }

    public readonly int Count
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _length;
    }

    public readonly CsvCol Current
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _row[_start + _current];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool MoveNext()
    {
        _current++;
        return _current < _length;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly CsvCols GetEnumerator() => this;
}
