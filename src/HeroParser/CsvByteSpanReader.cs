using HeroParser.Utf8;
using System.Buffers;
using System.Runtime.CompilerServices;

namespace HeroParser;

/// <summary>
/// Streaming UTF-8 reader. Parses rows directly from byte spans.
/// </summary>
public ref struct CsvByteSpanReader
{
    private readonly ReadOnlySpan<byte> _utf8;
    private readonly CsvParserOptions _options;
    private readonly int[] _columnStartsBuffer;
    private readonly int[] _columnLengthsBuffer;
    private int _position;
    private int _rowCount;
    private CsvByteSpanRow _current;

    internal CsvByteSpanReader(ReadOnlySpan<byte> utf8, CsvParserOptions options)
    {
        _utf8 = utf8;
        _options = options;
        _position = 0;
        _rowCount = 0;
        _current = default;
        _columnStartsBuffer = ArrayPool<int>.Shared.Rent(options.MaxColumns);
        _columnLengthsBuffer = ArrayPool<int>.Shared.Rent(options.MaxColumns);
    }

    /// <summary>Current row (UTF-8).</summary>
    public CsvByteSpanRow Current => _current;

    /// <summary>Return the enumerator.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public CsvByteSpanReader GetEnumerator() => this;

    /// <summary>Advance to the next row.</summary>
    public bool MoveNext()
    {
        while (true)
        {
            if (_rowCount >= _options.MaxRows)
            {
                throw new CsvException(
                    CsvErrorCode.TooManyRows,
                    $"CSV exceeds maximum row limit of {_options.MaxRows}");
            }

            if (_position >= _utf8.Length)
                return false;

            var remaining = _utf8[_position..];
            var result = Utf8StreamingParser.ParseRow(
                remaining,
                (byte)_options.Delimiter,
                (byte)_options.Quote,
                _columnStartsBuffer.AsSpan(0, _options.MaxColumns),
                _columnLengthsBuffer.AsSpan(0, _options.MaxColumns),
                _options.MaxColumns);

            if (result.CharsConsumed == 0)
                return false;

            var rowBytes = remaining[..result.RowLength];
            if (rowBytes.IsEmpty)
            {
                _position += result.CharsConsumed;
                continue;
            }

            _current = new CsvByteSpanRow(
                rowBytes,
                _columnStartsBuffer,
                _columnLengthsBuffer,
                result.ColumnCount);

            _position += result.CharsConsumed;
            _rowCount++;
            return true;
        }
    }

    /// <summary>Return pooled buffers.</summary>
    public void Dispose()
    {
        ArrayPool<int>.Shared.Return(_columnStartsBuffer, clearArray: false);
        ArrayPool<int>.Shared.Return(_columnLengthsBuffer, clearArray: false);
    }
}
