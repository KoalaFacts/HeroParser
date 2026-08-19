using HeroParser.Cli;
using HeroParser.Tests.ConsoleUi;
using Xunit;

namespace HeroParser.Tests.Cli;

/// <summary>
/// Covers the CLI's argument parser and command routing.
///
/// Every option here is a promise to the user — that -d takes the next token, that a
/// missing --output is caught before any work happens, that an unknown flag fails loudly
/// rather than being ignored. Main is an ordinary method, so all of it can be driven
/// directly; only the AI commands are exercised through their guard paths, since running
/// them for real would shell out to a locally installed agent.
/// </summary>
[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
[Collection(AnsiConsoleCurrentCollection.NAME)]
public sealed class ProgramArgumentTests : IDisposable
{
    private readonly List<string> tempFiles = [];

    public void Dispose()
    {
        foreach (string path in tempFiles.Where(File.Exists))
        {
            File.Delete(path);
        }
    }

    private string Csv(string contents = "Name,Age\nAlice,30\nBob,25")
    {
        string path = Path.Join(Path.GetTempPath(), Path.GetRandomFileName() + ".csv");
        File.WriteAllText(path, contents);
        tempFiles.Add(path);
        return path;
    }

    private string OutputPath(string extension = ".jsonl")
    {
        string path = Path.Join(Path.GetTempPath(), Path.GetRandomFileName() + extension);
        tempFiles.Add(path);
        return path;
    }

    // ---- help ------------------------------------------------------------------

    [Theory]
    [InlineData("-h")]
    [InlineData("--help")]
    [InlineData("help")]
    public async Task GlobalHelpFlags_Succeed(string flag)
        => Assert.Equal(0, await Program.Main([flag]));

    [Theory]
    [InlineData("detect")]
    [InlineData("validate")]
    [InlineData("profile")]
    [InlineData("convert")]
    [InlineData("repair")]
    [InlineData("schema")]
    [InlineData("query")]
    [InlineData("ask")]
    [InlineData("translate")]
    [InlineData("bogus")]
    public async Task PerCommandHelp_Succeeds(string command)
        => Assert.Equal(0, await Program.Main([command, "--help"]));

    // ---- option parsing --------------------------------------------------------

    [Theory]
    [InlineData("-d")]
    [InlineData("--delimiter")]
    [InlineData("-s")]
    [InlineData("--sheet")]
    [InlineData("-sh")]
    [InlineData("--shape")]
    [InlineData("-p")]
    [InlineData("--ai-provider")]
    [InlineData("-k")]
    [InlineData("--ai-key")]
    [InlineData("-m")]
    [InlineData("--model")]
    [InlineData("-o")]
    [InlineData("--output")]
    public async Task OptionWithNoValue_Fails(string option)
        => Assert.Equal(1, await Program.Main(["detect", "file.csv", option]));

    [Fact]
    public async Task UnknownOption_Fails()
        => Assert.Equal(1, await Program.Main(["detect", "file.csv", "--nope"]));

    [Fact]
    public async Task UnknownCommand_Fails()
        => Assert.Equal(1, await Program.Main(["frobnicate"]));

    [Fact]
    public async Task BatchSize_RejectsNonNumericValues()
        => Assert.Equal(1, await Program.Main(["translate", Csv(), "x", "-o", OutputPath(), "-b", "many"]));

    [Fact]
    public async Task Delimiter_AcceptsEscapedTab()
    {
        // "\t" arrives as two literal characters from a shell, so it needs decoding.
        string path = Csv("Name\tAge\nAlice\t30");
        Assert.Equal(0, await Program.Main(["validate", path, "-d", "\\t"]));
    }

    [Fact]
    public async Task Delimiter_TakesTheFirstCharacterOfTheValue()
        => Assert.Equal(0, await Program.Main(["validate", Csv("Name;Age\nAlice;30"), "--delimiter", ";"]));

    [Fact]
    public async Task Delimiter_EmptyValue_IsIgnored()
        => Assert.Equal(0, await Program.Main(["validate", Csv(), "-d", ""]));

    // ---- command routing -------------------------------------------------------

    [Theory]
    [InlineData("detect")]
    [InlineData("validate")]
    [InlineData("profile")]
    [InlineData("schema")]
    public async Task FileCommand_WithNoPath_Fails(string command)
        => Assert.Equal(1, await Program.Main([command]));

    [Theory]
    [InlineData("detect")]
    [InlineData("validate")]
    [InlineData("profile")]
    [InlineData("schema")]
    public async Task FileCommand_WithAPath_Succeeds(string command)
        => Assert.Equal(0, await Program.Main([command, Csv()]));

    [Fact]
    public async Task Profile_AcceptsASheetName()
        => Assert.Equal(0, await Program.Main(["profile", Csv(), "--sheet", "Sheet1"]));

    [Fact]
    public async Task Convert_WritesTheOutputFile()
    {
        string outPath = OutputPath();
        Assert.Equal(0, await Program.Main(["convert", Csv(), outPath]));
        Assert.True(File.Exists(outPath), "convert should have produced its output file");
    }

    [Fact]
    public async Task Convert_TakesTheOutputFromTheFlag()
    {
        string outPath = OutputPath();
        Assert.Equal(0, await Program.Main(["convert", Csv(), "--output", outPath]));
        Assert.True(File.Exists(outPath), "convert should honour --output");
    }

    [Fact]
    public async Task Convert_AcceptsAJsonlShape()
    {
        // The openai shape maps a question/answer pair, so the input has to carry one.
        string input = Csv("Question,Answer\nWhat is 2+2?,4");
        string outPath = OutputPath();

        Assert.Equal(0, await Program.Main(["convert", input, outPath, "--shape", "openai"]));
        Assert.Contains("messages", File.ReadAllText(outPath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Convert_WithNoInput_Fails()
        => Assert.Equal(1, await Program.Main(["convert"]));

    [Fact]
    public async Task Convert_WithNoOutput_Fails()
        => Assert.Equal(1, await Program.Main(["convert", Csv()]));

    [Fact]
    public async Task Repair_WritesTheOutputFile()
    {
        string outPath = OutputPath(".csv");
        Assert.Equal(0, await Program.Main(["repair", Csv("Name,Age\n```\nAlice,30"), outPath]));
        Assert.True(File.Exists(outPath), "repair should have produced its output file");
    }

    [Fact]
    public async Task Repair_WithNoInput_Fails()
        => Assert.Equal(1, await Program.Main(["repair"]));

    [Fact]
    public async Task Repair_WithNoOutput_Fails()
        => Assert.Equal(1, await Program.Main(["repair", Csv()]));

    [Theory]
    [InlineData("query")]
    [InlineData("ask")]
    public async Task Query_WithoutAPrompt_Fails(string command)
        => Assert.Equal(1, await Program.Main([command, Csv()]));

    [Fact]
    public async Task Translate_WithoutAPrompt_Fails()
        => Assert.Equal(1, await Program.Main(["translate", Csv()]));

    [Fact]
    public async Task Translate_WithoutAnOutput_Fails()
        => Assert.Equal(1, await Program.Main(["translate", Csv(), "make it french"]));

    [Fact]
    public async Task MissingFile_IsReportedWithoutFailingTheProcess()
    {
        // A missing input is a user error the command reports; the CLI still exits 0
        // because the command itself ran to completion.
        Assert.Equal(0, await Program.Main(["detect", "definitely-not-here.csv"]));
    }

    [Fact]
    public async Task SingleExistingFileArgument_ProfilesItWhenOutputIsRedirected()
    {
        // Test hosts always redirect stdout, which is the signal the CLI uses to decide
        // it is not attached to a terminal and should not open the wizard.
        Assert.Equal(0, await Program.Main([Csv()]));
    }

    [Fact]
    public async Task NoArguments_PrintsHelpWhenOutputIsRedirected()
        => Assert.Equal(0, await Program.Main([]));
}
