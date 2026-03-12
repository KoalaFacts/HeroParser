using System.Buffers;
using System.IO.Pipelines;
using HeroParser.FixedWidths;

namespace HeroParser;

internal sealed class FixedWidthPipeSequenceReader : IAsyncDisposable
{
    private readonly PipeReader reader;
    private readonly FixedWidthReadOptions options;
    private readonly bool trackSourceLineNumbers;

    private bool disposed;
    private bool bomProcessed;
    private bool hasCurrent;
    private bool hasBufferedRead;
    private bool bufferedIsCompleted;
    private SequencePosition bufferedExamined;
    private ReadOnlySequence<byte> bufferedData;
    private ReadOnlySequence<byte> currentRowData;
    private int remainingRowsToSkip;
    private int recordCount;
    private int sourceLineNumber;
    private long totalBytesRead;
    private long previousUnreadBytes;
    private int currentRecordNumber;
    private int currentSourceLineNumber;

    internal FixedWidthPipeSequenceReader(PipeReader reader, FixedWidthReadOptions options)
    {
        this.reader = reader;
        this.options = options;
        trackSourceLineNumbers = options.TrackSourceLineNumbers;
        remainingRowsToSkip = options.SkipRows + (options.HasHeaderRow ? 1 : 0);
        sourceLineNumber = trackSourceLineNumbers ? 1 : 0;
    }

    internal FixedWidthPipeSequenceRow Current
    {
        get
        {
            ThrowIfDisposed();
            if (!hasCurrent)
            {
                throw new InvalidOperationException("No current row is available. Call MoveNextAsync() first.");
            }

            return new FixedWidthPipeSequenceRow(
                currentRowData,
                currentRecordNumber,
                currentSourceLineNumber,
                options);
        }
    }

    internal async ValueTask<bool> MoveNextAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        hasCurrent = false;

