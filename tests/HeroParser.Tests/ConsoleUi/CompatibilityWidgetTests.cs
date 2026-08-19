// See AnsiColorAndStyleTests for why the product's Color must be aliased here.
using System.Globalization;
using AnsiBuf = HeroParser.Console.AnsiBuffer;
using AnsiColor = HeroParser.Console.Color;
using CompatFiglet = HeroParser.Console.FigletText;
using CompatMarkup = HeroParser.Console.Markup;
using CompatPanel = HeroParser.Console.Panel;
using CompatRule = HeroParser.Console.Rule;
using CompatTable = HeroParser.Console.Table;
using CompatText = HeroParser.Console.Text;
using Xunit;

namespace HeroParser.Tests.ConsoleUi;

/// <summary>
/// Covers the Spectre.Console-shaped compatibility widgets in Compatibility.cs.
///
/// These exist so callers written against Spectre's API keep working, which makes
/// them exactly the kind of code that gets shipped untested: every one was at zero
/// coverage. They are ordinary widgets, so the same buffer-backed rendering used for
/// the native widgets reaches all of them.
/// </summary>
[Trait("Category", "Unit")]
public class CompatibilityWidgetTests
{
    private static string Render(HeroParser.Console.Widgets.IConsoleWidget widget, int maxWidth = 60)
    {
        using var sw = new StringWriter(CultureInfo.InvariantCulture);
        Span<char> scratch = new char[16 * 1024];
        var buffer = new AnsiBuf(scratch, sw);
        widget.Render(ref buffer, maxWidth);
        buffer.Flush();
        return sw.ToString();
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
    public void FigletText_DrawsBannerAroundText()
    {
        string visible = Visible(Render(new CompatFiglet("HERO")));
        Assert.Contains("HERO", visible, StringComparison.Ordinal);
        Assert.Contains('╔', visible);
        Assert.Contains('╝', visible);
    }

    [Fact]
    public void FigletText_Color_IsFluentAndStyles()
    {
        var banner = new CompatFiglet("X");
        Assert.Same(banner, banner.Color(AnsiColor.Red));

        string ansi = Render(banner);
        Assert.Contains("91", ansi, StringComparison.Ordinal);
        Assert.Contains("X", Visible(ansi), StringComparison.Ordinal);
    }

    [Fact]
    public void FigletText_NullText_TreatedAsEmpty()
        => Assert.Contains('╔', Visible(Render(new CompatFiglet(null!))));

    [Fact]
    public void FigletText_TextWiderThanBanner_StillCloses()
    {
        // Right padding goes negative for long text; the closing edge must survive.
        string visible = Visible(Render(new CompatFiglet(new string('L', 80))));
        Assert.Contains('║', visible);
        Assert.Contains('╚', visible);
    }

    [Fact]
    public void Text_RendersValue()
    {
        var text = new CompatText("plain value");
        Assert.Equal("plain value", text.Value);
        Assert.Contains("plain value", Visible(Render(text)), StringComparison.Ordinal);
    }

    [Fact]
    public void Text_NullValue_BecomesEmpty()
        => Assert.Equal(string.Empty, new CompatText(null!).Value);

    [Fact]
    public void Markup_RendersThroughMarkupParser()
    {
        var markup = new CompatMarkup("[green]ok[/]");
        Assert.Equal("[green]ok[/]", markup.Value);
        Assert.Equal("ok", Visible(Render(markup)).Trim());
    }

    [Fact]
    public void Markup_NullValue_BecomesEmpty()
        => Assert.Equal(string.Empty, new CompatMarkup(null!).Value);

    [Theory]
    [InlineData("a[b]c", "a[[b]]c")]
    [InlineData("[", "[[")]
    [InlineData("]", "]]")]
    [InlineData("none", "none")]
    [InlineData("", "")]
    public void Markup_Escape_DoublesBrackets(string input, string expected)
        => Assert.Equal(expected, CompatMarkup.Escape(input));

    [Fact]
    public void Markup_Escape_NullPassesThrough()
        => Assert.Null(CompatMarkup.Escape(null!));

    [Fact]
    public void Panel_WrapsTextLikeItsBase()
    {
        string visible = Visible(Render(new CompatPanel("inside")));
        Assert.Contains("inside", visible, StringComparison.Ordinal);
        Assert.Contains('┌', visible);
    }

    [Fact]
    public void Panel_WrapsChildWidget()
    {
        string visible = Visible(Render(new CompatPanel(new CompatText("child"))));
        Assert.Contains("child", visible, StringComparison.Ordinal);
    }

    [Fact]
    public void Rule_RendersAndJustificationHelpersAreFluent()
    {
        var rule = new CompatRule("Title");
        // The justification helpers are no-ops kept for API compatibility; they must
        // still return the same instance so call chains work.
        Assert.Same(rule, rule.Centered());
        Assert.Same(rule, rule.LeftJustified());
        Assert.Same(rule, rule.RightJustified());
        Assert.Contains("Title", Visible(Render(rule)), StringComparison.Ordinal);
    }

    [Fact]
    public void Table_BorderIsFluentAndTableStillRenders()
    {
        var table = new CompatTable();
        Assert.Same(table, table.Border(HeroParser.Console.TableBorder.Rounded));

        table.AddColumn("Col");
        table.AddRow("val");
        string visible = Visible(Render(table));
        Assert.Contains("Col", visible, StringComparison.Ordinal);
        Assert.Contains("val", visible, StringComparison.Ordinal);
    }

    [Fact]
    public void PanelHeader_ExposesItsText()
        => Assert.Equal("hdr", new HeroParser.Console.PanelHeader("hdr").Text);
}
