using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using HeroParser.FixedWidths;

namespace HeroParser;

public static partial class FixedWidth
{
    /// <summary>
    /// Asynchronously reads fixed-width rows from a <see cref="PipeReader"/>.
    /// </summary>
    /// <param name="reader">The pipe reader to read from.</param>
    /// <param name="options">Optional parser options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async enumerable of fixed-width rows.</returns>
    /// <remarks>
    /// For zero-copy, O(1) low-allocation streaming, the yielded <see cref="FixedWidthPipeRow"/> borrows the active
    /// pipe/pooled buffer directly and remains valid only until the next <see cref="IAsyncEnumerator{T}.MoveNextAsync"/>
    /// call or enumerator disposal; call <see cref="FixedWidthPipeRow.Clone"/> to retain an independent copy across iterations.
    /// Row parsing honors the same fixed-width options as the existing span- and stream-based readers, including
    /// fixed-length mode, line-based mode, comments, skipped rows, headers, source line tracking, and short-row handling.
    /// </remarks>
    public static IAsyncEnumerable<FixedWidthPipeRow> ReadFromPipeReaderAsync(
        PipeReader reader,
        FixedWidthReadOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        options ??= FixedWidthReadOptions.Default;
        options.Validate();
        return new FixedWidthPipeRowAsyncEnumerable(reader, options, cancellationToken);
    }

