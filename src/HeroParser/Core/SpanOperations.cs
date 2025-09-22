using System.Runtime.CompilerServices;

namespace HeroParser.Core;

/// <summary>
/// High-performance string operations using Span&lt;T&gt; for zero-allocation parsing.
/// Provides optimized algorithms for CSV/fixed-length parsing operations.
/// </summary>
public static class SpanOperations
{
    /// <summary>
    /// Finds the index of the next delimiter in the span.
    /// </summary>
    /// <param name="span">The span to search.</param>
    /// <param name="delimiter">The delimiter character to find.</param>
    /// <returns>The index of the delimiter, or -1 if not found.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int IndexOfDelimiter(ReadOnlySpan<char> span, char delimiter)
    {
        return span.IndexOf(delimiter);
    }

    /// <summary>
    /// Finds the index of the next newline character (CR, LF, or CRLF).
    /// </summary>
    /// <param name="span">The span to search.</param>
    /// <returns>The index and length of the newline sequence, or (-1, 0) if not found.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static (int Index, int Length) IndexOfNewLine(ReadOnlySpan<char> span)
    {
        for (int i = 0; i < span.Length; i++)
        {
            if (span[i] == '\r')
            {
                if (i + 1 < span.Length && span[i + 1] == '\n')
                {
                    return (i, 2); // CRLF
                }
                return (i, 1); // CR only
            }
            else if (span[i] == '\n')
            {
                return (i, 1); // LF only
            }
        }
        return (-1, 0);
    }

    /// <summary>
    /// Counts the occurrences of a character in a span.
    /// </summary>
    /// <param name="span">The span to search.</param>
    /// <param name="value">The character to count.</param>
    /// <returns>The number of occurrences.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CountOccurrences(ReadOnlySpan<char> span, char value)
    {
        int count = 0;
        int index = 0;

        while ((index = span.Slice(index).IndexOf(value)) >= 0)
        {
            count++;
            index++;
            if (index >= span.Length)
                break;
        }

        return count;
    }

    /// <summary>
    /// Trims whitespace from both ends of a span and returns the trimmed range.
    /// </summary>
    /// <param name="span">The span to trim.</param>
    /// <returns>The start index and length of the trimmed content.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static (int Start, int Length) TrimWhitespace(ReadOnlySpan<char> span)
    {
        int start = 0;
        int end = span.Length - 1;

        // Trim from start
        while (start <= end && char.IsWhiteSpace(span[start]))
        {
            start++;
        }

        // Trim from end
        while (end >= start && char.IsWhiteSpace(span[end]))
        {
            end--;
        }

        return (start, Math.Max(0, end - start + 1));
    }

    /// <summary>
    /// Unescapes a quoted CSV field by removing quotes and handling escaped quotes.
    /// </summary>
    /// <param name="source">The source span containing the quoted field.</param>
    /// <param name="destination">The destination span to write the unescaped content.</param>
    /// <param name="quote">The quote character.</param>
    /// <returns>The number of characters written to the destination.</returns>
    public static int UnescapeQuotedField(ReadOnlySpan<char> source, Span<char> destination, char quote = '"')
    {
        if (source.IsEmpty)
            return 0;

        int srcIndex = 0;
        int destIndex = 0;

        // Skip leading quote if present
        if (source[0] == quote)
            srcIndex++;

        // Process until end (minus trailing quote if present)
        int endIndex = source.Length;
        if (endIndex > 0 && source[endIndex - 1] == quote)
            endIndex--;

        while (srcIndex < endIndex && destIndex < destination.Length)
        {
            if (source[srcIndex] == quote && srcIndex + 1 < endIndex && source[srcIndex + 1] == quote)
            {
                // Escaped quote - write single quote and skip both
                destination[destIndex++] = quote;
                srcIndex += 2;
            }
            else
            {
                // Normal character
                destination[destIndex++] = source[srcIndex++];
            }
        }

        return destIndex;
    }

