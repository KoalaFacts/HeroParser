using System.Runtime.CompilerServices;

namespace HeroParser.FixedWidths;

/// <summary>
/// A ref struct reader that iterates over fixed-width records in a character span.
/// </summary>
/// <remarks>
/// By default, records are delimited by newlines. When <see cref="FixedWidthParserOptions.RecordLength"/>
/// is specified, records are read as fixed-length blocks instead.
/// </remarks>
public ref struct FixedWidthCharSpanReader
{
    private readonly ReadOnlySpan<char> chars;
    private readonly FixedWidthParserOptions options;
    private int position;
    private int recordCount;
    private int sourceLineNumber;

    /// <summary>
    /// Initializes a new instance of the <see cref="FixedWidthCharSpanReader"/> struct.
    /// </summary>
    /// <param name="chars">The character span containing fixed-width data.</param>
    /// <param name="options">Parser options.</param>
    internal FixedWidthCharSpanReader(ReadOnlySpan<char> chars, FixedWidthParserOptions options)
    {
        this.chars = chars;
        this.options = options;
        position = 0;
        recordCount = 0;
        sourceLineNumber = options.TrackSourceLineNumbers ? 1 : 0;
        Current = default;

        // Skip initial rows if configured
        if (options.SkipRows > 0)
        {
            SkipInitialRows(options.SkipRows);
        }
    }

    private void SkipInitialRows(int rowsToSkip)
    {
        int skipped = 0;
        while (skipped < rowsToSkip && position < chars.Length)
        {
            var remaining = chars[position..];
            var lineEnd = FindLineEnd(remaining);

            if (lineEnd == -1)
            {
                // Last line without newline - skip to end
                position = chars.Length;
            }
            else
            {
                // Skip the newline character(s)
                position += lineEnd + 1;
                if (lineEnd < remaining.Length - 1 && remaining[lineEnd] == '\r' && remaining[lineEnd + 1] == '\n')
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
    public FixedWidthCharSpanRow Current { get; private set; }

    /// <summary>
    /// Estimates the number of rows remaining in the input.
    /// Used for pre-allocating collection capacity.
    /// </summary>
    internal readonly int EstimateRowCount()
    {
        var remaining = chars.Length - position;
        if (remaining <= 0) return 0;

        if (options.RecordLength is { } fixedLength)
        {
            // Exact count for fixed-length records
            return remaining / fixedLength;
        }

        // For line-delimited, estimate based on typical row length
        // Use 40 chars as a conservative default for fixed-width data
        // This errs on the side of slightly over-allocating to avoid resizes
        const int estimatedRowLength = 40;
        return Math.Max(1, remaining / estimatedRowLength);
    }

    /// <summary>Returns this instance as an enumerator.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly FixedWidthCharSpanReader GetEnumerator() => this;

    /// <summary>
    /// Advances to the next record.
    /// </summary>
    /// <returns><see langword="true"/> if a record was read; <see langword="false"/> if at end of data.</returns>
    public bool MoveNext()
    {
        if (position >= chars.Length)
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
        if (position + recordLength > chars.Length)
        {
            // Handle partial record at end
            if (position < chars.Length)
            {
                var remaining = chars.Length - position;
                throw new FixedWidthException(
                    FixedWidthErrorCode.InvalidRecordLength,
                    $"Expected record length of {recordLength}, but only {remaining} characters remaining.",
                    recordCount + 1);
            }
            return false;
        }

        recordCount++;
        var recordSpan = chars.Slice(position, recordLength);
        Current = new FixedWidthCharSpanRow(recordSpan, recordCount, sourceLineNumber, options);
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
        while (position < chars.Length)
        {
            var remaining = chars[position..];
            var lineEnd = FindLineEnd(remaining);

            ReadOnlySpan<char> line;
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
                // Skip the newline character(s)
                consumed = lineEnd + 1;
                if (lineEnd < remaining.Length - 1 && remaining[lineEnd] == '\r' && remaining[lineEnd + 1] == '\n')
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
            if (options.CommentCharacter is { } commentChar && line.Length > 0 && line[0] == commentChar)
            {
                if (options.TrackSourceLineNumbers)
                    sourceLineNumber++;
                continue;
            }

            recordCount++;
            Current = new FixedWidthCharSpanRow(line, recordCount, sourceLineNumber, options);

            if (options.TrackSourceLineNumbers)
                sourceLineNumber++;

            return true;
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int FindLineEnd(ReadOnlySpan<char> span)
    {
        for (int i = 0; i < span.Length; i++)
        {
            if (span[i] == '\n' || span[i] == '\r')
                return i;
        }
        return -1;
    }

    private static int CountNewlines(ReadOnlySpan<char> span)
    {
        int count = 0;
        for (int i = 0; i < span.Length; i++)
        {
            if (span[i] == '\n')
            {
                count++;
            }
            else if (span[i] == '\r')
            {
                count++;
                // Skip \n if it follows \r
                if (i + 1 < span.Length && span[i + 1] == '\n')
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
