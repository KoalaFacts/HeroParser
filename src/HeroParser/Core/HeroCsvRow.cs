using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace HeroParser.Core;

/// <summary>
/// A readonly ref struct that provides zero-allocation access to a CSV row.
/// Implements delayed evaluation pattern inspired by Sep for maximum performance.
/// </summary>
[DebuggerDisplay("Columns: {ColumnCount}")]
public readonly ref struct HeroCsvRow
{
    private readonly CsvReader _reader;
    private readonly ReadOnlySpan<char> _rowSpan;
    private readonly ReadOnlySpan<int> _columnStarts;
    private readonly ReadOnlySpan<int> _columnLengths;
    private readonly bool _trimValues;

    internal HeroCsvRow(
        CsvReader reader,
        ReadOnlySpan<char> rowSpan,
        ReadOnlySpan<int> columnStarts,
        ReadOnlySpan<int> columnLengths,
        bool trimValues)
    {
        _reader = reader;
        _rowSpan = rowSpan;
        _columnStarts = columnStarts;
        _columnLengths = columnLengths;
        _trimValues = trimValues;
    }

    /// <summary>
    /// Gets the number of columns in this row.
    /// </summary>
    public int ColumnCount
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _columnStarts.Length;
    }

    /// <summary>
    /// Gets whether this row is empty (no columns).
    /// </summary>
    public bool IsEmpty
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _columnStarts.IsEmpty;
    }

    /// <summary>
    /// Gets a column by index using zero-allocation delayed evaluation.
    /// </summary>
    /// <param name="index">The column index.</param>
    /// <returns>A HeroCsvCol representing the column at the specified index.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when index is out of range.</exception>
    public HeroCsvCol this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if ((uint)index >= (uint)_columnStarts.Length)
                ThrowIndexOutOfRange(index);

            var start = _columnStarts[index];
            var length = _columnLengths[index];
            var span = _rowSpan.Slice(start, length);

            return new HeroCsvCol(span, false, _trimValues);
        }
    }

    /// <summary>
    /// Gets a column by header name using zero-allocation delayed evaluation.
    /// </summary>
    /// <param name="columnName">The column name.</param>
    /// <returns>A HeroCsvCol representing the named column.</returns>
    /// <exception cref="ArgumentException">Thrown when column name is not found.</exception>
    public HeroCsvCol this[string columnName]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            var index = _reader.GetColumnIndex(columnName);
            return this[index];
        }
    }

#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
    /// <summary>
    /// Gets a range of columns as an enumerable sequence.
    /// Each column uses delayed evaluation to minimize allocations.
    /// </summary>
    /// <param name="range">The range of columns to get.</param>
    /// <returns>An enumerable of HeroCsvCol for the specified range.</returns>
    public HeroCsvCols this[Range range]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            var (start, length) = range.GetOffsetAndLength(ColumnCount);
            return new HeroCsvCols(this, start, length);
        }
    }
