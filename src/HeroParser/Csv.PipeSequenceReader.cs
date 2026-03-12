using System.Buffers;
using System.Buffers.Binary;
using System.IO.Pipelines;
using System.Text;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Reading.Shared;
using HeroParser.SeparatedValues.Reading.Rows;

namespace HeroParser;

public static partial class Csv
{
    /// <summary>
    /// Creates a borrowed CSV reader over a <see cref="PipeReader"/> without copying each row.
    /// </summary>
    /// <param name="reader">The pipe reader to read from.</param>
    /// <param name="options">Optional parser options.</param>
    /// <returns>
    /// A reader whose <see cref="CsvPipeSequenceReader.Current"/> row is backed by the underlying
    /// <see cref="ReadOnlySequence{T}"/> and is only valid until the next <c>MoveNextAsync</c> call.
    /// </returns>
    public static CsvPipeSequenceReader CreatePipeSequenceReader(
        PipeReader reader,
        CsvReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(reader);
        options ??= CsvReadOptions.Default;
        options.Validate();
        return new CsvPipeSequenceReader(reader, options);
    }

    internal static bool TryProcessUtf8Bom(ref ReadOnlySequence<byte> buffer, bool isCompleted, ref bool bomProcessed)
    {
        if (bomProcessed)
        {
            return true;
        }

        if (buffer.Length < 3)
        {
            if (!isCompleted)
            {
                return false;
            }

            bomProcessed = true;
            return true;
        }

        Span<byte> prefix = stackalloc byte[3];
        buffer.Slice(0, 3).CopyTo(prefix);
        if (prefix[0] == 0xEF && prefix[1] == 0xBB && prefix[2] == 0xBF)
        {
            buffer = buffer.Slice(3);
        }

        bomProcessed = true;
        return true;
    }

    internal static int CountNewlines(ReadOnlySequence<byte> sequence)
    {
        int count = 0;
        bool previousWasCr = false;

        foreach (var segment in sequence)
        {
            var span = segment.Span;
            int start = 0;

            if (previousWasCr)
            {
                if (!span.IsEmpty && span[0] == (byte)'\n')
                {
                    start = 1;
                }

                previousWasCr = false;
            }

            for (int i = start; i < span.Length; i++)
            {
                if (span[i] == (byte)'\n')
                {
                    count++;
                }
                else if (span[i] == (byte)'\r')
                {
                    count++;
                    if (i + 1 < span.Length && span[i + 1] == (byte)'\n')
                    {
                        i++;
                    }
                    else if (i + 1 == span.Length)
                    {
                        previousWasCr = true;
                    }
                }
            }
        }

        return count;
    }

    internal static bool IsLineTerminated(ReadOnlySpan<byte> span, int consumed)
    {
        if ((uint)(consumed - 1) >= (uint)span.Length)
        {
            return false;
        }

        byte last = span[consumed - 1];
        return last is (byte)'\n' or (byte)'\r';
    }

