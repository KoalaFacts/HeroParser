// HeroParser.Tests declares its own public `enum Color` (CoveragePushTests3), and
// members of an enclosing namespace win over using-directive imports, so the product's
// Color is unreachable by simple name from anywhere under HeroParser.Tests.*.
// Aliasing sidesteps the shadowing without renaming either type.
using AnsiColor = HeroParser.Console.Color;
using AnsiDecoration = HeroParser.Console.Decoration;
using AnsiStyle = HeroParser.Console.Style;
using Xunit;

namespace HeroParser.Tests.ConsoleUi;

/// <summary>
/// Covers <see cref="AnsiColor"/> and <see cref="AnsiStyle"/>, whose ANSI escape generation
/// carried almost all of their uncovered lines.
///
/// Assertions are written against the SGR codes the ANSI spec defines — 30-37 for the
/// normal colours, 90-97 for the bright ones, +10 to move any of them to the
/// background, 38/48;5;n for the 256-colour palette and 38/48;2;r;g;b for truecolour —
/// so they pin the escape sequences a terminal actually needs rather than restating
/// whatever the implementation happens to emit.
/// </summary>
[Trait("Category", "Unit")]
public class AnsiColorAndStyleTests
{
    private static string Ansi(AnsiColor color, bool isForeground)
    {
        Span<char> destination = stackalloc char[64];
        int written = color.FormatAnsi(destination, isForeground);
        return new string(destination[..written]);
    }

    private static string Ansi(AnsiStyle style)
    {
        Span<char> destination = stackalloc char[128];
        int written = style.FormatAnsi(destination);
        return new string(destination[..written]);
    }

    [Fact]
    public void Default_UsesResetCodes()
    {
        Assert.Equal("39", Ansi(AnsiColor.Default, isForeground: true));
        Assert.Equal("49", Ansi(AnsiColor.Default, isForeground: false));
        Assert.True(AnsiColor.Default.IsDefault);
    }

