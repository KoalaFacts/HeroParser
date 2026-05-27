using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using HeroParser;
using HeroParser.AI;
using HeroParser.SeparatedValues.Core;
using Xunit;

namespace HeroParser.Tests.AI;

[GenerateBinder]
public sealed class LlmTestRecord
{
    public string Name { get; set; } = string.Empty;
    public int Age { get; set; }
}

public class LlmRepairTests
{
    [Fact]
    public void RepairText_HandlesNullOrWhitespace()
    {
        Assert.Equal(string.Empty, LlmRepair.RepairText(null!));
        Assert.Equal(string.Empty, LlmRepair.RepairText("  \n  "));
    }

    [Fact]
    public void RepairText_StripsMarkdownBlockWrappers()
    {
        var input = @"```csv
Name,Age
Alice,30
Bob,25
```";
        var expected = "Name,Age\nAlice,30\nBob,25";
        var output = LlmRepair.RepairText(input);
        Assert.Equal(expected.Replace("\r\n", "\n"), output.Replace("\r\n", "\n"));
    }

    [Fact]
    public void RepairText_StripsMarkdownBlockWrappersWithoutLanguage()
    {
        var input = @"```
Name,Age
Alice,30
```";
        var expected = "Name,Age\nAlice,30";
        var output = LlmRepair.RepairText(input);
        Assert.Equal(expected.Replace("\r\n", "\n"), output.Replace("\r\n", "\n"));
    }

    [Fact]
    public void RepairText_RepairsUnclosedQuotesAtEnd()
    {
        var input = "Name,Age\n\"Alice,30";
        var output = LlmRepair.RepairText(input);
        Assert.Equal("Name,Age\n\"Alice,30\"\n", output.Replace("\r\n", "\n"));
    }

    [Fact]
    public void RepairText_RepairsUnclosedQuotesAtEndWithEscape()
    {
        var input = "Name,Age\n\"Alice,30\\\"";
        var output = LlmRepair.RepairText(input, '"', '\\');
        // Unclosed because the final quote inside was escaped, but the outer quote wasn't closed.
        Assert.Equal("Name,Age\n\"Alice,30\\\"\"\n", output.Replace("\r\n", "\n"));
    }

    [Fact]
    public async Task ReadFromTextAsync_BindsRepairedText()
    {
        var input = @"```csv
Age,Name
30,Alice
25,Bob
```";
        var results = await LlmRepair.ReadFromTextAsync<LlmTestRecord>(input).ToListAsync();
        Assert.Equal(2, results.Count);
        Assert.Equal("Alice", results[0].Name);
        Assert.Equal(30, results[0].Age);
        Assert.Equal("Bob", results[1].Name);
        Assert.Equal(25, results[1].Age);
    }

    [Fact]
    public async Task ReadFromTextAsync_WithAllOptions_BindsRepairedText()
    {
        var input = "Age,Name\n30,Alice\n25,Bob";
        var options1 = new CsvReadOptions
        {
            Delimiter = ',',
            Quote = '"',
            EscapeCharacter = '\\',
            MaxColumnCount = 10,
            MaxRowCount = 1000,
            UseSimdIfAvailable = false,
            AllowNewlinesInsideQuotes = false,
            EnableQuotedFields = false,
            CommentCharacter = '#',
            TrimFields = true,
            MaxFieldSize = 500,
            MaxRowSize = 1000,
            MaxInputSize = 5000
        };

        var results1 = await LlmRepair.ReadFromTextAsync<LlmTestRecord>(input, options1).ToListAsync();
        Assert.Equal(2, results1.Count);
        Assert.Equal("Alice", results1[0].Name);

        var options2 = new CsvReadOptions
        {
            UseSimdIfAvailable = true,
            EnableQuotedFields = true
        };
        var results2 = await LlmRepair.ReadFromTextAsync<LlmTestRecord>(input, options2).ToListAsync();
        Assert.Equal(2, results2.Count);
        Assert.Equal("Alice", results2[0].Name);
    }

    [Fact]
    public async Task ReadFromStreamAsync_BindsRepairedStream()
    {
        var input = "Age,Name\n30,Alice\n25,Bob";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(input));
        var results = await LlmRepair.ReadFromStreamAsync<LlmTestRecord>(stream, cancellationToken: TestContext.Current.CancellationToken).ToListAsync();
        Assert.Equal(2, results.Count);
        Assert.Equal("Alice", results[0].Name);
        Assert.Equal(30, results[0].Age);
        Assert.Equal("Bob", results[1].Name);
        Assert.Equal(25, results[1].Age);
    }

    [Fact]
    public async Task ReadFromStreamAsync_ThrowsOnNullStream()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await foreach (var _ in LlmRepair.ReadFromStreamAsync<LlmTestRecord>(null!, cancellationToken: TestContext.Current.CancellationToken))
            {
            }
        });
    }
}