    internal static int ParsePipeSequenceRow(
        ReadOnlySequence<byte> rowData,
        CsvReadOptions options,
        int rowNumber,
        byte quote,
        byte? escape,
        Span<int> columnEnds)
    {
        if (rowData.IsEmpty)
        {
            return 0;
        }

        if (rowData.IsSingleSegment)
        {
            var parseResult = CsvRowParser.ParseRow<byte, NoTrackLineNumbers>(
                rowData.FirstSpan,
                options,
                columnEnds);

            return parseResult.ColumnCount;
        }

        const byte space = (byte)' ';
        const byte tab = (byte)'\t';
        byte delimiter = (byte)options.Delimiter;
        bool enableQuotes = options.EnableQuotedFields;

        if (options.CommentCharacter is { } commentChar)
        {
            var commentReader = new SequenceReader<byte>(rowData);
            while (commentReader.TryRead(out byte value))
            {
                if (value == (byte)commentChar)
                {
                    return 0;
                }

                if (value != space && value != tab)
                {
                    break;
                }
            }
        }

        columnEnds[0] = -1;

        var reader = new SequenceReader<byte>(rowData);
        int columnCount = 0;
        int currentStart = 0;
        bool inQuotes = false;
        int quoteStartPosition = -1;

        while (reader.TryRead(out byte current))
        {
            int index = checked((int)(reader.Consumed - 1));

            if (escape.HasValue && current == escape.Value && reader.Remaining > 0)
            {
                reader.Advance(1);
                continue;
            }

            if (enableQuotes && current == quote)
            {
                if (inQuotes && reader.TryPeek(out byte next) && next == quote)
                {
                    reader.Advance(1);
                    continue;
                }

                if (!inQuotes)
                {
                    quoteStartPosition = index;
                }

                inQuotes = !inQuotes;
                continue;
            }

            if (enableQuotes && inQuotes && !options.AllowNewlinesInsideQuotes &&
                (current == (byte)'\r' || current == (byte)'\n'))
            {
                throw new CsvException(
                    CsvErrorCode.ParseError,
                    "Newlines inside quoted fields are disabled. Enable AllowNewlinesInsideQuotes to parse them.");
            }

            if (enableQuotes && inQuotes)
            {
                continue;
            }

            if (current == delimiter)
            {
                AppendPipeColumn(index, ref columnCount, ref currentStart, columnEnds, options.MaxColumnCount, options.MaxFieldSize);
            }
        }

        if (enableQuotes && inQuotes)
        {
            throw CsvException.UnterminatedQuote(
                "Unterminated quoted field detected while parsing CSV data.",
                rowNumber,
                quoteStartPosition);
        }

        AppendFinalPipeColumn(checked((int)rowData.Length), ref columnCount, ref currentStart, columnEnds, options.MaxColumnCount, options.MaxFieldSize);
        return columnCount;
    }

    private static void AppendPipeColumn(
        int delimiterIndex,
        ref int columnCount,
        ref int currentStart,
        Span<int> columnEnds,
        int maxColumns,
        int? maxFieldLength)
    {
        if (columnCount + 1 > maxColumns)
        {
            throw new CsvException(
                CsvErrorCode.TooManyColumns,
                $"Row has more than {maxColumns} columns");
        }

        int fieldLength = delimiterIndex - currentStart;
        if (maxFieldLength.HasValue && fieldLength > maxFieldLength.Value)
        {
            throw new CsvException(
                CsvErrorCode.ParseError,
                $"Field length {fieldLength} exceeds maximum allowed length of {maxFieldLength.Value}");
        }

        columnEnds[columnCount + 1] = delimiterIndex;
        columnCount++;
        currentStart = delimiterIndex + 1;
    }

    private static void AppendFinalPipeColumn(
        int rowLength,
        ref int columnCount,
        ref int currentStart,
        Span<int> columnEnds,
        int maxColumns,
        int? maxFieldLength)
    {
        if (columnCount + 1 > maxColumns)
        {
            throw new CsvException(
                CsvErrorCode.TooManyColumns,
                $"Row has more than {maxColumns} columns");
        }

        int fieldLength = rowLength - currentStart;
        if (maxFieldLength.HasValue && fieldLength > maxFieldLength.Value)
        {
            throw new CsvException(
                CsvErrorCode.ParseError,
                $"Field length {fieldLength} exceeds maximum allowed length of {maxFieldLength.Value}");
        }

        columnEnds[columnCount + 1] = rowLength;
        columnCount++;
    }
}

/// <summary>
/// Async CSV reader over a <see cref="PipeReader"/> that borrows row data directly from the pipe buffer.
/// </summary>
/// <remarks>
/// The <see cref="Current"/> row is only valid until the next <see cref="MoveNextAsync"/> call.
/// Use <see cref="Csv.ReadFromPipeReaderAsync(PipeReader, CsvReadOptions?, CancellationToken)"/> if you need
/// an owning row object that remains valid after advancing.
/// </remarks>
public sealed class CsvPipeSequenceReader : IAsyncDisposable
{
    private readonly PipeReader reader;
    private readonly CsvReadOptions options;
    private readonly PooledColumnEnds columnEndsBuffer;
    private readonly byte quote;
    private readonly byte? escape;
    private readonly bool enableQuotes;
    private readonly bool trackLineNumbers;
    private readonly int skipRows;
    private readonly int? maxRowSize;

