using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Text;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Reading.Shared;
using HeroParser.SeparatedValues.Reading.Rows;

namespace HeroParser;

public static partial class Csv
{
    /// <summary>
    /// Asynchronously reads CSV rows from a <see cref="PipeReader"/>.
    /// </summary>
    /// <param name="reader">The PipeReader to read from.</param>
    /// <param name="options">Optional parser options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async enumerable of CSV rows.</returns>
    /// <remarks>
    /// <para>
    /// This method enables efficient CSV parsing from any source that supports
    /// <see cref="System.IO.Pipelines"/> (network sockets, HTTP response bodies, etc.)
    /// without buffering the entire payload in memory.
    /// </para>
    /// <para>
    /// Each row is yielded as soon as a complete row is available from the pipe.
    /// The returned <see cref="CsvPipeRow"/> provides column access as UTF-8 spans.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var pipe = PipeReader.Create(networkStream);
    /// await foreach (var row in Csv.ReadFromPipeReaderAsync(pipe))
    /// {
    ///     Console.WriteLine(row[0].ToString());
    /// }
    /// </code>
    /// </example>
    public static async IAsyncEnumerable<CsvPipeRow> ReadFromPipeReaderAsync(
        PipeReader reader,
        CsvReadOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        options ??= CsvReadOptions.Default;
        options.Validate();

        var quote = (byte)options.Quote;
        var escape = options.EscapeCharacter is { } escapeChar ? (byte)escapeChar : (byte?)null;
        var enableQuotes = options.EnableQuotedFields;
        int rowNumber = 0;
        int? maxRowSize = options.MaxRowSize;
        bool bomProcessed = false;

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                ReadResult result = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                ReadOnlySequence<byte> buffer = result.Buffer;

                if (!TryProcessUtf8Bom(ref buffer, result.IsCompleted, ref bomProcessed))
                {
                    reader.AdvanceTo(buffer.Start, buffer.End);
                    continue;
                }

                while (TryReadRow(ref buffer, quote, escape, enableQuotes, out var rowData))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    EnsureRowSize(rowData.Length, maxRowSize);
                    rowNumber++;
                    EnsureRowCount(rowNumber, options.MaxRowCount);
                    yield return ParsePipeRow(rowData, options, rowNumber, quote, escape);
                }

                if (!result.IsCompleted)
                {
                    EnsureRowSize(buffer.Length, maxRowSize);
                }

                reader.AdvanceTo(buffer.Start, buffer.End);

