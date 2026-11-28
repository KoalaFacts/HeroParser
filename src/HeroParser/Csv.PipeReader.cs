using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Reading.Shared;
using HeroParser.SeparatedValues.Reading.Rows;
using System.Globalization;

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
    /// For zero-copy, O(1) low-allocation streaming, the yielded <see cref="CsvPipeRow"/> borrows the active
    /// pipe/pooled buffer directly and is only valid until the next <see cref="IAsyncEnumerator{T}.MoveNextAsync"/>
    /// call or enumerator disposal. Call <see cref="CsvPipeRow.Clone"/> if you need to retain a row past the current iteration.
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
    public static IAsyncEnumerable<CsvPipeRow> ReadFromPipeReaderAsync(
        PipeReader reader,
        CsvReadOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        options ??= CsvReadOptions.Default;
        options.Validate();
        return new CsvPipeRowAsyncEnumerable(reader, options, cancellationToken);
    }
}

internal sealed class CsvPipeRowAsyncEnumerable : IAsyncEnumerable<CsvPipeRow>
{
    private readonly PipeReader reader;
    private readonly CsvReadOptions options;
    private readonly CancellationToken cancellationToken;

    public CsvPipeRowAsyncEnumerable(
        PipeReader reader,
        CsvReadOptions options,
        CancellationToken cancellationToken)
    {
        this.reader = reader;
        this.options = options;
        this.cancellationToken = cancellationToken;
    }

    public IAsyncEnumerator<CsvPipeRow> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        => new Enumerator(reader, options, this.cancellationToken, cancellationToken);

    private sealed class Enumerator : IAsyncEnumerator<CsvPipeRow>
    {
        private readonly PipeReader reader;
        private readonly CsvReadOptions options;
        private readonly CancellationTokenSource? linkedCancellationSource;
        private readonly CancellationToken cancellationToken;

        private CsvPipeSequenceReader? sequenceReader;
        private byte[]? rentedRowBuffer;
        private bool completed;

        public Enumerator(
            PipeReader reader,
            CsvReadOptions options,
            CancellationToken methodCancellationToken,
            CancellationToken enumeratorCancellationToken)
        {
            this.reader = reader;
            this.options = options;

            if (!methodCancellationToken.CanBeCanceled)
            {
                cancellationToken = enumeratorCancellationToken;
            }
            else if (!enumeratorCancellationToken.CanBeCanceled || enumeratorCancellationToken == methodCancellationToken)
            {
                cancellationToken = methodCancellationToken;
            }
            else
            {
                linkedCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(
                    methodCancellationToken,
                    enumeratorCancellationToken);
                cancellationToken = linkedCancellationSource.Token;
            }
        }

