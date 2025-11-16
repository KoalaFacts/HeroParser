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
    public bool MoveNext()
    {
        // Dispose previous row's pooled arrays
        if (_hasCurrentRow)
        {
            _currentRow.Dispose();
            _hasCurrentRow = false;
        }

        // Loop to skip empty lines (avoid recursion/stack overflow)
        while (true)
        {
            if (_position >= _csv.Length)
                return false;

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

            // Skip empty lines (continue loop)
            if (line.IsEmpty)
                continue;

            // Parse the line into columns using SIMD-optimized parser
            _currentRow = ParseRow(line);
            _hasCurrentRow = true;
            return true;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private CsvRow ParseRow(ReadOnlySpan<char> line)
    {
        // Always use ArrayPool for memory safety
        // ArrayPool achieves zero-allocation after warmup (reuses arrays)
        // NOTE: stackalloc is unsafe here because spans escape method scope
        const int MaxColumnsPooled = 10000;

        // Estimate column count for efficient allocation
        int estimatedColumns = EstimateColumnCount(line);
        int bufferSize = Math.Max(Math.Min(estimatedColumns * 2, MaxColumnsPooled), 16);

        // Rent arrays from pool
        var startsArray = ArrayPool<int>.Shared.Rent(bufferSize);
        var lengthsArray = ArrayPool<int>.Shared.Rent(bufferSize);

        var starts = startsArray.AsSpan();
        var lengths = lengthsArray.AsSpan();

        int actualCount = _parser.ParseColumns(line, _delimiter, starts, lengths);

        // CsvRow owns these arrays and will return them to pool on Dispose()
        return new CsvRow(line, starts.Slice(0, actualCount), lengths.Slice(0, actualCount),
            startsArray, lengthsArray);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int EstimateColumnCount(ReadOnlySpan<char> line)
    {
        // Quick estimation: count delimiters in first 256 chars
        var sample = line.Length > 256 ? line.Slice(0, 256) : line;
        int delimiterCount = 0;

        for (int i = 0; i < sample.Length; i++)
        {
            if (sample[i] == _delimiter) delimiterCount++;
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
