using HeroParser.Simd;
using System.Buffers;
using System.Runtime.CompilerServices;

namespace HeroParser;

/// <summary>
/// Zero-allocation CSV reader using ref struct for stack-only semantics.
/// Columns are parsed lazily only when accessed - no unnecessary work.
/// </summary>
public ref struct CsvReader
{
    private readonly ReadOnlySpan<char> _csv;
    private readonly CsvParserOptions _options;
    private int _position;
    private int _rowCount;
    private CsvRow _currentRow;

    // Parser strategy selected at construction based on hardware
    private readonly ISimdParser _parser;

    // Shared buffers for all rows - rent once, use many times
    private readonly int[] _columnStartsBuffer;
    private readonly int[] _columnLengthsBuffer;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal CsvReader(ReadOnlySpan<char> csv, CsvParserOptions options)
    {
        _csv = csv;
        _options = options;
        _position = 0;
        _rowCount = 0;
        _currentRow = default;

        // Select optimal parser based on hardware capabilities
        _parser = SimdParserFactory.GetParser();

        // Rent shared buffers ONCE for all rows - critical for zero-allocation performance
        _columnStartsBuffer = ArrayPool<int>.Shared.Rent(options.MaxColumns);
        _columnLengthsBuffer = ArrayPool<int>.Shared.Rent(options.MaxColumns);
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
        // Check row limit
        if (_rowCount >= _options.MaxRows)
        {
            throw new CsvException(
                CsvErrorCode.TooManyRows,
                $"CSV exceeds maximum row limit of {_options.MaxRows}");
        }

        // Loop to skip empty lines
        while (true)
        {
            if (_position >= _csv.Length)
                return false;

            // Get remaining CSV from current position
            var remaining = _csv[_position..];

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
                line = remaining[..lineEnd];
                _position += lineEnd + lineEndLength;
            }

            // Skip empty lines (continue loop)
            if (line.IsEmpty)
                continue;

            // Create lazy row with shared buffers (zero allocation per row)
            _currentRow = new CsvRow(
                line,
                _options.Delimiter,
                _options.Quote,
                _columnStartsBuffer.AsSpan(0, _options.MaxColumns),
                _columnLengthsBuffer.AsSpan(0, _options.MaxColumns),
                _parser);
            _rowCount++;
            return true;
        }
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

    /// <summary>
    /// Return shared buffers to pool - critical to avoid memory leaks.
    /// </summary>
    public readonly void Dispose()
    {
        // Return shared buffers to pool
        if (_columnStartsBuffer != null)
            ArrayPool<int>.Shared.Return(_columnStartsBuffer, clearArray: false);

        if (_columnLengthsBuffer != null)
            ArrayPool<int>.Shared.Return(_columnLengthsBuffer, clearArray: false);
    }
}
