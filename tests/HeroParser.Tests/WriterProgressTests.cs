using HeroParser.SeparatedValues.Writing;
using Xunit;

namespace HeroParser.Tests;

/// <summary>
/// Tests for progress reporting during CSV and FixedWidth writing.
/// </summary>
[Collection("AsyncWriterTests")]
public class WriterProgressTests
{
    public record SimpleRecord(string Name, int Age);

    #region CSV Writer Progress

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvWriteToText_WithProgress_ReportsProgress()
    {
        var records = Enumerable.Range(1, 100)
            .Select(i => new SimpleRecord($"Name{i}", i))
            .ToList();

        var reports = new List<CsvWriteProgress>();
        var options = new CsvWriteOptions
        {
            WriteProgress = new Progress<CsvWriteProgress>(reports.Add),
            WriteProgressIntervalRows = 10,
        };

        Csv.WriteToText(records, options);

        // Should have received progress reports at intervals of 10
        Assert.True(reports.Count >= 1);
        // Last report should have all rows
        Assert.Equal(100, reports[^1].RowsWritten);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvWriteToText_WithProgress_ReportsRowCounts()
    {
        var records = Enumerable.Range(1, 50)
            .Select(i => new SimpleRecord($"Name{i}", i))
            .ToList();

        var reports = new List<CsvWriteProgress>();
        var options = new CsvWriteOptions
        {
            WriteProgress = new Progress<CsvWriteProgress>(reports.Add),
            WriteProgressIntervalRows = 25,
        };

        Csv.WriteToText(records, options);

        // Should get reports at row 25 and final at 50
        Assert.True(reports.Count >= 1);
        Assert.All(reports, r => Assert.True(r.RowsWritten > 0));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvWriteToText_WithProgress_ReportsBytesWritten()
    {
        SimpleRecord[] records = [new SimpleRecord("Alice", 30)];

        var reports = new List<CsvWriteProgress>();
        var options = new CsvWriteOptions
        {
            WriteProgress = new Progress<CsvWriteProgress>(reports.Add),
            WriteProgressIntervalRows = 1,
        };

        Csv.WriteToText(records, options);

        Assert.True(reports.Count >= 1);
        // At least the header + one row should have been written
        Assert.True(reports[^1].BytesWritten > 0);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvWriteToText_NoProgress_NoError()
    {
        SimpleRecord[] records = [new SimpleRecord("Alice", 30)];

        // Should work fine without progress reporting
        var result = Csv.WriteToText(records);
        Assert.Contains("Alice", result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvWriteToText_ProgressIntervalOne_ReportsEveryRow()
    {
        var records = Enumerable.Range(1, 5)
            .Select(i => new SimpleRecord($"Name{i}", i))
            .ToList();

        var reports = new List<CsvWriteProgress>();
        var options = new CsvWriteOptions
        {
            WriteProgress = new Progress<CsvWriteProgress>(reports.Add),
            WriteProgressIntervalRows = 1,
        };

        Csv.WriteToText(records, options);

        // Should get a report for each of the 5 rows (plus possibly a final)
        Assert.True(reports.Count >= 5);
    }

    #endregion

    #region Async CSV Writer Progress

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task CsvWriteToStreamAsync_WithProgress_ReportsProgress()
    {
        var records = Enumerable.Range(1, 100)
            .Select(i => new SimpleRecord($"Name{i}", i))
            .ToList();

        var reports = new List<CsvWriteProgress>();
        var options = new CsvWriteOptions
        {
            WriteProgress = new Progress<CsvWriteProgress>(reports.Add),
            WriteProgressIntervalRows = 50,
        };

        using var stream = new MemoryStream();
        await Csv.WriteToStreamAsync(stream, records, options, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(reports.Count >= 1);
    }

    #endregion

    #region Progress Struct

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvWriteProgress_Properties_SetCorrectly()
    {
        var progress = new CsvWriteProgress
        {
            RowsWritten = 100,
            BytesWritten = 5000,
        };

        Assert.Equal(100, progress.RowsWritten);
        Assert.Equal(5000, progress.BytesWritten);
    }

    #endregion

    #region Builder API

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvWriterBuilder_WithProgress_ConfiguresProgress()
    {
        var reports = new List<CsvWriteProgress>();
        SimpleRecord[] records = [new SimpleRecord("Alice", 30)];

        var result = Csv.Write<SimpleRecord>()
            .WithProgress(new Progress<CsvWriteProgress>(reports.Add))
            .WithProgressInterval(1)
            .ToText(records);

        Assert.Contains("Alice", result);
        Assert.True(reports.Count >= 1);
    }

    #endregion
}
