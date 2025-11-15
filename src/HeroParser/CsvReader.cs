using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using HeroParser.Simd;

namespace HeroParser;

/// <summary>
/// Zero-allocation CSV reader using ref struct for stack-only semantics.
/// Iterates rows one at a time with lazy parsing - no heap allocations.
/// </summary>
public ref struct CsvReader
{
    private readonly ReadOnlySpan<char> _csv;
    private readonly char _delimiter;
    private int _position;
    private CsvRow _currentRow;
    private bool _hasCurrentRow;

    // Parser strategy selected at construction based on hardware
    private readonly ISimdParser _parser;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal CsvReader(ReadOnlySpan<char> csv, char delimiter)
    {
        _csv = csv;
        _delimiter = delimiter;
        _position = 0;
        _currentRow = default;
        _hasCurrentRow = false;

        // Select optimal parser based on hardware capabilities
        _parser = SimdParserFactory.GetParser();
    }

    /// <summary>
    /// Current row being read. Only valid after MoveNext() returns true.
    /// </summary>
    public readonly CsvRow Current
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _currentRow;
    }

    /// <summary>
    /// Advance to the next row in the CSV.
    /// </summary>
    /// <returns>True if a row was read, false if end of CSV reached</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool MoveNext()
    {
        if (_position >= _csv.Length)
        {
            _hasCurrentRow = false;
            return false;
        }

        // Get remaining CSV from current position
        var remaining = _csv.Slice(_position);

        // Find end of line
        var lineEnd = FindLineEnd(remaining, out int lineEndLength);

        ReadOnlySpan<char> line;
        if (lineEnd == -1)
        {
            // Last line without newline
            line = remaining;
            _position = _csv.Length;
        }
        else
        {
            line = remaining.Slice(0, lineEnd);
            _position += lineEnd + lineEndLength;
        }

        // Skip empty lines
        if (line.IsEmpty)
        {
            return MoveNext(); // Recurse to next non-empty line
        }

        // Parse the line into columns using SIMD-optimized parser
        _currentRow = ParseRow(line);
        _hasCurrentRow = true;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private CsvRow ParseRow(ReadOnlySpan<char> line)
    {
        // Rent arrays from pool for column positions
        const int MaxColumnsStackAlloc = 16;
        const int MaxColumnsPooled = 10000;

        // Fast path: estimate column count (most CSVs have consistent column counts)
        int estimatedColumns = EstimateColumnCount(line);

        if (estimatedColumns <= MaxColumnsStackAlloc)
        {
            // Stack allocation for small rows
            Span<int> starts = stackalloc int[estimatedColumns * 2]; // Over-allocate
            Span<int> lengths = stackalloc int[estimatedColumns * 2];

            int actualCount = _parser.ParseColumns(line, _delimiter, starts, lengths);
            return new CsvRow(line, starts.Slice(0, actualCount), lengths.Slice(0, actualCount));
        }
        else
        {
            // Pool allocation for large rows
            var startsArray = ArrayPool<int>.Shared.Rent(Math.Min(estimatedColumns * 2, MaxColumnsPooled));
            var lengthsArray = ArrayPool<int>.Shared.Rent(Math.Min(estimatedColumns * 2, MaxColumnsPooled));

            var starts = startsArray.AsSpan();
            var lengths = lengthsArray.AsSpan();

            int actualCount = _parser.ParseColumns(line, _delimiter, starts, lengths);

            // CsvRow will return these to pool when done
            return new CsvRow(line, starts.Slice(0, actualCount), lengths.Slice(0, actualCount),
                startsArray, lengthsArray);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int EstimateColumnCount(ReadOnlySpan<char> line)
    {
        // Quick estimation: count delimiters in first 256 chars
        var sample = line.Length > 256 ? line.Slice(0, 256) : line;
        int delimiterCount = 0;

        for (int i = 0; i < sample.Length; i++)
        {
            if (sample[i] == ',') delimiterCount++; // Assume comma for estimation
        }

        return delimiterCount + 1; // columns = delimiters + 1
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int FindLineEnd(ReadOnlySpan<char> span, out int lineEndLength)
    {
        // Fast scan for newline characters
        for (int i = 0; i < span.Length; i++)
        {
            if (span[i] == '\n')
            {
                lineEndLength = 1;
                return i;
            }
            if (span[i] == '\r')
            {
                if (i + 1 < span.Length && span[i + 1] == '\n')
                {
                    lineEndLength = 2; // CRLF
                    return i;
                }
                lineEndLength = 1; // CR only
                return i;
            }
        }

        lineEndLength = 0;
        return -1; // No line end found
    }

    /// <summary>
    /// Get the enumerator for foreach support.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly CsvReader GetEnumerator() => this;
}