        public CsvPipeRow Current { get; private set; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ValueTask<bool> MoveNextAsync()
        {
            Current = default;

            if (completed)
            {
                return ValueTask.FromResult(false);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return ValueTask.FromCanceled<bool>(cancellationToken);
            }

            if (sequenceReader is { } sr)
            {
                try
                {
                    if (sr.TryMoveNextBorrowedRow(out var fastRow))
                    {
                        Current = fastRow;
                        return ValueTask.FromResult(true);
                    }
                }
                catch (CsvException ex)
                {
                    return ValueTask.FromException<bool>(ex);
                }
            }
            else
            {
                sr = new CsvPipeSequenceReader(reader, options);
                sequenceReader = sr;
            }

            ValueTask<bool> moveNextTask = sr.MoveNextAsync(cancellationToken);
            if (moveNextTask.IsCompletedSuccessfully)
            {
                if (!moveNextTask.Result)
                {
                    completed = true;
                    return ValueTask.FromResult(false);
                }

                Current = sr.CreateBorrowedCurrentRow(ref rentedRowBuffer);
                return ValueTask.FromResult(true);
            }

            return MoveNextSlowAsync(moveNextTask);
        }

        private async ValueTask<bool> MoveNextSlowAsync(ValueTask<bool> pendingMoveNext)
        {
            if (!await pendingMoveNext.ConfigureAwait(false))
            {
                completed = true;
                return false;
            }

            cancellationToken.ThrowIfCancellationRequested();
            Current = sequenceReader!.CreateBorrowedCurrentRow(ref rentedRowBuffer);
            return true;
        }

        public ValueTask DisposeAsync()
        {
            Current = default;
            if (rentedRowBuffer is { } bufferToReturn)
            {
                rentedRowBuffer = null;
                ArrayPool<byte>.Shared.Return(bufferToReturn, clearArray: false);
            }

            if (sequenceReader is { } readerToDispose)
            {
                sequenceReader = null;
                ValueTask disposeTask = readerToDispose.DisposeAsync();
                if (disposeTask.IsCompletedSuccessfully)
                {
                    disposeTask.GetAwaiter().GetResult();
                    linkedCancellationSource?.Dispose();
                    return default;
                }

                return DisposeSlowAsync(disposeTask);
            }

            linkedCancellationSource?.Dispose();
            return default;
        }

        private async ValueTask DisposeSlowAsync(ValueTask disposeTask)
        {
            try
            {
                await disposeTask.ConfigureAwait(false);
            }
            finally
            {
                linkedCancellationSource?.Dispose();
            }
        }
    }

}

public static partial class Csv
{
    internal static bool TryReadRow(
        ref ReadOnlySequence<byte> buffer,
        CsvReadOptions options,
        byte quote,
        byte? escape,
        bool enableQuotes,
        Span<int> columnEnds,
        bool isCompleted,
        out ReadOnlySequence<byte> rowData,
        out int columnCount,
        out int newlineCount)
    {
        const byte lf = (byte)'\n';
        const byte cr = (byte)'\r';
        const byte space = (byte)' ';
        const byte tab = (byte)'\t';

        rowData = default;
        columnCount = 0;
        newlineCount = 0;

        var reader = new SequenceReader<byte>(buffer);
        byte delimiter = (byte)options.Delimiter;
        bool inQuotes = false;
        bool skipNext = false;
        bool pendingCrInQuotes = false;
        bool isCommentLine = false;
        byte? commentCharacter = options.CommentCharacter is { } comment ? (byte)comment : null;
        bool commentCandidate = commentCharacter.HasValue;
        int currentStart = 0;

        columnEnds[0] = -1;

        while (reader.Remaining > 0)
        {
            if (!reader.TryRead(out byte current))
                break;

            int index = checked((int)(reader.Consumed - 1));

            if (skipNext)
            {
                skipNext = false;
                continue;
            }

            if (commentCandidate)
            {
                if (current == space || current == tab)
                {
                    // Still a comment candidate, but whitespace is also part of the raw row.
                }
                else if (commentCharacter.HasValue && current == commentCharacter.Value)
                {
                    isCommentLine = true;
                    continue;
                }
                else
                {
                    commentCandidate = false;
                }
            }

            if (escape.HasValue && current == escape.Value)
            {
                if (reader.Remaining == 0 && !isCompleted)
                {
                    return false;
                }
                if (reader.Remaining > 0)
                {
                    skipNext = true;
                    continue;
                }
            }

            if (isCommentLine)
            {
                if (current == cr)
                {
                    if (reader.Remaining == 0 && !isCompleted)
                    {
                        return false;
                    }
                    long consumed = reader.Consumed;
                    bool hasLf = reader.TryPeek(out byte next) && next == lf;
                    if (hasLf)
                    {
                        reader.Advance(1);
                        consumed++;
                    }

                    rowData = buffer.Slice(0, consumed - (hasLf ? 2 : 1));
                    buffer = buffer.Slice(consumed);
                    columnCount = 0;
                    newlineCount = 1;
                    return true;
                }

                if (current == lf)
                {
                    long consumed = reader.Consumed;
                    rowData = buffer.Slice(0, consumed - 1);
                    buffer = buffer.Slice(consumed);
                    columnCount = 0;
                    newlineCount = 1;
                    return true;
                }

                continue;
            }

            if (enableQuotes && current == quote)
            {
                if (inQuotes)
                {
                    if (reader.Remaining == 0 && !isCompleted)
                    {
                        return false;
                    }
                    if (reader.TryPeek(out byte next) && next == quote)
                    {
                        reader.Advance(1);
                        continue;
                    }
                }

                inQuotes = !inQuotes;
                pendingCrInQuotes = false;
                continue;
            }

            if (enableQuotes && inQuotes && !options.AllowNewlinesInsideQuotes &&
                (current == cr || current == lf))
            {
                throw new CsvException(
                    CsvErrorCode.ParseError,
                    "Newlines inside quoted fields are disabled. Enable AllowNewlinesInsideQuotes to parse them.");
            }

            if (enableQuotes && inQuotes)
            {
                if (pendingCrInQuotes)
                {
                    pendingCrInQuotes = false;
                    if (current == lf)
                    {
                        continue;
                    }
                }

                if (current == cr)
                {
                    newlineCount++;
                    pendingCrInQuotes = true;
                }
                else if (current == lf)
                {
                    newlineCount++;
                }

                continue;
            }

            if (current == delimiter)
            {
                AppendPipeColumn(index, ref columnCount, ref currentStart, columnEnds, options.MaxColumnCount, options.MaxFieldSize);
                continue;
            }

            if (current == cr)
            {
                if (reader.Remaining == 0 && !isCompleted)
                {
                    return false;
                }
                long consumed = reader.Consumed;
                bool hasLf = reader.TryPeek(out byte next) && next == lf;
                if (hasLf)
                {
                    reader.Advance(1);
                    consumed++;
                }

                int rowLength = index;
                rowData = buffer.Slice(0, rowLength);
                buffer = buffer.Slice(consumed);
                if (rowLength == 0)
                {
                    columnCount = 0;
                    newlineCount++;
                    return true;
                }

                AppendFinalPipeColumn(rowLength, ref columnCount, ref currentStart, columnEnds, options.MaxColumnCount, options.MaxFieldSize);
                newlineCount++;
                return true;
            }

            if (current == lf)
            {
                long consumed = reader.Consumed;
                int rowLength = index;
                rowData = buffer.Slice(0, rowLength);
                buffer = buffer.Slice(consumed);
                if (rowLength == 0)
                {
                    columnCount = 0;
                    newlineCount++;
                    return true;
                }

                AppendFinalPipeColumn(rowLength, ref columnCount, ref currentStart, columnEnds, options.MaxColumnCount, options.MaxFieldSize);
                newlineCount++;
                return true;
            }
        }

        return false;
    }

