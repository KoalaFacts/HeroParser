using System.Runtime.CompilerServices;

namespace HeroParser.FixedWidths;

/// <summary>
/// A ref struct reader that iterates over fixed-width records in a UTF-8 byte span.
/// </summary>
/// <remarks>
/// By default, records are delimited by newlines. When <see cref="FixedWidthParserOptions.RecordLength"/>
/// is specified, records are read as fixed-length blocks instead.
/// </remarks>
public ref struct FixedWidthByteSpanReader
{
    private readonly ReadOnlySpan<byte> bytes;
    private readonly FixedWidthParserOptions options;
    private int position;
    private int recordCount;
    private int sourceLineNumber;

    private const byte CR = (byte)'\r';
    private const byte LF = (byte)'\n';

    /// <summary>
    /// Initializes a new instance of the <see cref="FixedWidthByteSpanReader"/> struct.
    /// </summary>
    /// <param name="bytes">The UTF-8 byte span containing fixed-width data.</param>
    /// <param name="options">Parser options.</param>
    internal FixedWidthByteSpanReader(ReadOnlySpan<byte> bytes, FixedWidthParserOptions options)
    {
        this.bytes = bytes;
        this.options = options;
        position = 0;
        recordCount = 0;
        sourceLineNumber = options.TrackSourceLineNumbers ? 1 : 0;
        Current = default;

        // Strip UTF-8 BOM if present
        if (this.bytes.Length >= 3 &&
            this.bytes[0] == 0xEF &&
            this.bytes[1] == 0xBB &&
            this.bytes[2] == 0xBF)
        {
            position = 3;
        }

        // Skip initial rows if configured
        if (options.SkipRows > 0)
        {
            SkipInitialRows(options.SkipRows);
        }
    }

    private void SkipInitialRows(int rowsToSkip)
    {
        int skipped = 0;
        while (skipped < rowsToSkip && position < bytes.Length)
        {
            var remaining = bytes[position..];
            var lineEnd = FindLineEnd(remaining);

            if (lineEnd == -1)
            {
                // Last line without newline - skip to end
                position = bytes.Length;
            }
            else
            {
                // Skip the newline byte(s)
                position += lineEnd + 1;
                if (lineEnd < remaining.Length - 1 && remaining[lineEnd] == CR && remaining[lineEnd + 1] == LF)
                {
                    position++;
                }
            }

            if (options.TrackSourceLineNumbers)
                sourceLineNumber++;

            skipped++;
        }
    }

    /// <summary>Gets the current record.</summary>
    public FixedWidthByteSpanRow Current { get; private set; }

    /// <summary>
    /// Estimates the number of rows remaining in the input.
    /// Used for pre-allocating collection capacity.
    /// </summary>
    internal readonly int EstimateRowCount()
    {
        var remaining = bytes.Length - position;
        if (remaining <= 0) return 0;

        if (options.RecordLength is { } fixedLength)
        {
            // Exact count for fixed-length records
            return remaining / fixedLength;
        }

        // For line-delimited, estimate based on typical row length
        const int estimatedRowLength = 40;
        return Math.Max(1, remaining / estimatedRowLength);
    }

    /// <summary>Returns this instance as an enumerator.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly FixedWidthByteSpanReader GetEnumerator() => this;

    /// <summary>
    /// Advances to the next record.
    /// </summary>
    /// <returns><see langword="true"/> if a record was read; <see langword="false"/> if at end of data.</returns>
    public bool MoveNext()
    {
        if (position >= bytes.Length)
            return false;

        if (recordCount >= options.MaxRecordCount)
        {
            throw new FixedWidthException(
                FixedWidthErrorCode.TooManyRecords,
                $"Maximum record count of {options.MaxRecordCount} exceeded.");
        }

        if (options.RecordLength is { } fixedLength)
        {
            return MoveNextFixedLength(fixedLength);
        }

        return MoveNextLineBased();
    }

    private bool MoveNextFixedLength(int recordLength)
    {
        // Check if we have enough data for another record
        if (position + recordLength > bytes.Length)
        {
            // Handle partial record at end
            if (position < bytes.Length)
            {
                var remaining = bytes.Length - position;
                throw new FixedWidthException(
                    FixedWidthErrorCode.InvalidRecordLength,
                    $"Expected record length of {recordLength}, but only {remaining} bytes remaining.",
                    recordCount + 1);
            }
            return false;
        }

        recordCount++;
        var recordSpan = bytes.Slice(position, recordLength);
        Current = new FixedWidthByteSpanRow(recordSpan, recordCount, sourceLineNumber, options);
        position += recordLength;

        // Track newlines within the record for source line tracking
        if (options.TrackSourceLineNumbers)
        {
            sourceLineNumber += CountNewlines(recordSpan);
        }

        return true;
    }

    private bool MoveNextLineBased()
    {
        var commentByte = options.CommentCharacter.HasValue ? (byte)options.CommentCharacter.Value : (byte)0;
        var hasCommentChar = options.CommentCharacter.HasValue;

        while (position < bytes.Length)
        {
            var remaining = bytes[position..];
            var lineEnd = FindLineEnd(remaining);

            ReadOnlySpan<byte> line;
            int consumed;

            if (lineEnd == -1)
            {
                // Last line without newline
                line = remaining;
                consumed = remaining.Length;
            }
            else
            {
                line = remaining[..lineEnd];
                // Skip the newline byte(s)
                consumed = lineEnd + 1;
                if (lineEnd < remaining.Length - 1 && remaining[lineEnd] == CR && remaining[lineEnd + 1] == LF)
                {
                    consumed++;
                }
            }

            position += consumed;

            // Handle empty lines
            if (line.IsEmpty && options.SkipEmptyLines)
            {
                if (options.TrackSourceLineNumbers)
                    sourceLineNumber++;
                continue;
            }

            // Handle comment lines
            if (hasCommentChar && line.Length > 0 && line[0] == commentByte)
            {
                if (options.TrackSourceLineNumbers)
                    sourceLineNumber++;
                continue;
            }

            recordCount++;
            Current = new FixedWidthByteSpanRow(line, recordCount, sourceLineNumber, options);

            if (options.TrackSourceLineNumbers)
                sourceLineNumber++;

            return true;
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int FindLineEnd(ReadOnlySpan<byte> span)
    {
        // Find the first occurrence of CR or LF
        for (int i = 0; i < span.Length; i++)
        {
            if (span[i] == CR || span[i] == LF)
                return i;
        }
        return -1;
    }

    private static int CountNewlines(ReadOnlySpan<byte> span)
    {
        int count = 0;
        for (int i = 0; i < span.Length; i++)
        {
            if (span[i] == LF)
            {
                count++;
            }
            else if (span[i] == CR)
            {
                count++;
                // Skip LF if it follows CR
                if (i + 1 < span.Length && span[i + 1] == LF)
                    i++;
            }
        }
        return count;
    }

    /// <summary>Disposes the reader.</summary>
    public readonly void Dispose()
    {
        // No-op for span-based reader
    }
}
