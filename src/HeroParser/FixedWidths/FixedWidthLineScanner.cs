using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;

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

    // Fixed-width UTF-8 readers must compare the encoded comment character,
    // not its truncated low byte (which can also match unrelated data such as NUL).
    public static bool StartsWithComment(ReadOnlySpan<byte> line, char commentChar)
    {
        if (line.IsEmpty || char.IsSurrogate(commentChar))
        {
            return false;
        }

        if (commentChar <= 0x7F)
        {
            return line[0] == (byte)commentChar;
        }

        Span<byte> utf8 = stackalloc byte[3];
        int byteCount = EncodeCommentCharacter(commentChar, utf8);
        return line.StartsWith(utf8[..byteCount]);
    }

    public static bool StartsWithComment(ReadOnlySequence<byte> line, char commentChar)
    {
        if (line.IsEmpty || char.IsSurrogate(commentChar))
        {
            return false;
        }

        if (commentChar <= 0x7F)
        {
            var reader = new SequenceReader<byte>(line);
            return reader.TryPeek(out byte first) && first == (byte)commentChar;
        }

        Span<byte> utf8 = stackalloc byte[3];
        int byteCount = EncodeCommentCharacter(commentChar, utf8);
        if (line.Length < byteCount)
        {
            return false;
        }

        Span<byte> prefix = stackalloc byte[3];
        line.Slice(0, byteCount).CopyTo(prefix);
        return prefix[..byteCount].SequenceEqual(utf8[..byteCount]);
    }

    private static int EncodeCommentCharacter(char commentChar, Span<byte> utf8)
    {
        // A non-surrogate BMP character requires at most three UTF-8 bytes.
        Span<char> character = stackalloc char[1];
        character[0] = commentChar;
        return Encoding.UTF8.GetBytes(character, utf8);
    }

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