    /// <summary>
    /// Splits a span by a delimiter into segments.
    /// </summary>
    /// <param name="span">The span to split.</param>
    /// <param name="delimiter">The delimiter character.</param>
    /// <param name="segments">The array to store segment positions.</param>
    /// <returns>The number of segments found.</returns>
    public static int SplitIntoSegments(ReadOnlySpan<char> span, char delimiter, Span<(int Start, int Length)> segments)
    {
        if (span.IsEmpty || segments.IsEmpty)
            return 0;

        int segmentCount = 0;
        int currentStart = 0;

        for (int i = 0; i < span.Length && segmentCount < segments.Length; i++)
        {
            if (span[i] == delimiter)
            {
                segments[segmentCount++] = (currentStart, i - currentStart);
                currentStart = i + 1;
            }
        }

        // Add the last segment if there's room
        if (segmentCount < segments.Length && currentStart <= span.Length)
        {
            segments[segmentCount++] = (currentStart, span.Length - currentStart);
        }

        return segmentCount;
    }

    /// <summary>
    /// Counts line breaks in a span.
    /// </summary>
    public static int CountLineBreaks(ReadOnlySpan<char> span)
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
                if (i + 1 < span.Length && span[i + 1] == '\n')
                {
                    i++; // Skip CRLF
                }
            }
        }
        return count;
    }

    /// <summary>
    /// Checks if a span starts with a specific prefix.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool StartsWith(ReadOnlySpan<char> span, ReadOnlySpan<char> value, StringComparison comparison = StringComparison.Ordinal)
    {
        return span.StartsWith(value, comparison);
    }

    /// <summary>
    /// Checks if a span ends with a specific suffix.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool EndsWith(ReadOnlySpan<char> span, ReadOnlySpan<char> value, StringComparison comparison = StringComparison.Ordinal)
    {
        return span.EndsWith(value, comparison);
    }

#if !NETSTANDARD2_0
    /// <summary>
    /// Unsafe fast path for scanning characters in CSV parsing.
    /// Finds the next delimiter, quote, or line ending character.
    /// </summary>
    /// <param name="span">The span to search.</param>
    /// <param name="delimiter">The delimiter character.</param>
    /// <param name="quote">The quote character.</param>
    /// <returns>The index of the next special character, or -1 if not found.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe int FastScanForCsvSpecialChars(ReadOnlySpan<char> span, char delimiter, char quote)
    {
        if (span.IsEmpty)
            return -1;

        fixed (char* ptr = span)
        {
            char* current = ptr;
            char* end = ptr + span.Length;

            while (current < end)
            {
                char c = *current;
                if (c == delimiter || c == quote || c == '\r' || c == '\n')
                {
                    return (int)(current - ptr);
                }
                current++;
            }
        }

        return -1;
    }

    /// <summary>
    /// Unsafe fast copy for field data with bounds checking.
    /// </summary>
    /// <param name="source">Source span to copy from.</param>
    /// <param name="destination">Destination span to copy to.</param>
    /// <param name="length">Number of characters to copy.</param>
    /// <returns>The number of characters actually copied.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe int FastCopyChars(ReadOnlySpan<char> source, Span<char> destination, int length)
    {
        if (length <= 0)
            return 0;

        int copyLength = Math.Min(Math.Min(source.Length, destination.Length), length);
        if (copyLength == 0)
            return 0;

        fixed (char* srcPtr = source)
        fixed (char* dstPtr = destination)
        {
            char* src = srcPtr;
            char* dst = dstPtr;
            char* srcEnd = src + copyLength;

            // Copy in 8-character chunks when possible
            while (src + 8 <= srcEnd)
            {
                *(long*)dst = *(long*)src;
                *(long*)(dst + 4) = *(long*)(src + 4);
                src += 8;
                dst += 8;
            }

            // Copy remaining characters
            while (src < srcEnd)
            {
                *dst++ = *src++;
            }
        }

        return copyLength;
    }
#endif
}