using HeroParser.Console;
using HeroParser.Console.Widgets;

namespace HeroParser.AotTests.Tests;

/// <summary>
/// AOT compatibility tests for HeroParser.Console rendering widgets and core logic.
/// </summary>
public static class HeroParserConsoleAotTests
{
    public static void Run(TestRunner runner)
    {
        runner.PrintSection("HeroParser.Console AOT Tests");

        runner.Run("HeroParser.Console: Color and Style ANSI sequences", TestStyleFormatting);
        runner.Run("HeroParser.Console: TableWidget layout calculation", TestTableWidget);
        runner.Run("HeroParser.Console: Panel and Rule widgets", TestPanelAndRuleWidgets);
    }

    private static void TestStyleFormatting()
    {
        var style = new Style(Color.Red, Color.Blue, Decoration.Bold);
        Span<char> buf = stackalloc char[128];
        int written = style.FormatAnsi(buf);
        var result = buf[..written].ToString();

        if (string.IsNullOrEmpty(result))
            throw new Exception("Styled ANSI sequence formatting returned empty string");
    }

    private static void TestTableWidget()
    {
        var table = new TableWidget()
            .AddColumn("Col A")
            .AddColumn("Col B")
            .AddRow("Val A", "Val B");

        Span<char> charBuf = stackalloc char[4096];
        var buffer = new AnsiBuffer(charBuf);
        table.Render(ref buffer, 40);

        var output = charBuf[..buffer.Position].ToString();
        if (!output.Contains("Col A") || !output.Contains("Val B"))
            throw new Exception($"Table rendering did not produce expected output. Output: {output}");
    }

    private static void TestPanelAndRuleWidgets()
    {
        var text = new TextWidget("Wrapped test text");
        var panel = new PanelWidget("Inside Panel", "Panel Title");
        var rule = new RuleWidget("Rule Label");

        Span<char> charBuf = stackalloc char[4096];
        var buffer = new AnsiBuffer(charBuf);

        text.Render(ref buffer, 20);
        panel.Render(ref buffer, 20);
        rule.Render(ref buffer, 20);

        var output = charBuf[..buffer.Position].ToString();
        if (string.IsNullOrEmpty(output))
            throw new Exception("Widget rendering returned empty buffer");
    }
}
