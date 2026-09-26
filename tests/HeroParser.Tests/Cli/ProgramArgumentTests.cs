using HeroParser.Cli;
using HeroParser.Tests.ConsoleUi;
using System.Text;
using System.Text.Json;
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
    [InlineData("inspect")]
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

    [Theory]
    [InlineData("0")]
    [InlineData("10001")]
    [InlineData("many")]
    public async Task Inspect_RejectsInvalidSampleRows(string value)
        => Assert.Equal(1, await Program.Main(["inspect", Csv(), "--sample-rows", value]));

    [Fact]
    public async Task Inspect_RequiresSampleRowsValue()
        => Assert.Equal(1, await Program.Main(["inspect", Csv(), "--sample-rows"]));

    [Fact]
    public async Task SampleRows_OnAnotherCommand_Fails()
        => Assert.Equal(1, await Program.Main(["validate", Csv(), "--sample-rows", "10"]));

    [Fact]
    public async Task ImportPlan_InspectValidateAndConvert_ReuseTheSameDelimiter()
    {
        string input = Csv("Name;Age\nAlice;30\nBob;25\n");
        string planPath = OutputPath(".json");
        string output = OutputPath();

        Assert.Equal(0, await Program.Main(["inspect", input, "--save-plan", planPath]));
        using (var document = JsonDocument.Parse(File.ReadAllText(planPath)))
        {
            var plan = document.RootElement;
            Assert.Equal(1, plan.GetProperty("version").GetInt32());
            Assert.Equal(";", plan.GetProperty("delimiter").GetString());
            Assert.Equal(2, plan.GetProperty("expectedColumnCount").GetInt32());
            Assert.Equal(2, plan.GetProperty("sampledDataRows").GetInt32());
        }

        Assert.Equal(0, await Program.Main(["validate", input, "--plan", planPath]));
        Assert.Equal(0, await Program.Main(["convert", input, output, "--plan", planPath]));
        Assert.Contains("\"Name\":\"Alice\"", File.ReadAllText(output), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ImportPlan_FullValidationFindsAnomalyBeyondInspectedRows()
    {
        string input = Csv("Name;Age\nAlice;30\nBob\n");
        string planPath = OutputPath(".json");
        string reportPath = OutputPath(".json");
        string output = OutputPath();

        Assert.Equal(0, await Program.Main(["inspect", input, "--delimiter", ";", "--sample-rows", "1", "--save-plan", planPath]));
        Assert.Equal(1, await Program.Main(["validate", input, "--plan", planPath, "--report", reportPath]));
        using (var report = JsonDocument.Parse(File.ReadAllText(reportPath)))
        {
            Assert.False(report.RootElement.GetProperty("valid").GetBoolean());
            Assert.False(report.RootElement.GetProperty("stoppedEarly").GetBoolean());
            Assert.Equal(3, report.RootElement.GetProperty("validatedRows").GetInt32());
            Assert.Equal(3, report.RootElement.GetProperty("errors")[0].GetProperty("rowNumber").GetInt32());
        }
        Assert.Equal(1, await Program.Main(["convert", input, output, "--plan", planPath]));
        Assert.False(File.Exists(output));
    }

    [Fact]
    public async Task ImportPlan_ConvertsQuotedNewlinesAndWideCsv()
    {
        string[] headers = [.. Enumerable.Range(0, 101).Select(i => $"C{i}")];
        string[] values = [.. Enumerable.Repeat("value", 101)];
        values[0] = "\"first\nsecond\"";
        string input = Csv(string.Join(';', headers) + "\n" + string.Join(';', values) + "\n");
        string planPath = OutputPath(".json");
        string output = OutputPath();

        Assert.Equal(0, await Program.Main(["inspect", input, "--delimiter", ";", "--save-plan", planPath]));
        Assert.Equal(0, await Program.Main(["validate", input, "--plan", planPath]));
        Assert.Equal(0, await Program.Main(["convert", input, output, "--plan", planPath]));
        using var result = JsonDocument.Parse(File.ReadAllText(output));
        Assert.Equal("first\nsecond", result.RootElement.GetProperty("C0").GetString());
        Assert.Equal("value", result.RootElement.GetProperty("C100").GetString());
    }

    [Fact]
    public async Task ImportPlan_TsvCanBeValidatedAndConverted()
    {
        string input = OutputPath(".tsv");
        File.WriteAllText(input, "Name\tAge\nAlice\t30\n");
        string planPath = OutputPath(".json");
        string output = OutputPath();

        Assert.Equal(0, await Program.Main(["inspect", input, "--save-plan", planPath]));
        Assert.Equal(0, await Program.Main(["validate", input, "--plan", planPath]));
        Assert.Equal(0, await Program.Main(["convert", input, output, "--plan", planPath]));
        Assert.Contains("\"Name\":\"Alice\"", File.ReadAllText(output), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ImportPlan_ConvertsMoreThanDefaultRowLimit()
    {
        string input = Csv("Name,Age\n" + string.Concat(Enumerable.Repeat("Alice,30\n", 100_001)));
        string planPath = OutputPath(".json");
        string output = OutputPath();

        Assert.Equal(0, await Program.Main(["inspect", input, "--delimiter", ",", "--save-plan", planPath]));
        Assert.Equal(0, await Program.Main(["validate", input, "--plan", planPath]));
        Assert.Equal(0, await Program.Main(["convert", input, output, "--plan", planPath]));
        Assert.Equal(100_001, File.ReadLines(output).Count());
    }

    [Fact]
    public async Task ImportPlan_SchemaChecksTheSampledHeaderWidth()
    {
        string input = Csv("Name;Age\nAlice;30\n");
        string planPath = OutputPath(".json");
        Assert.Equal(0, await Program.Main(["inspect", input, "--delimiter", ";", "--save-plan", planPath]));

        Assert.Equal(0, await Program.Main(["schema", input, "--plan", planPath]));

        File.WriteAllText(input, "Name;Age;City\nAlice;30;Sydney\n");
        Assert.Equal(1, await Program.Main(["schema", input, "--plan", planPath]));
    }

    [Fact]
    public async Task ImportPlan_RejectsConflictsAndUnsupportedUse()
    {
        string input = Csv("Name;Age\nAlice;30\n");
        string planPath = OutputPath(".json");
        Assert.Equal(0, await Program.Main(["inspect", input, "--delimiter", ";", "--save-plan", planPath]));

        Assert.Equal(1, await Program.Main(["validate", input, "--plan", planPath, "--delimiter", ","]));
        Assert.Equal(1, await Program.Main(["profile", input, "--plan", planPath]));
        Assert.Equal(1, await Program.Main(["validate", input, "--save-plan", OutputPath(".json")]));
        Assert.Equal(1, await Program.Main(["inspect", input, "--report", OutputPath(".json")]));
        Assert.Equal(1, await Program.Main(["convert", input, OutputPath(".csv"), "--plan", planPath]));
        Assert.Equal(1, await Program.Main(["convert", input, OutputPath(".txt"), "--plan", planPath]));
        string jsonlPlanPath = OutputPath();
        File.Copy(planPath, jsonlPlanPath);
        Assert.Equal(1, await Program.Main(["convert", input, jsonlPlanPath, "--plan", jsonlPlanPath]));
        Assert.Contains("\"version\": 1", File.ReadAllText(jsonlPlanPath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ImportPlan_RejectsCaseAliasOnCaseInsensitiveVolume()
    {
        string input = Csv("Name,Age\nAlice,30\n");
        string planPath = OutputPath(".jsonl");
        Assert.Equal(0, await Program.Main(["inspect", input, "--delimiter", ",", "--save-plan", planPath]));
        string alias = Path.Combine(Path.GetDirectoryName(planPath)!, Path.GetFileName(planPath).ToUpperInvariant());
        if (!File.Exists(alias))
            return;

        Assert.Equal(1, await Program.Main(["convert", input, alias, "--plan", planPath]));
        Assert.Contains("\"version\": 1", File.ReadAllText(planPath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ImportPlan_AllowsDistinctCaseSensitiveOutput()
    {
        string input = Csv("Name,Age\nAlice,30\n");
        string planPath = OutputPath(".jsonl");
        Assert.Equal(0, await Program.Main(["inspect", input, "--delimiter", ",", "--save-plan", planPath]));
        string output = Path.Combine(Path.GetDirectoryName(planPath)!, Path.GetFileName(planPath).ToUpperInvariant());
        if (File.Exists(output))
            return;
        tempFiles.Add(output);
        File.WriteAllText(output, "previous result");

        Assert.Equal(0, await Program.Main(["convert", input, output, "--plan", planPath]));
        Assert.Contains("\"Name\":\"Alice\"", File.ReadAllText(output), StringComparison.Ordinal);
        Assert.Contains("\"version\": 1", File.ReadAllText(planPath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ImportPlan_RejectsBadVersionAndDoesNotOverwriteFiles()
    {
        string input = Csv();
        string planPath = OutputPath(".json");
        File.WriteAllText(planPath, "{\"version\":2}");

        Assert.Equal(1, await Program.Main(["validate", input, "--plan", planPath]));
        Assert.Equal(1, await Program.Main(["inspect", input, "--delimiter", ",", "--save-plan", planPath]));
        Assert.Equal("{\"version\":2}", File.ReadAllText(planPath));
        Assert.Equal(1, await Program.Main(["inspect", input, "--delimiter", ",", "--save-plan", input]));
        Assert.StartsWith("Name,Age", File.ReadAllText(input), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidationReport_DoesNotOverwriteInputOrAnExistingReport()
    {
        string input = Csv();
        string reportPath = OutputPath(".json");
        File.WriteAllText(reportPath, "keep");

        Assert.Equal(1, await Program.Main(["validate", input, "--report", input]));
        Assert.Equal(1, await Program.Main(["validate", input, "--report", reportPath]));
        Assert.Equal("keep", File.ReadAllText(reportPath));
        Assert.StartsWith("Name,Age", File.ReadAllText(input), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidationReport_MarksTheBoundedErrorLimit()
    {
        string input = Csv("A,B\n" + string.Join('\n', Enumerable.Repeat("only-one", 105)) + "\n");
        string reportPath = OutputPath(".json");

        Assert.Equal(1, await Program.Main(["validate", input, "--delimiter", ",", "--report", reportPath]));
        using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
        Assert.True(report.RootElement.GetProperty("stoppedEarly").GetBoolean());
        Assert.Equal(100, report.RootElement.GetProperty("errors").GetArrayLength());
    }

    [Fact]
    public async Task ValidationReport_Utf16AlsoHonorsErrorLimit()
    {
        string input = OutputPath(".csv");
        File.WriteAllText(input, "A,B\n" + string.Concat(Enumerable.Repeat("only-one\n", 105)), Encoding.Unicode);
        string reportPath = OutputPath(".json");

        Assert.Equal(1, await Program.Main(["validate", input, "--delimiter", ",", "--report", reportPath]));
        using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
        Assert.True(report.RootElement.GetProperty("stoppedEarly").GetBoolean());
        Assert.Equal(100, report.RootElement.GetProperty("errors").GetArrayLength());
    }

    // ---- command routing -------------------------------------------------------

    [Theory]
    [InlineData("detect")]
    [InlineData("inspect")]
    [InlineData("validate")]
    [InlineData("profile")]
    [InlineData("schema")]
    public async Task FileCommand_WithNoPath_Fails(string command)
        => Assert.Equal(1, await Program.Main([command]));

    [Theory]
    [InlineData("detect")]
    [InlineData("inspect")]
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
    public async Task MissingFile_FailsTheProcess()
    {
        Assert.Equal(1, await Program.Main(["detect", "definitely-not-here.csv"]));
    }

    [Fact]
    public async Task InvalidCsv_FailsValidationAndProcess()
        => Assert.Equal(1, await Program.Main(["validate", Csv("A,B\n1\n2,3")]));

    [Fact]
    public async Task Utf16Csv_StillValidatesAndProfiles()
    {
        string path = Csv();
        File.WriteAllText(path, "Name,Age\nAlice,30\n", Encoding.Unicode);

        Assert.Equal(0, await Program.Main(["validate", path]));
        Assert.Equal(0, await Program.Main(["profile", path]));
    }

    [Fact]
    public async Task UnsupportedConversion_FailsTheProcess()
        => Assert.Equal(1, await Program.Main(["convert", Csv(), OutputPath(".xyz")]));

    [Fact]
    public async Task BatchSize_RejectsZero()
        => Assert.Equal(1, await Program.Main(["translate", Csv(), "x", "-o", OutputPath(), "-b", "0"]));

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
