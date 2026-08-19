// See AnsiColorAndStyleTests for why the product's Color must be aliased here.
using System.Globalization;
using HeroParser.Console.Prompts;
using AnsiBuf = HeroParser.Console.AnsiBuffer;
using AnsiConsoleApi = HeroParser.Console.AnsiConsole;
using Xunit;

namespace HeroParser.Tests.ConsoleUi;

/// <summary>
/// Covers the word-wrapping paths in the text and panel widgets, the table's
/// shrink-to-fit path, and prompt navigation keys.
///
/// Wrapping only engages when a line is wider than the space it is given, so the widget
/// tests here deliberately render at widths narrower than their content. Arrow keys have
/// no character form, which is why the prompts are driven from a scripted key queue
/// rather than through a reader.
/// </summary>
[Trait("Category", "Unit")]
[Collection(AnsiConsoleCurrentCollection.NAME)]
public class WidgetWrappingTests
{
    private static string Render(HeroParser.Console.Widgets.IConsoleWidget widget, int maxWidth)
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        Span<char> scratch = new char[32 * 1024];
        var buffer = new AnsiBuf(scratch, writer);
        widget.Render(ref buffer, maxWidth);
        buffer.Flush();
        return writer.ToString();
    }

    private static string Visible(string ansi)
    {
        var sb = new System.Text.StringBuilder(ansi.Length);
        for (int i = 0; i < ansi.Length; i++)
        {
            if (ansi[i] == '\x1b')
            {
                while (i < ansi.Length && ansi[i] != 'm') i++;
                continue;
            }
            sb.Append(ansi[i]);
        }
        return sb.ToString();
    }

    [Fact]
    public void TextWidget_WrapsOnWordBoundaries()
    {
        string visible = Visible(Render(new HeroParser.Console.Text("alpha beta gamma delta"), maxWidth: 12));

        string[] lines = visible.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.True(lines.Length > 1, "text wider than the console must wrap onto more lines");
        // Wrapping at a space means no word is split and the space itself is consumed.
        foreach (string line in lines)
        {
            Assert.DoesNotContain("  ", line, StringComparison.Ordinal);
            Assert.True(line.Length <= 12, $"line '{line}' exceeded the render width");
        }
        Assert.Contains("alpha", visible, StringComparison.Ordinal);
        Assert.Contains("delta", visible, StringComparison.Ordinal);
    }

    [Fact]
    public void TextWidget_UnbreakableWord_IsSplitAtTheWidth()
    {
        // No space to wrap on, so the widget has to cut mid-word rather than overflow.
        string visible = Visible(Render(new HeroParser.Console.Text(new string('x', 30)), maxWidth: 10));
        foreach (string line in visible.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
        {
            Assert.True(line.Length <= 10, $"line of length {line.Length} exceeded the render width");
        }
    }

    [Fact]
    public void PanelWidget_WrapsLongTextInsideItsBorder()
    {
        string visible = Visible(Render(new HeroParser.Console.Panel("one two three four five six seven"), maxWidth: 20));

        Assert.Contains('┌', visible);
        Assert.Contains('┘', visible);
        Assert.Contains("one", visible, StringComparison.Ordinal);
        Assert.Contains("seven", visible, StringComparison.Ordinal);
        Assert.True(
            visible.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Length > 3,
            "wrapped content must add body lines between the panel's borders");
    }

    [Fact]
    public void TableWidget_ShrinksColumnsThatCannotFit()
    {
        var table = new HeroParser.Console.Table();
        foreach (string header in new[] { "LongHeaderOne", "LongHeaderTwo", "LongHeaderThree", "LongHeaderFour" })
        {
            table.AddColumn(header);
        }
        table.AddRow("aaaaaaaaaa", "bbbbbbbbbb", "cccccccccc", "dddddddddd");

        // Every column is wider than its share of 30 characters, forcing the proportional
        // shrink path rather than the grow-to-fill one.
        string visible = Visible(Render(table, maxWidth: 30));
        foreach (string line in visible.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
        {
            Assert.True(line.Length <= 30, $"table line of length {line.Length} exceeded the render width");
        }
    }

    [Fact]
    public void TableWidget_StyledHeaders_AreNotSplitAcrossLines()
    {
        // Wrapping used to measure raw characters, so "[red bold]Row[/]" counted as 16 and
        // was cut mid-tag — the CLI's validation table printed "[re / d / bol / d]R" down
        // its first column instead of the word.
        var table = new HeroParser.Console.Table();
        table.AddColumn("[red bold]Row[/]");
        table.AddColumn("[blue]Validation Error Description[/]");
        table.AddRow("[red]2[/]", "Row has 2 columns, expected 3");

        string visible = Visible(Render(table, maxWidth: 80));

        Assert.Contains("Row", visible, StringComparison.Ordinal);
        Assert.DoesNotContain("[re", visible, StringComparison.Ordinal);
        Assert.DoesNotContain("[/]", visible, StringComparison.Ordinal);
        Assert.DoesNotContain("bold", visible, StringComparison.Ordinal);
    }

    [Fact]
    public void TableWidget_StyledCells_KeepTheirColour()
    {
        var table = new HeroParser.Console.Table();
        table.AddColumn("Value");
        table.AddRow("[green]ok[/]");

        string ansi = Render(table, maxWidth: 40);

        Assert.Contains("\x1b[92m", ansi, StringComparison.Ordinal);
        Assert.Contains("ok", Visible(ansi), StringComparison.Ordinal);
    }

    [Fact]
    public void TableWidget_StyledContentWiderThanItsColumn_WrapsOnVisibleWidth()
    {
        var table = new HeroParser.Console.Table();
        table.AddColumn("A");
        table.AddColumn("B");
        table.AddRow("[green]alpha beta gamma delta epsilon[/]", "x");

        string visible = Visible(Render(table, maxWidth: 30));

        Assert.DoesNotContain("[green]", visible, StringComparison.Ordinal);
        Assert.Contains("alpha", visible, StringComparison.Ordinal);
        Assert.Contains("epsilon", visible, StringComparison.Ordinal);
        foreach (string line in visible.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
        {
            Assert.True(line.Length <= 30, $"line of length {line.Length} exceeded the render width");
        }
    }

    [Fact]
    public void TableWidget_EscapedBracketsInACell_SurviveWrapping()
    {
        var table = new HeroParser.Console.Table();
        table.AddColumn("Value");
        table.AddRow("a[[b]]c");

        Assert.Contains("a[b]c", Visible(Render(table, maxWidth: 40)), StringComparison.Ordinal);
    }

    [Fact]
    public void SelectionPrompt_DownArrow_MovesToTheNextChoice()
    {
        var console = new RecordingConsole([Key(ConsoleKey.DownArrow), Key(ConsoleKey.Enter)]);
        Assert.Equal("beta", new SelectionPrompt<string>("t").AddChoices(["alpha", "beta", "gamma"]).Show(console));
    }

    [Fact]
    public void SelectionPrompt_DownArrowPastTheEnd_WrapsToTheFirstChoice()
    {
        var console = new RecordingConsole([Key(ConsoleKey.DownArrow), Key(ConsoleKey.DownArrow), Key(ConsoleKey.Enter)]);
        Assert.Equal("alpha", new SelectionPrompt<string>("t").AddChoices(["alpha", "beta"]).Show(console));
    }

    [Fact]
    public void SelectionPrompt_UpArrowFromTheTop_WrapsToTheLastChoice()
    {
        var console = new RecordingConsole([Key(ConsoleKey.UpArrow), Key(ConsoleKey.Enter)]);
        Assert.Equal("gamma", new SelectionPrompt<string>("t").AddChoices(["alpha", "beta", "gamma"]).Show(console));
    }

    [Fact]
    public void SelectionPrompt_UpThenDown_ReturnsToWhereItStarted()
    {
        var console = new RecordingConsole([Key(ConsoleKey.DownArrow), Key(ConsoleKey.UpArrow), Key(ConsoleKey.Enter)]);
        Assert.Equal("alpha", new SelectionPrompt<string>("t").AddChoices(["alpha", "beta"]).Show(console));
    }

    [Fact]
    public void SelectionPrompt_ParameterlessShow_UsesTheCurrentConsole()
    {
        var previous = AnsiConsoleApi.Current;
        try
        {
            AnsiConsoleApi.Current = new RecordingConsole([Key(ConsoleKey.DownArrow), Key(ConsoleKey.Enter)]);
            Assert.Equal("second", new SelectionPrompt<string>("t").AddChoices(["first", "second"]).Show());
        }
        finally
        {
            AnsiConsoleApi.Current = previous;
        }
    }

    [Fact]
    public void TextPrompt_ParameterlessShow_UsesTheCurrentConsole()
    {
        var previous = AnsiConsoleApi.Current;
        try
        {
            AnsiConsoleApi.Current = new RecordingConsole(lines: ["typed"]);
            Assert.Equal("typed", new TextPrompt<string>("t").Show());
        }
        finally
        {
            AnsiConsoleApi.Current = previous;
        }
    }

    private static ConsoleKeyInfo Key(ConsoleKey key) => new('\0', key, false, false, false);
}
