// See AnsiColorAndStyleTests for why the product's Color must be aliased here.
using System.Globalization;
using AnsiBuf = HeroParser.Console.AnsiBuffer;
using AnsiConsoleApi = HeroParser.Console.AnsiConsole;
using AnsiStyle = HeroParser.Console.Style;
using CompatMarkup = HeroParser.Console.Markup;
using Xunit;

namespace HeroParser.Tests.ConsoleUi;

/// <summary>
/// Covers literal brackets in markup.
///
/// <see cref="CompatMarkup.Escape"/> doubles brackets so that arbitrary text — file paths,
/// exception messages, anything a caller passes through — can be rendered without being
/// mistaken for a style tag. The parser has to undo that doubling, or escaping silently
/// deletes the very characters it was protecting.
/// </summary>
[Trait("Category", "Unit")]
public class MarkupEscapingTests
{
    private static string Render(string markup)
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        Span<char> scratch = new char[4096];
        var buffer = new AnsiBuf(scratch, writer);
        AnsiConsoleApi.Markup(markup.AsSpan(), ref buffer, AnsiStyle.Default);
        buffer.Flush();
        return Visible(writer.ToString());
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

    [Theory]
    [InlineData("[[", "[")]
    [InlineData("]]", "]")]
    [InlineData("[[info]]", "[info]")]
    [InlineData("a[[b]]c", "a[b]c")]
    [InlineData("[[[[", "[[")]
    [InlineData("100[[%]]", "100[%]")]
    public void EscapedBrackets_RenderAsLiterals(string markup, string expected)
        => Assert.Equal(expected, Render(markup));

    [Fact]
    public void EscapedBrackets_SurviveInsideAStyleTag()
        => Assert.Equal("[info] done", Render("[grey][[info]][/] done"));

    [Fact]
    public void EscapeThenRender_IsARoundTrip()
    {
        // Anything a caller escapes must come back out unchanged, tags or not.
        foreach (string original in new[] { "plain", "a[b]c", "[red]not a tag[/]", "[[", "]]", "100%", "C:\\dir\\[x]" })
        {
            Assert.Equal(original, Render(CompatMarkup.Escape(original)));
        }
    }

    [Fact]
    public void LoneClosingBracket_IsOrdinaryText()
        => Assert.Equal("a]b", Render("a]b"));

    [Fact]
    public void UnterminatedTag_IsOrdinaryText()
        => Assert.Equal("ab[unclosed", Render("ab[unclosed"));

    [Fact]
    public void StyleTagsStillApply()
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        Span<char> scratch = new char[4096];
        var buffer = new AnsiBuf(scratch, writer);
        AnsiConsoleApi.Markup("[red]x[/]".AsSpan(), ref buffer, AnsiStyle.Default);
        buffer.Flush();

        Assert.Contains("\x1b[91m", writer.ToString(), StringComparison.Ordinal);
        Assert.Equal("x", Visible(writer.ToString()));
    }

    [Theory]
    [InlineData("[[", 1)]
    [InlineData("]]", 1)]
    [InlineData("[[info]]", 6)]
    [InlineData("[red]abc[/]", 3)]
    [InlineData("a[[b]]c", 5)]
    [InlineData("ab[unclosed", 11)]
    [InlineData("a]b", 3)]
    public void VisualLength_CountsWhatIsActuallyDrawn(string markup, int expected)
    {
        // Widgets pad to this number, so it has to agree with the renderer exactly —
        // a mismatch shows up as a table column that does not line up.
        Assert.Equal(expected, AnsiConsoleApi.GetMarkupVisualLength(markup));
        Assert.Equal(expected, Render(markup).Length);
    }
}