    internal static bool TryReadRow(
        ref ReadOnlySequence<byte> buffer,
        byte quote,
        byte? escape,
        bool enableQuotes,
        byte? commentCharacter,
        bool isCompleted,
        out ReadOnlySequence<byte> rowData)
    {
        var reader = new SequenceReader<byte>(buffer);
        bool inQuotes = false;
        bool skipNext = false;
        bool commentCandidate = commentCharacter.HasValue;
        bool isCommentLine = false;

        while (reader.Remaining > 0)
        {
            if (!reader.TryRead(out byte current))
                break;

            // A comment line ends at its first physical newline. Its content is not CSV:
            // quotes and backslashes must never consume the following data record.
            if (commentCandidate)
            {
                if (current == commentCharacter.GetValueOrDefault())
                {
                    isCommentLine = true;
                    commentCandidate = false;
                    inQuotes = false;
                }
                else if (current != (byte)' ' && current != (byte)'\t')
                {
                    commentCandidate = false;
                }
            }

            if (isCommentLine)
            {
                if (current != (byte)'\r' && current != (byte)'\n')
                    continue;
            }
            else
            {
                if (skipNext)
                {
                    skipNext = false;
                    continue;
                }

                if (escape.HasValue && current == escape.Value)
                {
                    if (reader.Remaining == 0 && !isCompleted)
                    {
                        rowData = default;
                        return false;
                    }
                    if (reader.Remaining > 0)
                    {
                        skipNext = true;
                        continue;
                    }
                }

                if (enableQuotes && current == quote)
                {
                    if (inQuotes)
                    {
                        if (reader.Remaining == 0 && !isCompleted)
                        {
                            rowData = default;
                            return false;
                        }
                        if (reader.TryPeek(out byte next) && next == quote)
                        {
                            reader.Advance(1);
                            continue;
                        }
                    }

                    inQuotes = !inQuotes;
                    continue;
                }
            }

            if (!inQuotes && current == (byte)'\r')
            {
                if (reader.Remaining == 0 && !isCompleted)
                {
                    rowData = default;
                    return false;
                }
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
                string.Create(CultureInfo.InvariantCulture, $"Row exceeds maximum size of {maxRowSize.Value:N0} bytes. Ensure rows have proper line endings."));
        }
    }
}

/// <summary>
/// Represents a single CSV row parsed from a <see cref="PipeReader"/>.
/// </summary>
/// <remarks>
/// Provides column access as UTF-8 byte spans. Column values include quote characters
/// which can be stripped using <see cref="CsvPipeColumn.ToUnquotedString"/>.
/// Rows yielded by <see cref="Csv.ReadFromPipeReaderAsync(PipeReader, CsvReadOptions?, CancellationToken)"/>
/// borrow the active pipe/pooled buffer directly and are valid only until the next <c>MoveNextAsync</c> call or
/// enumerator disposal; call <see cref="Clone"/> to retain an independent copy beyond the current iteration.
/// </remarks>
public readonly struct CsvPipeRow
{
    private const byte FLAG_TRIM_FIELDS = 1 << 0;
    private const byte FLAG_ENABLE_QUOTES = 1 << 1;
    private const byte FLAG_HAS_ESCAPE = 1 << 2;

    private readonly ReadOnlyMemory<byte> data;
    private readonly int[] columnEnds;
    private readonly int endsStart;
    private readonly int baseOffset;
    private readonly int columnCount;
    private readonly byte flags;
    private readonly byte quote;
    private readonly byte escapeByte;

    /// <summary>
    /// Gets the 1-based row number.
    /// </summary>
    public int RowNumber { get; }

    /// <summary>
    /// Gets the 1-based physical line where this row starts when
    /// <see cref="CsvReadOptions.TrackSourceLineNumbers"/> is enabled;
    /// otherwise gets the 1-based logical row number.
    /// </summary>
    public int SourceLineNumber { get; }

    /// <summary>
    /// Gets the number of columns in this row.
    /// </summary>
    public int ColumnCount => columnCount;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal CsvPipeRow(
        ReadOnlyMemory<byte> data,
        int[] columnEnds,
        int endsStart,
        int baseOffset,
        int columnCount,
        int rowNumber,
        int sourceLineNumber,
        bool trimFields,
        bool enableQuotedFields,
        byte quote,
        byte? escape)
    {
        this.data = data;
        this.columnEnds = columnEnds;
        this.endsStart = endsStart;
        this.baseOffset = baseOffset;
        this.columnCount = columnCount;
        byte packedFlags = 0;
        if (trimFields) packedFlags |= FLAG_TRIM_FIELDS;
        if (enableQuotedFields) packedFlags |= FLAG_ENABLE_QUOTES;
        if (escape.HasValue) packedFlags |= FLAG_HAS_ESCAPE;
        flags = packedFlags;
        this.quote = quote;
        escapeByte = escape.GetValueOrDefault();
        RowNumber = rowNumber;
        SourceLineNumber = sourceLineNumber;
    }

    private CsvPipeRow(
        ReadOnlyMemory<byte> data,
        int[] columnEnds,
        int endsStart,
        int baseOffset,
        int columnCount,
        int rowNumber,
        int sourceLineNumber,
        byte flags,
        byte quote,
        byte escapeByte)
    {
        this.data = data;
        this.columnEnds = columnEnds;
        this.endsStart = endsStart;
        this.baseOffset = baseOffset;
        this.columnCount = columnCount;
        this.flags = flags;
        this.quote = quote;
        this.escapeByte = escapeByte;
        RowNumber = rowNumber;
        SourceLineNumber = sourceLineNumber;
    }

    /// <summary>
    /// Gets the column at the specified index.
    /// </summary>
    /// <param name="index">The zero-based column index.</param>
    /// <returns>The column value as a <see cref="CsvPipeColumn"/>.</returns>
    public CsvPipeColumn this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if ((uint)index >= (uint)columnCount)
                throw new IndexOutOfRangeException($"Column index {index} is out of range. Row has {columnCount} columns.");

            int offset = endsStart + index;
            int start = columnEnds[offset] + 1 - baseOffset;
            int end = columnEnds[offset + 1] - baseOffset;

            if ((flags & FLAG_TRIM_FIELDS) != 0)
            {
                (start, end) = TrimBounds(data.Span, start, end);
            }

            byte? escape = (flags & FLAG_HAS_ESCAPE) != 0 ? escapeByte : null;
            return new CsvPipeColumn(data[start..end], quote, escape, (flags & FLAG_ENABLE_QUOTES) != 0);
        }
    }

    private (int start, int end) TrimBounds(ReadOnlySpan<byte> rowSpan, int start, int end)
    {
        const byte space = (byte)' ';
        const byte tab = (byte)'\t';

        if (end - start >= 2 && rowSpan[start] == quote && rowSpan[end - 1] == quote)
        {
            return (start, end);
        }

        while (start < end && (rowSpan[start] == space || rowSpan[start] == tab))
        {
            start++;
        }

        while (end > start && (rowSpan[end - 1] == space || rowSpan[end - 1] == tab))
        {
            end--;
        }

        return (start, end);
    }

    /// <summary>
    /// Creates an independent copy of this row whose memory remains valid after the enumerator advances or is disposed.
    /// </summary>
    /// <returns>A detached <see cref="CsvPipeRow"/> owning its own backing buffer.</returns>
    public CsvPipeRow Clone()
    {
        if (columnEnds is null)
        {
            return default;
        }

        byte[] dataCopy = GC.AllocateUninitializedArray<byte>(data.Length);
        data.Span.CopyTo(dataCopy);

        int[] endsCopy = GC.AllocateUninitializedArray<int>(columnCount + 1);
        for (int i = 0; i <= columnCount; i++)
        {
            endsCopy[i] = columnEnds[endsStart + i] - baseOffset;
        }

        return new CsvPipeRow(
            dataCopy,
            endsCopy,
            endsStart: 0,
            baseOffset: 0,
            columnCount,
            RowNumber,
            SourceLineNumber,
            flags,
            quote,
            escapeByte);
    }
}