    private bool disposed;
    private bool bomProcessed;
    private bool hasCurrent;
    private bool hasBufferedRead;
    private bool bufferedIsCompleted;
    private SequencePosition bufferedExamined;
    private ReadOnlySequence<byte> bufferedData;
    private ReadOnlySequence<byte> currentRowData;
    private int currentColumnCount;
    private int currentRowNumber;
    private int currentSourceLineNumber;
    private int rowNumber;
    private int sourceLineNumber;
    private int skippedRows;

    internal CsvPipeSequenceReader(PipeReader reader, CsvReadOptions options, int skipRows = 0)
    {
        this.reader = reader;
        this.options = options;
        columnEndsBuffer = new PooledColumnEnds(options.MaxColumnCount + 1);
        quote = (byte)options.Quote;
        escape = options.EscapeCharacter is { } escapeChar ? (byte)escapeChar : null;
        enableQuotes = options.EnableQuotedFields;
        trackLineNumbers = options.TrackSourceLineNumbers;
        this.skipRows = skipRows;
        maxRowSize = options.MaxRowSize;
        sourceLineNumber = 1;
    }

    /// <summary>The current row; valid until the next <see cref="MoveNextAsync"/> call.</summary>
    public CsvPipeSequenceRow Current
    {
        get
        {
            ThrowIfDisposed();
            if (!hasCurrent)
            {
                throw new InvalidOperationException("No current row is available. Call MoveNextAsync() first.");
            }

            return new CsvPipeSequenceRow(
                currentRowData,
                columnEndsBuffer.Buffer.AsSpan(0, currentColumnCount + 1),
                currentColumnCount,
                currentRowNumber,
                currentSourceLineNumber,
                options.TrimFields,
                quote,
                escape);
        }
    }

    /// <summary>
    /// Advances to the next row, reading from the underlying <see cref="PipeReader"/> as needed.
    /// </summary>
    public async ValueTask<bool> MoveNextAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        hasCurrent = false;

