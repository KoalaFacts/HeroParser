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
    /// The returned <see cref="FixedWidthPipeRow"/> owns its row data, so it remains valid after the underlying
    /// <see cref="PipeReader"/> advances. Row parsing honors the same fixed-width options as the existing
    /// span- and stream-based readers, including fixed-length mode, line-based mode, comments, skipped rows,
    /// headers, source line tracking, and short-row handling.
    /// </remarks>
    public static async IAsyncEnumerable<FixedWidthPipeRow> ReadFromPipeReaderAsync(
        PipeReader reader,
        FixedWidthReadOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        options ??= FixedWidthReadOptions.Default;
        options.Validate();

        int remainingRowsToSkip = options.SkipRows + (options.HasHeaderRow ? 1 : 0);
        int recordCount = 0;
        int sourceLineNumber = options.TrackSourceLineNumbers ? 1 : 0;
        long totalBytesRead = 0;
        long previousUnreadBytes = 0;
        bool bomProcessed = false;

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var result = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                var originalBuffer = result.Buffer;
                var newBytes = originalBuffer.Length - previousUnreadBytes;
                if (newBytes > 0)
                {
                    totalBytesRead += newBytes;
                    options.ValidateInputSize(totalBytesRead);
                }

                var buffer = originalBuffer;
                if (!TryProcessUtf8Bom(ref buffer, result.IsCompleted, ref bomProcessed))
                {
                    reader.AdvanceTo(buffer.Start, buffer.End);
                    previousUnreadBytes = buffer.Length;
                    continue;
                }

                if (options.RecordLength is { } recordLength)
                {
                    while (TryReadPipeFixedLengthRecord(ref buffer, recordLength, out var rowData))
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        if (remainingRowsToSkip > 0)
                        {
                            if (options.TrackSourceLineNumbers)
                            {
                                sourceLineNumber += FixedWidthLineScanner.CountNewlines(rowData.ToArray());
                            }

                            remainingRowsToSkip--;
                            continue;
                        }

                        recordCount++;
                        EnsureRecordCount(recordCount, options.MaxRecordCount);

                        int rowStartLine = options.TrackSourceLineNumbers ? sourceLineNumber : 0;
                        var rowBytes = rowData.ToArray();
                        if (options.TrackSourceLineNumbers)
                        {
                            sourceLineNumber += FixedWidthLineScanner.CountNewlines(rowBytes);
                        }

                        yield return new FixedWidthPipeRow(rowBytes, recordCount, rowStartLine, options);
                    }

                    if (result.IsCompleted && buffer.Length > 0 && remainingRowsToSkip <= 0)
                    {
                        ThrowInvalidPipeRecordLength(recordLength, buffer.Length, recordCount + 1, sourceLineNumber, options.TrackSourceLineNumbers);
                    }
                }
                else
                {
                    while (TryReadPipeLineRecord(ref buffer, result.IsCompleted, out var rowData))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        int rowStartLine = options.TrackSourceLineNumbers ? sourceLineNumber : 0;

                        if (remainingRowsToSkip > 0)
                        {
                            remainingRowsToSkip--;
                            if (options.TrackSourceLineNumbers)
                            {
                                sourceLineNumber++;
                            }

                            continue;
                        }

                        var rowBytes = rowData.ToArray();

                        if (rowBytes.Length == 0 && options.SkipEmptyLines)
                        {
                            if (options.TrackSourceLineNumbers)
                            {
                                sourceLineNumber++;
                            }

                            continue;
                        }

                        if (options.CommentCharacter is { } commentChar && rowBytes.Length > 0 && rowBytes[0] == (byte)commentChar)
                        {
                            if (options.TrackSourceLineNumbers)
                            {
                                sourceLineNumber++;
                            }

                            continue;
                        }

                        recordCount++;
                        EnsureRecordCount(recordCount, options.MaxRecordCount);
                        yield return new FixedWidthPipeRow(rowBytes, recordCount, rowStartLine, options);

                        if (options.TrackSourceLineNumbers)
                        {
                            sourceLineNumber++;
                        }
                    }

                }

                reader.AdvanceTo(buffer.Start, buffer.End);
                previousUnreadBytes = buffer.Length;

                if (result.IsCompleted)
                {
                    break;
                }
            }
        }
        finally
        {
            await reader.CompleteAsync().ConfigureAwait(false);
        }
    }

    private static bool TryProcessUtf8Bom(ref ReadOnlySequence<byte> buffer, bool isCompleted, ref bool bomProcessed)
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

    private static bool TryReadPipeFixedLengthRecord(
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

    private static bool TryReadPipeLineRecord(
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

    private static void EnsureRecordCount(int recordCount, int maxRecordCount)
    {
        if (recordCount > maxRecordCount)
        {
            throw new FixedWidthException(
                FixedWidthErrorCode.TooManyRecords,
                $"Maximum record count of {maxRecordCount} exceeded.");
        }
    }

    private static void ThrowInvalidPipeRecordLength(
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
}

/// <summary>
/// Represents a single fixed-width row parsed from a <see cref="PipeReader"/>.
/// </summary>
/// <remarks>
/// The row owns its backing buffer, so it can safely outlive the async enumeration step that produced it.
/// Field access mirrors <see cref="FixedWidthByteSpanRow"/>, using byte offsets and lengths.
/// </remarks>
public readonly struct FixedWidthPipeRow
{
    private readonly byte[] data;
    private readonly FixedWidthReadOptions options;

    internal FixedWidthPipeRow(byte[] data, int recordNumber, int sourceLineNumber, FixedWidthReadOptions options)
    {
        this.data = data;
        this.options = options;
        RecordNumber = recordNumber;
        SourceLineNumber = sourceLineNumber;
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
    public ReadOnlySpan<byte> RawRecord => data;

    /// <summary>
    /// Gets a field at the specified byte position using the default padding options.
    /// </summary>
    public FixedWidthByteSpanColumn GetField(int start, int length)
        => GetField(start, length, (byte)options.DefaultPadChar, options.DefaultAlignment);

    /// <summary>
    /// Gets a field at the specified byte position with custom padding options.
    /// </summary>
    public FixedWidthByteSpanColumn GetField(int start, int length, byte padByte, FieldAlignment alignment)
    {
        if (start < 0)
            throw new ArgumentOutOfRangeException(nameof(start), start, "Start position cannot be negative.");

        if (length < 0)
            throw new ArgumentOutOfRangeException(nameof(length), length, "Field length cannot be negative.");

        var fieldEnd = start + length;
        if (fieldEnd > data.Length)
        {
            if (!options.AllowShortRows)
            {
                throw new FixedWidthException(
                    FixedWidthErrorCode.FieldOutOfBounds,
                    $"Field at position {start} with length {length} extends beyond the record length ({data.Length}). " +
                    $"Enable AllowShortRows to handle short records gracefully.",
                    RecordNumber,
                    SourceLineNumber);
            }

            if (start >= data.Length)
            {
                return new FixedWidthByteSpanColumn([]);
            }
        }

        int actualLength = Math.Min(length, data.Length - start);
        ReadOnlySpan<byte> span = data.AsSpan(start, actualLength);

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
    public string ToDecodedString() => System.Text.Encoding.UTF8.GetString(data);

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
