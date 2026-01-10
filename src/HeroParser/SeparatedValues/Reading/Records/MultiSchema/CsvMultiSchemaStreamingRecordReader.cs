using System.Buffers;
using System.Text;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Reading.Rows;
using HeroParser.SeparatedValues.Reading.Shared;

namespace HeroParser.SeparatedValues.Reading.Records.MultiSchema;

/// <summary>
/// Async streaming reader for multi-schema CSV files that reads from a stream without loading
/// the entire file into memory.
/// </summary>
/// <remarks>
/// <para>
/// This reader is designed for large files where loading the entire content into memory
/// is not practical. It reads data in chunks and yields records as they become available.
/// </para>
/// <para>
/// Thread-Safety: This type is not thread-safe. Each instance should be used on a single thread.
/// </para>
/// </remarks>
public sealed class CsvMultiSchemaStreamingRecordReader : IAsyncDisposable
{
    // Absolute maximum buffer size (128 MB) to prevent unbounded memory growth
    private const int ABSOLUTE_MAX_BUFFER_SIZE = 128 * 1024 * 1024;
    private const int MAX_LINE_ENDING_LENGTH = 2;

    private readonly ArrayPool<char> charPool;
    private readonly StreamReader reader;
    private readonly CsvParserOptions parserOptions;
    private readonly CsvMultiSchemaBinder<char> binder;
    private readonly int skipRows;
    private readonly IProgress<CsvProgress>? progress;
    private readonly int progressInterval;
    private readonly bool trackLineNumbers;
    private readonly int maxRowSize;
    private readonly int maxBufferSize;
    private readonly PooledColumnEnds columnEndsBuffer;

    private char[] buffer;
    private int offset;
    private int length;
    private int rowNumber;
    private int skippedCount;
    private int dataRowCount;
    private int sourceLineNumber;
    private bool endOfStream;
    private bool disposed;

    /// <summary>
    /// Gets the current record. Valid after <see cref="MoveNextAsync"/> returns <see langword="true"/>.
    /// </summary>
    public object Current { get; private set; } = null!;

    /// <summary>
    /// Gets the approximate number of bytes read from the underlying stream.
    /// </summary>
    public long BytesRead { get; private set; }

