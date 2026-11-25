using System.Buffers;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace HeroParser.Internal;

/// <summary>
/// Culture-aware number parsing that also accepts the ASCII hyphen-minus as the negative sign when the culture's negative sign is a minus decorated with
/// bidirectional marks (fa-IR <c>U+200E U+2212</c>, ar-SA <c>U+061C -</c>, he-IL <c>U+200E -</c>).
/// </summary>
/// <remarks>
/// Generated binders parse integer columns invariantly, so they already accept <c>-1</c> on every culture; .NET itself
/// accepts <c>-</c> for cultures whose sign is a bare U+2212. Without this fallback, <c>-1.5</c> failed to parse under
/// the cultures above while <c>-1</c> parsed, and .xlsx numeric cells (stored as <c>-1.5</c>) could not be read with them.
/// The fallback runs only after the culture parse fails, so the common path is unchanged.
/// </remarks>
internal readonly struct CultureNumberParser(IFormatProvider? provider)
{
    private const int STACK_ALLOC_CHAR_LIMIT = 256;

    /// <summary>Parses <paramref name="chars"/> with the configured provider, then retries with the culture's negative sign in place of <c>-</c>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryParse<TNumber>(ReadOnlySpan<char> chars, NumberStyles styles, out TNumber result)
        where TNumber : INumberBase<TNumber>
    {
        if (TNumber.TryParse(chars, styles, provider, out result!))
            return true;

        return TryParseWithAsciiMinus(chars, styles, out result);
    }

    /// <summary>
    /// Parses like <see cref="TryParse{TNumber}"/>; when neither form parses, throws the exception the culture parse throws.
    /// </summary>
    public TNumber Parse<TNumber>(ReadOnlySpan<char> chars, NumberStyles styles)
        where TNumber : INumberBase<TNumber>
        => TryParse(chars, styles, out TNumber result) ? result : TNumber.Parse(chars, styles, provider);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool TryParseWithAsciiMinus<TNumber>(ReadOnlySpan<char> chars, NumberStyles styles, out TNumber result)
        where TNumber : INumberBase<TNumber>
    {
        result = TNumber.Zero;
        int hyphens = chars.Count('-');
        if (hyphens == 0)
            return false;

        string sign = NumberFormatInfo.GetInstance(provider).NegativeSign;
        if (!IsMarkedMinus(sign))
            return false;

        int length = chars.Length + (hyphens * (sign.Length - 1));
        char[]? rented = null;
        Span<char> buffer = length <= STACK_ALLOC_CHAR_LIMIT
            ? stackalloc char[STACK_ALLOC_CHAR_LIMIT]
            : (rented = ArrayPool<char>.Shared.Rent(length));
        try
        {
            int written = 0;
            foreach (char c in chars)
            {
                if (c == '-')
                {
                    sign.AsSpan().CopyTo(buffer[written..]);
                    written += sign.Length;
                }
                else
                {
                    buffer[written++] = c;
                }
            }

            return TNumber.TryParse(buffer[..written], styles, provider, out result!);
        }
        finally
        {
            if (rented is not null)
                ArrayPool<char>.Shared.Return(rented);
        }
    }

    /// <summary>
    /// True when <paramref name="sign"/> is not <c>-</c> itself but is <c>-</c> or U+2212 with only bidirectional marks
    /// (U+200E, U+200F, U+061C) around it.
    /// </summary>
    private static bool IsMarkedMinus(string sign)
    {
        if (sign.Length < 2)
            return false;

        int minus = 0;
        foreach (char c in sign)
        {
            switch (c)
            {
                case '-' or '\u2212':
                    minus++;
                    break;
                case '\u200E' or '\u200F' or '\u061C':
                    break;
                default:
                    return false;
            }
        }

        return minus == 1;
    }
}
