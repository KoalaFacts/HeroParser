// HeroParser.Tests declares its own public `enum Color` (CoveragePushTests3), and
// members of an enclosing namespace win over using-directive imports, so the product's
// Color is unreachable by simple name from anywhere under HeroParser.Tests.*.
using System.Globalization;
using HeroParser.Console.Widgets;
using AnsiBuf = HeroParser.Console.AnsiBuffer;
using AnsiColor = HeroParser.Console.Color;
using AnsiConsoleApi = HeroParser.Console.AnsiConsole;
using AnsiDecoration = HeroParser.Console.Decoration;
using AnsiStyle = HeroParser.Console.Style;
using Xunit;

namespace HeroParser.Tests.ConsoleUi;

/// <summary>
/// Renders every widget through <see cref="IConsoleWidget.Render"/> into an
/// <see cref="AnsiBuf"/> backed by a StringWriter.
///
/// The widgets were uncovered only because nothing had ever called Render outside of
/// AnsiConsole, which writes to the real stdout. The buffer already accepts a
/// TextWriter, so the whole rendering surface is reachable without touching the
/// console or changing production code.
/// </summary>
[Trait("Category", "Unit")]
public class WidgetRenderingTests
{
    private static string Render(IConsoleWidget widget, int maxWidth = 40)
    {
        using var sw = new StringWriter(CultureInfo.InvariantCulture);
        Span<char> scratch = new char[16 * 1024];
        var buffer = new AnsiBuf(scratch, sw);
        widget.Render(ref buffer, maxWidth);
        buffer.Flush();
        return sw.ToString();
    }

    /// <summary>Strips SGR escapes so assertions can talk about what the user sees.</summary>
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
    public void TextWidget_PlainText_RendersVerbatim()
        => Assert.Contains("hello", Visible(Render(new TextWidget("hello"))), StringComparison.Ordinal);

    [Fact]
    public void TextWidget_Styled_EmitsEscapeSequence()
    {
        string ansi = Render(new TextWidget("hi", new AnsiStyle(AnsiColor.Red)));
        Assert.Contains('\x1b', ansi);
        Assert.Contains("91", ansi, StringComparison.Ordinal);   // bright red foreground
        Assert.Contains("hi", Visible(ansi), StringComparison.Ordinal);
    }

    [Fact]
    public void TextWidget_Markup_ResolvesTags()
    {
        string visible = Visible(Render(new TextWidget("[red]danger[/]", default, isMarkup: true)));
        // The tags themselves must not survive into the output.
        Assert.Equal("danger", visible.Trim());
    }

    [Fact]
    public void TextWidget_EmptyText_RendersWithoutThrowing()
        => Assert.NotNull(Render(new TextWidget(string.Empty)));

    [Fact]
    public void RuleWidget_NoLabel_FillsWidthWithBorderChar()
    {
        string visible = Visible(Render(new RuleWidget(borderChar: '-'), maxWidth: 20)).Trim();
        Assert.Equal(new string('-', visible.Length), visible);
        Assert.True(visible.Length > 0);
    }

    [Fact]
    public void RuleWidget_WithLabel_IncludesLabel()
    {
        string visible = Visible(Render(new RuleWidget("Section", '='), maxWidth: 40));
        Assert.Contains("Section", visible, StringComparison.Ordinal);
        Assert.Contains('=', visible);
    }

    [Fact]
    public void RuleWidget_LabelLongerThanWidth_StillRenders()
        => Assert.NotEmpty(Visible(Render(new RuleWidget(new string('x', 100)), maxWidth: 10)));

    [Fact]
    public void PanelWidget_Text_DrawsBoxAroundContent()
    {
        string visible = Visible(Render(new PanelWidget("body"), maxWidth: 30));
        Assert.Contains("body", visible, StringComparison.Ordinal);
        // Light box-drawing corners on the top and bottom edges.
        Assert.Contains('┌', visible);
        Assert.Contains('┘', visible);
    }

    [Fact]
    public void PanelWidget_WithTitle_ShowsTitle()
    {
        string visible = Visible(Render(new PanelWidget("body", "Title"), maxWidth: 30));
        Assert.Contains("Title", visible, StringComparison.Ordinal);
        Assert.Contains("body", visible, StringComparison.Ordinal);
    }

