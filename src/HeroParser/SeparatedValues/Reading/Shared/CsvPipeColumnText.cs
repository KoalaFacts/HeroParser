using System.Buffers;
using System.Text;

namespace HeroParser.SeparatedValues.Reading.Shared;

internal static class CsvPipeColumnText
{
    private const int STACK_BYTE_LIMIT = 512;
    private const int STACK_CHAR_LIMIT = 256;

    public static string Decode(ReadOnlySequence<byte> value, byte quote, byte? escape)
    {
        if (value.IsSingleSegment)
        {
            return Decode(value.FirstSpan, quote, escape);
        }

        int length = checked((int)value.Length);
        byte[]? rented = null;
        Span<byte> bytes = length <= STACK_BYTE_LIMIT
            ? stackalloc byte[length]
            : (rented = ArrayPool<byte>.Shared.Rent(length));
        try
        {
            value.CopyTo(bytes);
            return Decode(bytes[..length], quote, escape);
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }

    public static string Decode(ReadOnlySpan<byte> value, byte quote, byte? escape)
    {
        if (value.Length >= 2 && value[0] == quote && value[^1] == quote)
        {
            value = value[1..^1];
        }

        if (value.IsEmpty)
        {
            return string.Empty;
        }

        bool hasSpecialCharacters = escape is byte escapeByte
            ? value.IndexOfAny(quote, escapeByte) >= 0
            : value.Contains(quote);
        if (!hasSpecialCharacters)
        {
            return Encoding.UTF8.GetString(value);
        }

        char[]? rented = null;
        // UTF-8 decoding with the default replacement fallback produces at most one char per byte.
        Span<char> chars = value.Length <= STACK_CHAR_LIMIT
            ? stackalloc char[value.Length]
            : (rented = ArrayPool<char>.Shared.Rent(value.Length));
        try
        {
            int count = Encoding.UTF8.GetChars(value, chars);
            int written = 0;
            char quoteChar = (char)quote;
            char? escapeChar = escape.HasValue ? (char)escape.Value : null;
            // Decode first so escape removal cannot join invalid UTF-8 bytes into a different character.
            for (int i = 0; i < count; i++)
            {
                char current = chars[i];
                if (current == escapeChar && i + 1 < count)
                {
                    current = chars[++i];
                }
                else if (current == quoteChar && i + 1 < count && chars[i + 1] == quoteChar)
                {
                    i++;
                }

                chars[written++] = current;
            }

            return new string(chars[..written]);
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<char>.Shared.Return(rented);
            }
        }
    }
}