                if (result.IsCompleted)
                {
                    // Process any remaining data that doesn't end with a newline
                    if (buffer.Length > 0)
                    {
                        EnsureRowSize(buffer.Length, maxRowSize);
                        rowNumber++;
                        EnsureRowCount(rowNumber, options.MaxRowCount);
                        yield return ParsePipeRow(buffer, options, rowNumber, quote, escape);
                    }
                    break;
                }
            }
        }
        finally
        {
            await reader.CompleteAsync().ConfigureAwait(false);
        }
    }

    internal static bool TryReadRow(
        ref ReadOnlySequence<byte> buffer,
        byte quote,
        byte? escape,
        bool enableQuotes,
        out ReadOnlySequence<byte> rowData)
    {
        var reader = new SequenceReader<byte>(buffer);
        bool inQuotes = false;
        bool skipNext = false;

        while (reader.Remaining > 0)
        {
            if (!reader.TryRead(out byte current))
                break;

            if (skipNext)
            {
                skipNext = false;
                continue;
            }

            if (escape.HasValue && current == escape.Value && reader.Remaining > 0)
            {
                skipNext = true;
                continue;
            }

            if (enableQuotes && current == quote)
            {
                if (inQuotes && reader.TryPeek(out byte next) && next == quote)
                {
                    reader.Advance(1);
                    continue;
                }

                inQuotes = !inQuotes;
                continue;
            }

            if (!inQuotes && current == (byte)'\r')
            {
                long consumed = reader.Consumed;
                bool hasLf = reader.TryPeek(out byte next) && next == (byte)'\n';
                if (hasLf)
                {
                    reader.Advance(1);
                    consumed++;
                }

                long rowEnd = consumed - (hasLf ? 2 : 1);
                rowData = buffer.Slice(0, rowEnd);
                buffer = buffer.Slice(consumed);
                return true;
            }

            if (!inQuotes && current == (byte)'\n')
            {
                long consumed = reader.Consumed;
                long rowEnd = consumed - 1;
                rowData = buffer.Slice(0, rowEnd);
                buffer = buffer.Slice(consumed);
                return true;
            }
        }

        rowData = default;
        return false;
    }

    private static CsvPipeRow ParsePipeRow(
        ReadOnlySequence<byte> rowData,
        CsvReadOptions options,
        int rowNumber,
        byte quote,
        byte? escape)
    {
        int rowLength = checked((int)rowData.Length);
        if (rowLength == 0)
        {
            return CreateEmptyPipeRow(rowNumber, options.TrimFields, quote, escape);
        }

        var rowBytes = GC.AllocateUninitializedArray<byte>(rowLength);
        rowData.CopyTo(rowBytes);

        int[] scratchColumnEnds = ArrayPool<int>.Shared.Rent(options.MaxColumnCount + 1);
        try
        {
            var parseResult = CsvRowParser.ParseRow<byte, NoTrackLineNumbers>(
                rowBytes,
                options,
                scratchColumnEnds.AsSpan(0, options.MaxColumnCount + 1));

            if (parseResult.ColumnCount == 0)
            {
                return CreateEmptyPipeRow(rowNumber, options.TrimFields, quote, escape);
            }

            var columnEnds = GC.AllocateUninitializedArray<int>(parseResult.ColumnCount + 1);
            scratchColumnEnds.AsSpan(0, parseResult.ColumnCount + 1).CopyTo(columnEnds);

            return new CsvPipeRow(
                rowBytes,
                columnEnds,
                parseResult.ColumnCount,
                rowNumber,
                options.TrimFields,
                quote,
                escape);
        }
        finally
        {
            ArrayPool<int>.Shared.Return(scratchColumnEnds, clearArray: false);
        }
    }

    private static CsvPipeRow CreateEmptyPipeRow(int rowNumber, bool trimFields, byte quote, byte? escape)
    {
        return new CsvPipeRow(
            [],
            [-1, 0],
            1,
            rowNumber,
            trimFields,
            quote,
            escape);
    }

    internal static void EnsureRowCount(int rowNumber, int maxRowCount)
    {
        if (rowNumber > maxRowCount)
        {
            throw new CsvException(
                CsvErrorCode.TooManyRows,
                $"CSV exceeds maximum row limit of {maxRowCount}");
        }
    }

    internal static void EnsureRowSize(long rowSize, int? maxRowSize)
    {
        if (maxRowSize.HasValue && rowSize > maxRowSize.Value)
        {
            throw new CsvException(
                CsvErrorCode.ParseError,
                $"Row exceeds maximum size of {maxRowSize.Value:N0} bytes. Ensure rows have proper line endings.");
        }
    }
}