    // The bright colours live at 90-97, not 30-37, which is the mapping most easily
    // got wrong: ConsoleColor.Red is bright red, ConsoleColor.DarkRed is the normal one.
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
    public void FromConsoleColor_MapsToSgrCode(ConsoleColor consoleColor, int foregroundCode)
    {
        var color = AnsiColor.FromConsoleColor(consoleColor);
        Assert.Equal(foregroundCode.ToString(), Ansi(color, isForeground: true));
        // Background is the foreground code shifted by 10.
        Assert.Equal((foregroundCode + 10).ToString(), Ansi(color, isForeground: false));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    [InlineData(128)]
    [InlineData(255)]
    public void FromPalette_UsesExtendedColourForm(byte index)
    {
        var color = AnsiColor.FromPalette(index);
        Assert.Equal($"38;5;{index}", Ansi(color, isForeground: true));
        Assert.Equal($"48;5;{index}", Ansi(color, isForeground: false));
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(255, 255, 255)]
    [InlineData(1, 22, 255)]
    public void FromRgb_UsesTrueColourForm(byte r, byte g, byte b)
    {
        var color = AnsiColor.FromRgb(r, g, b);
        Assert.Equal($"38;2;{r};{g};{b}", Ansi(color, isForeground: true));
        Assert.Equal($"48;2;{r};{g};{b}", Ansi(color, isForeground: false));
    }

    // Every branch of FormatAnsi refuses to write a partial escape when the
    // destination is too small; a truncated sequence would corrupt the terminal.
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void FormatAnsi_DestinationTooSmall_WritesNothing(int size)
    {
        Span<char> tiny = size == 0 ? [] : stackalloc char[size];
        Assert.Equal(0, AnsiColor.Default.FormatAnsi(tiny, true));
        Assert.Equal(0, AnsiColor.FromPalette(7).FormatAnsi(tiny, true));
        Assert.Equal(0, AnsiColor.FromRgb(1, 2, 3).FormatAnsi(tiny, true));
    }

    [Fact]
    public void NamedColours_MatchTheirConsoleColour()
    {
        Assert.Equal(AnsiColor.FromConsoleColor(ConsoleColor.Red), AnsiColor.Red);
        Assert.Equal(AnsiColor.FromConsoleColor(ConsoleColor.White), AnsiColor.White);
        Assert.Equal(AnsiColor.FromConsoleColor(ConsoleColor.DarkBlue), AnsiColor.DarkBlue);
        Assert.Equal(AnsiColor.FromRgb(0, 255, 255), AnsiColor.Aqua);
    }

    [Fact]
    public void Equality_DistinguishesColourKinds()
    {
        // Same channel bytes, different kind: palette 7 must not equal ConsoleColor.Gray.
        Assert.NotEqual(AnsiColor.FromPalette(7), AnsiColor.FromConsoleColor(ConsoleColor.Gray));
        Assert.True(AnsiColor.FromRgb(1, 2, 3) == AnsiColor.FromRgb(1, 2, 3));
        Assert.True(AnsiColor.FromRgb(1, 2, 3) != AnsiColor.FromRgb(3, 2, 1));
        Assert.Equal(AnsiColor.FromRgb(1, 2, 3).GetHashCode(), AnsiColor.FromRgb(1, 2, 3).GetHashCode());
        Assert.True(AnsiColor.Red.Equals((object)AnsiColor.Red));
        Assert.False(AnsiColor.Red.Equals("not a colour"));
    }

    [Fact]
    public void Style_Default_WritesResetOnly()
    {
        Assert.Equal("0", Ansi(AnsiStyle.Default));
        Assert.True(AnsiStyle.Default.IsDefault);
        Assert.True(default(AnsiStyle).IsDefault);
    }

    [Theory]
    [InlineData(AnsiDecoration.Bold, "1")]
    [InlineData(AnsiDecoration.Dim, "2")]
    [InlineData(AnsiDecoration.Italic, "3")]
    [InlineData(AnsiDecoration.Underline, "4")]
    [InlineData(AnsiDecoration.Blink, "5")]
    [InlineData(AnsiDecoration.Invert, "7")]
    [InlineData(AnsiDecoration.Strikethrough, "9")]
    public void Style_Decoration_WritesItsSgrCode(AnsiDecoration decoration, string expected)
        => Assert.Equal(expected, Ansi(new AnsiStyle(AnsiColor.Default, AnsiColor.Default, decoration)));

    [Fact]
    public void Style_CombinesDecorationsForegroundAndBackground()
    {
        var style = new AnsiStyle(AnsiColor.FromRgb(1, 2, 3), AnsiColor.FromPalette(9), AnsiDecoration.Bold | AnsiDecoration.Underline);
        // Decorations first, then foreground, then background, semicolon-separated.
        Assert.Equal("1;4;38;2;1;2;3;48;5;9", Ansi(style));
    }

    [Fact]
    public void Style_ForegroundOnly_OmitsBackground()
        => Assert.Equal("91", Ansi(new AnsiStyle(AnsiColor.Red)));

    [Fact]
    public void Style_BackgroundOnly_OmitsForeground()
        => Assert.Equal("104", Ansi(new AnsiStyle(AnsiColor.Default, AnsiColor.Blue)));

    [Fact]
    public void Style_Builders_Accumulate()
    {
        var style = AnsiStyle.Default
            .WithForeground(AnsiColor.Red)
            .WithBackground(AnsiColor.Black)
            .WithBold()
            .WithDim()
            .WithItalic()
            .WithUnderline()
            .WithStrikethrough();

        Assert.Equal(AnsiColor.Red, style.Foreground);
        Assert.Equal(AnsiColor.Black, style.Background);
        Assert.Equal(
            AnsiDecoration.Bold | AnsiDecoration.Dim | AnsiDecoration.Italic | AnsiDecoration.Underline | AnsiDecoration.Strikethrough,
            style.Decorations);
        Assert.False(style.IsDefault);
    }

    [Fact]
    public void Style_WithDecoration_IsAdditive()
    {
        var style = AnsiStyle.Default.WithDecoration(AnsiDecoration.Bold).WithDecoration(AnsiDecoration.Italic);
        Assert.Equal(AnsiDecoration.Bold | AnsiDecoration.Italic, style.Decorations);
    }

    [Fact]
    public void Style_Equality()
    {
        var a = new AnsiStyle(AnsiColor.Red, AnsiColor.Black, AnsiDecoration.Bold);
        var b = new AnsiStyle(AnsiColor.Red, AnsiColor.Black, AnsiDecoration.Bold);
        var c = new AnsiStyle(AnsiColor.Red, AnsiColor.Black, AnsiDecoration.Dim);

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.True(a.Equals((object)b));
        Assert.False(a.Equals("not a style"));
    }
}
