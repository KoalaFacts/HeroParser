using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace HeroParser.SeparatedValues.Writing;

internal static class CsvWriterQuoting
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static (bool needsQuoting, int quoteCount) AnalyzeFieldForQuoting(
        ReadOnlySpan<char> value,
        char delimiter,
        char quote)
    {
        if (Avx2.IsSupported && value.Length >= Vector256<ushort>.Count)
        {
            return AnalyzeFieldSimd256(value, delimiter, quote);
        }
        else if (Sse2.IsSupported && value.Length >= Vector128<ushort>.Count)
        {
            return AnalyzeFieldSimd128(value, delimiter, quote);
        }

        return AnalyzeFieldScalar(value, delimiter, quote);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CountQuotes(ReadOnlySpan<char> value, char quote)
    {
        int count = 0;
        foreach (char c in value)
        {
            if (c == quote) count++;
        }
        return count;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (bool needsQuoting, int quoteCount) AnalyzeFieldScalar(
        ReadOnlySpan<char> value,
        char delimiter,
        char quote)
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

    private static (bool needsQuoting, int quoteCount) AnalyzeFieldSimd256(
        ReadOnlySpan<char> value,
        char delimiter,
        char quote)
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

        for (; i < value.Length; i++)
        {
            char c = value[i];
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

    private static (bool needsQuoting, int quoteCount) AnalyzeFieldSimd128(
        ReadOnlySpan<char> value,
        char delimiter,
        char quote)
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

        for (; i < value.Length; i++)
        {
            char c = value[i];
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
}
