using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Text;
using HeroParser.SeparatedValues.Core;
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

        var delimiter = (byte)options.Delimiter;
        var quote = (byte)options.Quote;
        var enableQuotes = options.EnableQuotedFields;
        int rowNumber = 0;
        int? maxRowSize = options.MaxRowSize;

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                ReadResult result = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                ReadOnlySequence<byte> buffer = result.Buffer;

                while (TryReadRow(ref buffer, quote, enableQuotes, out var rowData))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    EnsureRowSize(rowData.Length, maxRowSize);
                    rowNumber++;
                    EnsureRowCount(rowNumber, options.MaxRowCount);
                    yield return ParsePipeRow(
                        rowData.ToArray(),
                        delimiter,
                        quote,
                        enableQuotes,
                        rowNumber,
                        options.MaxColumnCount,
                        options.MaxFieldSize);
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
                        yield return ParsePipeRow(
                            buffer.ToArray(),
                            delimiter,
                            quote,
                            enableQuotes,
                            rowNumber,
                            options.MaxColumnCount,
                            options.MaxFieldSize);
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

    private static bool TryReadRow(
        ref ReadOnlySequence<byte> buffer,
        byte quote,
        bool enableQuotes,
        out ReadOnlySequence<byte> rowData)
    {
        var reader = new SequenceReader<byte>(buffer);
        bool inQuotes = false;

        while (reader.Remaining > 0)
        {
            if (!reader.TryRead(out byte current))
                break;

            if (enableQuotes && current == quote)
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (!inQuotes && current == (byte)'\n')
            {
                long consumed = reader.Consumed;
                // Determine row end (exclude the \n, and optionally \r before it)
                long rowEnd = consumed - 1; // exclude \n
                if (rowEnd > 0)
                {
                    var slice = buffer.Slice(0, rowEnd);
                    if (slice.Length > 0 && slice.Slice(slice.Length - 1).First.Span[0] == (byte)'\r')
                        rowEnd--;
                }

                rowData = buffer.Slice(0, rowEnd);
                buffer = buffer.Slice(consumed);
                return true;
            }
        }

        rowData = default;
        return false;
    }

    private static CsvPipeRow ParsePipeRow(
        byte[] rowBytes,
        byte delimiter,
        byte quote,
        bool enableQuotes,
        int rowNumber,
        int maxColumnCount,
        int? maxFieldSize)
    {
        var columnStarts = new List<int>();
        var columnLengths = new List<int>();

        int start = 0;
        bool inQuotes = false;

        void AddColumn(int endExclusive)
        {
            int fieldLength = endExclusive - start;
            if (maxFieldSize.HasValue && fieldLength > maxFieldSize.Value)
            {
                throw new CsvException(
                    CsvErrorCode.ParseError,
                    $"Field length {fieldLength} exceeds maximum allowed length of {maxFieldSize.Value}");
            }

            if (columnStarts.Count + 1 > maxColumnCount)
            {
                throw new CsvException(
                    CsvErrorCode.TooManyColumns,
                    $"Row has more than {maxColumnCount} columns");
            }

            columnStarts.Add(start);
            columnLengths.Add(fieldLength);
        }

        for (int i = 0; i < rowBytes.Length; i++)
        {
            if (enableQuotes && rowBytes[i] == quote)
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (!inQuotes && rowBytes[i] == delimiter)
            {
                AddColumn(i);
                start = i + 1;
            }
        }

        // Last column
        AddColumn(rowBytes.Length);

        return new CsvPipeRow(rowBytes, [.. columnStarts], [.. columnLengths], rowNumber);
    }

    private static void EnsureRowCount(int rowNumber, int maxRowCount)
    {
        if (rowNumber > maxRowCount)
        {
            throw new CsvException(
                CsvErrorCode.TooManyRows,
                $"CSV exceeds maximum row limit of {maxRowCount}");
        }
    }

    private static void EnsureRowSize(long rowSize, int? maxRowSize)
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
    private readonly int[] columnStarts;
    private readonly int[] columnLengths;

    /// <summary>
    /// Gets the 1-based row number.
    /// </summary>
    public int RowNumber { get; }

    /// <summary>
    /// Gets the number of columns in this row.
    /// </summary>
    public int ColumnCount => columnStarts.Length;

    internal CsvPipeRow(byte[] data, int[] columnStarts, int[] columnLengths, int rowNumber)
    {
        this.data = data;
        this.columnStarts = columnStarts;
        this.columnLengths = columnLengths;
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
            if ((uint)index >= (uint)columnStarts.Length)
                throw new IndexOutOfRangeException($"Column index {index} is out of range. Row has {columnStarts.Length} columns.");

            return new CsvPipeColumn(data, columnStarts[index], columnLengths[index]);
        }
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

    internal CsvPipeColumn(byte[] data, int start, int length)
    {
        this.data = data;
        this.start = start;
        this.length = length;
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
        if (span.Length >= 2 && span[0] == (byte)'"' && span[^1] == (byte)'"')
        {
            span = span[1..^1];
            // Handle escaped quotes
            var str = Encoding.UTF8.GetString(span);
            return str.Replace("\"\"", "\"");
        }
        return Encoding.UTF8.GetString(span);
    }

    /// <summary>
    /// Converts the column value to a string (including any quote characters).
    /// </summary>
    public override string ToString()
    {
        return ToUnquotedString();
    }
}
