using System.Runtime.CompilerServices;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Reading.Shared;

namespace HeroParser.SeparatedValues.Reading.Rows;

/// <summary>
/// Enumerates CSV rows from a span without allocating intermediate objects.
/// </summary>
/// <typeparam name="T">The element type: <see cref="char"/> for UTF-16 or <see cref="byte"/> for UTF-8.</typeparam>
/// <remarks>
/// <para>Rows are parsed lazily as <see cref="MoveNext"/> advances.</para>
/// <para>
/// This reader uses a pooled column buffer per instance to reduce allocations. The buffer
/// is returned to the pool when <see cref="Dispose"/> is called (including by <c>foreach</c>).
/// </para>
/// <para>Uses Ends-only column indexing to minimize memory writes during parsing.</para>
/// </remarks>
public ref struct CsvRowReader<T> where T : unmanaged, IEquatable<T>
{
    private readonly ReadOnlySpan<T> data;
    private readonly CsvParserOptions options;
    private readonly PooledColumnEnds columnEndsBuffer;
    private readonly bool trackLineNumbers;
    private int position;
    private int rowCount;
    private int sourceLineNumber; // Track source line number (1-based), only when TrackSourceLineNumbers enabled

    internal CsvRowReader(ReadOnlySpan<T> data, CsvParserOptions options)
    {
        this.data = data;
        this.options = options;
        trackLineNumbers = options.TrackSourceLineNumbers;
        position = 0;
        rowCount = 0;
        sourceLineNumber = 1; // Start at line 1
        Current = default;
        // Ends-only storage: need maxColumns + 1 entries
        columnEndsBuffer = new PooledColumnEnds(options.MaxColumnCount + 1);
    }

    /// <summary>Gets the current row.</summary>
    /// <remarks>The value is only valid after <see cref="MoveNext"/> returns <see langword="true"/>.</remarks>
    public CsvRow<T> Current { get; private set; }

    /// <summary>Returns this instance so it can be consumed by <c>foreach</c>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly CsvRowReader<T> GetEnumerator() => this;

    /// <summary>
    /// Advances to the next row in the input span.
    /// </summary>
    /// <returns><see langword="true"/> when another row was parsed; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="CsvException">Thrown when the input violates <see cref="CsvParserOptions"/>.</exception>
    public bool MoveNext()
    {
        while (true)
        {
            if (position >= data.Length)
                return false;

            var remaining = data[position..];
            int rowStartLine = trackLineNumbers ? sourceLineNumber : 0; // Only capture when tracking enabled
            var result = CsvRowParser.ParseRow(
                remaining,
                options,
                columnEndsBuffer.Span,
                trackLineNumbers);

            if (result.CharsConsumed == 0)
                return false;

            // Update source line number based on newlines encountered (only when tracking enabled)
            if (trackLineNumbers)
                sourceLineNumber += result.NewlineCount;

            var rowData = remaining[..result.RowLength];
            if (rowData.IsEmpty)
            {
                position += result.CharsConsumed;
                continue;
            }

            rowCount++;
            Current = new CsvRow<T>(
                rowData,
                columnEndsBuffer.Buffer,
                result.ColumnCount,
                rowCount,
                trackLineNumbers ? rowStartLine : rowCount, // Use rowCount as fallback when tracking disabled
                options.TrimFields);

            position += result.CharsConsumed;
            if (rowCount > options.MaxRowCount)
            {
                throw new CsvException(
                    CsvErrorCode.TooManyRows,
                    $"CSV exceeds maximum row limit of {options.MaxRowCount}");
            }
            return true;
        }
    }

    /// <summary>
    /// Returns pooled buffers for column tracking.
    /// </summary>
    public readonly void Dispose()
    {
        columnEndsBuffer.Return();
    }
}
