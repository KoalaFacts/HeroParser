using System.Globalization;
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

    private string TempFile(string contents, string extension = ".csv")
    {
        string path = Path.Join(Path.GetTempPath(), Path.GetRandomFileName() + extension);
        File.WriteAllText(path, contents);
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
        var runner = new ScriptedRunner("{\"Name\":\"x\",\"Age\":\"1\"}");
        string csv = "Name,Age\n" + string.Join('\n', Enumerable.Range(0, 5).Select(i => $"n{i},{i}"));

        await CliCommands.TranslateAsync(
            TempFile(csv), null, null, "t", TempPath(), batchSize: 2, null, null, null, ClientFor(runner));

        // 5 rows in batches of 2 is three calls, the last one short.
        Assert.Equal(3, runner.Prompts.Count);
        Assert.Contains("Transform the input rows according to this prompt: \"t\"", runner.Prompts[0], StringComparison.Ordinal);
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

    private sealed class FailingRunner : ILlmCliRunner
    {
        public Task<string> RunAsync(string commandName, string arguments, string prompt, CancellationToken cancellationToken)
            => throw new InvalidOperationException("agent unavailable");
    }
}
