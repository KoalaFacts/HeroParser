using Xunit;
using HeroParser.Simd;

namespace HeroParser.Tests;

/// <summary>
/// Direct tests of individual parser implementations.
/// </summary>
public class ParserTests
{
    [Fact]
    public void ScalarParser_BasicLine_Works()
    {
        var parser = ScalarParser.Instance;
        var starts = new int[10];
        var lengths = new int[10];

        int count = parser.ParseColumns("a,b,c".AsSpan(), ',', starts, lengths);

        Assert.Equal(3, count);
        Assert.Equal(0, starts[0]);
        Assert.Equal(1, lengths[0]);
        Assert.Equal(2, starts[1]);
        Assert.Equal(1, lengths[1]);
        Assert.Equal(4, starts[2]);
        Assert.Equal(1, lengths[2]);
    }

    [Fact]
    public void ScalarParser_EmptyFields_Works()
    {
        var parser = ScalarParser.Instance;
        var starts = new int[10];
        var lengths = new int[10];

        int count = parser.ParseColumns("a,,c".AsSpan(), ',', starts, lengths);

        Assert.Equal(3, count);
        Assert.Equal(0, lengths[1]); // Empty field
    }

    [Fact]
    public void ScalarParser_NoDelimiters_Works()
    {
        var parser = ScalarParser.Instance;
        var starts = new int[10];
        var lengths = new int[10];

        int count = parser.ParseColumns("abc".AsSpan(), ',', starts, lengths);

        Assert.Equal(1, count);
        Assert.Equal(0, starts[0]);
        Assert.Equal(3, lengths[0]);
    }

    [Fact]
    public void ScalarParser_EmptyLine_Works()
    {
        var parser = ScalarParser.Instance;
        var starts = new int[10];
        var lengths = new int[10];

        int count = parser.ParseColumns("".AsSpan(), ',', starts, lengths);

        Assert.Equal(0, count);
    }

    [Fact]
    public void SimdParserFactory_ReturnsParser()
    {
        var parser = SimdParserFactory.GetParser();
        Assert.NotNull(parser);
    }

    [Fact]
    public void SimdParserFactory_GetHardwareInfo_ReturnsString()
    {
        var info = SimdParserFactory.GetHardwareInfo();
        Assert.NotNull(info);
        Assert.NotEmpty(info);
        Assert.Contains("Using:", info);
    }

    [Fact]
    public void Avx512Parser_Available_WhenSupported()
    {
        if (System.Runtime.Intrinsics.X86.Avx512F.IsSupported &&
            System.Runtime.Intrinsics.X86.Avx512BW.IsSupported)
        {
            var parser = Avx512Parser.Instance;
            Assert.NotNull(parser);

            var starts = new int[10];
            var lengths = new int[10];
            int count = parser.ParseColumns("a,b,c".AsSpan(), ',', starts, lengths);

            Assert.Equal(3, count);
        }
    }

    [Fact]
    public void Avx2Parser_Available_WhenSupported()
    {
        if (System.Runtime.Intrinsics.X86.Avx2.IsSupported)
        {
            var parser = Avx2Parser.Instance;
            Assert.NotNull(parser);

            var starts = new int[10];
            var lengths = new int[10];
            int count = parser.ParseColumns("a,b,c".AsSpan(), ',', starts, lengths);

            Assert.Equal(3, count);
        }
    }

    [Fact]
    public void NeonParser_Available_WhenSupported()
    {
        if (System.Runtime.Intrinsics.Arm.AdvSimd.IsSupported)
        {
            var parser = NeonParser.Instance;
            Assert.NotNull(parser);

            var starts = new int[10];
            var lengths = new int[10];
            int count = parser.ParseColumns("a,b,c".AsSpan(), ',', starts, lengths);

            Assert.Equal(3, count);
        }
    }
}
