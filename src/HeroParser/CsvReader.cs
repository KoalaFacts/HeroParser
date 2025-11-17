using HeroParser.Simd;
using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace HeroParser;

/// <summary>
/// Zero-allocation CSV reader using ref struct for stack-only semantics.
/// Columns are parsed lazily (on first access) or eagerly (in MoveNext) based on options.
/// Uses Sep-inspired SIMD newline search (P2.5): processes 32 chars at once via vector packing.
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

    // Batch row boundary cache (amortizes FindLineEnd SIMD cost across multiple rows)
    private readonly int[] _rowBoundaries;  // Pairs of (start, end) positions
    private int _rowBoundaryCount;          // Number of cached rows
    private int _rowBoundaryIndex;          // Current index in cache
    private readonly int _batchSize;        // How many rows to scan per batch

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

        // Initialize batch boundary cache if enabled
        _batchSize = options.BatchSize;
        _rowBoundaries = _batchSize > 0 ? ArrayPool<int>.Shared.Rent(_batchSize * 2) : null!;
        _rowBoundaryCount = 0;
        _rowBoundaryIndex = 0;
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
    /// Uses SIMD-accelerated newline search (P2) for optimal performance.
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

        // Use batch mode if enabled, otherwise fallback to per-row parsing
        if (_batchSize > 0)
            return MoveNextBatched();
        else
            return MoveNextSingleRow();
    }

    /// <summary>
    /// P2 per-row parsing: Process one row at a time with SIMD newline search.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool MoveNextSingleRow()
    {
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

            // Create row with shared buffers (zero allocation per row)
            if (_options.EagerParsing)
            {
                // Eager mode: parse columns now in MoveNext
                int columnCount = ParseLine(line);
                _currentRow = new CsvRow(
                    line,
                    _options.Delimiter,
                    _options.Quote,
                    _columnStartsBuffer.AsSpan(0, _options.MaxColumns),
                    _columnLengthsBuffer.AsSpan(0, _options.MaxColumns),
                    _parser,
                    columnCount);
            }
            else
            {
                // Lazy mode: parse columns on first access
                _currentRow = new CsvRow(
                    line,
                    _options.Delimiter,
                    _options.Quote,
                    _columnStartsBuffer.AsSpan(0, _options.MaxColumns),
                    _columnLengthsBuffer.AsSpan(0, _options.MaxColumns),
                    _parser);
            }
            _rowCount++;
            return true;
        }
    }

    /// <summary>
    /// Batched parsing: Use cached row boundaries when available.
    /// Amortizes SIMD overhead by scanning multiple row boundaries at once.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool MoveNextBatched()
    {
        // Use cached boundary if available
        if (_rowBoundaryIndex < _rowBoundaryCount)
        {
            var start = _rowBoundaries[_rowBoundaryIndex * 2];
            var end = _rowBoundaries[_rowBoundaryIndex * 2 + 1];
            var line = _csv[start..end];

            _rowBoundaryIndex++;

            // Skip empty lines
            if (line.IsEmpty)
                return MoveNextBatched();  // Recurse to get next

            // Create row (eager or lazy based on option)
            if (_options.EagerParsing)
            {
                int columnCount = ParseLine(line);
                _currentRow = new CsvRow(
                    line,
                    _options.Delimiter,
                    _options.Quote,
                    _columnStartsBuffer.AsSpan(0, _options.MaxColumns),
                    _columnLengthsBuffer.AsSpan(0, _options.MaxColumns),
                    _parser,
                    columnCount);
            }
            else
            {
                _currentRow = new CsvRow(
                    line,
                    _options.Delimiter,
                    _options.Quote,
                    _columnStartsBuffer.AsSpan(0, _options.MaxColumns),
                    _columnLengthsBuffer.AsSpan(0, _options.MaxColumns),
                    _parser);
            }

            _rowCount++;
            return true;
        }

        // No cached boundaries, scan next batch
        if (_position >= _csv.Length)
            return false;

        ScanRowBoundaries();
        return MoveNextBatched();  // Recurse to use first cached boundary
    }

    /// <summary>
    /// Scan next batch of row boundaries.
    /// Amortizes FindLineEnd SIMD cost over multiple rows.
    /// </summary>
    private void ScanRowBoundaries()
    {
        int found = 0;
        int searchPos = _position;

        while (found < _batchSize && searchPos < _csv.Length)
        {
            var remaining = _csv[searchPos..];
            var lineEnd = FindLineEnd(remaining, out int lineEndLength);

            if (lineEnd == -1)
            {
                // Last line without newline - only add if not empty
                if (searchPos < _csv.Length)
                {
                    _rowBoundaries[found * 2] = searchPos;
                    _rowBoundaries[found * 2 + 1] = _csv.Length;
                    found++;
                }
                searchPos = _csv.Length;
                break;
            }

            // Store boundary: [start, end] pairs
            _rowBoundaries[found * 2] = searchPos;
            _rowBoundaries[found * 2 + 1] = searchPos + lineEnd;

            searchPos += lineEnd + lineEndLength;
            found++;
        }

        _position = searchPos;
        _rowBoundaryCount = found;
        _rowBoundaryIndex = 0;
    }

    /// <summary>
    /// Parse a single line's columns using hybrid SIMD strategy.
    /// Used for eager parsing mode only.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private readonly int ParseLine(ReadOnlySpan<char> line)
    {
        // Hybrid strategy: avoid SIMD overhead on short rows
        const int SimdThreshold = 100;

        if (line.Length < SimdThreshold)
        {
            // Short row: use scalar parser (no SIMD overhead)
            return ScalarParser.Instance.ParseColumns(
                line,
                _options.Delimiter,
                _options.Quote,
                _columnStartsBuffer.AsSpan(0, _options.MaxColumns),
                _columnLengthsBuffer.AsSpan(0, _options.MaxColumns),
                _options.MaxColumns);
        }
        else
        {
            // Long row: use SIMD parser (amortizes setup cost)
            return _parser.ParseColumns(
                line,
                _options.Delimiter,
                _options.Quote,
                _columnStartsBuffer.AsSpan(0, _options.MaxColumns),
                _columnLengthsBuffer.AsSpan(0, _options.MaxColumns),
                _options.MaxColumns);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int FindLineEnd(ReadOnlySpan<char> span, out int lineEndLength)
    {
        // SIMD-accelerated newline search for better performance
        if (Avx2.IsSupported && span.Length >= Vector256<ushort>.Count)
        {
            return FindLineEndSimd(span, out lineEndLength);
        }

        // Fallback to scalar search for short spans or unsupported hardware
        return FindLineEndScalar(span, out lineEndLength);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe int FindLineEndSimd(ReadOnlySpan<char> span, out int lineEndLength)
    {
        // Sep's optimization: Pack 2 char vectors into 1 byte vector (process 32 chars at once!)
        var lf = Vector256.Create((byte)'\n');
        var cr = Vector256.Create((byte)'\r');
        int i = 0;
        const int charsPerIteration = 32;  // Process 32 chars (2 vectors packed into bytes)

        fixed (char* ptr = span)
        {
            // Process 32 chars at a time with AVX2 packing
            while (i + charsPerIteration <= span.Length)
            {
                byte* bytePtr = (byte*)(ptr + i);

                // Load 2 char vectors (16 chars each = 32 bytes each)
                var v0 = Avx.LoadVector256((short*)bytePtr);           // First 16 chars
                var v1 = Avx.LoadVector256((short*)(bytePtr + 32));    // Next 16 chars

                // Pack 2 short vectors into 1 byte vector (32 chars -> 32 bytes)
                var packed = Avx2.PackUnsignedSaturate(v0, v1);

                // Pack interleaves the vectors, permute them back to correct order
                var bytes = Avx2.Permute4x64(packed.AsInt64(), 0b_11_01_10_00).AsByte();

                // Find newlines in the packed byte vector
                var lfMask = Avx2.CompareEqual(bytes, lf);
                var crMask = Avx2.CompareEqual(bytes, cr);
                var anyNewline = Avx2.Or(lfMask, crMask);

                // Early exit if no newlines found (Sep's optimization)
                if (!Avx2.TestZ(anyNewline, anyNewline))
                {
                    // Found a newline in this vector
                    uint mask = (uint)Avx2.MoveMask(anyNewline);
                    int offset = BitOperations.TrailingZeroCount(mask);
                    int pos = i + offset;

                    if (span[pos] == '\n')
                    {
                        lineEndLength = 1;
                        return pos;
                    }
                    if (span[pos] == '\r')
                    {
                        if (pos + 1 < span.Length && span[pos + 1] == '\n')
                        {
                            lineEndLength = 2;
                            return pos;
                        }
                        lineEndLength = 1;
                        return pos;
                    }
                }

                i += charsPerIteration;
            }
        }

        // Handle remaining chars with scalar
        var remaining = FindLineEndScalar(span.Slice(i), out lineEndLength);
        return remaining == -1 ? -1 : remaining + i;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int FindLineEndScalar(ReadOnlySpan<char> span, out int lineEndLength)
    {
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

        if (_rowBoundaries != null)
            ArrayPool<int>.Shared.Return(_rowBoundaries, clearArray: false);
    }
}
