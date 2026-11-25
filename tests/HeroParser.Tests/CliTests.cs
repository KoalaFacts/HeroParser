using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using HeroParser.Cli;
using HeroParser.Tests.ConsoleUi;

namespace HeroParser.Tests;

[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
[Collection(AnsiConsoleCurrentCollection.NAME)]
public sealed class CliTests
{
    private readonly CliCommands cliCommands = new();

    [Fact]
    public async Task Main_NoArgs_ReturnsSuccessAndPrintsHelp()
    {
        // Act
        int exitCode = await Program.Main([]);

        // Assert
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task Main_InvalidCommand_ReturnsError()
    {
        // Act
        int exitCode = await Program.Main(["nonexistentcommand"]);

        // Assert
        Assert.Equal(1, exitCode);
    }

    [Fact]
    public void Detect_ValidCsv_PrintsCorrectDetails()
    {
        // Arrange
        string csv = "Name,Age,Email\nAlice,30,alice@example.com\nBob,25,bob@example.com";
        string tempFile = Path.GetTempFileName() + ".csv";
        File.WriteAllText(tempFile, csv);

        try
        {
            // Act & Assert (Should not throw)
            cliCommands.Detect(tempFile);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task Validate_ValidCsv_Succeeds()
    {
        // Arrange
        string csv = "Name,Age,Email\nAlice,30,alice@example.com\nBob,25,bob@example.com";
        string tempFile = Path.GetTempFileName() + ".csv";
        File.WriteAllText(tempFile, csv);

        try
        {
            // Act & Assert
            Assert.True(await cliCommands.ValidateAsync(tempFile, ','));
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task Validate_InvalidCsv_HandlesErrorsGracefully()
    {
        // Arrange
        string csv = "Name,Age,Email\nAlice,30\nBob,25,bob@example.com,ExtraColumn";
        string tempFile = Path.GetTempFileName() + ".csv";
        File.WriteAllText(tempFile, csv);

        try
        {
            // Act & Assert
            Assert.False(await cliCommands.ValidateAsync(tempFile, ','));
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task Profile_ValidCsv_PrintsMarkdownProfile()
    {
        // Arrange
        string csv = "Name,Age,Active\nAlice,30,true\nBob,25,false\nCharlie,,true";
        string tempFile = Path.GetTempFileName() + ".csv";
        File.WriteAllText(tempFile, csv);

        try
        {
            // Act & Assert
            Assert.True(await cliCommands.ProfileAsync(tempFile, ',', null));
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void Convert_CsvToJsonlAndBack_Succeeds()
    {
        // Arrange
        string csv = "Name,Age\nAlice,30\nBob,25";
        string tempCsv = Path.GetTempFileName() + ".csv";
        string tempJsonl = Path.GetTempFileName() + ".jsonl";
        string tempCsvBack = Path.GetTempFileName() + ".csv";

        File.WriteAllText(tempCsv, csv);

        try
        {
            // Act - Convert to JSONL
            cliCommands.Convert(tempCsv, tempJsonl, ',', "flat", null);
            Assert.True(File.Exists(tempJsonl));
            var jsonlLines = File.ReadAllLines(tempJsonl);
            Assert.Equal(2, jsonlLines.Length);
            Assert.Contains("\"Name\":\"Alice\"", jsonlLines[0]);

            // Act - Convert JSONL back to CSV
            cliCommands.Convert(tempJsonl, tempCsvBack, ',', null, null);
            Assert.True(File.Exists(tempCsvBack));
            var csvLines = File.ReadAllLines(tempCsvBack);
            Assert.Equal(3, csvLines.Length); // Header + 2 data lines
            Assert.Equal("Name,Age", csvLines[0]);
        }
        finally
        {
            if (File.Exists(tempCsv)) File.Delete(tempCsv);
            if (File.Exists(tempJsonl)) File.Delete(tempJsonl);
            if (File.Exists(tempCsvBack)) File.Delete(tempCsvBack);
        }
    }

    [Fact]
    public void Repair_CutoffMarkdown_CleansAndRepairsQuotes()
    {
        // Arrange
        string rawLlm = "```csv\nName,Quote\nAlice,\"Hello world\n";
        string tempInput = Path.GetTempFileName() + ".txt";
        string tempOutput = Path.GetTempFileName() + ".csv";

        File.WriteAllText(tempInput, rawLlm);

        try
        {
            // Act
            cliCommands.Repair(tempInput, tempOutput);

            // Assert
            Assert.True(File.Exists(tempOutput));
            var lines = File.ReadAllLines(tempOutput);
            Assert.Equal(2, lines.Length);
            Assert.Equal("Name,Quote", lines[0]);
            Assert.Equal("Alice,\"Hello world\"", lines[1]); // Fixed ending quote
        }
        finally
        {
            if (File.Exists(tempInput)) File.Delete(tempInput);
            if (File.Exists(tempOutput)) File.Delete(tempOutput);
        }
    }

    [Fact]
    public async Task Schema_LocalInference_GeneratesValidClassStructure()
    {
        // Arrange
        string csv = "CustomerId,Balance,IsPremium,JoinDate\n1,100.50,true,2026-01-01";
        string tempCsv = Path.GetTempFileName() + ".csv";
        File.WriteAllText(tempCsv, csv);

        try
        {
            // Act - local inference (useAi = false)
            await cliCommands.SchemaAsync(tempCsv, ',', useAi: false, null, null, null);
        }
        finally
        {
            if (File.Exists(tempCsv)) File.Delete(tempCsv);
        }
    }

    [Fact]
    public void Convert_CsvAndJsonl_ToExcelAndHtbRoundTrip_Succeed()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"hero-cli-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            string csvPath = Path.Combine(dir, "input.csv");
            string csvXlsxPath = Path.Combine(dir, "from-csv.xlsx");
            string jsonlPath = Path.Combine(dir, "input.jsonl");
            string jsonlXlsxPath = Path.Combine(dir, "from-jsonl.xlsx");
            string htbPath = Path.Combine(dir, "output.htb");
            string htbCsvPath = Path.Combine(dir, "from-htb.csv");

            File.WriteAllText(csvPath, "Name,Age,Score\nAlice,30,9.5\nBob,25,8.0\n");
            File.WriteAllText(jsonlPath, "{\"Name\":\"Alice\",\"Age\":\"30\"}\n{\"Name\":\"Bob\",\"Age\":\"25\"}\n");

            Assert.True(cliCommands.Convert(csvPath, csvXlsxPath, null, null, null));
            Assert.True(File.Exists(csvXlsxPath));
            using (var reader = HeroParser.Excel.CreateDataReader(csvXlsxPath))
            {
                Assert.True(reader.Read());
                Assert.Equal("Alice", reader.GetString(0));
                Assert.Equal("30", reader.GetString(1));
                Assert.Equal("9.5", reader.GetString(2));
                Assert.True(reader.Read());
                Assert.Equal("Bob", reader.GetString(0));
                Assert.False(reader.Read());
            }

            Assert.True(cliCommands.Convert(jsonlPath, jsonlXlsxPath, null, null, "Data"));
            Assert.True(File.Exists(jsonlXlsxPath));
            using (var reader = HeroParser.Excel.CreateDataReader(jsonlXlsxPath, sheetName: "Data"))
            {
                Assert.True(reader.Read());
                Assert.Equal("Alice", reader.GetString(0));
                Assert.Equal("30", reader.GetString(1));
                Assert.True(reader.Read());
                Assert.Equal("Bob", reader.GetString(0));
                Assert.False(reader.Read());
            }

            Assert.True(cliCommands.Convert(csvPath, htbPath, null, null, null));
            Assert.True(File.Exists(htbPath));

            Assert.True(cliCommands.Convert(htbPath, htbCsvPath, null, null, null));
            Assert.True(File.Exists(htbCsvPath));
            var lines = File.ReadAllLines(htbCsvPath);
            Assert.Equal(3, lines.Length);
            Assert.Equal("Name,Age,Score", lines[0]);
            Assert.Equal("Alice,30,9.5", lines[1]);
            Assert.Equal("Bob,25,8.0", lines[2]);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Convert_CompressedCsvToFixedWidth_DecompressesInputCorrectly()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"heroparser-cli-gz-fw-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            string gzCsvPath = Path.Combine(dir, "input.csv.gz");
            string txtOutPath = Path.Combine(dir, "output.txt");
            string gzTxtOutPath = Path.Combine(dir, "output.txt.gz");

            using (var fs = File.Create(gzCsvPath))
            using (var gz = new GZipStream(fs, CompressionLevel.Fastest))
            using (var sw = new StreamWriter(gz, Encoding.UTF8))
            {
                sw.Write("Name,Age,City\nAlice,30,Sydney\nBob,25,Melbourne\n");
            }

            Assert.True(cliCommands.Convert(gzCsvPath, txtOutPath, null, null, null));
            Assert.True(File.Exists(txtOutPath));
            string fixedText = File.ReadAllText(txtOutPath);
            Assert.Contains("Alice", fixedText, StringComparison.Ordinal);
            Assert.Contains("Sydney", fixedText, StringComparison.Ordinal);
            Assert.Contains("Melbourne", fixedText, StringComparison.Ordinal);

            Assert.True(cliCommands.Convert(gzCsvPath, gzTxtOutPath, null, null, null));
            Assert.True(File.Exists(gzTxtOutPath));
            using var readFs = File.OpenRead(gzTxtOutPath);
            using var readGz = new GZipStream(readFs, CompressionMode.Decompress);
            using var reader = new StreamReader(readGz, Encoding.UTF8);
            string gzFixedText = reader.ReadToEnd();
            Assert.Equal(fixedText, gzFixedText);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
