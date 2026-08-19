using System.Globalization;
using HeroParser.Cli;
using HeroParser.Tests.ConsoleUi;
using Xunit;
using AnsiConsoleApi = HeroParser.Console.AnsiConsole;
using SystemConsole = HeroParser.Console.SystemAnsiConsole;

namespace HeroParser.Tests.Cli;

/// <summary>
/// Covers the non-AI CLI commands: the conversion matrix, each command's missing-file and
/// failure reporting, and the reporting details that only appear for particular inputs.
///
/// These are what a user actually runs. Their happy paths were tested; the branches that
/// tell someone what went wrong were not, so a command could fail silently — or crash
/// where it meant to print an error — without any test noticing.
/// </summary>
[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
[Collection(AnsiConsoleCurrentCollection.NAME)]
public sealed class CliCommandCoverageTests : IDisposable
{
    private readonly HeroParser.Console.IAnsiConsole previousConsole = AnsiConsoleApi.Current;
    private readonly StringWriter output = new(CultureInfo.InvariantCulture);
    private readonly List<string> tempFiles = [];

    public CliCommandCoverageTests()
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
    }

    private string Output => output.ToString();

    private string TempFile(string contents, string extension = ".csv")
    {
        string path = TempPath(extension);
        File.WriteAllText(path, contents);
        return path;
    }

    private string TempPath(string extension = ".csv")
    {
        string path = Path.Join(Path.GetTempPath(), Path.GetRandomFileName() + extension);
        tempFiles.Add(path);
        return path;
    }

    // ---- detect ----------------------------------------------------------------

    [Theory]
    [InlineData("a;b;c\n1;2;3\n", "Semicolon")]
    [InlineData("a|b|c\n1|2|3\n", "Pipe")]
    [InlineData("a\tb\tc\n1\t2\t3\n", "Tab")]
    public void Detect_NamesEachDelimiterItRecognises(string csv, string expected)
    {
        CliCommands.Detect(TempFile(csv));
        Assert.Contains(expected, Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Detect_RaggedInput_WarnsAboutConfidence()
    {
        // Wildly inconsistent column counts leave the detector unsure, and the user needs
        // to be told rather than handed a guess.
        CliCommands.Detect(TempFile("a,b,c\n1,2\n3,4,5,6\n7,8,9\n"));
        Assert.Contains("Low delimiter confidence", Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Detect_UndelimitedInput_ReportsTheFailure()
    {
        // With no consistent delimiter the detector refuses rather than guessing, and the
        // command has to turn that into a message instead of a stack trace.
        CliCommands.Detect(TempFile("one two three\nfour five\nsix\n"));
        Assert.Contains("Detection failed", Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Detect_MissingFile_IsReported()
    {
        CliCommands.Detect("no-such-file.csv");
        Assert.Contains("File not found", Output, StringComparison.Ordinal);
    }

    // ---- validate --------------------------------------------------------------

    [Fact]
    public void Validate_MissingFile_IsReported()
    {
        CliCommands.Validate("no-such-file.csv", null);
        Assert.Contains("File not found", Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_ManyErrors_SummarisesTheRemainder()
    {
        // The table is capped, so the count of what it left out has to be reported.
        var rows = new List<string> { "a,b,c" };
        rows.AddRange(Enumerable.Range(0, 40).Select(i => $"{i},{i}"));

        CliCommands.Validate(TempFile(string.Join('\n', rows) + "\n"), null);

        Assert.Contains("and ", Output, StringComparison.Ordinal);
        Assert.Contains("Validation Failed", Output, StringComparison.Ordinal);
    }

    // ---- profile ---------------------------------------------------------------

    [Fact]
    public void Profile_MissingFile_IsReported()
    {
        CliCommands.Profile("no-such-file.csv", null, null);
        Assert.Contains("File not found", Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Profile_HeaderOnlyFile_ReportsNoData()
    {
        CliCommands.Profile(TempFile("a,b,c\n"), null, null);
        Assert.Contains("No data", Output, StringComparison.Ordinal);
    }

    // ---- convert ---------------------------------------------------------------

    [Fact]
    public void Convert_MissingInput_IsReported()
    {
        CliCommands.Convert("no-such-file.csv", TempPath(".jsonl"), null, null, null);
        Assert.Contains("Input file not found", Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Convert_CsvToJsonl_WithTheAnthropicShape()
    {
        string input = TempFile("Question,Answer\nwhy?,because\n");
        string outputPath = TempPath(".jsonl");

        CliCommands.Convert(input, outputPath, null, "anthropic", null);

        Assert.Contains("messages", File.ReadAllText(outputPath), StringComparison.Ordinal);
    }

    [Fact]
    public void Convert_CsvToFixedWidth_WritesAlignedText()
    {
        string input = TempFile("Name,Age\nAlice,30\nBob,25\n");
        string outputPath = TempPath(".txt");

        CliCommands.Convert(input, outputPath, null, null, null);

        string written = File.ReadAllText(outputPath);
        Assert.Contains("Alice", written, StringComparison.Ordinal);
        Assert.Contains("Converted", Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Convert_FixedWidthToCsv_ExplainsWhyItCannot()
    {
        // Widths cannot be inferred from the text alone, so the CLI declines rather than guessing.
        CliCommands.Convert(TempFile("Alice 30\n", ".txt"), TempPath(".csv"), null, null, null);

        Assert.Contains("requires column widths", Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Convert_UnsupportedDirection_IsReported()
    {
        CliCommands.Convert(TempFile("a,b\n", ".csv"), TempPath(".xyz"), null, null, null);
        Assert.Contains("Unsupported conversion direction", Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Convert_ExcelToCsv_WritesEveryRow()
    {
        string input = ExcelFile(new Person { Name = "Alice", Age = "30" });
        string outputPath = TempPath(".csv");

        CliCommands.Convert(input, outputPath, null, null, null);

        string written = File.ReadAllText(outputPath);
        Assert.Contains("Name,Age", written, StringComparison.Ordinal);
        Assert.Contains("Alice,30", written, StringComparison.Ordinal);
    }

    [Fact]
    public void Convert_ExcelToJsonl_UsesTheFirstRowAsKeys()
    {
        string input = ExcelFile(new Person { Name = "Alice", Age = "30" });
        string outputPath = TempPath(".jsonl");

        CliCommands.Convert(input, outputPath, null, null, null);

        string written = File.ReadAllText(outputPath);
        Assert.Contains("\"Name\":\"Alice\"", written, StringComparison.Ordinal);
        Assert.Contains("\"Age\":\"30\"", written, StringComparison.Ordinal);
    }

    [Fact]
    public void Convert_EmptyExcelSheet_IsReported()
    {
        string input = EmptyExcelFile();
        string outputPath = TempPath(".csv");

        CliCommands.Convert(input, outputPath, null, null, null);

        Assert.Contains("empty", Output, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(string.Empty, File.ReadAllText(outputPath));
    }

    [Fact]
    public void Convert_ExcelToAnUnsupportedFormat_IsReported()
    {
        CliCommands.Convert(ExcelFile(new Person { Name = "a", Age = "1" }), TempPath(".xyz"), null, null, null);
        Assert.Contains("Unsupported output extension", Output, StringComparison.Ordinal);
    }

    // ---- repair ----------------------------------------------------------------

    [Fact]
    public void Repair_MissingInput_IsReported()
    {
        CliCommands.Repair("no-such-file.csv", TempPath());
        Assert.Contains("not found", Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Repair_StripsMarkdownFencing()
    {
        string input = TempFile("```csv\nName,Age\nAlice,30\n```\n");
        string outputPath = TempPath();

        CliCommands.Repair(input, outputPath);

        string written = File.ReadAllText(outputPath);
        Assert.DoesNotContain("```", written, StringComparison.Ordinal);
        Assert.Contains("Alice", written, StringComparison.Ordinal);
    }

    /// <summary>A record whose property names become the sheet's first row.</summary>
    public sealed class Person
    {
        public string Name { get; set; } = "";
        public string Age { get; set; } = "";
    }

    /// <summary>Writes a small workbook with a header row and returns its path.</summary>
    private string ExcelFile(params Person[] people)
    {
        string path = TempPath(".xlsx");
        HeroParser.Excel.Write<Person>().WithSheetName("Sheet1").ToFile(path, people);
        return path;
    }

    /// <summary>Writes a workbook whose sheet has no rows at all.</summary>
    private string EmptyExcelFile()
    {
        string path = TempPath(".xlsx");
        HeroParser.Excel.Write<Person>().WithSheetName("Sheet1").WithoutHeader().ToFile(path, []);
        return path;
    }
}
