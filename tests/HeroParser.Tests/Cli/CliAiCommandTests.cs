using System.Globalization;
using System.Text;
using HeroParser.Cli;
using HeroParser.Cli.AI;
using HeroParser.Tests.ConsoleUi;
using Xunit;
using AnsiConsoleApi = HeroParser.Console.AnsiConsole;
using SystemConsole = HeroParser.Console.SystemAnsiConsole;

namespace HeroParser.Tests.Cli;

/// <summary>
/// Covers the three AI-backed CLI commands end to end with a scripted model.
///
/// Each of these commands used to construct its own LlmClient, so running one meant
/// shelling out to a real agent CLI — none of them had ever been executed by a test.
/// They now accept a client, which lets the whole pipeline (read file, profile it, build
/// the prompt, parse the answer, write the output) run against a canned response.
/// </summary>
[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
[Collection(AnsiConsoleCurrentCollection.NAME)]
public class CliAiCommandTests : IDisposable
{
    private readonly HeroParser.Console.IAnsiConsole previousConsole = AnsiConsoleApi.Current;
    private readonly StringWriter output = new(CultureInfo.InvariantCulture);
    private readonly List<string> tempFiles = [];

    public CliAiCommandTests()
    {
        AnsiConsoleApi.Current = new SystemConsole(output);
    }

    public void Dispose()
    {
        AnsiConsoleApi.Current = previousConsole;
        output.Dispose();
        foreach (string path in tempFiles.Where(File.Exists))
        {
            File.Delete(path);
        }
        GC.SuppressFinalize(this);
    }

    /// <summary>Replays a fixed model answer and remembers the prompt it was given.</summary>
    private sealed class ScriptedRunner(params string[] responses) : ILlmCliRunner
    {
        private int index;

        public List<string> Prompts { get; } = [];

        public Task<string> RunAsync(string commandName, string arguments, string prompt, CancellationToken cancellationToken)
        {
            Prompts.Add(prompt);
            string response = responses.Length == 0 ? string.Empty : responses[Math.Min(index, responses.Length - 1)];
            index++;
            return Task.FromResult(response);
        }
    }

    private static LlmClient ClientFor(ScriptedRunner runner) => new(LlmProvider.Google, null, runner);

    private string TempFile(string contents, string extension = ".csv", Encoding? encoding = null)
    {
        string path = Path.Join(Path.GetTempPath(), Path.GetRandomFileName() + extension);
        File.WriteAllText(path, contents, encoding ?? new UTF8Encoding(false));
        tempFiles.Add(path);
        return path;
    }

    private string TempPath(string extension = ".csv")
    {
        string path = Path.Join(Path.GetTempPath(), Path.GetRandomFileName() + extension);
        tempFiles.Add(path);
        return path;
    }

    private const string SAMPLE_CSV = "Name,Age\nAlice,30\nBob,25";

    // ---- schema ----------------------------------------------------------------

    [Fact]
    public async Task Schema_WithoutAi_GeneratesALocalRecordClass()
    {
        var runner = new ScriptedRunner();
        await CliCommands.SchemaAsync(TempFile(SAMPLE_CSV), null, useAi: false, null, null, null, ClientFor(runner));

        // The model must not be consulted when the caller did not ask for it.
        Assert.Empty(runner.Prompts);
    }

    [Fact]
    public async Task Schema_WithAi_SendsTheLocalSchemaAndProfileToTheModel()
    {
        var runner = new ScriptedRunner("```csharp\npublic sealed class Refined { }\n```");
        await CliCommands.SchemaAsync(TempFile(SAMPLE_CSV), null, useAi: true, null, null, null, ClientFor(runner));

        string prompt = Assert.Single(runner.Prompts);
        Assert.Contains("Dataset Profile", prompt, StringComparison.Ordinal);   // the context card
        Assert.Contains("[GenerateBinder]", prompt, StringComparison.Ordinal);  // the locally inferred class
        Assert.Contains("TabularMap(Name = \"Name\")", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Schema_WithAi_StopsReadingBeforeOversizedTail()
    {
        var runner = new ScriptedRunner("```csharp\npublic sealed class Refined { }\n```");
        string csv = "Id,Active\n" + string.Concat(Enumerable.Range(0, 100).Select(i => $"{i},true\n")) +
            new string('x', 600_000);

        Assert.True(await CliCommands.SchemaAsync(TempFile(csv), null, useAi: true, null, null, null, ClientFor(runner)));

        string prompt = Assert.Single(runner.Prompts);
        Assert.Contains("at most the first 10 data rows", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain(new string('x', 100), prompt, StringComparison.Ordinal);
        Assert.Contains("Types inferred from 100 data rows", output.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Schema_Utf16_SamplesOnlyFirstRows(bool bigEndian)
    {
        var runner = new ScriptedRunner("```csharp\npublic sealed class Refined { }\n```");
        string csv = "Name;Count\n" + string.Concat(Enumerable.Range(0, 100).Select(i => $"猫{i};{i}\n")) +
            new string('x', 600_000);

        Assert.True(await CliCommands.SchemaAsync(TempFile(csv, encoding: bigEndian ? Encoding.BigEndianUnicode : Encoding.Unicode),
            ';', useAi: true, null, null, null, ClientFor(runner)), output.ToString());

        string prompt = Assert.Single(runner.Prompts);
        Assert.Contains("TabularMap(Name = \"Name\")", prompt, StringComparison.Ordinal);
        Assert.Contains("public int Count", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain(new string('x', 100), prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Schema_ColumnTypes_ComeFromInference()
    {
        var runner = new ScriptedRunner("x");
        await CliCommands.SchemaAsync(
            TempFile("Id,Price,When,Ok\n1,2.5,2024-01-31,true"), null, useAi: true, null, null, null, ClientFor(runner));

        string prompt = Assert.Single(runner.Prompts);
        Assert.Contains("public int Id", prompt, StringComparison.Ordinal);
        Assert.Contains("public double Price", prompt, StringComparison.Ordinal);
        Assert.Contains("public DateTime When", prompt, StringComparison.Ordinal);
        Assert.Contains("public bool Ok", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Schema_ColumnNamesThatAreNotIdentifiers_BecomeValidProperties()
    {
        var runner = new ScriptedRunner("x");
        await CliCommands.SchemaAsync(
            TempFile("first name,%,2nd\na,b,c"), null, useAi: true, null, null, null, ClientFor(runner));

        string prompt = Assert.Single(runner.Prompts);
        Assert.Contains("public string Firstname", prompt, StringComparison.Ordinal);
        Assert.Contains("TabularMap(Name = \"%\")", prompt, StringComparison.Ordinal);
        Assert.Contains("public string Property", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Schema_MissingFile_ReportsAnErrorAndDoesNotCallTheModel()
    {
        var runner = new ScriptedRunner();
        await CliCommands.SchemaAsync("does-not-exist.csv", null, useAi: true, null, null, null, ClientFor(runner));

        Assert.Empty(runner.Prompts);
        Assert.Contains("File not found", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Schema_ModelFailure_IsReportedRatherThanThrown()
    {
        await CliCommands.SchemaAsync(
            TempFile(SAMPLE_CSV), null, useAi: true, null, null, null, new LlmClient(LlmProvider.Google, null, new FailingRunner()));

        Assert.Contains("Schema generation failed", output.ToString(), StringComparison.Ordinal);
    }

    // ---- query -----------------------------------------------------------------

    [Fact]
    public async Task Query_SendsTheProfileAndSampleRowsAndShowsTheAnswer()
    {
        var runner = new ScriptedRunner("Alice is the oldest.");
        await CliCommands.QueryAsync(TempFile(SAMPLE_CSV), null, null, "who is oldest?", null, null, null, ClientFor(runner));

        string prompt = Assert.Single(runner.Prompts);
        Assert.Contains("who is oldest?", prompt, StringComparison.Ordinal);
        Assert.Contains("Dataset Profile", prompt, StringComparison.Ordinal);
        Assert.Contains("| Name | Age |", prompt, StringComparison.Ordinal);
        Assert.Contains("Alice is the oldest.", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Query_EscapesPipesInSampleRows()
    {
        // An unescaped pipe would break the markdown table the model is shown.
        var runner = new ScriptedRunner("ok");
        await CliCommands.QueryAsync(
            TempFile("Name,Note\nAlice,\"x|y\""), ',', null, "q", null, null, null, ClientFor(runner));

        Assert.Contains("x\\|y", Assert.Single(runner.Prompts), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Query_ProfilesAllRowsButShowsOnlyTenSamples()
    {
        var runner = new ScriptedRunner("ok");
        string csv = "Name,Age\n" + string.Join('\n', Enumerable.Range(0, 200).Select(i => $"n{i},{i}"));

        Assert.True(await CliCommands.QueryAsync(
            TempFile(csv), null, null, "count the rows", null, null, null, ClientFor(runner)));

        string prompt = Assert.Single(runner.Prompts);
        Assert.Contains("(200 rows)", prompt, StringComparison.Ordinal);
        Assert.Contains("| n9 | 9 |", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("| n10 | 10 |", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Query_Utf16_ProfilesAllRowsAndSamplesTen()
    {
        var runner = new ScriptedRunner("ok");
        string csv = "Name;Age\n" + string.Join('\n', Enumerable.Range(0, 20).Select(i => $"猫{i};{i}"));

        Assert.True(await CliCommands.QueryAsync(TempFile(csv, encoding: Encoding.BigEndianUnicode),
            null, null, "count", null, null, null, ClientFor(runner)));

        string prompt = Assert.Single(runner.Prompts);
        Assert.Contains("(20 rows)", prompt, StringComparison.Ordinal);
        Assert.Contains("| 猫9 | 9 |", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("| 猫10 | 10 |", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Utf16_ProfileAndQuery_SupportMoreThanDefaultRowLimit()
    {
        var runner = new ScriptedRunner("ok");
        string csv = "Name,Age\n" + string.Concat(Enumerable.Range(0, 100_001).Select(i => $"n{i},{i}\n"));
        string path = TempFile(csv, encoding: Encoding.Unicode);

        Assert.True(await CliCommands.ProfileAsync(path, ',', null), output.ToString());
        Assert.True(await CliCommands.QueryAsync(path, ',', null, "count", null, null, null, ClientFor(runner)),
            output.ToString());
        Assert.Contains($"({100_001:N0} rows)", Assert.Single(runner.Prompts), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Query_ProfilesLargeRowBeyondSamples()
    {
        var runner = new ScriptedRunner("ok");
        string csv = "Value\n" + string.Concat(Enumerable.Repeat("small\n", 10)) + new string('x', 600_000);

        Assert.True(await CliCommands.QueryAsync(
            TempFile(csv), ',', null, "count", null, null, null, ClientFor(runner)));

        Assert.Contains("(11 rows)", Assert.Single(runner.Prompts), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Query_RejectsTruncatedUndetectableDelimiterSample()
    {
        var runner = new ScriptedRunner("ok");
        string csv = "\"" + new string('x', 70_000) + "\";B\n1;2";

        Assert.False(await CliCommands.QueryAsync(
            TempFile(csv), null, null, "count", null, null, null, ClientFor(runner)));

        Assert.Empty(runner.Prompts);
        Assert.Contains("specify --delimiter", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Query_MissingFile_ReportsAnError()
    {
        var runner = new ScriptedRunner();
        await CliCommands.QueryAsync("nope.csv", null, null, "q", null, null, null, ClientFor(runner));

        Assert.Empty(runner.Prompts);
        Assert.Contains("File not found", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Query_ModelFailure_IsReportedRatherThanThrown()
    {
        await CliCommands.QueryAsync(
            TempFile(SAMPLE_CSV), null, null, "q", null, null, null, new LlmClient(LlmProvider.Google, null, new FailingRunner()));

        Assert.Contains("Query failed", output.ToString(), StringComparison.Ordinal);
    }

    // ---- translate -------------------------------------------------------------

    [Fact]
    public async Task Translate_WritesTheModelsRowsToTheOutputFile()
    {
        var runner = new ScriptedRunner("{\"Name\":\"ALICE\",\"Age\":\"30\"}\n{\"Name\":\"BOB\",\"Age\":\"25\"}");
        string outputPath = TempPath();

        await CliCommands.TranslateAsync(
            TempFile(SAMPLE_CSV), null, null, "uppercase the names", outputPath, batchSize: 10, null, null, null, ClientFor(runner));

        string written = File.ReadAllText(outputPath);
        Assert.Contains("Name,Age", written, StringComparison.Ordinal);
        Assert.Contains("ALICE,30", written, StringComparison.Ordinal);
        Assert.Contains("BOB,25", written, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Translate_HeaderComesFromTheFirstParsedObject()
    {
        // The model may rename or add columns; the output header has to follow it.
        var runner = new ScriptedRunner("{\"Upper\":\"ALICE\",\"Decade\":\"3\"}");
        string outputPath = TempPath();

        await CliCommands.TranslateAsync(
            TempFile("Name,Age\nAlice,30"), null, null, "t", outputPath, batchSize: 10, null, null, null, ClientFor(runner));

        Assert.StartsWith("Upper,Decade", File.ReadAllText(outputPath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Translate_BatchesRowsAndCallsTheModelOncePerBatch()
    {
        var runner = new ScriptedRunner("{\"Name\":\"x\",\"Age\":\"1\"}\n{\"Name\":\"y\",\"Age\":\"2\"}",
            "{\"Name\":\"x\",\"Age\":\"1\"}\n{\"Name\":\"y\",\"Age\":\"2\"}",
            "{\"Name\":\"z\",\"Age\":\"3\"}");
        string csv = "Name,Age\n" + string.Join('\n', Enumerable.Range(0, 5).Select(i => $"n{i},{i}"));

        await CliCommands.TranslateAsync(
            TempFile(csv), null, null, "t", TempPath(), batchSize: 2, null, null, null, ClientFor(runner));

        // 5 rows in batches of 2 is three calls, the last one short.
        Assert.Equal(3, runner.Prompts.Count);
        Assert.Contains("Transform the input rows according to this prompt: \"t\"", runner.Prompts[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Translate_Utf16_UsesConfiguredBatches()
    {
        var runner = new ScriptedRunner("{\"Name\":\"ok\"}\n{\"Name\":\"ok\"}", "{\"Name\":\"ok\"}");
        string outputPath = TempPath();

        Assert.True(await CliCommands.TranslateAsync(
            TempFile("Name\nA\nB\nC", encoding: Encoding.Unicode), null, null, "t",
            outputPath, batchSize: 2, null, null, null, ClientFor(runner)));

        Assert.Equal(2, runner.Prompts.Count);
        Assert.Contains("\"A\"", runner.Prompts[0], StringComparison.Ordinal);
        Assert.Contains("\"C\"", runner.Prompts[1], StringComparison.Ordinal);
        Assert.Contains("ok", File.ReadAllText(outputPath), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"Name\":\"only-one\"}")]
    [InlineData("{\"Name\":\"first\"}\n{\"Name\":\"second\"}\n{\"Name\":\"extra\"}")]
    public async Task Translate_RejectsWrongRecordCountWithoutReplacingOutput(string response)
    {
        var runner = new ScriptedRunner(response);
        string outputPath = TempPath();
        File.WriteAllText(outputPath, "previous result");

        Assert.False(await CliCommands.TranslateAsync(
            TempFile("Name\nA\nB\n"), ',', null, "t", outputPath, batchSize: 2, null, null, null, ClientFor(runner)));

        Assert.Equal("previous result", File.ReadAllText(outputPath));
    }

    [Fact]
    public async Task Translate_CountsAndReadsLargeRow()
    {
        var runner = new ScriptedRunner("{\"Value\":\"ok\"}");
        string csv = "Value\n" + new string('x', 600_000);
        string outputPath = TempPath();

        Assert.True(await CliCommands.TranslateAsync(
            TempFile(csv), ',', null, "t", outputPath, batchSize: 1, null, null, null, ClientFor(runner)));

        Assert.Single(runner.Prompts);
        Assert.Contains("ok", File.ReadAllText(outputPath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Translate_SkipsLinesThatAreNotJsonObjects()
    {
        var runner = new ScriptedRunner("Here you go:\n{\"Name\":\"ok\",\"Age\":\"1\"}\nthat's all");
        string outputPath = TempPath();

        await CliCommands.TranslateAsync(
            TempFile("Name,Age\nAlice,30"), null, null, "t", outputPath, batchSize: 10, null, null, null, ClientFor(runner));

        string written = File.ReadAllText(outputPath);
        Assert.Contains("ok,1", written, StringComparison.Ordinal);
        Assert.DoesNotContain("that's all", written, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Translate_MalformedJsonLine_IsWarnedAboutAndSkipped()
    {
        var runner = new ScriptedRunner("{\"Name\":\"good\",\"Age\":\"1\"}\n{\"broken\": ");
        string outputPath = TempPath();

        await CliCommands.TranslateAsync(
            TempFile("Name,Age\nAlice,30"), null, null, "t", outputPath, batchSize: 10, null, null, null, ClientFor(runner));

        Assert.Contains("Failed to parse output line", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("good,1", File.ReadAllText(outputPath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Translate_MissingColumnInResponse_BecomesEmpty()
    {
        var runner = new ScriptedRunner("{\"Name\":\"a\",\"Age\":\"1\"}\n{\"Name\":\"b\"}");
        string outputPath = TempPath();

        await CliCommands.TranslateAsync(
            TempFile("Name,Age\nAlice,30\nBob,25"), null, null, "t", outputPath, batchSize: 10, null, null, null, ClientFor(runner));

        Assert.Contains("b,", File.ReadAllText(outputPath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Translate_MissingFile_ReportsAnError()
    {
        var runner = new ScriptedRunner();
        await CliCommands.TranslateAsync(
            "nope.csv", null, null, "t", TempPath(), batchSize: 10, null, null, null, ClientFor(runner));

        Assert.Empty(runner.Prompts);
        Assert.Contains("File not found", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Translate_ModelFailure_IsReportedRatherThanThrown()
    {
        await CliCommands.TranslateAsync(
            TempFile(SAMPLE_CSV), null, null, "t", TempPath(), batchSize: 10, null, null, null,
            new LlmClient(LlmProvider.Google, null, new FailingRunner()));

        Assert.Contains("Translation pipeline failed", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Translate_DoesNotOverwriteItsInput()
    {
        string input = TempFile(SAMPLE_CSV);
        var runner = new ScriptedRunner("{}");

        Assert.False(await CliCommands.TranslateAsync(
            input, null, null, "t", input, batchSize: 2, null, null, null, ClientFor(runner)));

        Assert.Equal(SAMPLE_CSV, File.ReadAllText(input));
        Assert.Empty(runner.Prompts);
    }

    private sealed class FailingRunner : ILlmCliRunner
    {
        public Task<string> RunAsync(string commandName, string arguments, string prompt, CancellationToken cancellationToken)
            => throw new InvalidOperationException("agent unavailable");
    }
}
