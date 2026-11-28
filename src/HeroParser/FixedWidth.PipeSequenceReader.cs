using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
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
    private long remainingRowsToSkip;
    private int recordCount;
    private int sourceLineNumber;
    private bool previousRecordEndedWithCr;
    private long totalBytesRead;
    private long previousUnreadBytes;
    private int currentRecordNumber;
    private int currentSourceLineNumber;

    internal FixedWidthPipeSequenceReader(PipeReader reader, FixedWidthReadOptions options)
    {
        this.reader = reader;
        this.options = options;
        trackSourceLineNumbers = options.TrackSourceLineNumbers;
        remainingRowsToSkip = (long)options.SkipRows + (options.HasHeaderRow ? 1 : 0);
        sourceLineNumber = trackSourceLineNumbers ? 1 : 0;
        previousRecordEndedWithCr = false;
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ValueTask<bool> MoveNextAsync(CancellationToken cancellationToken = default)
    {
        if (disposed)
        {
            return ValueTask.FromException<bool>(new ObjectDisposedException(nameof(FixedWidthPipeSequenceReader)));
        }

        // Current is invalid after any MoveNextAsync attempt, even one canceled
        // before a new PipeReader read. Buffered bytes remain available to retry.
        hasCurrent = false;
        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromCanceled<bool>(cancellationToken);
        }

        if (hasBufferedRead)
        {
            try
            {
                if (TryAdvanceBuffered(cancellationToken, out bool hasMore))
                {
                    return ValueTask.FromResult(hasMore);
                }
            }
            catch (Exception ex)
            {
                return ValueTask.FromException<bool>(ex);
            }
        }

        return MoveNextSlowAsync(cancellationToken);
    }

    private async ValueTask<bool> MoveNextSlowAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            if (!hasBufferedRead)
            {
                var result = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                if (result.IsCanceled)
                {
                    // CancelPendingRead can cancel a read without canceling the caller's token.
                    // Keep any buffered data unconsumed and unexamined so a retry can resume.
                    reader.AdvanceTo(result.Buffer.Start, result.Buffer.Start);
                    throw new OperationCanceledException("The underlying PipeReader read was canceled.", cancellationToken);
                }

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

            if (TryAdvanceBuffered(cancellationToken, out bool hasMore))
            {
                return hasMore;
            }
        }
    }

    private bool TryAdvanceBuffered(CancellationToken cancellationToken, out bool hasMore)
    {
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
                        sourceLineNumber += new FixedWidthLineScanner().CountNewlines(rowData, ref previousRecordEndedWithCr);
                    }

                    remainingRowsToSkip--;
                    continue;
                }

                recordCount++;
                FixedWidth.EnsureRecordCount(recordCount, options.MaxRecordCount);

                int rowStartLine = trackSourceLineNumbers ? sourceLineNumber : 0;
                if (trackSourceLineNumbers)
                {
                    sourceLineNumber += new FixedWidthLineScanner().CountNewlines(rowData, ref previousRecordEndedWithCr);
                }

                currentRowData = rowData;
                currentRecordNumber = recordCount;
                currentSourceLineNumber = rowStartLine;
                hasCurrent = true;
                bufferedData = unreadBuffer;
                hasMore = true;
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
                    new FixedWidthLineScanner().StartsWithComment(rowData, commentChar))
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

                hasMore = true;
                return true;
            }
        }

        if (!bufferedIsCompleted)
        {
            ReleaseBufferedRead(unreadBuffer.Start, unreadBuffer.Length);
            hasMore = false;
            return false;
        }

        ReleaseBufferedRead(unreadBuffer.Start, unreadBuffer.Length);
        hasMore = false;
        return true;
    }

    internal FixedWidthPipeRow CreateBorrowedCurrentRow(ref byte[]? rentedBuffer)
    {
        if (currentRowData.IsEmpty)
        {
            return new FixedWidthPipeRow(ReadOnlyMemory<byte>.Empty, currentRecordNumber, currentSourceLineNumber, options);
        }

        if (currentRowData.IsSingleSegment)
        {
            return new FixedWidthPipeRow(currentRowData.First, currentRecordNumber, currentSourceLineNumber, options);
        }

        int rowLength = checked((int)currentRowData.Length);
        byte[] buffer = rentedBuffer is { Length: var currentLen } && currentLen >= rowLength
            ? rentedBuffer
            : GrowRentedBuffer(ref rentedBuffer, rowLength);
        currentRowData.CopyTo(buffer.AsSpan(0, rowLength));
        return new FixedWidthPipeRow(buffer.AsMemory(0, rowLength), currentRecordNumber, currentSourceLineNumber, options);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static byte[] GrowRentedBuffer(ref byte[]? rentedBuffer, int requiredLength)
    {
        byte[] replacement = ArrayPool<byte>.Shared.Rent(Math.Max(requiredLength, 256));
        if (rentedBuffer is { } previous)
        {
            ArrayPool<byte>.Shared.Return(previous, clearArray: false);
        }

        rentedBuffer = replacement;
        return replacement;
    }

    public ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return default;
        }

        disposed = true;
        if (hasBufferedRead)
        {
            ReleaseBufferedRead(bufferedData.Start, bufferedData.Length);
        }

        return reader.CompleteAsync();
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
