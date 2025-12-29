using System.Buffers;
using System.Text;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Reading.Rows;

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

    private readonly ArrayPool<char> charPool;
    private readonly StreamReader reader;
    private readonly CsvParserOptions parserOptions;
    private readonly CsvMultiSchemaBinder<char> binder;
    private readonly int skipRows;
    private readonly IProgress<CsvProgress>? progress;
    private readonly int progressInterval;

    private char[] buffer;
    private int offset;
    private int length;
    private int rowNumber;
    private int skippedCount;
    private int dataRowCount;
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

        charPool = ArrayPool<char>.Shared;
        reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: leaveOpen);
        buffer = RentBuffer(Math.Max(4096, parserOptions.MaxRowSize ?? 4096));
        offset = 0;
        length = 0;
        rowNumber = 0;
        skippedCount = 0;
        dataRowCount = 0;
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
            // Ensure we have data in the buffer
            if (!endOfStream && offset >= length)
            {
                await FillBufferAsync(cancellationToken).ConfigureAwait(false);
            }

            // Try to find a complete line
            var span = buffer.AsSpan(offset, length - offset);
            var lineEnd = FindLineEnd(span);

            if (lineEnd == -1)
            {
                if (endOfStream)
                {
                    // Process remaining data as last line
                    if (span.Length > 0)
                    {
                        var result = ProcessLine(span);
                        offset = length;
                        if (result is not null)
                        {
                            Current = result;
                            return true;
                        }
                    }
                    ReportFinalProgress();
                    return false;
                }

                // Need more data
                await FillBufferAsync(cancellationToken).ConfigureAwait(false);
                continue;
            }

            // Handle quoted fields that may contain newlines
            if (parserOptions.AllowNewlinesInsideQuotes)
            {
                var quoteResult = HandleQuotedNewlines(span, lineEnd);
                if (quoteResult == -1)
                {
                    // Need more data
                    if (endOfStream)
                    {
                        throw new CsvException(
                            CsvErrorCode.ParseError,
                            "Unterminated quoted field at end of file.",
                            rowNumber + 1, 0);
                    }
                    await FillBufferAsync(cancellationToken).ConfigureAwait(false);
                    continue;
                }
                lineEnd = quoteResult;
            }

            // Calculate consumed bytes (including line ending)
            var line = span[..lineEnd];
            int consumed = lineEnd + 1;
            if (lineEnd < span.Length - 1 && span[lineEnd] == '\r' && span[lineEnd + 1] == '\n')
            {
                consumed++;
            }

            offset += consumed;

            var processResult = ProcessLine(line);
            if (processResult is not null)
            {
                Current = processResult;
                return true;
            }
            // Continue to next line (this row was skipped or was header)
        }
    }

    private object? ProcessLine(ReadOnlySpan<char> line)
    {
        rowNumber++;

        // Skip initial rows if requested
        if (skippedCount < skipRows)
        {
            skippedCount++;
            return null;
        }

        // Skip empty lines if comment character is set (common pattern)
        if (line.IsEmpty && parserOptions.CommentCharacter.HasValue)
        {
            return null;
        }

        // Skip comment lines
        if (parserOptions.CommentCharacter.HasValue && line.Length > 0 && line[0] == parserOptions.CommentCharacter.Value)
        {
            return null;
        }

        // Parse the line into a row
        var row = ParseRow(line);

        // Handle header resolution
        if (binder.NeedsHeaderResolution)
        {
            binder.BindHeader(row, rowNumber);
            return null;
        }

        // Check row limit
        if (dataRowCount >= parserOptions.MaxRowCount)
        {
            throw new CsvException(
                CsvErrorCode.TooManyRows,
                $"Maximum row count of {parserOptions.MaxRowCount} exceeded.",
                rowNumber, 0);
        }

        // Bind the row
        var result = binder.Bind(row, rowNumber);
        if (result is null)
        {
            return null;
        }

        dataRowCount++;
        ReportProgress();
        return result;
    }

    private CsvRow<char> ParseRow(ReadOnlySpan<char> line)
    {
        // Use a simple inline parser for streaming
        // This is a simplified version - for production, should use the full parser
        var columnEnds = ArrayPool<int>.Shared.Rent(parserOptions.MaxColumnCount + 1);
        try
        {
            int columnCount = 0;
            columnEnds[0] = -1;

            bool inQuotes = false;
            char delimiter = parserOptions.Delimiter;
            char quote = parserOptions.Quote;
            bool enableQuotes = parserOptions.EnableQuotedFields;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (enableQuotes && c == quote)
                {
                    inQuotes = !inQuotes;
                }
                else if (!inQuotes && c == delimiter)
                {
                    columnCount++;
                    if (columnCount >= parserOptions.MaxColumnCount)
                    {
                        throw new CsvException(
                            CsvErrorCode.TooManyColumns,
                            $"Row has more than {parserOptions.MaxColumnCount} columns.",
                            rowNumber, columnCount);
                    }
                    columnEnds[columnCount] = i;
                }
            }

            // Final column
            columnCount++;
            columnEnds[columnCount] = line.Length;

            return new CsvRow<char>(
                line,
                columnEnds.AsSpan(0, columnCount + 1),
                columnCount,
                rowNumber,
                rowNumber,
                parserOptions.TrimFields);
        }
        finally
        {
            ArrayPool<int>.Shared.Return(columnEnds);
        }
    }

    private int HandleQuotedNewlines(ReadOnlySpan<char> span, int initialLineEnd)
    {
        // Count quotes before the line end
        int quoteCount = 0;
        char quote = parserOptions.Quote;

        for (int i = 0; i < initialLineEnd; i++)
        {
            if (span[i] == quote)
            {
                quoteCount++;
            }
        }

        // If odd number of quotes, we're inside a quoted field
        if (quoteCount % 2 == 1)
        {
            // Find the actual end of the line (after the closing quote)
            int searchStart = initialLineEnd + 1;
            while (searchStart < span.Length)
            {
                int nextLineEnd = FindLineEnd(span[searchStart..]);
                if (nextLineEnd == -1)
                {
                    return -1; // Need more data
                }

                nextLineEnd += searchStart;

                // Count quotes in this segment
                for (int i = searchStart; i < nextLineEnd; i++)
                {
                    if (span[i] == quote)
                    {
                        quoteCount++;
                    }
                }

                if (quoteCount % 2 == 0)
                {
                    return nextLineEnd;
                }

                searchStart = nextLineEnd + 1;
            }

            return -1; // Need more data
        }

        return initialLineEnd;
    }

    private static int FindLineEnd(ReadOnlySpan<char> span)
    {
        return span.IndexOfAny('\r', '\n');
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
            if (buffer.Length >= ABSOLUTE_MAX_BUFFER_SIZE)
            {
                throw new CsvException(
                    CsvErrorCode.ParseError,
                    $"Row exceeds maximum size of {ABSOLUTE_MAX_BUFFER_SIZE:N0} characters. " +
                    "Ensure rows have proper line endings.");
            }

            var newBuffer = RentBuffer(buffer.Length * 2);
            buffer.AsSpan(0, length).CopyTo(newBuffer);
            ReturnBuffer(buffer);
            buffer = newBuffer;
        }

        // Read more data
        var read = await reader.ReadAsync(buffer.AsMemory(length, buffer.Length - length), cancellationToken).ConfigureAwait(false);
        if (read == 0)
        {
            endOfStream = true;
            return;
        }

        length += read;
        BytesRead += read;
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
        var rented = charPool.Rent(minimumLength);
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
        ReturnBuffer(buffer);
        buffer = null!;
        reader.Dispose();
        return ValueTask.CompletedTask;
    }
}
