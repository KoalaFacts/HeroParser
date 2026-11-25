using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;

namespace HeroParser.SeparatedValues.Reading.Shared;

internal readonly struct CsvPipeColumnText
{
    private const int STACK_BYTE_LIMIT = 512;
    private const int STACK_CHAR_LIMIT = 256;

    private readonly byte quoteByte;
    private readonly byte? escapeByte;
    private readonly char quoteChar;
    private readonly char? escapeChar;
    private readonly bool enableQuotedFields;

    public CsvPipeColumnText(byte quote, byte? escape, bool enableQuotedFields = true)
    {
        quoteByte = quote;
        escapeByte = escape;
        quoteChar = (char)quote;
        escapeChar = escape.HasValue ? (char)escape.Value : null;
        this.enableQuotedFields = enableQuotedFields;
    }

    public CsvPipeColumnText(char quote, char? escape, bool enableQuotedFields = true)
    {
        quoteByte = (byte)quote;
        escapeByte = escape.HasValue ? (byte)escape.Value : null;
        quoteChar = quote;
        escapeChar = escape;
        this.enableQuotedFields = enableQuotedFields;
    }

    public string Decode(ReadOnlySequence<byte> value)
    {
        if (value.IsSingleSegment)
        {
            return Decode(value.FirstSpan);
        }

        int length = checked((int)value.Length);
        byte[]? rented = null;
        Span<byte> bytes = length <= STACK_BYTE_LIMIT
            ? stackalloc byte[length]
            : (rented = ArrayPool<byte>.Shared.Rent(length));
        try
        {
            value.CopyTo(bytes);
            return Decode(bytes[..length]);
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string Decode(ReadOnlySpan<byte> value)
    {
        if (enableQuotedFields && value.Length >= 2 && value[0] == quoteByte && value[^1] == quoteByte)
        {
            value = value[1..^1];
        }

        if (value.IsEmpty)
        {
            return string.Empty;
        }

        bool hasSpecialCharacters = (escapeByte is byte esc && value.Contains(esc)) ||
            (enableQuotedFields && value.Contains(quoteByte));
        if (!hasSpecialCharacters)
        {
            return Encoding.UTF8.GetString(value);
        }

        return DecodeEscaped(value);
    }

    private string DecodeEscaped(ReadOnlySpan<byte> value)
    {
        char[]? rented = null;
        // UTF-8 decoding with the default replacement fallback produces at most one char per byte.
        Span<char> chars = value.Length <= STACK_CHAR_LIMIT
            ? stackalloc char[value.Length]
            : (rented = ArrayPool<char>.Shared.Rent(value.Length));
        try
        {
            int count = Encoding.UTF8.GetChars(value, chars);
            int written = 0;
            // Decode first so escape removal cannot join invalid UTF-8 bytes into a different character.
            for (int i = 0; i < count; i++)
            {
                char current = chars[i];
                if (current == escapeChar && i + 1 < count)
                {
                    current = chars[++i];
                }
                else if (enableQuotedFields && current == quoteChar && i + 1 < count && chars[i + 1] == quoteChar)
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string Decode(ReadOnlySpan<char> value)
    {
        if (enableQuotedFields && value.Length >= 2 && value[0] == quoteChar && value[^1] == quoteChar)
            value = value[1..^1];

        bool hasSpecialCharacters = (escapeChar is char esc && value.Contains(esc)) ||
            (enableQuotedFields && value.Contains(quoteChar));
        if (!hasSpecialCharacters)
            return new string(value);

        return DecodeEscaped(value);
    }

    private string DecodeEscaped(ReadOnlySpan<char> value)
    {
        char[]? rented = null;
        Span<char> chars = value.Length <= STACK_CHAR_LIMIT
            ? stackalloc char[value.Length]
            : (rented = ArrayPool<char>.Shared.Rent(value.Length));
        try
        {
            int written = 0;
            for (int i = 0; i < value.Length; i++)
            {
                char current = value[i];
                if (current == escapeChar && i + 1 < value.Length)
                    current = value[++i];
                else if (enableQuotedFields && current == quoteChar && i + 1 < value.Length && value[i + 1] == quoteChar)
                    i++;
                chars[written++] = current;
            }
            return new string(chars[..written]);
        }
        finally
        {
            if (rented is not null)
                ArrayPool<char>.Shared.Return(rented);
        }
    }
}
