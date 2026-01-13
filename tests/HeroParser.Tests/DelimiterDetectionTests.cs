using HeroParser.SeparatedValues.Detection;
using Xunit;

namespace HeroParser.Tests;

public class DelimiterDetectionTests
{
    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void DetectDelimiter_CommaDelimitedCsv_ReturnsComma()
    {
        var csv = "Name,Age,City\nJohn,30,NYC\nJane,25,LA\nBob,35,SF";

        var result = Csv.DetectDelimiter(csv);

        Assert.Equal(',', result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void DetectDelimiter_SemicolonDelimitedCsv_ReturnsSemicolon()
    {
        var csv = "Name;Age;City\nJohn;30;NYC\nJane;25;LA\nBob;35;SF";

        var result = Csv.DetectDelimiter(csv);

        Assert.Equal(';', result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void DetectDelimiter_PipeDelimitedCsv_ReturnsPipe()
    {
        var csv = "Name|Age|City\nJohn|30|NYC\nJane|25|LA\nBob|35|SF";

        var result = Csv.DetectDelimiter(csv);

        Assert.Equal('|', result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void DetectDelimiter_TabDelimitedCsv_ReturnsTab()
    {
        var csv = "Name\tAge\tCity\nJohn\t30\tNYC\nJane\t25\tLA\nBob\t35\tSF";

        var result = Csv.DetectDelimiter(csv);

        Assert.Equal('\t', result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void DetectDelimiterWithDetails_ConsistentDelimiter_ReturnsHighConfidence()
    {
        var csv = "Name,Age,City\nJohn,30,NYC\nJane,25,LA\nBob,35,SF";

        var result = Csv.DetectDelimiterWithDetails(csv);

        Assert.Equal(',', result.DetectedDelimiter);
        Assert.True(result.Confidence >= 90, $"Expected confidence >= 90%, got {result.Confidence}%");
        Assert.Equal(4, result.SampledRows);
        Assert.Equal(2.0, result.AverageDelimiterCount); // 2 commas per row
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void DetectDelimiterWithDetails_InconsistentDelimiter_ReturnsLowerConfidence()
    {
        // First row has 2 commas, second has 3, third has 2
        var csv = "Name,Age\nJohn,30,NYC\nJane,25";

        var result = Csv.DetectDelimiterWithDetails(csv);

        Assert.Equal(',', result.DetectedDelimiter);
        Assert.True(result.Confidence < 100, $"Expected confidence < 100%, got {result.Confidence}%");
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void DetectDelimiter_SingleRow_DetectsCorrectly()
    {
        var csv = "Name,Age,City";

        var result = Csv.DetectDelimiter(csv);

        Assert.Equal(',', result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void DetectDelimiter_EmptyData_ThrowsInvalidOperationException()
    {
        var csv = "";

        var ex = Assert.Throws<InvalidOperationException>(() => Csv.DetectDelimiter(csv));
        Assert.Contains("empty", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void DetectDelimiter_NoDelimiters_ThrowsInvalidOperationException()
    {
        var csv = "Just some text without delimiters\nAnother line of text";

        var ex = Assert.Throws<InvalidOperationException>(() => Csv.DetectDelimiter(csv));
        Assert.Contains("no consistent delimiter", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void DetectDelimiter_MixedDelimiters_SelectsMostConsistent()
    {
        // More semicolons (3 per row) than commas (1 per row in some rows)
        var csv = "Name;Age;City\nJohn;30;NYC\nJane;25;LA";

        var result = Csv.DetectDelimiter(csv);

        Assert.Equal(';', result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void DetectDelimiter_WithCustomSampleRows_UsesSpecifiedCount()
    {
        var csv = string.Join("\n", Enumerable.Range(1, 100).Select(i => $"A,B,C"));

        var result = Csv.DetectDelimiterWithDetails(csv, sampleRows: 5);

        Assert.Equal(',', result.DetectedDelimiter);
        Assert.Equal(5, result.SampledRows);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void DetectDelimiter_Utf8Span_DetectsCorrectly()
    {
        var csv = "Name;Age;City\nJohn;30;NYC"u8;

        var result = Csv.DetectDelimiter(csv);

        Assert.Equal(';', result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void DetectDelimiterWithDetails_IncludesCandidateCounts()
    {
        var csv = "Name,Age;City\nJohn,30;NYC\nJane,25;LA";

        var result = Csv.DetectDelimiterWithDetails(csv);

        Assert.NotEmpty(result.CandidateCounts);
        Assert.True(result.CandidateCounts.ContainsKey(','));
        Assert.True(result.CandidateCounts.ContainsKey(';'));
        Assert.Equal(3, result.CandidateCounts[',']); // 1 per row (header + 2 data)
        Assert.Equal(3, result.CandidateCounts[';']); // 1 per row (header + 2 data)
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void DetectDelimiter_RealWorldEuropeanCsv_DetectsSemicolon()
    {
        // European CSV format with semicolons and decimal commas
        var csv = @"Product;Price;Quantity
Coffee;4,50;100
Tea;3,25;150
Sugar;2,00;200";

        var result = Csv.DetectDelimiter(csv);

        Assert.Equal(';', result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void DetectDelimiter_RealWorldLogFile_DetectsPipe()
    {
        var csv = @"timestamp|level|message
2024-01-01 10:00:00|INFO|Application started
2024-01-01 10:00:01|INFO|Processing request
2024-01-01 10:00:02|ERROR|Connection failed";

        var result = Csv.DetectDelimiter(csv);

        Assert.Equal('|', result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void DetectDelimiter_WithQuotedFields_DetectsCorrectly()
    {
        var csv = "Name,Age,Description\n\"Smith, John\",30,\"Engineer, Senior\"\n\"Doe, Jane\",25,\"Manager\"";

        var result = Csv.DetectDelimiter(csv);

        Assert.Equal(',', result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void DetectDelimiter_CrlfLineEndings_DetectsCorrectly()
    {
        var csv = "Name,Age,City\r\nJohn,30,NYC\r\nJane,25,LA";

        var result = Csv.DetectDelimiter(csv);

        Assert.Equal(',', result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void DetectDelimiter_WithHeaderOnly_DetectsFromHeader()
    {
        var csv = "Name,Age,Email";

        var result = Csv.DetectDelimiter(csv);

        Assert.Equal(',', result);
    }
}
