using System.Buffers;
using System.Runtime.CompilerServices;

namespace HeroParser.FixedWidths;

internal static class FixedWidthLineScanner
{
    private const byte CR = (byte)'\r';
    private const byte LF = (byte)'\n';

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool ContainsLineBreak(ReadOnlySpan<char> span)
        => span.IndexOfAny('\r', '\n') >= 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int FindLineEnd(ReadOnlySpan<byte> span)
    {
        for (int i = 0; i < span.Length; i++)
        {
            if (span[i] == CR || span[i] == LF)
                return i;
        }
        return -1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int FindLineEnd(ReadOnlySpan<char> span)
        => span.IndexOfAny('\r', '\n');

    public static int CountNewlines(ReadOnlySpan<byte> span)
    {
        bool previousWasCr = false;
        return CountNewlines(span, ref previousWasCr);
    }

    // A trailing CR counts immediately; a leading LF in the next chunk belongs to that CRLF.
    public static int CountNewlines(ReadOnlySpan<byte> span, ref bool previousWasCr)
    {
        if (span.IsEmpty)
        {
            return 0;
        }

        int count = 0;
        int start = previousWasCr && span[0] == LF ? 1 : 0;
        for (int i = start; i < span.Length; i++)
        {
            if (span[i] == LF)
            {
                count++;
            }
            else if (span[i] == CR)
            {
                count++;
                if (i + 1 < span.Length && span[i + 1] == LF)
                {
                    i++;
                }
            }
        }

        previousWasCr = span[^1] == CR;
        return count;
    }

    public static int CountNewlines(ReadOnlySequence<byte> sequence)
    {
        bool previousWasCr = false;
        return CountNewlines(sequence, ref previousWasCr);
    }

    public static int CountNewlines(ReadOnlySequence<byte> sequence, ref bool previousWasCr)
    {
        int count = 0;
        foreach (var segment in sequence)
        {
            count += CountNewlines(segment.Span, ref previousWasCr);
        }

        return count;
    }

    public static int CountNewlines(ReadOnlySpan<char> span)
    {
        bool previousWasCr = false;
        return CountNewlines(span, ref previousWasCr);
    }

    public static int CountNewlines(ReadOnlySpan<char> span, ref bool previousWasCr)
    {
        if (span.IsEmpty)
        {
            return 0;
        }

        int count = 0;
        int start = previousWasCr && span[0] == '\n' ? 1 : 0;
        for (int i = start; i < span.Length; i++)
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
                    i++;
                }
            }
        }

        previousWasCr = span[^1] == '\r';
        return count;
    }
}