        while (true)
        {
            if (!hasBufferedRead)
            {
                var result = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                var originalBuffer = result.Buffer;
                var newBytes = originalBuffer.Length - previousUnreadBytes;
                if (newBytes > 0)
                {
                    totalBytesRead += newBytes;
                    options.ValidateInputSize(totalBytesRead);
                }

                var buffer = originalBuffer;
                if (!FixedWidth.TryProcessUtf8Bom(ref buffer, result.IsCompleted, ref bomProcessed))
                {
                    reader.AdvanceTo(buffer.Start, buffer.End);
                    previousUnreadBytes = buffer.Length;
                    continue;
                }

                bufferedData = buffer;
                bufferedExamined = buffer.End;
                bufferedIsCompleted = result.IsCompleted;
                hasBufferedRead = true;
            }

            var unreadBuffer = bufferedData;
            if (options.RecordLength is { } recordLength)
            {
                while (FixedWidth.TryReadPipeFixedLengthRecord(ref unreadBuffer, recordLength, out var rowData))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (remainingRowsToSkip > 0)
                    {
                        if (trackSourceLineNumbers)
                        {
                            sourceLineNumber += FixedWidthLineScanner.CountNewlines(rowData);
                        }

                        remainingRowsToSkip--;
                        continue;
                    }

                    recordCount++;
                    FixedWidth.EnsureRecordCount(recordCount, options.MaxRecordCount);

                    int rowStartLine = trackSourceLineNumbers ? sourceLineNumber : 0;
                    if (trackSourceLineNumbers)
                    {
                        sourceLineNumber += FixedWidthLineScanner.CountNewlines(rowData);
                    }

                    currentRowData = rowData;
                    currentRecordNumber = recordCount;
                    currentSourceLineNumber = rowStartLine;
                    hasCurrent = true;
                    bufferedData = unreadBuffer;
                    return true;
                }

                if (bufferedIsCompleted && unreadBuffer.Length > 0 && remainingRowsToSkip <= 0)
                {
                    FixedWidth.ThrowInvalidPipeRecordLength(
                        recordLength,
                        unreadBuffer.Length,
                        recordCount + 1,
                        sourceLineNumber,
                        trackSourceLineNumbers);
                }
            }
            else
            {
                while (FixedWidth.TryReadPipeLineRecord(ref unreadBuffer, bufferedIsCompleted, out var rowData))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int rowStartLine = trackSourceLineNumbers ? sourceLineNumber : 0;

                    if (remainingRowsToSkip > 0)
                    {
                        remainingRowsToSkip--;
                        if (trackSourceLineNumbers)
                        {
                            sourceLineNumber++;
                        }

                        continue;
                    }

                    if (rowData.Length == 0 && options.SkipEmptyLines)
                    {
                        if (trackSourceLineNumbers)
                        {
                            sourceLineNumber++;
                        }

                        continue;
                    }

                    if (options.CommentCharacter is { } commentChar &&
                        FixedWidth.TryGetFirstByte(rowData, out byte firstByte) &&
                        firstByte == (byte)commentChar)
                    {
                        if (trackSourceLineNumbers)
                        {
                            sourceLineNumber++;
                        }

                        continue;
                    }

                    recordCount++;
                    FixedWidth.EnsureRecordCount(recordCount, options.MaxRecordCount);
                    currentRowData = rowData;
                    currentRecordNumber = recordCount;
                    currentSourceLineNumber = rowStartLine;
                    hasCurrent = true;
                    bufferedData = unreadBuffer;

                    if (trackSourceLineNumbers)
                    {
                        sourceLineNumber++;
                    }

                    return true;
                }
            }

            if (!bufferedIsCompleted)
            {
                ReleaseBufferedRead(unreadBuffer.Start, unreadBuffer.Length);
                continue;
            }

            ReleaseBufferedRead(unreadBuffer.Start, unreadBuffer.Length);
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        if (hasBufferedRead)
        {
            ReleaseBufferedRead(bufferedData.Start, bufferedData.Length);
        }

        await reader.CompleteAsync().ConfigureAwait(false);
    }

    private void ReleaseBufferedRead(SequencePosition consumed, long unreadBytes)
    {
        if (!hasBufferedRead)
        {
            return;
        }

        reader.AdvanceTo(consumed, bufferedExamined);
        previousUnreadBytes = unreadBytes;
        hasBufferedRead = false;
        bufferedIsCompleted = false;
        bufferedData = default;
        bufferedExamined = default;
    }

    private void ThrowIfDisposed()
    {
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(FixedWidthPipeSequenceReader));
        }
    }
}

internal readonly ref struct FixedWidthPipeSequenceRow
{
    private readonly ReadOnlySequence<byte> data;
    private readonly FixedWidthReadOptions options;

    internal FixedWidthPipeSequenceRow(
        ReadOnlySequence<byte> data,
        int recordNumber,
        int sourceLineNumber,
        FixedWidthReadOptions options)
    {
        this.data = data;
        this.options = options;
        RecordNumber = recordNumber;
        SourceLineNumber = sourceLineNumber;
    }

    internal int RecordNumber { get; }

    internal int SourceLineNumber { get; }

    internal int Length => checked((int)data.Length);

    internal bool TryGetContiguousRow(out FixedWidthByteSpanRow row)
    {
        if (data.IsSingleSegment)
        {
            row = new FixedWidthByteSpanRow(data.FirstSpan, RecordNumber, SourceLineNumber, options);
            return true;
        }

        row = default;
        return false;
    }

    internal FixedWidthByteSpanRow CreateContiguousRow(ReadOnlySpan<byte> buffer)
        => new(buffer, RecordNumber, SourceLineNumber, options);

    internal void CopyTo(Span<byte> destination) => data.CopyTo(destination);

    internal FixedWidthPipeRow ToOwnedRow()
        => FixedWidth.CreatePipeRow(data, RecordNumber, SourceLineNumber, options);
}
