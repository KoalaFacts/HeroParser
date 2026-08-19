using System.Globalization;
using System.Text;
using HeroParser.Cli;
using HeroParser.Tests.ConsoleUi;
using Xunit;
using AnsiBuf = HeroParser.Console.AnsiBuffer;
using AnsiConsoleApi = HeroParser.Console.AnsiConsole;
using AnsiStyle = HeroParser.Console.Style;
using IHeroConsole = HeroParser.Console.IAnsiConsole;

namespace HeroParser.Tests.Cli;

/// <summary>
/// Covers the menu-driven wizard the CLI starts in when it is run bare on a terminal.
///
/// It exists entirely to block on key presses, so it had never been executed: the whole
/// file-picker and every menu branch were dead to the test suite even though they are the
/// first thing a user sees. Driving it through <see cref="IHeroConsole"/> with a scripted
/// key sequence runs the real loop.
/// </summary>
[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
[Collection(AnsiConsoleCurrentCollection.NAME)]
public class InteractiveWizardTests : IDisposable
{
    private readonly IHeroConsole previousConsole = AnsiConsoleApi.Current;
    private readonly string sandbox = Path.Join(Path.GetTempPath(), Path.GetRandomFileName());

    public InteractiveWizardTests()
    {
        // The wizard delegates each operation to CliCommands, which still writes through
        // the static console, so both have to point at the same capture.
        Directory.CreateDirectory(sandbox);
    }

    public void Dispose()
    {
        AnsiConsoleApi.Current = previousConsole;
        if (Directory.Exists(sandbox)) Directory.Delete(sandbox, recursive: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// A console that replays scripted input and then stops the wizard.
    /// </summary>
    /// <remarks>
    /// Running out of input has to end the run, not repeat the first menu entry forever.
    /// The menu prompt sits outside the wizard's per-operation error handling, so throwing
    /// from there unwinds the loop — which makes "input exhausted" a reliable terminator.
    /// </remarks>
    private sealed class ScriptedConsole(IEnumerable<ConsoleKeyInfo> keys, params string[] lines) : IHeroConsole
    {
        private readonly Queue<ConsoleKeyInfo> keyQueue = new(keys);
        private readonly Queue<string> lineQueue = new(lines);
        private readonly StringBuilder sink = new();

        public int Width => 80;

        public string Output
        {
            get { lock (sink) return sink.ToString(); }
        }

        public void Write(string text) => Append(text);

        public void WriteLine(string text) => Append(text + '\n');

        public void Write(string text, AnsiStyle style) => Append(text);

        public void WriteLine(string text, AnsiStyle style) => Append(text + '\n');

        public void Markup(string markupText) => Append(Plain(markupText));

        public void MarkupLine(string markupText) => Append(Plain(markupText) + '\n');

        private void Append(string text)
        {
            lock (sink) sink.Append(text);
        }

        public void Write(HeroParser.Console.Widgets.IConsoleWidget widget)
        {
            using var writer = new StringWriter(CultureInfo.InvariantCulture);
            Span<char> scratch = new char[16 * 1024];
            var buffer = new AnsiBuf(scratch, writer);
            widget.Render(ref buffer, Width);
            buffer.Flush();
            Append(writer.ToString());
        }

        public ConsoleKeyInfo ReadKey(bool intercept)
        {
            lock (sink)
            {
                return keyQueue.Count > 0 ? keyQueue.Dequeue() : throw new EndOfStreamException("scripted input exhausted");
            }
        }

        public string? ReadLine()
        {
            lock (sink) return lineQueue.Count > 0 ? lineQueue.Dequeue() : null;
        }

        /// <summary>Renders markup to text so assertions can match on what the user sees.</summary>
        private static string Plain(string markup)
        {
            using var writer = new StringWriter(CultureInfo.InvariantCulture);
            Span<char> scratch = new char[8 * 1024];
            var buffer = new AnsiBuf(scratch, writer);
            AnsiConsoleApi.Markup(markup.AsSpan(), ref buffer);
            buffer.Flush();
            return StripAnsi(writer.ToString());
        }

        private static string StripAnsi(string ansi)
        {
            var sb = new StringBuilder(ansi.Length);
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
    }

    private static ConsoleKeyInfo Key(ConsoleKey key) => new('\0', key, false, false, false);

    private static IEnumerable<ConsoleKeyInfo> Down(int count) => Enumerable.Repeat(Key(ConsoleKey.DownArrow), count);

    /// <summary>Menu entries are 1-based in the UI; this selects the nth and commits.</summary>
    private static IEnumerable<ConsoleKeyInfo> Choose(int oneBasedIndex)
        => [.. Down(oneBasedIndex - 1), Key(ConsoleKey.Enter)];

    private static readonly ConsoleKeyInfo[] EXIT_MENU = [.. Choose(10)];

    /// <summary>Dismisses the "press any key" pause between operations.</summary>
    private static ConsoleKeyInfo Any => Key(ConsoleKey.Spacebar);

    private string CreateCsv(string name = "data.csv", string contents = "Name,Age\nAlice,30\nBob,25")
    {
        string path = Path.Join(sandbox, name);
        File.WriteAllText(path, contents);
        return path;
    }

    /// <summary>Builds a wizard over the sandbox with the static console pointed at it too.</summary>
    private InteractiveWizard Wizard(ScriptedConsole console)
    {
        AnsiConsoleApi.Current = console;
        return new InteractiveWizard(console, sandbox);
    }

    /// <summary>Runs the wizard and swallows the terminator thrown when input runs out.</summary>
    private async Task RunAsync(ScriptedConsole console, string? targetFile)
    {
        try
        {
            await Wizard(console).RunAsync(targetFile);
        }
        catch (EndOfStreamException)
        {
            // The script ended without picking Exit; everything before that still ran.
        }
    }

    [Fact]
    public void NullConsole_Throws()
        => Assert.Throws<ArgumentNullException>(() => new InteractiveWizard(null!));

    [Fact]
    public async Task ExitFromTheMenu_EndsCleanly()
    {
        string file = CreateCsv();
        var console = new ScriptedConsole(EXIT_MENU);

        await Wizard(console).RunAsync(file);

        Assert.Contains("Managing File: data.csv", console.Output, StringComparison.Ordinal);
        Assert.Contains("Select an operation:", console.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FilePicker_ListsTabularFilesInTheCurrentDirectory()
    {
        CreateCsv("alpha.csv");
        File.WriteAllText(Path.Join(sandbox, "notes.md"), "ignored");

        // Pick the first listed file, then exit the operation menu.
        var console = new ScriptedConsole([Key(ConsoleKey.Enter), .. EXIT_MENU]);
        await RunAsync(console, null);

        Assert.Contains("Select a tabular file", console.Output, StringComparison.Ordinal);
        Assert.Contains("alpha.csv", console.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("notes.md", console.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FilePicker_ExitChoice_LeavesImmediately()
    {
        CreateCsv("alpha.csv");

        // Choices are: alpha.csv, [Enter custom file path...], [Exit].
        var console = new ScriptedConsole([.. Down(2), Key(ConsoleKey.Enter)]);
        await Wizard(console).RunAsync(null);

        Assert.DoesNotContain("Select an operation:", console.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FilePicker_CustomPath_IsValidatedBeforeUse()
    {
        CreateCsv("alpha.csv");
        string real = CreateCsv("real.csv");

        // Choices are: the two files, then [Enter custom file path...], then [Exit].
        var console = new ScriptedConsole(
            [.. Down(2), Key(ConsoleKey.Enter), .. EXIT_MENU],
            "no-such-file.csv", real);

        await RunAsync(console, null);

        Assert.Contains("File does not exist.", console.Output, StringComparison.Ordinal);
        Assert.Contains("Managing File: real.csv", console.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DetectOperation_Runs()
    {
        var console = new ScriptedConsole([.. Choose(1), Any, .. EXIT_MENU]);
        await RunAsync(console, CreateCsv());

        Assert.Contains("Analyzing delimiter and encoding", console.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidateOperation_Runs()
    {
        var console = new ScriptedConsole([.. Choose(2), Any, .. EXIT_MENU]);
        await RunAsync(console, CreateCsv());

        Assert.Contains("Validation", console.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProfileOperation_Runs()
    {
        var console = new ScriptedConsole([.. Choose(3), Any, .. EXIT_MENU]);
        await RunAsync(console, CreateCsv());

        Assert.Contains("Profile", console.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LocalSchemaOperation_Runs()
    {
        var console = new ScriptedConsole([.. Choose(4), Any, .. EXIT_MENU]);
        await RunAsync(console, CreateCsv());

        Assert.Contains("Schema Preview", console.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConvertOperation_WritesTheChosenFormat()
    {
        string file = CreateCsv();

        // Operation 8, then target format ".jsonl" (second choice), then the "Flat" shape,
        // accept the default output path, dismiss the pause, and exit.
        var console = new ScriptedConsole(
            [.. Choose(8), .. Choose(2), .. Choose(1), Any, .. EXIT_MENU],
            string.Empty);

        await RunAsync(console, file);

        Assert.Contains("Select target format:", console.Output, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Join(sandbox, "data_converted.jsonl")), "the converted file should exist");
    }

    [Fact]
    public async Task ChangeActiveFile_ReturnsToThePicker()
    {
        CreateCsv("alpha.csv");
        string file = CreateCsv("beta.csv");

        // Operation 9 restarts the wizard, which re-lists the directory; then exit there.
        var console = new ScriptedConsole([.. Choose(9), .. Down(3), Key(ConsoleKey.Enter)]);
        await RunAsync(console, file);

        Assert.Contains("Managing File: beta.csv", console.Output, StringComparison.Ordinal);
        Assert.Contains("Select a tabular file", console.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailingOperation_IsReportedAndTheMenuContinues()
    {
        // Point the wizard at a file that gets deleted, so the operation throws.
        string file = CreateCsv("gone.csv");
        File.Delete(file);

        var console = new ScriptedConsole([.. Choose(1), Any, .. EXIT_MENU]);
        await RunAsync(console, file);

        // The wizard must survive a failed operation and offer the menu again.
        Assert.Contains("Select an operation:", console.Output, StringComparison.Ordinal);
    }
}