        while (true)
        {
            if (!hasBufferedRead)
            {
                ReadResult result = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                ReadOnlySequence<byte> readBuffer = result.Buffer;

                bool bomHandled = bomProcessed;
                if (!Csv.TryProcessUtf8Bom(ref readBuffer, result.IsCompleted, ref bomHandled))
                {
                    reader.AdvanceTo(readBuffer.Start, readBuffer.End);
                    continue;
                }

                bomProcessed = bomHandled;
                bufferedData = readBuffer;
                bufferedExamined = readBuffer.End;
                bufferedIsCompleted = result.IsCompleted;
                hasBufferedRead = true;
            }

            ReadOnlySequence<byte> buffer = bufferedData;

            if (buffer.IsSingleSegment)
            {
                ReadOnlySpan<byte> span = buffer.FirstSpan;
                if (span.IsEmpty)
                {
                    bool isCompleted = bufferedIsCompleted;
                    ReleaseBufferedRead(buffer.Start);
                    if (!isCompleted)
                    {
                        continue;
                    }

                    return false;
                }

                int rowSourceLineNumber = trackLineNumbers ? sourceLineNumber : 0;
                CsvRowParseResult parseResult;
                try
                {
                    parseResult = trackLineNumbers
                        ? CsvRowParser.ParseRow<byte, TrackLineNumbers>(span, options, columnEndsBuffer.Span)
                        : CsvRowParser.ParseRow<byte, NoTrackLineNumbers>(span, options, columnEndsBuffer.Span);
                }
                catch (CsvException ex) when (!bufferedIsCompleted && ex.QuoteStartPosition.HasValue)
                {
                    Csv.EnsureRowSize(buffer.Length, maxRowSize);
                    ReleaseBufferedRead(buffer.Start);
                    continue;
                }

                if (parseResult.CharsConsumed == 0)
                {
                    bool isCompleted = bufferedIsCompleted;
                    ReleaseBufferedRead(buffer.Start);
                    if (!isCompleted)
                    {
                        continue;
                    }

                    return false;
                }

                Csv.EnsureRowSize(parseResult.RowLength, maxRowSize);
                if (options.CommentCharacter is not null &&
                    parseResult.RowLength == 0 &&
                    parseResult.CharsConsumed > 0 &&
                    !bufferedIsCompleted &&
                    !Csv.IsLineTerminated(span, parseResult.CharsConsumed))
                {
                    ReleaseBufferedRead(buffer.Start);
                    continue;
                }

                if (parseResult.RowLength == span.Length && !bufferedIsCompleted)
                {
                    ReleaseBufferedRead(buffer.Start);
                    continue;
                }

                currentRowData = bufferedData.Slice(0, parseResult.RowLength);
                bufferedData = buffer.Slice(parseResult.CharsConsumed);

                if (trackLineNumbers)
                {
                    sourceLineNumber += Csv.CountNewlines(currentRowData);
                    if (parseResult.CharsConsumed > parseResult.RowLength)
                    {
                        sourceLineNumber++;
                    }
                }

                if (parseResult.RowLength == 0)
                {
                    continue;
                }

                rowNumber++;
                Csv.EnsureRowCount(rowNumber, options.MaxRowCount);

                if (skippedRows < skipRows)
                {
                    skippedRows++;
                    continue;
                }

                currentColumnCount = parseResult.ColumnCount;
                currentRowNumber = rowNumber;
                currentSourceLineNumber = trackLineNumbers ? rowSourceLineNumber : rowNumber;
                hasCurrent = true;
                return true;
            }

            while (true)
            {
                ReadOnlySequence<byte> rowData;
                int columnCount;
                int newlineCount;

                if (options.CommentCharacter is null)
                {
                    if (!Csv.TryReadRow(
                        ref buffer,
                        options,
                        quote,
                        escape,
                        enableQuotes,
                        columnEndsBuffer.Span,
                        out rowData,
                        out columnCount,
                        out newlineCount))
                    {
                        break;
                    }
                }
                else
                {
                    if (!Csv.TryReadRow(ref buffer, quote, escape, enableQuotes, out rowData))
                    {
                        break;
                    }

                    columnCount = Csv.ParsePipeSequenceRow(rowData, options, rowNumber + 1, quote, escape, columnEndsBuffer.Span);
                    newlineCount = trackLineNumbers ? Csv.CountNewlines(rowData) + 1 : 0;
                }

                Csv.EnsureRowSize(rowData.Length, maxRowSize);

                int rowSourceLineNumber = trackLineNumbers ? sourceLineNumber : 0;
                if (trackLineNumbers)
                {
                    sourceLineNumber += newlineCount;
                }

                if (columnCount == 0)
                {
                    continue;
                }

                rowNumber++;
                Csv.EnsureRowCount(rowNumber, options.MaxRowCount);

                if (skippedRows < skipRows)
                {
                    skippedRows++;
                    continue;
                }

                currentRowData = rowData;
                currentColumnCount = columnCount;
                currentRowNumber = rowNumber;
                currentSourceLineNumber = trackLineNumbers ? rowSourceLineNumber : rowNumber;
                hasCurrent = true;
                bufferedData = buffer;
                return true;
            }

            if (!bufferedIsCompleted)
            {
                Csv.EnsureRowSize(buffer.Length, maxRowSize);
                ReleaseBufferedRead(buffer.Start);
                continue;
            }

            if (buffer.Length > 0)
            {
                Csv.EnsureRowSize(buffer.Length, maxRowSize);

                int rowSourceLineNumber = trackLineNumbers ? sourceLineNumber : 0;
                if (trackLineNumbers)
                {
                    sourceLineNumber += Csv.CountNewlines(buffer);
                }

                int columnCount = Csv.ParsePipeSequenceRow(buffer, options, rowNumber + 1, quote, escape, columnEndsBuffer.Span);
                if (columnCount == 0)
                {
                    ReleaseBufferedRead(buffer.End);
                    return false;
                }

                rowNumber++;
                Csv.EnsureRowCount(rowNumber, options.MaxRowCount);

                if (skippedRows < skipRows)
                {
                    skippedRows++;
                    ReleaseBufferedRead(buffer.End);
                    return false;
                }

                currentRowData = buffer;
                currentColumnCount = columnCount;
                currentRowNumber = rowNumber;
                currentSourceLineNumber = trackLineNumbers ? rowSourceLineNumber : rowNumber;
                hasCurrent = true;
                bufferedData = buffer.Slice(buffer.Length);
                return true;
            }

            ReleaseBufferedRead(buffer.Start);
            return false;
        }
    }

    /// <summary>
    /// Completes the underlying <see cref="PipeReader"/> and releases pooled buffers.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;

        if (hasBufferedRead)
        {
            ReleaseBufferedRead(bufferedData.Start);
        }

        columnEndsBuffer.Return();
        await reader.CompleteAsync().ConfigureAwait(false);
    }

    private void ReleaseBufferedRead(SequencePosition consumed)
    {
        if (!hasBufferedRead)
        {
            return;
        }

        reader.AdvanceTo(consumed, bufferedExamined);
        hasBufferedRead = false;
        bufferedIsCompleted = false;
        bufferedData = default;
        bufferedExamined = default;
    }

    private void ThrowIfDisposed()
    {
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(CsvPipeSequenceReader));
        }
    }
}

