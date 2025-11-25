using System.Buffers;
using System.Runtime.CompilerServices;

namespace HeroParser.SeparatedValues;

/// <summary>
/// Enumerates CSV rows from a UTF-16 span without allocating intermediate objects.
/// </summary>
/// <remarks>Rows are parsed lazily as <see cref="MoveNext"/> advances; call <see cref="Dispose"/> to return pooled buffers.</remarks>
public ref struct CsvCharSpanReader
{
    private readonly ReadOnlySpan<char> chars;
    private readonly CsvParserOptions options;
    private readonly int[] columnStartsBuffer;
    private readonly int[] columnLengthsBuffer;
    private int position;
    private int rowCount;

    internal CsvCharSpanReader(ReadOnlySpan<char> chars, CsvParserOptions options)
    {
        this.chars = chars;
        this.options = options;
        position = 0;
        rowCount = 0;
        Current = default;
        columnStartsBuffer = ArrayPool<int>.Shared.Rent(options.MaxColumns);
        columnLengthsBuffer = ArrayPool<int>.Shared.Rent(options.MaxColumns);
    }

    /// <summary>Gets the current UTF-16 backed row.</summary>
    /// <remarks>The value is only valid after <see cref="MoveNext"/> returns <see langword="true"/>.</remarks>
    public CsvCharSpanRow Current { get; private set; }

    /// <summary>Returns this instance so it can be consumed by <c>foreach</c>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly CsvCharSpanReader GetEnumerator() => this;

    /// <summary>
    /// Advances to the next row in the input span.
    /// </summary>
    /// <returns><see langword="true"/> when another row was parsed; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="CsvException">Thrown when the input violates <see cref="CsvParserOptions"/>.</exception>
    public bool MoveNext()
    {
        while (true)
        {
            if (position >= chars.Length)
                return false;

            var remaining = chars[position..];
            var result = CsvStreamingParser.ParseRow(
                remaining,
                options,
                columnStartsBuffer.AsSpan(0, options.MaxColumns),
                columnLengthsBuffer.AsSpan(0, options.MaxColumns));

            if (result.CharsConsumed == 0)
                return false;

            var rowChars = remaining[..result.RowLength];
            if (rowChars.IsEmpty)
            {
                position += result.CharsConsumed;
                continue;
            }

            rowCount++;
            Current = new CsvCharSpanRow(
                rowChars,
                columnStartsBuffer,
                columnLengthsBuffer,
                result.ColumnCount,
                rowCount);

            position += result.CharsConsumed;
            if (rowCount > options.MaxRows)
            {
                throw new CsvException(
                    CsvErrorCode.TooManyRows,
                    $"CSV exceeds maximum row limit of {options.MaxRows}");
            }
            return true;
        }
    }

    /// <summary>
    /// Returns pooled buffers used by the reader.
    /// </summary>
    /// <remarks>Always call this method (or use a <c>using</c> statement) when the reader is no longer needed.</remarks>
    public readonly void Dispose()
    {
        ArrayPool<int>.Shared.Return(columnStartsBuffer, clearArray: false);
        ArrayPool<int>.Shared.Return(columnLengthsBuffer, clearArray: false);
    }
}
