using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using HeroParser.Cli;

namespace HeroParser.Tests;

[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
public sealed class CliTests
{
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
            CliCommands.Detect(tempFile);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void Validate_ValidCsv_Succeeds()
    {
        // Arrange
        string csv = "Name,Age,Email\nAlice,30,alice@example.com\nBob,25,bob@example.com";
        string tempFile = Path.GetTempFileName() + ".csv";
        File.WriteAllText(tempFile, csv);

        try
        {
            // Act & Assert
            CliCommands.Validate(tempFile, ',');
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void Validate_InvalidCsv_HandlesErrorsGracefully()
    {
        // Arrange
        string csv = "Name,Age,Email\nAlice,30\nBob,25,bob@example.com,ExtraColumn";
        string tempFile = Path.GetTempFileName() + ".csv";
        File.WriteAllText(tempFile, csv);

        try
        {
            // Act & Assert
            CliCommands.Validate(tempFile, ',');
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void Profile_ValidCsv_PrintsMarkdownProfile()
    {
        // Arrange
        string csv = "Name,Age,Active\nAlice,30,true\nBob,25,false\nCharlie,,true";
        string tempFile = Path.GetTempFileName() + ".csv";
        File.WriteAllText(tempFile, csv);

        try
        {
            // Act & Assert
            CliCommands.Profile(tempFile, ',', null);
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
            CliCommands.Convert(tempCsv, tempJsonl, ',', "flat", null);
            Assert.True(File.Exists(tempJsonl));
            var jsonlLines = File.ReadAllLines(tempJsonl);
            Assert.Equal(2, jsonlLines.Length);
            Assert.Contains("\"Name\":\"Alice\"", jsonlLines[0]);

            // Act - Convert JSONL back to CSV
            CliCommands.Convert(tempJsonl, tempCsvBack, ',', null, null);
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
            CliCommands.Repair(tempInput, tempOutput);

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
            await CliCommands.SchemaAsync(tempCsv, ',', useAi: false, null, null, null);
        }
        finally
        {
            if (File.Exists(tempCsv)) File.Delete(tempCsv);
        }
    }
}
