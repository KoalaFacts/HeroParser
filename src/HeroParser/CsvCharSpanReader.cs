using HeroParser.Utf16;
using System.Buffers;
using System.Runtime.CompilerServices;

namespace HeroParser;

/// <summary>
/// Streaming UTF-16 reader. Each row is parsed exactly once via Utf16StreamingParser.
/// </summary>
public ref struct CsvCharSpanReader
{
    private readonly ReadOnlySpan<char> _chars;
    private readonly CsvParserOptions _options;
    private readonly int[] _columnStartsBuffer;
    private readonly int[] _columnLengthsBuffer;
    private int _position;
    private int _rowCount;
    private CsvCharSpanRow _current;

    internal CsvCharSpanReader(ReadOnlySpan<char> chars, CsvParserOptions options)
    {
        _chars = chars;
        _options = options;
        _position = 0;
        _rowCount = 0;
        _current = default;
        _columnStartsBuffer = ArrayPool<int>.Shared.Rent(options.MaxColumns);
        _columnLengthsBuffer = ArrayPool<int>.Shared.Rent(options.MaxColumns);
    }

    /// <summary>Current row.</summary>
    public CsvCharSpanRow Current => _current;

    /// <summary>Return the enumerator.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public CsvCharSpanReader GetEnumerator() => this;

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

            if (_position >= _chars.Length)
                return false;

            var remaining = _chars[_position..];
            var result = Utf16StreamingParser.ParseRow(
                remaining,
                _options.Delimiter,
                _options.Quote,
                _columnStartsBuffer.AsSpan(0, _options.MaxColumns),
                _columnLengthsBuffer.AsSpan(0, _options.MaxColumns),
                _options.MaxColumns);

            if (result.CharsConsumed == 0)
                return false;

            var rowChars = remaining[..result.RowLength];
            if (rowChars.IsEmpty)
            {
                _position += result.CharsConsumed;
                continue;
            }

            _current = new CsvCharSpanRow(
                rowChars,
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
