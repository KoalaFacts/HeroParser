using System.Buffers;
using System.Globalization;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;

namespace HeroParser.JsonLines.Reading;

/// <summary>
/// Zero-copy async line splitter over a <see cref="PipeReader"/>. Yields <see cref="JsonlLine"/> values
/// borrowing the active pipe segment (or a single reusable pooled buffer for lines spanning multiple segments),
/// strips a leading UTF-8 BOM from the first line, trims trailing <c>\r</c>, and enforces
/// <see cref="JsonlReadOptions.MaxLineSizeBytes"/>.
/// </summary>
internal readonly struct JsonlPipeLineReader(PipeReader reader, JsonlReadOptions options)
{
    private const byte LF = (byte)'\n';
    private const byte CR = (byte)'\r';

    public IAsyncEnumerable<JsonlLine> ReadLinesAsync(CancellationToken cancellationToken = default)
        => new LineAsyncEnumerable(reader, options, cancellationToken);

    private enum BufferedStep
    {
        Produced,
        NeedMoreData,
        Completed
    }

    private sealed class LineAsyncEnumerable(
        PipeReader reader,
        JsonlReadOptions options,
        CancellationToken cancellationToken) : IAsyncEnumerable<JsonlLine>
    {
        public IAsyncEnumerator<JsonlLine> GetAsyncEnumerator(CancellationToken enumeratorCancellationToken = default)
            => new Enumerator(reader, options, cancellationToken, enumeratorCancellationToken);
    }

    private sealed class Enumerator : IAsyncEnumerator<JsonlLine>
    {
        private readonly PipeReader reader;
        private readonly JsonlReadOptions options;
        private readonly CancellationTokenSource? linkedCancellationSource;
        private readonly CancellationToken cancellationToken;

        private byte[]? rentedLineBuffer;
        private ReadOnlySequence<byte> bufferedData;
        private SequencePosition bufferedExamined;
        private long lineNumber;
        private bool hasBufferedRead;
        private bool bufferedIsCompleted;
        private bool firstLineEmitted;
        private bool validated;
        private bool completed;

        public Enumerator(
            PipeReader reader,
            JsonlReadOptions options,
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

        public JsonlLine Current { get; private set; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ValueTask<bool> MoveNextAsync()
        {
            Current = default;

            if (completed)
            {
                return ValueTask.FromResult(false);
            }

            if (!validated)
            {
                try
                {
                    ArgumentNullException.ThrowIfNull(reader);
                    ArgumentNullException.ThrowIfNull(options);
                    options.Validate();
                    validated = true;
                }
                catch (Exception ex)
                {
                    completed = true;
                    return ValueTask.FromException<bool>(ex);
                }
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return ValueTask.FromCanceled<bool>(cancellationToken);
            }

            if (hasBufferedRead)
            {
                try
                {
                    BufferedStep step = TryAdvanceBuffered(out JsonlLine next);
                    if (step == BufferedStep.Produced)
                    {
                        Current = next;
                        return ValueTask.FromResult(true);
                    }

                    if (step == BufferedStep.Completed)
                    {
                        return ValueTask.FromResult(false);
                    }
                }
                catch (Exception ex)
                {
                    return ValueTask.FromException<bool>(ex);
                }
            }

            return MoveNextSlowAsync();
        }

        private async ValueTask<bool> MoveNextSlowAsync()
        {
            while (true)
            {
                if (!hasBufferedRead)
                {
                    ReadResult result = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                    if (result.IsCanceled)
                    {
                        reader.AdvanceTo(result.Buffer.Start, result.Buffer.Start);
                        throw new OperationCanceledException("The underlying PipeReader read was canceled.", cancellationToken);
                    }

                    bufferedData = result.Buffer;
                    bufferedExamined = result.Buffer.End;
                    bufferedIsCompleted = result.IsCompleted;
                    hasBufferedRead = true;
                }

                BufferedStep step = TryAdvanceBuffered(out JsonlLine next);
                if (step == BufferedStep.Produced)
                {
                    Current = next;
                    return true;
                }

                if (step == BufferedStep.Completed)
                {
                    return false;
                }
            }
        }

        private BufferedStep TryAdvanceBuffered(out JsonlLine nextLine)
        {
            if (TryReadLine(ref bufferedData, out ReadOnlySequence<byte> lineSeq))
            {
                if (lineSeq.Length > options.MaxLineSizeBytes)
                {
                    ReleaseBufferedRead(bufferedData.Start, bufferedExamined);
                    throw new JsonlException(
                        JsonlErrorCode.LineTooLong,
                        string.Create(CultureInfo.InvariantCulture, $"A single line exceeds the configured MaxLineSizeBytes of {options.MaxLineSizeBytes:N0} bytes."),
                        lineNumber + 1);
                }

                ReadOnlyMemory<byte> utf8 = BorrowLineMemory(lineSeq, ref firstLineEmitted, ref rentedLineBuffer);
                lineNumber++;
                nextLine = new JsonlLine(utf8, lineNumber);
                return BufferedStep.Produced;
            }

            if (bufferedIsCompleted)
            {
                if (!bufferedData.IsEmpty)
                {
                    if (bufferedData.Length > options.MaxLineSizeBytes)
                    {
                        ReleaseBufferedRead(bufferedData.End, bufferedData.End);
                        throw new JsonlException(
                            JsonlErrorCode.LineTooLong,
                            string.Create(CultureInfo.InvariantCulture, $"A single line exceeds the configured MaxLineSizeBytes of {options.MaxLineSizeBytes:N0} bytes."),
                            lineNumber + 1);
                    }

                    ReadOnlyMemory<byte> utf8 = BorrowLineMemory(bufferedData, ref firstLineEmitted, ref rentedLineBuffer);
                    // Keep hasBufferedRead true until the next MoveNextAsync or DisposeAsync so the borrowed pipe segment stays valid.
                    bufferedData = bufferedData.Slice(bufferedData.Length);
                    lineNumber++;
                    nextLine = new JsonlLine(utf8, lineNumber);
                    return BufferedStep.Produced;
                }

                ReleaseBufferedRead(bufferedData.End, bufferedData.End);
                completed = true;
                nextLine = default;
                return BufferedStep.Completed;
            }

            // A CR at the limit may be the first half of a split CRLF delimiter.
            // Keep that one byte until LF arrives; completed lines still enforce the limit.
            bool pendingCrLf = bufferedData.Length == (long)options.MaxLineSizeBytes + 1 &&
                TryGetLastByte(bufferedData, out byte lastByte) && lastByte == CR;
            if (bufferedData.Length > options.MaxLineSizeBytes && !pendingCrLf)
            {
                ReleaseBufferedRead(bufferedData.Start, bufferedExamined);
                throw new JsonlException(
                    JsonlErrorCode.LineTooLong,
                    string.Create(CultureInfo.InvariantCulture, $"A single line exceeds the configured MaxLineSizeBytes of {options.MaxLineSizeBytes:N0} bytes."),
                    lineNumber + 1);
            }

            ReleaseBufferedRead(bufferedData.Start, bufferedExamined);
            nextLine = default;
            return BufferedStep.NeedMoreData;
        }

        private void ReleaseBufferedRead(SequencePosition consumed, SequencePosition examined)
        {
            if (!hasBufferedRead)
            {
                return;
            }

            reader.AdvanceTo(consumed, examined);
            hasBufferedRead = false;
            bufferedIsCompleted = false;
            bufferedData = default;
            bufferedExamined = default;
        }

        public ValueTask DisposeAsync()
        {
            completed = true;
            Current = default;

            if (hasBufferedRead)
            {
                ReleaseBufferedRead(bufferedData.Start, bufferedData.Start);
            }

            if (rentedLineBuffer is { } bufferToReturn)
            {
                rentedLineBuffer = null;
                ArrayPool<byte>.Shared.Return(bufferToReturn, clearArray: false);
            }

            linkedCancellationSource?.Dispose();
            return default;
        }
    }

    private static bool TryReadLine(ref ReadOnlySequence<byte> buffer, out ReadOnlySequence<byte> line)
    {
        SequencePosition? newlinePos = buffer.PositionOf(LF);
        if (newlinePos is null)
        {
            line = default;
            return false;
        }

        line = buffer.Slice(0, newlinePos.Value);

        if (!line.IsEmpty)
        {
            ReadOnlySequence<byte> trimmable = line;
            if (TryGetLastByte(trimmable, out byte lastByte) && lastByte == CR)
                line = line.Slice(0, line.Length - 1);
        }

        buffer = buffer.Slice(buffer.GetPosition(1, newlinePos.Value));
        return true;
    }

    private static bool TryGetLastByte(ReadOnlySequence<byte> seq, out byte last)
    {
        if (seq.IsEmpty)
        {
            last = 0;
            return false;
        }

        if (seq.IsSingleSegment)
        {
            ReadOnlySpan<byte> span = seq.FirstSpan;
            last = span[^1];
            return true;
        }

        SequencePosition pos = seq.GetPosition(seq.Length - 1);
        ReadOnlySequence<byte> tail = seq.Slice(pos);
        last = tail.FirstSpan[0];
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ReadOnlyMemory<byte> BorrowLineMemory(
        ReadOnlySequence<byte> seq,
        ref bool firstLineEmitted,
        ref byte[]? rentedBuffer)
    {
        if (seq.IsSingleSegment)
        {
            ReadOnlyMemory<byte> memory = seq.First;
            if (!firstLineEmitted)
            {
                firstLineEmitted = true;
                ReadOnlySpan<byte> span = memory.Span;
                if (span.Length >= 3 && span[0] == 0xEF && span[1] == 0xBB && span[2] == 0xBF)
                {
                    memory = memory[3..];
                }
            }

            return memory;
        }

        return CopyMultiSegmentLine(seq, ref firstLineEmitted, ref rentedBuffer);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ReadOnlyMemory<byte> CopyMultiSegmentLine(
        ReadOnlySequence<byte> seq,
        ref bool firstLineEmitted,
        ref byte[]? rentedBuffer)
    {
        int length = checked((int)seq.Length);
        int offset = 0;

        if (!firstLineEmitted)
        {
            firstLineEmitted = true;
            if (length >= 3)
            {
                Span<byte> peek = stackalloc byte[3];
                seq.Slice(0, 3).CopyTo(peek);
                if (peek[0] == 0xEF && peek[1] == 0xBB && peek[2] == 0xBF)
                    offset = 3;
            }
        }

        int finalLength = length - offset;
        if (finalLength <= 0)
            return ReadOnlyMemory<byte>.Empty;

        byte[] buffer = rentedBuffer is { Length: var currentLen } && currentLen >= finalLength
            ? rentedBuffer
            : GrowRentedBuffer(ref rentedBuffer, finalLength);
        seq.Slice(offset, finalLength).CopyTo(buffer.AsSpan(0, finalLength));
        return buffer.AsMemory(0, finalLength);
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
}
