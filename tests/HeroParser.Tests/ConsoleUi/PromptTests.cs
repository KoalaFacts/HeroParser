// See AnsiColorAndStyleTests for why the product's Color must be aliased here.
using System.Globalization;
using HeroParser.Console.Prompts;
using AnsiColor = HeroParser.Console.Color;
using AnsiConsoleApi = HeroParser.Console.AnsiConsole;
using AnsiStyle = HeroParser.Console.Style;
using IHeroConsole = HeroParser.Console.IAnsiConsole;
using SystemConsole = HeroParser.Console.SystemAnsiConsole;
using Xunit;

namespace HeroParser.Tests.ConsoleUi;

/// <summary>
/// Drives <see cref="SelectionPrompt{T}"/> and <see cref="TextPrompt{T}"/> through the
/// <see cref="IHeroConsole"/> seam.
///
/// Both prompts block on real key and line input, so neither had ever been executed.
/// Rendering to a StringWriter and feeding input from a StringReader makes the whole
/// interaction loop — navigation, wrap-around, defaults, conversion, validation retry —
/// reachable in-process.
/// </summary>
[Trait("Category", "Unit")]
public class PromptTests
{
    /// <summary>Builds a console whose output is captured and whose input is scripted.</summary>
    private static (IHeroConsole Console, StringWriter Output) Scripted(string input)
    {
        var output = new StringWriter(CultureInfo.InvariantCulture);
        return (new SystemConsole(output, new StringReader(input)), output);
    }

    /// <summary>
    /// Strips CSI escape sequences. Prompts emit cursor moves and line erases whose
    /// final byte is not 'm', so stopping at 'm' would swallow the text between them.
    /// </summary>
    private static string Visible(string ansi)
    {
        var sb = new System.Text.StringBuilder(ansi.Length);
        int i = 0;
        while (i < ansi.Length)
        {
            if (ansi[i] == '\x1b')
            {
                i++;
                if (i < ansi.Length && ansi[i] == '[') i++;
                while (i < ansi.Length && ansi[i] is >= '0' and <= '?') i++;   // parameters
                while (i < ansi.Length && ansi[i] is >= ' ' and <= '/') i++;   // intermediates
                if (i < ansi.Length) i++;                                       // final byte
                continue;
            }
            sb.Append(ansi[i]);
            i++;
        }
        return sb.ToString();
    }

