using System;
using Xunit;
using HeroParser.Console;
using HeroParser.Console.Widgets;

namespace HeroParser.Console.Tests;

public class TableTests
{
    [Fact]
    public void Table_SimpleRender_FitsWidthAndAligns()
    {
        // Arrange
        var table = new TableWidget()
            .AddColumn("Name")
            .AddColumn("Value")
            .AddRow("Alice", "100")
            .AddRow("Bob", "200");

        Span<char> charBuf = stackalloc char[4096];
        var buffer = new AnsiBuffer(charBuf);

        // Act
        table.Render(ref buffer, 40);
        var output = charBuf[..buffer.Position].ToString();

        // Assert
        Assert.NotEmpty(output);
        Assert.Contains("Name", output);
        Assert.Contains("Value", output);
        Assert.Contains("Alice", output);
        Assert.Contains("Bob", output);
        Assert.Contains("100", output);
        Assert.Contains("200", output);
    }

    [Fact]
    public void Table_AutoSizingAndWrapping_AppliesCorrectly()
    {
        // Arrange
        var table = new TableWidget()
            .AddColumn("Description")
            .AddColumn("Status")
            .AddRow("This is a very long text that should exceed individual column widths and trigger wrapping behavior", "Done");

        Span<char> charBuf = stackalloc char[4096];
        var buffer = new AnsiBuffer(charBuf);

        // Act
        table.Render(ref buffer, 30);
        var output = charBuf[..buffer.Position].ToString();

        // Assert
        Assert.NotEmpty(output);
        Assert.Contains("Description", output);
        Assert.Contains("Sta", output);
        Assert.Contains("Don", output);
    }
}
