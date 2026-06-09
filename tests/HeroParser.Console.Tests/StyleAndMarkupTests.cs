using System;
using Xunit;
using HeroParser.Console;

namespace HeroParser.Console.Tests;

public class StyleAndMarkupTests
{
    [Fact]
    public void Color_StandardPredefined_FormatAnsiForeground()
    {
        // Arrange
        var red = Color.Red;
        Span<char> buf = stackalloc char[32];

        // Act
        int written = red.FormatAnsi(buf, isForeground: true);
        var result = buf[..written].ToString();

        // Assert
        Assert.Equal("91", result);
    }

    [Fact]
    public void Color_StandardPredefined_FormatAnsiBackground()
    {
        // Arrange
        var blue = Color.Blue;
        Span<char> buf = stackalloc char[32];

        // Act
        int written = blue.FormatAnsi(buf, isForeground: false);
        var result = buf[..written].ToString();

        // Assert
        // Blue is 94, so background is 94 + 10 = 104
        Assert.Equal("104", result);
    }

    [Fact]
    public void Style_CombinedStyle_FormatsCorrectAnsiParameter()
    {
        // Arrange
        var style = new Style(Color.Red, Color.Blue, Decoration.Bold | Decoration.Italic);
        Span<char> buf = stackalloc char[128];

        // Act
        int written = style.FormatAnsi(buf);
        var result = buf[..written].ToString();

        // Assert
        // Bold: 1, Italic: 3, Red: 91, Blue background: 104
        Assert.Contains("1", result);
        Assert.Contains("3", result);
        Assert.Contains("91", result);
        Assert.Contains("104", result);
    }

    [Fact]
    public void Markup_SimpleTags_RendersWithCorrectStyles()
    {
        // Arrange
        Span<char> charBuf = stackalloc char[1024];
        var buffer = new AnsiBuffer(charBuf);

        // Act
        AnsiConsole.Markup("[bold red]Hello[/] world", ref buffer);
        var result = charBuf[..buffer.Position].ToString();

        // Assert
        // Should contain red code and reset code
        Assert.Contains("\x1b[", result);
        Assert.Contains("Hello", result);
        Assert.Contains("\x1b[0m", result);
        Assert.Contains("world", result);
    }
}