    internal static bool TryProcessUtf8Bom(ref ReadOnlySequence<byte> buffer, bool isCompleted, ref bool bomProcessed)
    {
        if (bomProcessed)
        {
            return true;
        }

        if (buffer.Length < 3)
        {
            // Do not block a complete short record when the available bytes
            // already rule out a UTF-8 BOM. Only an EF or EF BB prefix is ambiguous.
            if (!buffer.IsEmpty)
            {
                var probe = new SequenceReader<byte>(buffer);
                if (probe.TryRead(out byte firstByte) &&
                    (firstByte != 0xEF || (probe.TryPeek(out byte secondByte) && secondByte != 0xBB)))
                {
                    bomProcessed = true;
                    return true;
                }
            }

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

    internal static bool TryReadPipeFixedLengthRecord(
        ref ReadOnlySequence<byte> buffer,
        int recordLength,
        out ReadOnlySequence<byte> rowData)
    {
        if (buffer.Length < recordLength)
        {
            rowData = default;
            return false;
        }

        rowData = buffer.Slice(0, recordLength);
        buffer = buffer.Slice(recordLength);
        return true;
    }

    internal static bool TryReadPipeLineRecord(
        ref ReadOnlySequence<byte> buffer,
        bool isCompleted,
        out ReadOnlySequence<byte> rowData)
    {
        var reader = new SequenceReader<byte>(buffer);

        while (reader.Remaining > 0)
        {
            if (!reader.TryRead(out byte current))
            {
                break;
            }

            if (current == (byte)'\r')
            {
                if (reader.Remaining == 0 && !isCompleted)
                {
                    rowData = default;
                    return false;
                }

                long delimiterLength = 1;
                if (reader.IsNext((byte)'\n', advancePast: true))
                {
                    delimiterLength = 2;
                }

                long consumed = reader.Consumed;
                rowData = buffer.Slice(0, consumed - delimiterLength);
                buffer = buffer.Slice(consumed);
                return true;
            }

            if (current == (byte)'\n')
            {
                long consumed = reader.Consumed;
                rowData = buffer.Slice(0, consumed - 1);
                buffer = buffer.Slice(consumed);
                return true;
            }
        }

        if (isCompleted && buffer.Length > 0)
        {
            rowData = buffer;
            buffer = buffer.Slice(buffer.Length);
            return true;
        }

        rowData = default;
        return false;
    }

    internal static void EnsureRecordCount(int recordCount, int maxRecordCount)
    {
        if (recordCount > maxRecordCount)
        {
            throw new FixedWidthException(
                FixedWidthErrorCode.TooManyRecords,
                $"Maximum record count of {maxRecordCount} exceeded.");
        }
    }

    internal static void ThrowInvalidPipeRecordLength(
        int expectedLength,
        long remainingLength,
        int recordNumber,
        int sourceLineNumber,
        bool trackSourceLineNumbers)
    {
        var message = $"Expected record length of {expectedLength}, but only {remainingLength} bytes remaining.";
        if (trackSourceLineNumbers)
        {
            throw new FixedWidthException(
                FixedWidthErrorCode.InvalidRecordLength,
                message,
                recordNumber,
                sourceLineNumber);
        }

        throw new FixedWidthException(
            FixedWidthErrorCode.InvalidRecordLength,
            message,
            recordNumber);
    }

    internal static FixedWidthPipeRow CreatePipeRow(
        ReadOnlySequence<byte> rowData,
        int recordNumber,
        int sourceLineNumber,
        FixedWidthReadOptions options)
    {
        int rowLength = checked((int)rowData.Length);
        if (rowLength == 0)
        {
            return new FixedWidthPipeRow(ReadOnlyMemory<byte>.Empty, recordNumber, sourceLineNumber, options);
        }

        var rowBytes = GC.AllocateUninitializedArray<byte>(rowLength);
        rowData.CopyTo(rowBytes);
        return new FixedWidthPipeRow(rowBytes, recordNumber, sourceLineNumber, options);
    }

    internal static bool TryGetFirstByte(ReadOnlySequence<byte> rowData, out byte value)
    {
        if (rowData.IsEmpty)
        {
            value = default;
            return false;
        }

        var firstSpan = rowData.FirstSpan;
        if (!firstSpan.IsEmpty)
        {
            value = firstSpan[0];
            return true;
        }

        // A non-empty multi-segment sequence may start with an empty segment.
        // SequenceReader skips empty segments without copying or allocating.
        var sequenceReader = new SequenceReader<byte>(rowData);
        return sequenceReader.TryPeek(out value);
    }
}

internal sealed class FixedWidthPipeRowAsyncEnumerable(
    PipeReader reader,
    FixedWidthReadOptions options,
    CancellationToken cancellationToken) : IAsyncEnumerable<FixedWidthPipeRow>
{
    public IAsyncEnumerator<FixedWidthPipeRow> GetAsyncEnumerator(CancellationToken enumeratorCancellationToken = default)
        => new Enumerator(reader, options, cancellationToken, enumeratorCancellationToken);

    private sealed class Enumerator : IAsyncEnumerator<FixedWidthPipeRow>
    {
        private readonly PipeReader reader;
        private readonly FixedWidthReadOptions options;
        private CancellationTokenSource? linkedCancellationSource;
        private readonly CancellationToken cancellationToken;

        private FixedWidthPipeSequenceReader? sequenceReader;
        private byte[]? rentedRowBuffer;
        private bool completed;

        public Enumerator(
            PipeReader reader,
            FixedWidthReadOptions options,
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

        public FixedWidthPipeRow Current { get; private set; }

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

            sequenceReader ??= new FixedWidthPipeSequenceReader(reader, options);

            ValueTask<bool> moveNextTask = sequenceReader.MoveNextAsync(cancellationToken);
            if (moveNextTask.IsCompletedSuccessfully)
            {
                if (!moveNextTask.Result)
                {
                    completed = true;
                    return ValueTask.FromResult(false);
                }

                Current = sequenceReader.CreateBorrowedCurrentRow(ref rentedRowBuffer);
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

            CancellationTokenSource? ctsToDispose = linkedCancellationSource;
            linkedCancellationSource = null;

            if (sequenceReader is { } readerToDispose)
            {
                sequenceReader = null;
                ValueTask disposeTask = readerToDispose.DisposeAsync();
                if (disposeTask.IsCompletedSuccessfully)
                {
                    disposeTask.GetAwaiter().GetResult();
                    using (ctsToDispose)
                    {
                        return default;
                    }
                }

                return DisposeSlowAsync(disposeTask, ctsToDispose);
            }

            using (ctsToDispose)
            {
                return default;
            }
        }

        private static async ValueTask DisposeSlowAsync(ValueTask disposeTask, CancellationTokenSource? ctsToDispose)
        {
            using (ctsToDispose)
            {
                await disposeTask.ConfigureAwait(false);
            }
        }
    }
}

/// <summary>
/// Represents a single fixed-width row parsed from a <see cref="PipeReader"/>.
/// </summary>
/// <remarks>
/// Rows yielded by <see cref="FixedWidth.ReadFromPipeReaderAsync(PipeReader, FixedWidthReadOptions?, CancellationToken)"/>
/// borrow the active pipe/pooled buffer directly and are valid only until the next <c>MoveNextAsync</c> call or
/// enumerator disposal; call <see cref="Clone"/> to retain an independent copy across iterations.
/// Field access mirrors <see cref="FixedWidthByteSpanRow"/>, using byte offsets and lengths.
/// </remarks>
public readonly struct FixedWidthPipeRow
{
    private readonly ReadOnlyMemory<byte> data;
    private readonly FixedWidthReadOptions options;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal FixedWidthPipeRow(ReadOnlyMemory<byte> data, int recordNumber, int sourceLineNumber, FixedWidthReadOptions options)
    {
        this.data = data;
        this.options = options;
        RecordNumber = recordNumber;
        SourceLineNumber = sourceLineNumber;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal FixedWidthPipeRow(byte[] data, int recordNumber, int sourceLineNumber, FixedWidthReadOptions options)
        : this((ReadOnlyMemory<byte>)data, recordNumber, sourceLineNumber, options)
    {
    }

    /// <summary>Gets the 1-based record number.</summary>
    public int RecordNumber { get; }

    /// <summary>
    /// Gets the 1-based source line number where this record started.
    /// Only populated when <see cref="FixedWidthReadOptions.TrackSourceLineNumbers"/> is <see langword="true"/>.
    /// </summary>
    public int SourceLineNumber { get; }

    /// <summary>Gets the length of the record in bytes.</summary>
    public int Length => data.Length;

    /// <summary>Gets the raw record as a UTF-8 byte span.</summary>
    public ReadOnlySpan<byte> RawRecord => data.Span;

    /// <summary>
    /// Creates an independent copy of this row whose memory remains valid after the enumerator advances or is disposed.
    /// </summary>
    /// <returns>A detached <see cref="FixedWidthPipeRow"/> owning its own backing buffer.</returns>
    public FixedWidthPipeRow Clone()
    {
        if (options is null)
        {
            return default;
        }

        if (data.IsEmpty)
        {
            return new FixedWidthPipeRow(ReadOnlyMemory<byte>.Empty, RecordNumber, SourceLineNumber, options);
        }

        byte[] copy = GC.AllocateUninitializedArray<byte>(data.Length);
        data.Span.CopyTo(copy);
        return new FixedWidthPipeRow(copy, RecordNumber, SourceLineNumber, options);
    }

    /// <summary>
    /// Gets a field at the specified byte position using the default padding options.
    /// </summary>
    public FixedWidthByteSpanColumn GetField(int start, int length)
    {
        char padChar = options.DefaultPadChar;
        if (padChar <= 0x7F)
            return GetField(start, length, (byte)padChar, options.DefaultAlignment);

        // The configured default is a Unicode character, not a padding byte.
        // Preserve complete UTF-8 sequences and never truncate it to a low byte.
        var raw = GetField(start, length, 0, FieldAlignment.None);
        return new FixedWidthByteSpanColumn(
            FixedWidthByteSpanRow.TrimUnicodePadding(raw.ByteSpan, padChar, options.DefaultAlignment));
    }

    /// <summary>
    /// Gets a field at the specified byte position with custom padding options.
    /// </summary>
    public FixedWidthByteSpanColumn GetField(int start, int length, byte padByte, FieldAlignment alignment)
    {
        if (start < 0)
            throw new ArgumentOutOfRangeException(nameof(start), start, "Start position cannot be negative.");

        if (length < 0)
            throw new ArgumentOutOfRangeException(nameof(length), length, "Field length cannot be negative.");

        int recordLength = data.Length;
        long fieldEnd = (long)start + length;
        if (fieldEnd > recordLength)
        {
            if (!options.AllowShortRows)
            {
                throw new FixedWidthException(
                    FixedWidthErrorCode.FieldOutOfBounds,
                    $"Field at position {start} with length {length} extends beyond the record length ({recordLength}). " +
                    $"Enable AllowShortRows to handle short records gracefully.",
                    RecordNumber,
                    SourceLineNumber);
            }

            if (start >= recordLength)
            {
                return new FixedWidthByteSpanColumn([]);
            }
        }

        int actualLength = Math.Min(length, recordLength - start);
        ReadOnlySpan<byte> span = data.Span.Slice(start, actualLength);

        span = alignment switch
        {
            FieldAlignment.Left => TrimEnd(span, padByte),
            FieldAlignment.Right => TrimStart(span, padByte),
            FieldAlignment.Center => Trim(span, padByte),
            FieldAlignment.None => span,
            _ => span
        };

        return new FixedWidthByteSpanColumn(span);
    }

    /// <summary>
    /// Gets the raw field at the specified byte position without trimming.
    /// </summary>
    public FixedWidthByteSpanColumn GetRawField(int start, int length)
        => GetField(start, length, 0, FieldAlignment.None);

    /// <summary>
    /// Converts the entire record to a UTF-8 decoded string.
    /// </summary>
    public string ToDecodedString() => System.Text.Encoding.UTF8.GetString(data.Span);

    private static ReadOnlySpan<byte> TrimStart(ReadOnlySpan<byte> span, byte padByte)
    {
        int start = 0;
        while (start < span.Length && span[start] == padByte)
        {
            start++;
        }

        return span[start..];
    }

    private static ReadOnlySpan<byte> TrimEnd(ReadOnlySpan<byte> span, byte padByte)
    {
        int end = span.Length;
        while (end > 0 && span[end - 1] == padByte)
        {
            end--;
        }

        return span[..end];
    }

    private static ReadOnlySpan<byte> Trim(ReadOnlySpan<byte> span, byte padByte)
        => TrimEnd(TrimStart(span, padByte), padByte);
}
