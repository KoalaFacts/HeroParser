using System.Text;
using HeroParser.FixedWidths;
using HeroParser.FixedWidths.Streaming;
using Xunit;

namespace HeroParser.Tests.Internal;

/// <summary>
/// Covers the async fixed-width writer's boxed-value dispatch.
///
/// It is the async twin of the synchronous writer's switch, and the two have to agree:
/// the same record written through either path must produce the same bytes. Neither
/// arm of the async one had run.
/// </summary>
[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
public class FixedWidthAsyncStreamWriterValueTests
{
    private static async Task<string> WriteAsync(object? value, int width = 20, string? format = null, FieldAlignment? alignment = null, char? padChar = null)
    {
        var ct = TestContext.Current.CancellationToken;
        using var stream = new MemoryStream();
        await using (var writer = new FixedWidthAsyncStreamWriter(stream, leaveOpen: true))
        {
            await writer.WriteFieldAsync(value, width, alignment, padChar, format, ct);
            await writer.EndRowAsync(ct);
        }
        return Encoding.UTF8.GetString(stream.ToArray()).TrimEnd('\r', '\n');
    }

    [Theory]
    [InlineData("abc", "abc")]
    [InlineData(42, "42")]
    [InlineData(9007199254740993L, "9007199254740993")]
    [InlineData(1.5d, "1.5")]
    [InlineData(2.5f, "2.5")]
    [InlineData(true, "True")]
    [InlineData(false, "False")]
    [InlineData((byte)7, "7")]
    [InlineData((short)-3, "-3")]
    [InlineData(8u, "8")]
    [InlineData(9ul, "9")]
    public async Task PrimitiveTypes_LandOnTheirOwnSwitchArm(object value, string expected)
        => Assert.Equal(expected, (await WriteAsync(value)).TrimEnd());

    [Fact]
    public async Task Null_UsesTheConfiguredNullValue()
        => Assert.Equal(string.Empty, (await WriteAsync(null)).TrimEnd());

    [Fact]
    public async Task Decimal_IsFormatted() => Assert.Equal("3.25", (await WriteAsync(3.25m)).TrimEnd());

    [Fact]
    public async Task DateTime_HonoursAFormat()
        => Assert.Equal("2024-03-17", (await WriteAsync(new DateTime(2024, 3, 17, 0, 0, 0, DateTimeKind.Unspecified), format: "yyyy-MM-dd")).TrimEnd());

    [Fact]
    public async Task DateTimeOffset_HonoursAFormat()
        => Assert.Equal("2024-03-17", (await WriteAsync(new DateTimeOffset(2024, 3, 17, 0, 0, 0, TimeSpan.Zero), format: "yyyy-MM-dd")).TrimEnd());

    [Fact]
    public async Task Guid_IsFormatted()
    {
        var value = new Guid("11112222-3333-4444-5555-666677778888");
        Assert.Equal(value.ToString("N"), (await WriteAsync(value, width: 40, format: "N")).TrimEnd());
    }

    [Fact]
    public async Task SpanFormattableType_UsesTheSpanPath()
        => Assert.Equal("01:02:03", (await WriteAsync(new TimeSpan(1, 2, 3))).TrimEnd());

    [Fact]
    public async Task FormattableType_UsesItsFormat()
        => Assert.Equal("formatted:X", (await WriteAsync(new OnlyFormattable(), format: "X")).TrimEnd());

    [Fact]
    public async Task PlainObject_FallsBackToToString()
        => Assert.Equal("plain", (await WriteAsync(new PlainObject())).TrimEnd());

    [Fact]
    public async Task Alignment_AndPadCharacterAreHonoured()
        => Assert.Equal("00000000abc", await WriteAsync("abc", width: 11, alignment: FieldAlignment.Right, padChar: '0'));

    [Fact]
    public async Task StringOverload_PadsToTheWidth()
    {
        var ct = TestContext.Current.CancellationToken;
        using var stream = new MemoryStream();
        await using (var writer = new FixedWidthAsyncStreamWriter(stream, leaveOpen: true))
        {
            await writer.WriteFieldAsync("ab", 5, ct);
            await writer.WriteFieldAsync("cd", 5, FieldAlignment.Right, ct);
            await writer.WriteFieldAsync("ef".AsMemory(), 5, FieldAlignment.Left, '.', ct);
            await writer.EndRowAsync(ct);
        }

        Assert.Equal("ab      cdef...", Encoding.UTF8.GetString(stream.ToArray()).TrimEnd('\r', '\n'));
    }

    private sealed class OnlyFormattable : IFormattable
    {
        public string ToString(string? format, IFormatProvider? formatProvider) => $"formatted:{format}";
    }

    private sealed class PlainObject
    {
        public override string ToString() => "plain";
    }
}