/// <summary>
/// Represents a single borrowed CSV row from a <see cref="CsvPipeSequenceReader"/>.
/// </summary>
public readonly ref struct CsvPipeSequenceRow
{
    private readonly ReadOnlySequence<byte> data;
    private readonly ReadOnlySpan<int> columnEnds;
    private readonly int columnCount;
    private readonly bool trimFields;
    private readonly byte quote;
    private readonly byte? escape;

    internal CsvPipeSequenceRow(
        ReadOnlySequence<byte> data,
        ReadOnlySpan<int> columnEnds,
        int columnCount,
        int rowNumber,
        int sourceLineNumber,
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
        SourceLineNumber = sourceLineNumber;
    }

    /// <summary>Gets the 1-based logical row number.</summary>
    public int RowNumber { get; }

    /// <summary>Gets the 1-based source line number where this row starts.</summary>
    public int SourceLineNumber { get; }

    /// <summary>Gets the number of parsed columns in this row.</summary>
    public int ColumnCount => columnCount;

    /// <summary>Gets the borrowed raw row bytes.</summary>
    public ReadOnlySequence<byte> RawRecord => data;

    /// <summary>Gets the column at the specified zero-based index.</summary>
    public CsvPipeSequenceColumn this[int index]
    {
        get
        {
            if ((uint)index >= (uint)columnCount)
            {
                throw new IndexOutOfRangeException($"Column index {index} is out of range. Row has {columnCount} columns.");
            }

            long start = columnEnds[index] + 1L;
            long end = columnEnds[index + 1];

            if (trimFields)
            {
                (start, end) = TrimBounds(start, end);
            }

            return new CsvPipeSequenceColumn(data.Slice(start, end - start), quote, escape);
        }
    }

    internal bool TryGetContiguousRow(out CsvRow<byte> row)
    {
        if (data.IsSingleSegment)
        {
            row = CreateContiguousRow(data.FirstSpan);
            return true;
        }

        row = default;
        return false;
    }

    internal CsvRow<byte> CreateContiguousRow(ReadOnlySpan<byte> contiguousData)
        => new(
            contiguousData,
            columnEnds,
            columnCount,
            RowNumber,
            SourceLineNumber,
            trimFields);

    internal CsvPipeRow ToOwnedRow()
    {
        int rowLength = checked((int)data.Length);
        int headerElementSize = CsvPipeRow.GetHeaderElementSize(rowLength);
        int headerLength = checked((columnCount + 1) * headerElementSize);
        var storage = GC.AllocateUninitializedArray<byte>(checked(headerLength + rowLength));

        var header = storage.AsSpan(0, headerLength);
        switch (headerElementSize)
        {
            case 1:
                for (int i = 0; i <= columnCount; i++)
                {
                    header[i] = checked((byte)(columnEnds[i] + 1));
                }
                break;

            case 2:
                for (int i = 0; i <= columnCount; i++)
                {
                    BinaryPrimitives.WriteUInt16LittleEndian(
                        header.Slice(i * sizeof(ushort), sizeof(ushort)),
                        checked((ushort)(columnEnds[i] + 1)));
                }
                break;

            default:
                for (int i = 0; i <= columnCount; i++)
                {
                    BinaryPrimitives.WriteInt32LittleEndian(
                        header.Slice(i * sizeof(int), sizeof(int)),
                        columnEnds[i] + 1);
                }
                break;
        }

        data.CopyTo(storage.AsSpan(headerLength));

        return new CsvPipeRow(
            storage,
            columnCount,
            RowNumber,
            trimFields,
            quote,
            escape,
            headerLength,
            headerElementSize);
    }

    private (long start, long end) TrimBounds(long start, long end)
    {
        const byte space = (byte)' ';
        const byte tab = (byte)'\t';

        if (end - start >= 2 &&
            CsvPipeSequenceColumn.GetByteAt(data, start) == quote &&
            CsvPipeSequenceColumn.GetByteAt(data, end - 1) == quote)
        {
            return (start, end);
        }

        while (start < end)
        {
            byte value = CsvPipeSequenceColumn.GetByteAt(data, start);
            if (value != space && value != tab)
            {
                break;
            }

            start++;
        }

        while (end > start)
        {
            byte value = CsvPipeSequenceColumn.GetByteAt(data, end - 1);
            if (value != space && value != tab)
            {
                break;
            }

            end--;
        }

        return (start, end);
    }
}

