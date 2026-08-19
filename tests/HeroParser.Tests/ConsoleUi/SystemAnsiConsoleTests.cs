// See AnsiColorAndStyleTests for why the product's Color must be aliased here.
using System.Globalization;
using AnsiColor = HeroParser.Console.Color;
using AnsiConsoleApi = HeroParser.Console.AnsiConsole;
using AnsiStyle = HeroParser.Console.Style;
using IHeroConsole = HeroParser.Console.IAnsiConsole;
using SystemConsole = HeroParser.Console.SystemAnsiConsole;
using Xunit;

namespace HeroParser.Tests.ConsoleUi;

/// <summary>
/// Covers <see cref="SystemConsole"/> — the default <see cref="IHeroConsole"/> — and the
/// static <see cref="AnsiConsoleApi"/> facade that forwards to it.
/// </summary>
[Trait("Category", "Unit")]
public class SystemAnsiConsoleTests
{
    private static (IHeroConsole Console, StringWriter Output) Redirected(string input = "", int width = 80)
    {
        var output = new StringWriter(CultureInfo.InvariantCulture);
        return (new SystemConsole(output, new StringReader(input), width), output);
    }

    [Fact]
    public void NullWriter_Throws()
        => Assert.Throws<ArgumentNullException>(() => new SystemConsole(null!));

    [Fact]
    public void Width_UsesConfiguredValueWhenRedirected()
        => Assert.Equal(120, Redirected(width: 120).Console.Width);

    [Fact]
    public void Width_OnProcessConsole_IsAlwaysPositive()
    {
        // The test host has no terminal, so this exercises the fallback rather than a
        // real window measurement; either way a widget must never be handed a zero width.
        Assert.True(new SystemConsole().Width > 0);
    }

    [Fact]
    public void Write_AndWriteLine_EmitPlainText()
    {
        var (console, output) = Redirected();
        console.Write("a");
        console.WriteLine("b");
        Assert.Equal("ab" + Environment.NewLine, output.ToString());
    }

    [Fact]
    public void Write_Styled_EmitsSgrCodes()
    {
        var (console, output) = Redirected();
        console.Write("x", new AnsiStyle(AnsiColor.Red));
        Assert.Contains("\x1b[91m", output.ToString(), StringComparison.Ordinal);
        Assert.Contains('x', output.ToString());
    }

    [Fact]
    public void WriteLine_Styled_EndsWithNewLine()
    {
        var (console, output) = Redirected();
        console.WriteLine("x", new AnsiStyle(AnsiColor.Green));
        Assert.EndsWith(Environment.NewLine, output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Markup_AndMarkupLine_StripTagsAndStyle()
    {
        var (console, output) = Redirected();
        console.Markup("[bold]on[/]");
        console.MarkupLine("[red]off[/]");

        string text = output.ToString();
        Assert.DoesNotContain("[bold]", text, StringComparison.Ordinal);
        Assert.Contains("on", text, StringComparison.Ordinal);
        Assert.Contains("off", text, StringComparison.Ordinal);
        Assert.EndsWith(Environment.NewLine, text, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_Widget_RendersAtConsoleWidth()
    {
        var (console, output) = Redirected(width: 20);
        console.Write(new HeroParser.Console.Rule("R"));
        Assert.Contains("R", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Write_NullWidget_Throws()
        => Assert.Throws<ArgumentNullException>(() => Redirected().Console.Write((HeroParser.Console.Widgets.IConsoleWidget)null!));

    [Fact]
    public void ReadLine_ReturnsLinesThenNull()
    {
        var console = Redirected("one\ntwo\n").Console;
        Assert.Equal("one", console.ReadLine());
        Assert.Equal("two", console.ReadLine());
        Assert.Null(console.ReadLine());
    }

    [Theory]
    [InlineData("\r", ConsoleKey.Enter)]
    [InlineData("\n", ConsoleKey.Enter)]
    [InlineData(" ", ConsoleKey.Spacebar)]
    [InlineData("\t", ConsoleKey.Tab)]
    [InlineData("\x1b", ConsoleKey.Escape)]
    [InlineData("q", ConsoleKey.Q)]
    [InlineData("Q", ConsoleKey.Q)]
    [InlineData("7", ConsoleKey.D7)]
    [InlineData("!", ConsoleKey.NoName)]
    public void ReadKey_MapsCharactersToKeys(string input, ConsoleKey expected)
        => Assert.Equal(expected, Redirected(input).Console.ReadKey(intercept: true).Key);

    [Theory]
    [InlineData("\x1b[A", ConsoleKey.UpArrow)]
    [InlineData("\x1b[B", ConsoleKey.DownArrow)]
    [InlineData("\x1b[C", ConsoleKey.RightArrow)]
    [InlineData("\x1b[D", ConsoleKey.LeftArrow)]
    [InlineData("\x1b[H", ConsoleKey.Home)]
    [InlineData("\x1b[F", ConsoleKey.End)]
    [InlineData("\x1b[Z", ConsoleKey.NoName)]
    public void ReadKey_DecodesCsiArrowSequences(string input, ConsoleKey expected)
        => Assert.Equal(expected, Redirected(input).Console.ReadKey(intercept: true).Key);

    [Fact]
    public void ReadKey_AtEndOfInput_ReportsEnter()
    {
        // Exhausted input must terminate a prompt loop rather than spin forever.
        Assert.Equal(ConsoleKey.Enter, Redirected(string.Empty).Console.ReadKey(intercept: true).Key);
    }

    [Fact]
    public void AnsiConsole_StaticEntryPoints_ForwardToCurrent()
    {
        var previous = AnsiConsoleApi.Current;
        try
        {
            var (console, output) = Redirected();
            AnsiConsoleApi.Current = console;

            AnsiConsoleApi.Write("w");
            AnsiConsoleApi.WriteLine("wl");
            AnsiConsoleApi.Write("s", new AnsiStyle(AnsiColor.Blue));
            AnsiConsoleApi.WriteLine("sl", new AnsiStyle(AnsiColor.Blue));
            AnsiConsoleApi.Markup("[green]m[/]");
            AnsiConsoleApi.MarkupLine("[green]ml[/]");
            AnsiConsoleApi.Write(new HeroParser.Console.Text("widget"));

            string text = output.ToString();
            foreach (string expected in new[] { "w", "wl", "s", "sl", "m", "ml", "widget" })
            {
                Assert.Contains(expected, text, StringComparison.Ordinal);
            }
        }
        finally
        {
            AnsiConsoleApi.Current = previous;
        }
    }

    [Fact]
    public void AnsiConsole_Current_RejectsNullByRestoringDefault()
    {
        var previous = AnsiConsoleApi.Current;
        try
        {
            AnsiConsoleApi.Current = null!;
            Assert.NotNull(AnsiConsoleApi.Current);
            Assert.IsType<SystemConsole>(AnsiConsoleApi.Current);
        }
        finally
        {
            AnsiConsoleApi.Current = previous;
        }
    }

    [Fact]
    public void AnsiConsole_Factories_ReturnRunners()
    {
        Assert.NotNull(AnsiConsoleApi.Status());
        Assert.NotNull(AnsiConsoleApi.Progress());
    }

    [Fact]
    public void Markup_ClosingTagAtRootIsIgnored()
    {
        // A stray [/] with nothing on the style stack must not underflow it.
        var (console, output) = Redirected();
        console.Markup("[/]text");
        Assert.Contains("text", output.ToString(), StringComparison.Ordinal);
    }
}