    internal CsvMultiSchemaStreamingRecordReader(
        Stream stream,
        CsvParserOptions csvParserOptions,
        CsvMultiSchemaBinder<char> schemaBinder,
        Encoding encoding,
        bool leaveOpen,
        int skipRowCount,
        IProgress<CsvProgress>? progressReporter,
        int progressIntervalRows)
    {
        parserOptions = csvParserOptions;
        binder = schemaBinder;
        skipRows = skipRowCount;
        progress = progressReporter;
        progressInterval = progressIntervalRows > 0 ? progressIntervalRows : 1000;
        trackLineNumbers = parserOptions.TrackSourceLineNumbers;
        maxRowSize = parserOptions.MaxRowSize ?? ABSOLUTE_MAX_BUFFER_SIZE;
        maxBufferSize = CalculateMaxBufferSize(maxRowSize);

        charPool = ArrayPool<char>.Shared;
        reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: leaveOpen);
        buffer = RentBuffer(Math.Max(4096, parserOptions.MaxRowSize ?? 4096));
        columnEndsBuffer = new PooledColumnEnds(parserOptions.MaxColumnCount + 1);
        offset = 0;
        length = 0;
        rowNumber = 0;
        skippedCount = 0;
        dataRowCount = 0;
        sourceLineNumber = 1;
        endOfStream = false;
        disposed = false;
    }

    /// <summary>
    /// Advances to the next record asynchronously.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> if a record was successfully read; otherwise, <see langword="false"/>.</returns>
    public async ValueTask<bool> MoveNextAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        while (true)
        {
            if (!endOfStream && offset >= length)
            {
                await FillBufferAsync(cancellationToken).ConfigureAwait(false);
            }

            var span = buffer.AsSpan(offset, length - offset);
            if (span.IsEmpty && endOfStream)
            {
                ReportFinalProgress();
                return false;
            }

            int rowStartOffset = offset;
            int rowStartLine = trackLineNumbers ? sourceLineNumber : 0;

            CsvRowParseResult result;
            try
            {
                result = trackLineNumbers
                    ? CsvRowParser.ParseRow<char, TrackLineNumbers>(span, parserOptions, columnEndsBuffer.Span)
                    : CsvRowParser.ParseRow<char, NoTrackLineNumbers>(span, parserOptions, columnEndsBuffer.Span);
            }
            catch (CsvException ex) when (!endOfStream && ex.QuoteStartPosition.HasValue)
            {
                await FillBufferAsync(cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (result.CharsConsumed == 0)
            {
                ReportFinalProgress();
                return false;
            }

            if (result.RowLength > maxRowSize)
            {
                throw new CsvException(
                    CsvErrorCode.ParseError,
                    $"Row exceeds maximum size of {maxRowSize:N0} characters. Ensure rows have proper line endings.");
            }

            if (result.RowLength == span.Length && !endOfStream)
            {
                await FillBufferAsync(cancellationToken).ConfigureAwait(false);
                continue;
            }

            offset = rowStartOffset + result.CharsConsumed;
            if (trackLineNumbers)
                sourceLineNumber += result.NewlineCount;

            if (result.RowLength == 0)
                continue;

            rowNumber++;

            if (skippedCount < skipRows)
            {
                skippedCount++;
                continue;
            }

            var row = new CsvRow<char>(
                buffer.AsSpan(rowStartOffset, result.RowLength),
                columnEndsBuffer.Buffer,
                result.ColumnCount,
                rowNumber,
                trackLineNumbers ? rowStartLine : rowNumber,
                parserOptions.TrimFields);

            if (binder.NeedsHeaderResolution)
            {
                binder.BindHeader(row, rowNumber);
                continue;
            }

            if (dataRowCount >= parserOptions.MaxRowCount)
            {
                throw new CsvException(
                    CsvErrorCode.TooManyRows,
                    $"Maximum row count of {parserOptions.MaxRowCount} exceeded.",
                    rowNumber, 0);
            }

            var bound = binder.Bind(row, rowNumber);
            if (bound is null)
            {
                continue;
            }

            dataRowCount++;
            ReportProgress();
            Current = bound;
            return true;
        }
    }

    private async ValueTask FillBufferAsync(CancellationToken cancellationToken)
    {
        // Compact buffer if needed
        if (offset > 0)
        {
            var remaining = buffer.AsSpan(offset, length - offset);
            remaining.CopyTo(buffer);
            length = remaining.Length;
            offset = 0;
        }

        // Grow buffer if full
        if (length == buffer.Length)
        {
            if (buffer.Length >= maxBufferSize)
            {
                throw new CsvException(
                    CsvErrorCode.ParseError,
                    $"Row exceeds maximum size of {maxRowSize:N0} characters. " +
                    "Ensure rows have proper line endings.");
            }

            var newBuffer = RentBuffer(Math.Min(buffer.Length * 2, maxBufferSize));
            buffer.AsSpan(0, length).CopyTo(newBuffer);
            ReturnBuffer(buffer);
            buffer = newBuffer;
        }

        // Read more data
        int start = length;
        var read = await reader.ReadAsync(buffer.AsMemory(length, buffer.Length - length), cancellationToken).ConfigureAwait(false);
        if (read == 0)
        {
            endOfStream = true;
            return;
        }

        BytesRead += reader.CurrentEncoding.GetByteCount(buffer.AsSpan(start, read));
        length += read;
    }

    private void ReportProgress()
    {
        if (progress is not null && dataRowCount % progressInterval == 0)
        {
            progress.Report(new CsvProgress
            {
                RowsProcessed = dataRowCount,
                BytesProcessed = BytesRead,
                TotalBytes = -1
            });
        }
    }

    private void ReportFinalProgress()
    {
        if (progress is not null && dataRowCount > 0)
        {
            progress.Report(new CsvProgress
            {
                RowsProcessed = dataRowCount,
                BytesProcessed = BytesRead,
                TotalBytes = -1
            });
        }
    }

    private char[] RentBuffer(int minimumLength)
    {
        int bufferSize = minimumLength;
        if (bufferSize > maxBufferSize)
            bufferSize = maxBufferSize;
        var rented = charPool.Rent(bufferSize);
        Array.Clear(rented);
        return rented;
    }

    private void ReturnBuffer(char[] toReturn)
    {
        charPool.Return(toReturn, clearArray: true);
    }

    /// <summary>
    /// Asynchronously releases resources used by the reader.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        if (disposed)
            return ValueTask.CompletedTask;

        disposed = true;
        columnEndsBuffer.Return();
        ReturnBuffer(buffer);
        buffer = null!;
        reader.Dispose();
        return ValueTask.CompletedTask;
    }

    private static int CalculateMaxBufferSize(int maxRowSize)
    {
        if (maxRowSize >= int.MaxValue - MAX_LINE_ENDING_LENGTH)
            return int.MaxValue;
        return maxRowSize + MAX_LINE_ENDING_LENGTH;
    }
}
