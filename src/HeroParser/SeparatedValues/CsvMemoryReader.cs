using System.Runtime.CompilerServices;

namespace HeroParser.SeparatedValues;

/// <summary>
/// Enumerates CSV rows from a <see cref="ReadOnlyMemory{T}"/> without stack-only constraints.
/// </summary>
/// <remarks>
/// <para>
/// Unlike <see cref="CsvCharSpanReader"/>, this reader can be stored in fields and enables
/// zero-allocation binding to <see cref="ReadOnlyMemory{T}"/> properties. Use this reader
/// when you need to bind to records with <see cref="ReadOnlyMemory{T}"/> string properties.
/// </para>
/// <para>
/// <b>Thread Safety:</b> This reader is NOT thread-safe. Each thread should create its own
/// reader instance. However, the <see cref="CsvMemoryRow"/> instances produced by this reader
/// are immutable and safe to pass between threads after creation.
/// </para>
/// <para>Uses Ends-only column indexing to minimize memory writes during parsing.</para>
/// </remarks>
public struct CsvMemoryReader
{
    private readonly ReadOnlyMemory<char> chars;
    private readonly CsvParserOptions options;
    private readonly int[] columnEndsBuffer;
    private readonly bool trackLineNumbers;
    private int position;
    private int rowCount;
    private int sourceLineNumber;

    /// <summary>
    /// Creates a new CSV reader for the specified memory.
    /// </summary>
    /// <param name="chars">The character memory to parse.</param>
    /// <param name="options">Parser options.</param>
    public CsvMemoryReader(ReadOnlyMemory<char> chars, CsvParserOptions options)
    {
        this.chars = chars;
        this.options = options;
        trackLineNumbers = options.TrackSourceLineNumbers;
        position = 0;
        rowCount = 0;
        sourceLineNumber = 1;
        Current = default;
        // Ends-only storage: need maxColumns + 1 entries
        columnEndsBuffer = new int[options.MaxColumnCount + 1];
    }

    /// <summary>Gets the current memory-backed row.</summary>
    public CsvMemoryRow Current { get; private set; }

    /// <summary>Returns this instance for <c>foreach</c> support.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly CsvMemoryReader GetEnumerator() => this;

    /// <summary>
    /// Advances to the next row in the input memory.
    /// </summary>
    public bool MoveNext()
    {
        while (true)
        {
            if (position >= chars.Length)
                return false;

            var remaining = chars[position..];
            int rowStartLine = trackLineNumbers ? sourceLineNumber : 0;
            var result = CsvStreamingParser.ParseRow(
                remaining.Span,
                options,
                columnEndsBuffer.AsSpan(0, options.MaxColumnCount + 1),
                trackLineNumbers);

            if (result.CharsConsumed == 0)
                return false;

            if (trackLineNumbers)
                sourceLineNumber += result.NewlineCount;

            if (result.RowLength == 0)
            {
                position += result.CharsConsumed;
                continue;
            }

            rowCount++;
            var rowMemory = remaining[..result.RowLength];

            // Ends-only storage: copy only the ends array
            int count = result.ColumnCount;
            var columnEnds = new int[count + 1];
            Array.Copy(columnEndsBuffer, 0, columnEnds, 0, count + 1);

            Current = new CsvMemoryRow(
                rowMemory,
                columnEnds,
                count,
                rowCount,
                trackLineNumbers ? rowStartLine : rowCount,
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

    /// <summary>No-op for compatibility.</summary>
    public readonly void Dispose()
    {
    }
}