    [Fact]
    public void PanelWidget_WithChildWidget_RendersChild()
    {
        var panel = new PanelWidget(new TextWidget("nested"), "Outer");
        string visible = Visible(Render(panel, maxWidth: 30));
        Assert.Contains("nested", visible, StringComparison.Ordinal);
        Assert.Contains("Outer", visible, StringComparison.Ordinal);
    }

    [Fact]
    public void PanelWidget_PropertyInitialisation_IsHonoured()
    {
        var panel = new PanelWidget
        {
            Text = "via-properties",
            Title = "T",
            BorderStyle = new AnsiStyle(AnsiColor.Blue),
            TitleStyle = new AnsiStyle(AnsiColor.Green, default, AnsiDecoration.Bold),
        };
        string visible = Visible(Render(panel, maxWidth: 30));
        Assert.Contains("via-properties", visible, StringComparison.Ordinal);
    }

    [Fact]
    public void PanelWidget_ContentWiderThanPanel_StillRenders()
        => Assert.NotEmpty(Visible(Render(new PanelWidget(new string('w', 200)), maxWidth: 20)));

    [Fact]
    public void TableWidget_RendersHeadersAndRows()
    {
        var table = new TableWidget();
        table.AddColumn("Name");
        table.AddColumn("Qty");
        table.AddRow("apple", "3");
        string visible = Visible(Render(table, maxWidth: 40));

        Assert.Contains("Name", visible, StringComparison.Ordinal);
        Assert.Contains("apple", visible, StringComparison.Ordinal);
        Assert.Contains('3', visible);
    }

    [Fact]
    public void GetMarkupVisualLength_CountsOnlyVisibleCharacters()
    {
        Assert.Equal(6, AnsiConsoleApi.GetMarkupVisualLength("[red]danger[/]"));
        Assert.Equal(5, AnsiConsoleApi.GetMarkupVisualLength("plain"));
        Assert.Equal(0, AnsiConsoleApi.GetMarkupVisualLength(string.Empty));
        // No closing bracket means no tag at all, so every character is visible.
        Assert.Equal(11, AnsiConsoleApi.GetMarkupVisualLength("ab[unclosed"));
    }

    private static string Markup(string text, AnsiStyle baseStyle = default)
    {
        using var sw = new StringWriter(CultureInfo.InvariantCulture);
        Span<char> scratch = new char[8 * 1024];
        var buffer = new AnsiBuf(scratch, sw);
        AnsiConsoleApi.Markup(text.AsSpan(), ref buffer, baseStyle);
        buffer.Flush();
        return sw.ToString();
    }

    [Fact]
    public void Markup_NestedTags_InheritOuterStyle()
    {
        // Inner tag sets only a decoration, so the outer colour must carry through.
        string ansi = Markup("[red]a[bold]b[/][/]");
        Assert.Equal("ab", Visible(ansi));
        Assert.Contains("91", ansi, StringComparison.Ordinal);
        Assert.Contains('1', ansi);
    }

    [Fact]
    public void Markup_OnKeyword_SetsBackground()
    {
        string ansi = Markup("[white on blue]x[/]");
        Assert.Equal("x", Visible(ansi));
        Assert.Contains("104", ansi, StringComparison.Ordinal);   // blue background
    }

    [Fact]
    public void Markup_UnknownTag_IsIgnoredButConsumed()
    {
        // The tag is not a colour or decoration, so it contributes no styling and
        // must not leak into the visible text either.
        Assert.Equal("text", Visible(Markup("[notacolour]text[/]")));
    }

    [Fact]
    public void Markup_UnclosedBracket_TreatsRemainderAsText()
        => Assert.Equal("ab[unclosed", Visible(Markup("ab[unclosed")));

    [Fact]
    public void Markup_PlainText_PassesThrough()
        => Assert.Equal("nothing special", Visible(Markup("nothing special")));

    [Fact]
    public void Markup_StrayCloseTagAtTopLevel_DoesNotUnderflow()
        => Assert.Equal("x", Visible(Markup("[/]x")));

    [Fact]
    public void Markup_GreyAndGrayAreBothAccepted()
        => Assert.Equal(Markup("[grey]g[/]"), Markup("[gray]g[/]"));
}
