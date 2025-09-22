using System.Runtime.CompilerServices;

namespace HeroParser.Core;

/// <summary>
/// High-performance string operations using Span for zero-allocation parsing.
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

#if NET6_0_OR_GREATER
    /// <summary>
    /// Hardware capabilities detection for SIMD optimizations.
    /// </summary>
    public static class HardwareCapabilities
    {
        /// <summary>
        /// True if AVX2 instructions are supported.
        /// </summary>
        public static readonly bool SupportsAvx2 = System.Runtime.Intrinsics.X86.Avx2.IsSupported;

        /// <summary>
        /// True if SSE2 instructions are supported.
        /// </summary>
        public static readonly bool SupportsSse2 = System.Runtime.Intrinsics.X86.Sse2.IsSupported;

        /// <summary>
        /// The optimal vector size in bytes for this hardware.
        /// </summary>
        public static readonly int OptimalVectorSize = SupportsAvx2 ? 32 : SupportsSse2 ? 16 : 0;
    }

    /// <summary>
    /// Fast scanning for CSV special characters with fallback implementation.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe int FastScanForCsvSpecialChars(ReadOnlySpan<char> span, char delimiter, char quote)
    {
        if (span.IsEmpty)
            return -1;

        // For now, use scalar fallback (SIMD implementation can be added later)
        return FastScanForCsvSpecialCharsScalar(span, delimiter, quote);
    }

    /// <summary>
    /// Scalar fallback for CSV special character scanning.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe int FastScanForCsvSpecialCharsScalar(ReadOnlySpan<char> span, char delimiter, char quote)
    {
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
#else
    /// <summary>
    /// Hardware capabilities fallback for older frameworks.
    /// </summary>
    public static class HardwareCapabilities
    {
        /// <summary>
        /// Always false on older frameworks.
        /// </summary>
        public static readonly bool SupportsAvx2 = false;

        /// <summary>
        /// Always false on older frameworks.
        /// </summary>
        public static readonly bool SupportsSse2 = false;

        /// <summary>
        /// Always 0 on older frameworks.
        /// </summary>
        public static readonly int OptimalVectorSize = 0;
    }

    /// <summary>
    /// Fast scanning fallback for older frameworks.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int FastScanForCsvSpecialChars(ReadOnlySpan<char> span, char delimiter, char quote)
    {
        for (int i = 0; i < span.Length; i++)
        {
            char c = span[i];
            if (c == delimiter || c == quote || c == '\r' || c == '\n')
            {
                return i;
            }
        }
        return -1;
    }
#endif
}