/// <summary>
/// Represents a single column value from a <see cref="CsvPipeRow"/>.
/// </summary>
public readonly struct CsvPipeColumn
{
    private readonly ReadOnlyMemory<byte> data;
    private readonly bool enableQuotedFields;
    private readonly byte quote;
    private readonly byte? escape;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal CsvPipeColumn(ReadOnlyMemory<byte> data, byte quote, byte? escape, bool enableQuotedFields = true)
    {
        this.data = data;
        this.enableQuotedFields = enableQuotedFields;
        this.quote = quote;
        this.escape = escape;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal CsvPipeColumn(byte[] data, int start, int length, byte quote, byte? escape, bool enableQuotedFields = true)
        : this(data.AsMemory(start, length), quote, escape, enableQuotedFields)
    {
    }

    /// <summary>
    /// Gets the raw UTF-8 byte span for this column.
    /// </summary>
    public ReadOnlySpan<byte> Span => data.Span;

    /// <summary>
    /// Converts the column value to a string, stripping quote characters.
    /// </summary>
    public string ToUnquotedString()
        => new CsvPipeColumnText(quote, escape, enableQuotedFields).Decode(Span);

    /// <summary>
    /// Converts the column value to decoded, unquoted text.
    /// </summary>
    public override string ToString()
    {
        return ToUnquotedString();
    }
}