/// <summary>
/// Represents a single CSV row parsed from a <see cref="PipeReader"/>.
/// </summary>
/// <remarks>
/// Provides column access as UTF-8 byte spans. Column values include quote characters
/// which can be stripped using <see cref="CsvPipeColumn.ToUnquotedString"/>.
/// </remarks>
public readonly struct CsvPipeRow
{
    private readonly byte[] data;
    private readonly int[] columnEnds;
    private readonly int columnCount;
    private readonly bool trimFields;
    private readonly byte quote;
    private readonly byte? escape;

    /// <summary>
    /// Gets the 1-based row number.
    /// </summary>
    public int RowNumber { get; }

    /// <summary>
    /// Gets the number of columns in this row.
    /// </summary>
    public int ColumnCount => columnCount;

    internal CsvPipeRow(
        byte[] data,
        int[] columnEnds,
        int columnCount,
        int rowNumber,
        bool trimFields,
        byte quote,
        byte? escape)
    {
        this.data = data;
        this.columnEnds = columnEnds;
        this.columnCount = columnCount;
        this.trimFields = trimFields;
        this.quote = quote;
        this.escape = escape;
        RowNumber = rowNumber;
    }

    /// <summary>
    /// Gets the column at the specified index.
    /// </summary>
    /// <param name="index">The zero-based column index.</param>
    /// <returns>The column value as a <see cref="CsvPipeColumn"/>.</returns>
    public CsvPipeColumn this[int index]
    {
        get
        {
            if ((uint)index >= (uint)columnCount)
                throw new IndexOutOfRangeException($"Column index {index} is out of range. Row has {columnCount} columns.");

            int start = columnEnds[index] + 1;
            int end = columnEnds[index + 1];

            if (trimFields)
            {
                (start, end) = TrimBounds(start, end);
            }

            return new CsvPipeColumn(data, start, end - start, quote, escape);
        }
    }

    private (int start, int end) TrimBounds(int start, int end)
    {
        const byte space = (byte)' ';
        const byte tab = (byte)'\t';

        if (end - start >= 2 && data[start] == quote && data[end - 1] == quote)
        {
            return (start, end);
        }

        while (start < end && (data[start] == space || data[start] == tab))
        {
            start++;
        }

        while (end > start && (data[end - 1] == space || data[end - 1] == tab))
        {
            end--;
        }

        return (start, end);
    }
}

/// <summary>
/// Represents a single column value from a <see cref="CsvPipeRow"/>.
/// </summary>
public readonly struct CsvPipeColumn
{
    private readonly byte[] data;
    private readonly int start;
    private readonly int length;
    private readonly byte quote;
    private readonly byte? escape;

    internal CsvPipeColumn(byte[] data, int start, int length, byte quote, byte? escape)
    {
        this.data = data;
        this.start = start;
        this.length = length;
        this.quote = quote;
        this.escape = escape;
    }

    /// <summary>
    /// Gets the raw UTF-8 byte span for this column.
    /// </summary>
    public ReadOnlySpan<byte> Span => data.AsSpan(start, length);

    /// <summary>
    /// Converts the column value to a string, stripping quote characters.
    /// </summary>
    public string ToUnquotedString()
    {
        var span = Span;
        if (span.Length >= 2 && span[0] == quote && span[^1] == quote)
        {
            span = span[1..^1];
        }

        if (span.IsEmpty)
            return string.Empty;

        var decoded = Encoding.UTF8.GetString(span);
        char quoteChar = (char)quote;

        if (escape is not null)
        {
            char escapeChar = (char)escape.Value;
            if (!decoded.AsSpan().Contains(escapeChar) && !decoded.AsSpan().Contains(quoteChar))
                return decoded;

            var result = new StringBuilder(decoded.Length);
            for (int i = 0; i < decoded.Length; i++)
            {
                char current = decoded[i];
                if (current == escapeChar && i + 1 < decoded.Length)
                {
                    i++;
                    result.Append(decoded[i]);
                }
                else if (current == quoteChar && i + 1 < decoded.Length && decoded[i + 1] == quoteChar)
                {
                    result.Append(quoteChar);
                    i++;
                }
                else
                {
                    result.Append(current);
                }
            }

            return result.ToString();
        }

        if (!decoded.AsSpan().Contains(quoteChar))
            return decoded;

        var unescaped = new StringBuilder(decoded.Length);
        for (int i = 0; i < decoded.Length; i++)
        {
            char current = decoded[i];
            if (current == quoteChar && i + 1 < decoded.Length && decoded[i + 1] == quoteChar)
            {
                unescaped.Append(quoteChar);
                i++;
            }
            else
            {
                unescaped.Append(current);
            }
        }

        return unescaped.ToString();
    }

    /// <summary>
    /// Converts the column value to a string (including any quote characters).
    /// </summary>
    public override string ToString()
    {
        return ToUnquotedString();
    }
}
