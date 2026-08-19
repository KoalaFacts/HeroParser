// See AnsiColorAndStyleTests for why the product's Color must be aliased here.
using System.Globalization;
using AnsiBuf = HeroParser.Console.AnsiBuffer;
using AnsiColor = HeroParser.Console.Color;
using AnsiStyle = HeroParser.Console.Style;
using Xunit;

namespace HeroParser.Tests.ConsoleUi;

/// <summary>
/// Covers the boundary behaviour of the console primitives: buffer refills, the named
/// colour table, and the "destination too small" paths every FormatAnsi overload guards
/// with. Those guards exist so a caller can never overrun a stack buffer, which makes
/// them worth pinning even though the library's own call sites always size generously.
/// </summary>
[Trait("Category", "Unit")]
public class ConsolePrimitiveEdgeCaseTests
{
    [Fact]
    public void AnsiBuffer_FreeCapacity_ShrinksAsItFills()
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        Span<char> scratch = new char[8];
        var buffer = new AnsiBuf(scratch, writer);

        Assert.Equal(8, buffer.FreeCapacity);
        buffer.Write('a');
        Assert.Equal(7, buffer.FreeCapacity);
    }

    [Fact]
    public void AnsiBuffer_WriteChar_FlushesWhenFull()
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        Span<char> scratch = new char[4];
        var buffer = new AnsiBuf(scratch, writer);

        for (int i = 0; i < 10; i++) buffer.Write((char)('0' + i));
        buffer.Flush();

        Assert.Equal("0123456789", writer.ToString());
    }

    [Fact]
    public void AnsiBuffer_WriteStyled_SpansMultipleBufferFills()
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        Span<char> scratch = new char[16];
        var buffer = new AnsiBuf(scratch, writer);

        // Longer than the buffer, so the copy loop has to flush and continue.
        string text = new('z', 100);
        buffer.WriteStyled(text.AsSpan(), AnsiStyle.Default);
        buffer.Flush();

        Assert.Equal(text, writer.ToString());
    }

    [Theory]
    [InlineData(ConsoleColor.Black, 30)]
    [InlineData(ConsoleColor.DarkRed, 31)]
    [InlineData(ConsoleColor.DarkGreen, 32)]
    [InlineData(ConsoleColor.DarkYellow, 33)]
    [InlineData(ConsoleColor.DarkBlue, 34)]
    [InlineData(ConsoleColor.DarkMagenta, 35)]
    [InlineData(ConsoleColor.DarkCyan, 36)]
    [InlineData(ConsoleColor.Gray, 37)]
    [InlineData(ConsoleColor.DarkGray, 90)]
    [InlineData(ConsoleColor.Red, 91)]
    [InlineData(ConsoleColor.Green, 92)]
    [InlineData(ConsoleColor.Yellow, 93)]
    [InlineData(ConsoleColor.Blue, 94)]
    [InlineData(ConsoleColor.Magenta, 95)]
    [InlineData(ConsoleColor.Cyan, 96)]
    [InlineData(ConsoleColor.White, 97)]
    public void Color_FromConsoleColor_UsesTheSgrCodeForThatColor(ConsoleColor color, int expected)
        => Assert.Equal(expected.ToString(CultureInfo.InvariantCulture), Format(AnsiColor.FromConsoleColor(color)));

    [Fact]
    public void Color_UnknownConsoleColor_FallsBackToDefaultForeground()
    {
        // 39 is the SGR "default foreground" code; an out-of-range value must land there
        // rather than emitting a nonsense sequence.
        Assert.Equal("39", Format(AnsiColor.FromConsoleColor((ConsoleColor)99)));
    }

    [Theory]
    [InlineData(nameof(AnsiColor.Black), 30)]
    [InlineData(nameof(AnsiColor.DarkRed), 31)]
    [InlineData(nameof(AnsiColor.DarkGreen), 32)]
    [InlineData(nameof(AnsiColor.DarkYellow), 33)]
    [InlineData(nameof(AnsiColor.DarkBlue), 34)]
    [InlineData(nameof(AnsiColor.DarkMagenta), 35)]
    [InlineData(nameof(AnsiColor.DarkCyan), 36)]
    [InlineData(nameof(AnsiColor.Gray), 37)]
    [InlineData(nameof(AnsiColor.DarkGray), 90)]
    [InlineData(nameof(AnsiColor.Red), 91)]
    [InlineData(nameof(AnsiColor.Green), 92)]
    [InlineData(nameof(AnsiColor.Yellow), 93)]
    [InlineData(nameof(AnsiColor.Blue), 94)]
    [InlineData(nameof(AnsiColor.Magenta), 95)]
    [InlineData(nameof(AnsiColor.Cyan), 96)]
    [InlineData(nameof(AnsiColor.White), 97)]
    public void Color_NamedProperties_MatchTheirSgrCode(string name, int expected)
    {
        var property = typeof(AnsiColor).GetProperty(name);
        Assert.NotNull(property);
        Assert.Equal(expected.ToString(CultureInfo.InvariantCulture), Format((AnsiColor)property.GetValue(null)!));
    }

    [Fact]
    public void Color_PaletteAndRgb_NeedRoomForTheirSequence()
    {
        Span<char> tooSmall = stackalloc char[4];
        Assert.Equal(0, AnsiColor.FromPalette(200).FormatAnsi(tooSmall, isForeground: true));
        Assert.Equal(0, AnsiColor.FromRgb(1, 2, 3).FormatAnsi(tooSmall, isForeground: true));
    }

    [Fact]
    public void Color_Default_NeedsRoomForTwoCharacters()
    {
        Span<char> tooSmall = stackalloc char[1];
        Assert.Equal(0, AnsiColor.Default.FormatAnsi(tooSmall, isForeground: true));
    }

    [Fact]
    public void Style_Default_NeedsRoomForOneCharacter()
        => Assert.Equal(0, AnsiStyle.Default.FormatAnsi([]));

    [Fact]
    public void Style_EqualityOperators_MatchEquals()
    {
        var red = new AnsiStyle(AnsiColor.Red);
        var alsoRed = new AnsiStyle(AnsiColor.Red);
        var green = new AnsiStyle(AnsiColor.Green);

        Assert.True(red == alsoRed);
        Assert.False(red == green);
        Assert.True(red != green);
        Assert.False(red != alsoRed);
    }

    private static string Format(AnsiColor color)
    {
        Span<char> destination = stackalloc char[32];
        int written = color.FormatAnsi(destination, isForeground: true);
        return destination[..written].ToString();
    }
}
