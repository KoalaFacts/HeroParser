using HeroParser.SeparatedValues.Reading.Shared;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace HeroParser.SeparatedValues.Writing;

internal readonly struct CsvWriterQuoting(
    char delimiter,
    char quote,
    IReadOnlySet<char>? additionalDangerousChars = null)
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal (bool needsQuoting, int quoteCount) AnalyzeFieldForQuoting(ReadOnlySpan<char> value)
    {
        if (HardwareCapabilities.Current.Avx512BWIsSupported && value.Length >= Vector512<ushort>.Count)
        {
            return AnalyzeFieldSimd512(value);
        }
        else if (Avx2.IsSupported && value.Length >= Vector256<ushort>.Count)
        {
            return AnalyzeFieldSimd256(value);
        }
        else if (Sse2.IsSupported && value.Length >= Vector128<ushort>.Count)
        {
            return AnalyzeFieldSimd128(value);
        }
        else if (Sse2.IsSupported && value.Length >= 4)
        {
            return AnalyzeFieldSimdShort(value);
        }

        return AnalyzeFieldScalar(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private (bool needsQuoting, int quoteCount) AnalyzeFieldSimdShort(ReadOnlySpan<char> value)
    {
        ref byte byteRef = ref Unsafe.As<char, byte>(ref MemoryMarshal.GetReference(value));
        ulong lo = Unsafe.ReadUnaligned<ulong>(ref byteRef);
        ulong hi = Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref byteRef, (value.Length - 4) * sizeof(char)));
        var chars = Vector128.Create(lo, hi).AsUInt16();

        var matchDelimiter = Vector128.Equals(chars, Vector128.Create((ushort)delimiter));
        var matchQuote = Vector128.Equals(chars, Vector128.Create((ushort)quote));
        var matchCr = Vector128.Equals(chars, Vector128.Create((ushort)'\r'));
        var matchLf = Vector128.Equals(chars, Vector128.Create((ushort)'\n'));

        var combined = Vector128.BitwiseOr(
            Vector128.BitwiseOr(matchDelimiter, matchQuote),
            Vector128.BitwiseOr(matchCr, matchLf));

        if (combined == Vector128<ushort>.Zero)
        {
            return (false, 0);
        }

        if (matchQuote == Vector128<ushort>.Zero)
        {
            return (true, 0);
        }

        ulong validBits = 0x0Ful | ((0xF0ul << (8 - value.Length)) & 0xF0ul);
        int quoteCount = BitOperations.PopCount(matchQuote.ExtractMostSignificantBits() & validBits);
        return (true, quoteCount);
    }

    private (bool needsQuoting, int quoteCount) AnalyzeFieldSimd512(ReadOnlySpan<char> value)
    {
        var delimiterVec = Vector512.Create((ushort)delimiter);
        var quoteVec = Vector512.Create((ushort)quote);
        var crVec = Vector512.Create((ushort)'\r');
        var lfVec = Vector512.Create((ushort)'\n');

        bool needsQuoting = false;
        int quoteCount = 0;
        int i = 0;
        int vectorLength = Vector512<ushort>.Count;
        int lastVectorStart = value.Length - vectorLength;

        while (i <= lastVectorStart)
        {
            var chars = Vector512.LoadUnsafe(ref Unsafe.As<char, ushort>(ref Unsafe.AsRef(in value[i])));

            var matchDelimiter = Vector512.Equals(chars, delimiterVec);
            var matchQuote = Vector512.Equals(chars, quoteVec);
            var matchCr = Vector512.Equals(chars, crVec);
            var matchLf = Vector512.Equals(chars, lfVec);

            var combined = Vector512.BitwiseOr(
                Vector512.BitwiseOr(matchDelimiter, matchQuote),
                Vector512.BitwiseOr(matchCr, matchLf));

            if (combined != Vector512<ushort>.Zero)
            {
                needsQuoting = true;
                if (matchQuote != Vector512<ushort>.Zero)
                {
                    quoteCount += BitOperations.PopCount(matchQuote.ExtractMostSignificantBits());
                }
            }

            i += vectorLength;
        }

        if (i < value.Length)
        {
            var chars = Vector512.LoadUnsafe(ref Unsafe.As<char, ushort>(ref Unsafe.AsRef(in value[lastVectorStart])));

            var matchDelimiter = Vector512.Equals(chars, delimiterVec);
            var matchQuote = Vector512.Equals(chars, quoteVec);
            var matchCr = Vector512.Equals(chars, crVec);
            var matchLf = Vector512.Equals(chars, lfVec);

            var combined = Vector512.BitwiseOr(
                Vector512.BitwiseOr(matchDelimiter, matchQuote),
                Vector512.BitwiseOr(matchCr, matchLf));

            if (combined != Vector512<ushort>.Zero)
            {
                needsQuoting = true;
                if (matchQuote != Vector512<ushort>.Zero)
                {
                    ulong tailQuotes = matchQuote.ExtractMostSignificantBits() >> (vectorLength - (value.Length - i));
                    quoteCount += BitOperations.PopCount(tailQuotes);
                }
            }
        }

        return (needsQuoting, quoteCount);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal int CountQuotes(ReadOnlySpan<char> value)
    {
        int count = 0;
        foreach (char c in value)
        {
            if (c == quote) count++;
        }
        return count;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private (bool needsQuoting, int quoteCount) AnalyzeFieldScalar(ReadOnlySpan<char> value)
    {
        bool needsQuoting = false;
        int quoteCount = 0;

        foreach (char c in value)
        {
            if (c == quote)
            {
                needsQuoting = true;
                quoteCount++;
            }
            else if (c == delimiter || c == '\r' || c == '\n')
            {
                needsQuoting = true;
            }
        }

        return (needsQuoting, quoteCount);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private (bool needsQuoting, int quoteCount) AnalyzeFieldSimd256(ReadOnlySpan<char> value)
    {
        var delimiterVec = Vector256.Create((ushort)delimiter);
        var quoteVec = Vector256.Create((ushort)quote);
        var crVec = Vector256.Create((ushort)'\r');
        var lfVec = Vector256.Create((ushort)'\n');

        bool needsQuoting = false;
        int quoteCount = 0;
        int i = 0;
        int vectorLength = Vector256<ushort>.Count;
        int lastVectorStart = value.Length - vectorLength;

        while (i <= lastVectorStart)
        {
            var chars = Vector256.LoadUnsafe(ref Unsafe.As<char, ushort>(ref Unsafe.AsRef(in value[i])));

            var matchDelimiter = Vector256.Equals(chars, delimiterVec);
            var matchQuote = Vector256.Equals(chars, quoteVec);
            var matchCr = Vector256.Equals(chars, crVec);
            var matchLf = Vector256.Equals(chars, lfVec);

            var combined = Vector256.BitwiseOr(
                Vector256.BitwiseOr(matchDelimiter, matchQuote),
                Vector256.BitwiseOr(matchCr, matchLf));

            if (combined != Vector256<ushort>.Zero)
            {
                needsQuoting = true;
                if (matchQuote != Vector256<ushort>.Zero)
                {
                    quoteCount += BitOperations.PopCount(matchQuote.ExtractMostSignificantBits());
                }
            }

            i += vectorLength;
        }

        if (i < value.Length)
        {
            var chars = Vector256.LoadUnsafe(ref Unsafe.As<char, ushort>(ref Unsafe.AsRef(in value[lastVectorStart])));

            var matchDelimiter = Vector256.Equals(chars, delimiterVec);
            var matchQuote = Vector256.Equals(chars, quoteVec);
            var matchCr = Vector256.Equals(chars, crVec);
            var matchLf = Vector256.Equals(chars, lfVec);

            var combined = Vector256.BitwiseOr(
                Vector256.BitwiseOr(matchDelimiter, matchQuote),
                Vector256.BitwiseOr(matchCr, matchLf));

            if (combined != Vector256<ushort>.Zero)
            {
                needsQuoting = true;
                if (matchQuote != Vector256<ushort>.Zero)
                {
                    ulong tailQuotes = matchQuote.ExtractMostSignificantBits() >> (vectorLength - (value.Length - i));
                    quoteCount += BitOperations.PopCount(tailQuotes);
                }
            }
        }

        return (needsQuoting, quoteCount);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private (bool needsQuoting, int quoteCount) AnalyzeFieldSimd128(ReadOnlySpan<char> value)
    {
        var delimiterVec = Vector128.Create((ushort)delimiter);
        var quoteVec = Vector128.Create((ushort)quote);
        var crVec = Vector128.Create((ushort)'\r');
        var lfVec = Vector128.Create((ushort)'\n');

        bool needsQuoting = false;
        int quoteCount = 0;
        int i = 0;
        int vectorLength = Vector128<ushort>.Count;
        int lastVectorStart = value.Length - vectorLength;

        while (i <= lastVectorStart)
        {
            var chars = Vector128.LoadUnsafe(ref Unsafe.As<char, ushort>(ref Unsafe.AsRef(in value[i])));

            var matchDelimiter = Vector128.Equals(chars, delimiterVec);
            var matchQuote = Vector128.Equals(chars, quoteVec);
            var matchCr = Vector128.Equals(chars, crVec);
            var matchLf = Vector128.Equals(chars, lfVec);

            var combined = Vector128.BitwiseOr(
                Vector128.BitwiseOr(matchDelimiter, matchQuote),
                Vector128.BitwiseOr(matchCr, matchLf));

            if (combined != Vector128<ushort>.Zero)
            {
                needsQuoting = true;
                if (matchQuote != Vector128<ushort>.Zero)
                {
                    quoteCount += BitOperations.PopCount(matchQuote.ExtractMostSignificantBits());
                }
            }

            i += vectorLength;
        }

        if (i < value.Length)
        {
            var chars = Vector128.LoadUnsafe(ref Unsafe.As<char, ushort>(ref Unsafe.AsRef(in value[lastVectorStart])));

            var matchDelimiter = Vector128.Equals(chars, delimiterVec);
            var matchQuote = Vector128.Equals(chars, quoteVec);
            var matchCr = Vector128.Equals(chars, crVec);
            var matchLf = Vector128.Equals(chars, lfVec);

            var combined = Vector128.BitwiseOr(
                Vector128.BitwiseOr(matchDelimiter, matchQuote),
                Vector128.BitwiseOr(matchCr, matchLf));

            if (combined != Vector128<ushort>.Zero)
            {
                needsQuoting = true;
                if (matchQuote != Vector128<ushort>.Zero)
                {
                    ulong tailQuotes = matchQuote.ExtractMostSignificantBits() >> (vectorLength - (value.Length - i));
                    quoteCount += BitOperations.PopCount(tailQuotes);
                }
            }
        }

        return (needsQuoting, quoteCount);
    }

    // Default dangerous characters for CSV injection (always dangerous)
    // Note: '-' and '+' are handled separately with smart detection
    private static ReadOnlySpan<char> AlwaysDangerousChars => ['=', '@', '\t', '\r'];

    /// <summary>
    /// Checks if a field starts with a dangerous character that could trigger formula execution.
    /// Uses switch statement for O(1) lookup of common dangerous characters.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool IsDangerousField(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty) return false;

        char first = value[0];

        switch (first)
        {
            // Always-dangerous characters
            case '=':
            case '@':
            case '\t':
            case '\r':
                return true;

            // Smart detection for '-' and '+':
            // - If followed by digit or '.', it's likely a number/phone number (safe)
            // - If followed by letter or '(', it's likely a formula (dangerous)
            case '-':
            case '+':
                // Single character is safe
                if (value.Length == 1) return false;
                char second = value[1];
                // Safe patterns: -123, +1-555, -.5, +.5
                // Use uint comparison to check digit range in one instruction
                return !((uint)(second - '0') <= 9 || second == '.');

            default:
                // Check additional custom dangerous characters
                return additionalDangerousChars is not null && additionalDangerousChars.Contains(first);
        }
    }

    /// <summary>
    /// Strips dangerous leading characters from a value.
    /// </summary>
    internal ReadOnlySpan<char> StripDangerousPrefix(ReadOnlySpan<char> value)
    {
        int start = 0;
        while (start < value.Length)
        {
            char c = value[start];
            // Strip always-dangerous chars, plus '-' and '+' that aren't followed by digit/decimal
            bool isDangerous = AlwaysDangerousChars.Contains(c) ||
                               (additionalDangerousChars is not null && additionalDangerousChars.Contains(c));

            // Smart handling for '-' and '+': check if followed by digit or decimal (safe pattern)
            if (!isDangerous && (c == '-' || c == '+') && start + 1 < value.Length)
            {
                char next = value[start + 1];
                isDangerous = !char.IsDigit(next) && next != '.';
            }

            if (!isDangerous)
            {
                break;
            }
            start++;
        }
        return value[start..];
    }
}
