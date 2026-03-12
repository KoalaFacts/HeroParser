using System.Buffers;
using System.Buffers.Binary;
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

        public async ValueTask<bool> MoveNextAsync()
        {
            if (completed)
            {
                return false;
            }

            cancellationToken.ThrowIfCancellationRequested();
            sequenceReader ??= new CsvPipeSequenceReader(reader, options);

            if (!await sequenceReader.MoveNextAsync(cancellationToken).ConfigureAwait(false))
            {
                completed = true;
                return false;
            }

            cancellationToken.ThrowIfCancellationRequested();
            Current = sequenceReader.Current.ToOwnedRow();
            return true;
        }

        public async ValueTask DisposeAsync()
        {
            if (sequenceReader is not null)
            {
                await sequenceReader.DisposeAsync().ConfigureAwait(false);
                sequenceReader = null;
            }

            linkedCancellationSource?.Dispose();
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

            if (escape.HasValue && current == escape.Value && reader.Remaining > 0)
            {
                skipNext = true;
                continue;
            }

            if (isCommentLine)
            {
                if (current == cr)
                {
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
                if (inQuotes && reader.TryPeek(out byte next) && next == quote)
                {
                    reader.Advance(1);
                    continue;
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
    private const int BYTE_PACKED_ROW_LENGTH_MAX = byte.MaxValue - 1;
    private const int UINT16_PACKED_ROW_LENGTH_MAX = ushort.MaxValue - 1;

    private readonly byte[] storage;
    private readonly int dataOffset;
    private readonly byte headerElementSize;
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
        byte[] storage,
        int columnCount,
        int rowNumber,
        bool trimFields,
        byte quote,
        byte? escape,
        int dataOffset,
        int headerElementSize)
    {
        this.storage = storage;
        this.columnCount = columnCount;
        this.trimFields = trimFields;
        this.quote = quote;
        this.escape = escape;
        this.dataOffset = dataOffset;
        this.headerElementSize = checked((byte)headerElementSize);
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

            int start = ReadColumnEnd(index) + 1;
            int end = ReadColumnEnd(index + 1);

            if (trimFields)
            {
                (start, end) = TrimBounds(start, end);
            }

            return new CsvPipeColumn(storage, dataOffset + start, end - start, quote, escape);
        }
    }

    private (int start, int end) TrimBounds(int start, int end)
    {
        const byte space = (byte)' ';
        const byte tab = (byte)'\t';
        int absoluteStart = dataOffset + start;
        int absoluteEnd = dataOffset + end;

        if (end - start >= 2 && storage[absoluteStart] == quote && storage[absoluteEnd - 1] == quote)
        {
            return (start, end);
        }

        while (absoluteStart < absoluteEnd && (storage[absoluteStart] == space || storage[absoluteStart] == tab))
        {
            absoluteStart++;
        }

        while (absoluteEnd > absoluteStart && (storage[absoluteEnd - 1] == space || storage[absoluteEnd - 1] == tab))
        {
            absoluteEnd--;
        }

        return (absoluteStart - dataOffset, absoluteEnd - dataOffset);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int ReadColumnEnd(int index)
    {
        int offset = index * headerElementSize;
        return headerElementSize switch
        {
            1 => storage[offset] - 1,
            2 => BinaryPrimitives.ReadUInt16LittleEndian(storage.AsSpan(offset, sizeof(ushort))) - 1,
            _ => BinaryPrimitives.ReadInt32LittleEndian(storage.AsSpan(offset, sizeof(int))) - 1
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int GetHeaderElementSize(int rowLength)
    {
        if (rowLength <= BYTE_PACKED_ROW_LENGTH_MAX)
        {
            return 1;
        }

        if (rowLength <= UINT16_PACKED_ROW_LENGTH_MAX)
        {
            return 2;
        }

        return sizeof(int);
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
