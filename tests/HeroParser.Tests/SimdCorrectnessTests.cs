using Xunit;
using HeroParser.Simd;

namespace HeroParser.Tests;

/// <summary>
/// Verify that SIMD parsers produce identical results to scalar parser.
/// Critical for ensuring optimizations don't break correctness.
/// </summary>
public class SimdCorrectnessTests
{
    [Theory]
    [InlineData("a,b,c")]
    [InlineData("1,2,3,4,5,6,7,8,9,10")]
    [InlineData("")]
    [InlineData("single")]
    [InlineData("a,,c")]
    public void Avx512_MatchesScalar(string line)
    {
        if (!System.Runtime.Intrinsics.X86.Avx512F.IsSupported)
        {
            return; // Skip if no AVX-512
        }

        var scalarResult = ParseWithParser(line, ScalarParser.Instance);
        var avx512Result = ParseWithParser(line, Avx512Parser.Instance);

        AssertResultsEqual(scalarResult, avx512Result, "AVX-512");
    }

    [Theory]
    [InlineData("a,b,c")]
    [InlineData("1,2,3,4,5,6,7,8,9,10")]
    [InlineData("")]
    [InlineData("single")]
    [InlineData("a,,c")]
    public void Avx2_MatchesScalar(string line)
    {
        if (!System.Runtime.Intrinsics.X86.Avx2.IsSupported)
        {
            return; // Skip if no AVX2
        }

        var scalarResult = ParseWithParser(line, ScalarParser.Instance);
        var avx2Result = ParseWithParser(line, Avx2Parser.Instance);

        AssertResultsEqual(scalarResult, avx2Result, "AVX2");
    }

    [Fact]
    public void LongLine_AllParsersMatch()
    {
        // Generate line with >64 chars to test chunking
        var values = Enumerable.Range(0, 100).Select(i => $"val{i}");
        var line = string.Join(",", values);

        var scalarResult = ParseWithParser(line, ScalarParser.Instance);

        if (System.Runtime.Intrinsics.X86.Avx512F.IsSupported)
        {
            var avx512Result = ParseWithParser(line, Avx512Parser.Instance);
            AssertResultsEqual(scalarResult, avx512Result, "AVX-512");
        }

        if (System.Runtime.Intrinsics.X86.Avx2.IsSupported)
        {
            var avx2Result = ParseWithParser(line, Avx2Parser.Instance);
            AssertResultsEqual(scalarResult, avx2Result, "AVX2");
        }
    }

    private static (int[] starts, int[] lengths, int count) ParseWithParser(string line, ISimdParser parser)
    {
        var starts = new int[1000];
        var lengths = new int[1000];

        int count = parser.ParseColumns(line.AsSpan(), ',', starts, lengths);

        return (starts, lengths, count);
    }

    private static void AssertResultsEqual(
        (int[] starts, int[] lengths, int count) expected,
        (int[] starts, int[] lengths, int count) actual,
        string parserName)
    {
        Assert.Equal(expected.count, actual.count);

        for (int i = 0; i < expected.count; i++)
        {
            Assert.Equal(expected.starts[i], actual.starts[i]);
            Assert.Equal(expected.lengths[i], actual.lengths[i]);
        }
    }
}
