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
                if (i + 1 < span.Length && span[i + 1] == LF)
                    i++;
            }
        }
        return count;
    }

    public static int CountNewlines(ReadOnlySpan<char> span)
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
                    i++;
            }
        }
        return count;
    }
}