#endif

    /// <summary>
    /// Gets a range of columns by start index and length.
    /// Each column uses delayed evaluation to minimize allocations.
    /// </summary>
    /// <param name="start">The starting column index.</param>
    /// <param name="length">The number of columns to include.</param>
    /// <returns>An enumerable of HeroCsvCol for the specified range.</returns>
    public HeroCsvCols GetRange(int start, int length)
    {
        if (start < 0 || start >= ColumnCount)
            throw new ArgumentOutOfRangeException(nameof(start));
        if (length < 0 || start + length > ColumnCount)
            throw new ArgumentOutOfRangeException(nameof(length));

        return new HeroCsvCols(this, start, length);
    }

    /// <summary>
    /// Tries to get a column by index without throwing exceptions.
    /// </summary>
    /// <param name="index">The column index.</param>
    /// <param name="column">The column if successful.</param>
    /// <returns>True if the index is valid, false otherwise.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetColumn(int index, out HeroCsvCol column)
    {
        if ((uint)index < (uint)_columnStarts.Length)
        {
            var start = _columnStarts[index];
            var length = _columnLengths[index];
            var span = _rowSpan.Slice(start, length);
            column = new HeroCsvCol(span, false, _trimValues);
            return true;
        }

        column = default;
        return false;
    }

    /// <summary>
    /// Tries to get a column by header name without throwing exceptions.
    /// </summary>
    /// <param name="columnName">The column name.</param>
    /// <param name="column">The column if successful.</param>
    /// <returns>True if the column name is found, false otherwise.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetColumn(string columnName, out HeroCsvCol column)
    {
        if (_reader.TryGetColumnIndex(columnName, out var index))
        {
            return TryGetColumn(index, out column);
        }

        column = default;
        return false;
    }

    /// <summary>
    /// Enumerates all columns in this row using delayed evaluation.
    /// </summary>
    /// <returns>An enumerator for all columns in the row.</returns>
    public HeroCsvCols.Enumerator GetEnumerator()
    {
        return new HeroCsvCols.Enumerator(this, 0, ColumnCount);
    }

    /// <summary>
    /// Converts the entire row to a string array.
    /// Warning: This allocates strings for each column.
    /// Use column access methods when possible to avoid allocations.
    /// </summary>
    /// <returns>An array of strings representing all columns.</returns>
    public string[] ToStringArray()
    {
        var result = new string[ColumnCount];
        for (int i = 0; i < ColumnCount; i++)
        {
            result[i] = this[i].ToString();
        }
        return result;
    }

    /// <summary>
    /// Copies column values to the provided span.
    /// </summary>
    /// <param name="destination">The destination span to copy column strings to.</param>
    /// <returns>True if all columns fit in the destination, false otherwise.</returns>
    public bool TryCopyTo(Span<string> destination)
    {
        if (destination.Length < ColumnCount)
            return false;

        for (int i = 0; i < ColumnCount; i++)
        {
            destination[i] = this[i].ToString();
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowIndexOutOfRange(int index)
    {
        throw new ArgumentOutOfRangeException(nameof(index), index, "Column index is out of range.");
    }
}

/// <summary>
/// A readonly ref struct that provides access to a range of columns within a row.
/// Enables LINQ-like operations over column ranges without allocation.
/// </summary>
public readonly ref struct HeroCsvCols
{
    private readonly HeroCsvRow _row;
    private readonly int _start;
    private readonly int _length;

    internal HeroCsvCols(HeroCsvRow row, int start, int length)
    {
        _row = row;
        _start = start;
        _length = length;
    }

    /// <summary>
    /// Gets the number of columns in this range.
    /// </summary>
    public int Length
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _length;
    }

    /// <summary>
    /// Gets whether this column range is empty.
    /// </summary>
    public bool IsEmpty
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _length == 0;
    }

    /// <summary>
    /// Gets a column within this range by relative index.
    /// </summary>
    /// <param name="index">The relative index within this range.</param>
    /// <returns>The column at the specified relative index.</returns>
    public HeroCsvCol this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if ((uint)index >= (uint)_length)
                throw new ArgumentOutOfRangeException(nameof(index));

            return _row[_start + index];
        }
    }

    /// <summary>
    /// Gets an enumerator for this column range.
    /// </summary>
    /// <returns>An enumerator for the columns in this range.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Enumerator GetEnumerator() => new(this);

    /// <summary>
    /// Enumerator for HeroCsvCols that provides zero-allocation iteration.
    /// </summary>
    public ref struct Enumerator
    {
        private readonly HeroCsvRow _row;
        private readonly int _end;
        private int _current;

        internal Enumerator(HeroCsvRow row, int start, int length)
        {
            _row = row;
            _current = start - 1;
            _end = start + length;
        }

        internal Enumerator(HeroCsvCols cols) : this(cols._row, cols._start, cols._length)
        {
        }

        /// <summary>
        /// Gets the current column.
        /// </summary>
        public HeroCsvCol Current
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _row[_current];
        }

        /// <summary>
        /// Moves to the next column.
        /// </summary>
        /// <returns>True if there are more columns, false if at the end.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext() => ++_current < _end;
    }
}