    [Fact]
    public void SelectionPrompt_EnterImmediately_ReturnsFirstChoice()
    {
        var (console, output) = Scripted("\r");
        var prompt = new SelectionPrompt<string>("Pick one").AddChoices(["alpha", "beta", "gamma"]);

        Assert.Equal("alpha", prompt.Show(console));
        string visible = Visible(output.ToString());
        Assert.Contains("Pick one", visible, StringComparison.Ordinal);
        Assert.Contains("alpha", visible, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectionPrompt_NoChoices_Throws()
        => Assert.Throws<InvalidOperationException>(() => new SelectionPrompt<string>("t").Show(Scripted("\r").Console));

    [Fact]
    public void SelectionPrompt_NullConsole_Throws()
        => Assert.Throws<ArgumentNullException>(
            () => new SelectionPrompt<string>("t").AddChoice("a").Show(null!));

    [Fact]
    public void SelectionPrompt_RestoresCursorVisibility()
    {
        var (console, output) = Scripted("\r");
        new SelectionPrompt<string>("t").AddChoice("only").Show(console);

        // The prompt hides the cursor while drawing; leaving it hidden would corrupt
        // the host terminal for everything that follows.
        string raw = output.ToString();
        Assert.Contains("\x1b[?25l", raw, StringComparison.Ordinal);
        Assert.Contains("\x1b[?25h", raw, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectionPrompt_SingleChoice_IsSelectable()
        => Assert.Equal("only", new SelectionPrompt<string>("t").AddChoice("only").Show(Scripted("\r").Console));

    [Fact]
    public void SelectionPrompt_BuilderMethodsAreFluent()
    {
        var prompt = new SelectionPrompt<string>();
        Assert.Same(prompt, prompt.Title("t"));
        Assert.Same(prompt, prompt.PageSize(5));
        Assert.Same(prompt, prompt.MoreChoicesText("more"));
        Assert.Same(prompt, prompt.AddChoice("a"));
        Assert.Same(prompt, prompt.AddChoices(["b", "c"]));
        Assert.Same(prompt, prompt.HighlightStyle(new AnsiStyle(AnsiColor.Green)));

        Assert.Equal("a", prompt.Show(Scripted("\r").Console));
    }

    [Fact]
    public void SelectionPrompt_NonNavigationKey_IsIgnored()
    {
        // 'x' is neither navigation nor commit, so the loop must keep waiting.
        var (console, _) = Scripted("x\r");
        Assert.Equal("first", new SelectionPrompt<string>("t").AddChoices(["first", "second"]).Show(console));
    }

    [Fact]
    public void TextPrompt_ReturnsTypedInput()
    {
        var (console, output) = Scripted("hello\n");
        Assert.Equal("hello", new TextPrompt<string>("Name").Show(console));
        Assert.Contains("Name", Visible(output.ToString()), StringComparison.Ordinal);
    }

    [Fact]
    public void TextPrompt_NullConsole_Throws()
        => Assert.Throws<ArgumentNullException>(() => new TextPrompt<string>("t").Show(null!));

    [Fact]
    public void TextPrompt_Int_ConvertsBuiltInType()
        => Assert.Equal(42, new TextPrompt<int>("N").Show(Scripted("42\n").Console));

    [Fact]
    public void TextPrompt_Double_ConvertsBuiltInType()
        => Assert.Equal(1.5, new TextPrompt<double>("D").Show(Scripted("1.5\n").Console));

    [Fact]
    public void TextPrompt_UnsupportedTypeWithoutConverter_Throws()
        => Assert.Throws<InvalidOperationException>(
            () => new TextPrompt<Guid>("G").Show(Scripted("x\n").Console));

    [Fact]
    public void TextPrompt_CustomConverter_IsUsed()
    {
        var prompt = new TextPrompt<Guid>("G").WithConverter(Guid.Parse);
        var expected = new Guid("11112222-3333-4444-5555-666677778888");
        Assert.Equal(expected, prompt.Show(Scripted(expected + "\n").Console));
    }

    [Fact]
    public void TextPrompt_EmptyInputWithDefault_ReturnsDefault()
    {
        var (console, output) = Scripted("\n");
        Assert.Equal("fallback", new TextPrompt<string>("Name").DefaultValue("fallback").Show(console));
        // The default is advertised in the prompt line.
        Assert.Contains("fallback", Visible(output.ToString()), StringComparison.Ordinal);
    }

    [Fact]
    public void TextPrompt_BadInputThenGood_RetriesUntilParsed()
    {
        var (console, output) = Scripted("notanumber\n7\n");
        Assert.Equal(7, new TextPrompt<int>("N").Show(console));
        Assert.Contains("Invalid input format", Visible(output.ToString()), StringComparison.Ordinal);
    }

    [Fact]
    public void TextPrompt_FailedValidation_RetriesAndShowsMessage()
    {
        var (console, output) = Scripted("3\n11\n");
        var prompt = new TextPrompt<int>("N")
            .Validate(value => value > 10 ? ValidationResult.Success() : ValidationResult.Error("too small"));

        Assert.Equal(11, prompt.Show(console));
        Assert.Contains("too small", Visible(output.ToString()), StringComparison.Ordinal);
    }

    [Fact]
    public void TextPrompt_FailedValidationWithBlankMessage_FallsBackToDefaultText()
    {
        var (console, output) = Scripted("3\n11\n");
        var prompt = new TextPrompt<int>("N")
            .Validate(value => value > 10 ? ValidationResult.Success() : ValidationResult.Error(string.Empty));

        Assert.Equal(11, prompt.Show(console));
        Assert.Contains("Invalid input", Visible(output.ToString()), StringComparison.Ordinal);
    }

    [Fact]
    public void TextPrompt_BuilderMethodsAreFluent()
    {
        var prompt = new TextPrompt<int>();
        Assert.Same(prompt, prompt.Title("t"));
        Assert.Same(prompt, prompt.DefaultValue(1));
        Assert.Same(prompt, prompt.WithConverter(int.Parse));
        Assert.Same(prompt, prompt.Validate(_ => ValidationResult.Success()));
    }

    [Fact]
    public void ValidationResult_CarriesSuccessAndMessage()
    {
        Assert.True(ValidationResult.Success().Successful);
        Assert.Equal(string.Empty, ValidationResult.Success().Message);
        Assert.False(ValidationResult.Error("bad").Successful);
        Assert.Equal("bad", ValidationResult.Error("bad").Message);
    }

    [Fact]
    public void AnsiConsole_PromptOverloads_UseCurrentConsole()
    {
        var previous = AnsiConsoleApi.Current;
        try
        {
            var (console, _) = Scripted("scripted\n");
            AnsiConsoleApi.Current = console;

            Assert.Equal("scripted", AnsiConsoleApi.Prompt(new TextPrompt<string>("T")));

            AnsiConsoleApi.Current = Scripted("\r").Console;
            Assert.Equal("one", AnsiConsoleApi.Prompt(new SelectionPrompt<string>("T").AddChoices(["one", "two"])));
        }
        finally
        {
            AnsiConsoleApi.Current = previous;
        }
    }
}