/// <summary>
/// Represents a single borrowed CSV column from a <see cref="CsvPipeSequenceRow"/>.
/// </summary>
public readonly ref struct CsvPipeSequenceColumn
{
    private readonly ReadOnlySequence<byte> data;
    private readonly byte quote;
    private readonly byte? escape;

    internal CsvPipeSequenceColumn(ReadOnlySequence<byte> data, byte quote, byte? escape)
    {
        this.data = data;
        this.quote = quote;
        this.escape = escape;
    }

    /// <summary>Gets the length of the column in bytes.</summary>
    public int Length => checked((int)data.Length);

    /// <summary>Gets the borrowed bytes for this column.</summary>
    public ReadOnlySequence<byte> Sequence => data;

    /// <summary>Gets whether the column data is stored in a single contiguous segment.</summary>
    public bool IsSingleSegment => data.IsSingleSegment;

    /// <summary>
    /// Gets the raw column bytes as a span when the column is contiguous.
    /// </summary>
    public ReadOnlySpan<byte> Span
        => data.IsSingleSegment
            ? data.FirstSpan
            : throw new InvalidOperationException("Column data spans multiple segments. Use Sequence, ToArray(), or ToUnquotedString().");

    /// <summary>Copies the column bytes into a new array.</summary>
    public byte[] ToArray() => data.ToArray();

    /// <summary>Copies the column bytes into the destination span.</summary>
    public bool TryCopyTo(Span<byte> destination)
    {
        if (destination.Length < data.Length)
        {
            return false;
        }

        data.CopyTo(destination);
        return true;
    }

    /// <summary>
    /// Decodes the column as UTF-8 text and removes CSV quoting/escaping.
    /// </summary>
    public string ToUnquotedString()
    {
        ReadOnlySequence<byte> value = data;
        if (value.Length >= 2 &&
            GetByteAt(value, 0) == quote &&
            GetByteAt(value, value.Length - 1) == quote)
        {
            value = value.Slice(1, value.Length - 2);
        }

        if (value.IsEmpty)
        {
            return string.Empty;
        }

        string decoded = value.IsSingleSegment
            ? Encoding.UTF8.GetString(value.FirstSpan)
            : Encoding.UTF8.GetString(value.ToArray());

        char quoteChar = (char)quote;

        if (escape is not null)
        {
            char escapeChar = (char)escape.Value;
            if (!decoded.AsSpan().Contains(escapeChar) && !decoded.AsSpan().Contains(quoteChar))
            {
                return decoded;
            }

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
        {
            return decoded;
        }

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

    /// <summary>Returns the decoded, unquoted column text.</summary>
    public override string ToString() => ToUnquotedString();

    internal static byte GetByteAt(ReadOnlySequence<byte> sequence, long index)
        => sequence.Slice(index, 1).FirstSpan[0];
}
