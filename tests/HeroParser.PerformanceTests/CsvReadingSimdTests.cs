using HeroParser.Core;
using HeroParser.Configuration;
using Xunit;
using System.Diagnostics;

namespace HeroParser.PerformanceTests;

/// <summary>
/// Platform-specific SIMD tests for CSV reading performance.
/// T029: Testing SIMD optimization effectiveness across different hardware.
/// </summary>
public class CsvReadingSimdTests
{
    private const string TestCsvSmall = "Name,Age,City\nJohn,25,NYC\nJane,30,LA\nBob,35,Chicago";
    private static readonly string TestCsvLarge = CreateLargeCsv();

    private static string CreateLargeCsv()
    {
        var builder = new System.Text.StringBuilder();
        builder.AppendLine("Name,Age,City,Country,Salary");
        for (int i = 0; i < 10000; i++)
        {
            builder.AppendLine($"Person{i},{20 + (i % 50)},City{i % 100},Country{i % 20},{30000 + (i * 10)}");
        }
        return builder.ToString();
    }

    [Fact]
    public void HardwareCapabilities_ShouldReportAccurately()
    {
        // Test hardware detection
        var supportsAvx2 = SpanOperations.HardwareCapabilities.SupportsAvx2;
        var supportsSse2 = SpanOperations.HardwareCapabilities.SupportsSse2;
        var vectorSize = SpanOperations.HardwareCapabilities.OptimalVectorSize;

        // Log capabilities for debugging
        Console.WriteLine($"Hardware Capabilities:");
        Console.WriteLine($"  AVX2: {supportsAvx2}");
        Console.WriteLine($"  SSE2: {supportsSse2}");
        Console.WriteLine($"  Optimal Vector Size: {vectorSize} bytes");

#if NET6_0_OR_GREATER
        // On modern platforms, we should detect some SIMD capability
        Assert.True(supportsSse2 || supportsAvx2, "Should detect at least SSE2 support on modern hardware");

        if (supportsAvx2)
        {
            Assert.Equal(32, vectorSize);
        }
        else if (supportsSse2)
        {
            Assert.Equal(16, vectorSize);
        }
#else
        // On older frameworks, capabilities should be false
        Assert.False(supportsAvx2);
        Assert.False(supportsSse2);
        Assert.Equal(0, vectorSize);
#endif
    }

    [Fact]
    public void FastScanForCsvSpecialChars_ShouldFindDelimiters()
    {
        var testData = "John,25,NYC\nJane,30";
        var span = testData.AsSpan();

        // Test comma detection
        var commaIndex = SpanOperations.FastScanForCsvSpecialChars(span, ',', '"');
        Assert.Equal(4, commaIndex); // First comma after "John"

        // Test newline detection
        var newlineIndex = SpanOperations.FastScanForCsvSpecialChars(span.Slice(5), '\n', '"');
        Assert.Equal(6, newlineIndex); // Newline after "NYC" (position within slice)
    }

    [Fact]
    public void IntegratedSIMD_ShouldParseCorrectly()
    {
        var config = CsvReadConfiguration.Default;
        var records = Csv.ParseString(TestCsvSmall, config);

        Assert.Equal(4, records.Length); // Header + 3 data rows
        Assert.Equal(3, records[0].Length); // 3 columns
        Assert.Equal("Name", records[0][0]);
        Assert.Equal("John", records[1][0]);
        Assert.Equal("25", records[1][1]);
    }

    [Fact]
    public void IntegratedSIMD_PerformanceComparison()
    {
        var config = CsvReadConfiguration.Default;
        var iterations = 100;

        // Measure integrated SIMD performance
        var stopwatch = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            var records = Csv.ParseString(TestCsvLarge, config);
        }
        stopwatch.Stop();
        var simdTime = stopwatch.ElapsedMilliseconds;

        // Log performance results
        Console.WriteLine($"Integrated SIMD Performance Test Results:");
        Console.WriteLine($"  Data Size: ~{TestCsvLarge.Length / 1024.0:F1} KB");
        Console.WriteLine($"  Iterations: {iterations}");
        Console.WriteLine($"  Total Time: {simdTime} ms");
        Console.WriteLine($"  Average Time: {simdTime / (double)iterations:F2} ms per parse");
        Console.WriteLine($"  Throughput: {(TestCsvLarge.Length * iterations / 1024.0 / 1024.0) / (simdTime / 1000.0):F1} MB/s");

        // Verify functionality while measuring performance
        var finalRecords = Csv.ParseString(TestCsvLarge, config);
        Assert.Equal(10001, finalRecords.Length); // Header + 10000 data rows
    }

    [Theory]
    [InlineData("simple,csv,data\nrow2,val2,val3")]
    [InlineData("\"quoted,field\",normal,\"another\"")]
    [InlineData("field1,field2\nval1,val2\nval3,val4")]
    public void IntegratedSIMD_ShouldHandleVariousFormats(string csvData)
    {
        var config = CsvReadConfiguration.Default;
        var records = Csv.ParseString(csvData, config);

        // Basic validation - should not throw and should parse some data
        Assert.NotEmpty(records);
        Assert.All(records, record => Assert.NotEmpty(record));

        Console.WriteLine($"Parsed {records.Length} records from: {csvData}");
    }

    [Fact]
    public void IntegratedSIMD_ShouldHandleEmptyAndEdgeCases()
    {
        var config = CsvReadConfiguration.Default;

        // Empty string
        var emptyRecords = Csv.ParseString("", config);
        Assert.Empty(emptyRecords);

        // Single field
        var singleRecords = Csv.ParseString("single", config);
        Assert.Single(singleRecords);
        Assert.Single(singleRecords[0]);
        Assert.Equal("single", singleRecords[0][0]);

        // Just header
        var headerOnlyRecords = Csv.ParseString("Name,Age", config);
        Assert.Single(headerOnlyRecords);
        Assert.Equal(2, headerOnlyRecords[0].Length);
    }

    [Fact]
    public void SpanOperations_IndexOfNewLine_ShouldDetectAllFormats()
    {
        // Test CR
        var crSpan = "text\rmore".AsSpan();
        var crResult = SpanOperations.IndexOfNewLine(crSpan);
        Assert.Equal(4, crResult.Index);
        Assert.Equal(1, crResult.Length);

        // Test LF
        var lfSpan = "text\nmore".AsSpan();
        var lfResult = SpanOperations.IndexOfNewLine(lfSpan);
        Assert.Equal(4, lfResult.Index);
        Assert.Equal(1, lfResult.Length);

        // Test CRLF
        var crlfSpan = "text\r\nmore".AsSpan();
        var crlfResult = SpanOperations.IndexOfNewLine(crlfSpan);
        Assert.Equal(4, crlfResult.Index);
        Assert.Equal(2, crlfResult.Length);

        // Test no newline
        var noNewlineSpan = "text".AsSpan();
        var noResult = SpanOperations.IndexOfNewLine(noNewlineSpan);
        Assert.Equal(-1, noResult.Index);
        Assert.Equal(0, noResult.Length);
    }

    [Fact]
    public void SpanOperations_UnescapeQuotedField_ShouldHandleEscapes()
    {
        var source = "\"field with \"\"quoted\"\" content\"".AsSpan();
        Span<char> destination = stackalloc char[source.Length];

        var length = SpanOperations.UnescapeQuotedField(source, destination);
        var result = destination.Slice(0, length).ToString();

        Assert.Equal("field with \"quoted\" content", result);
    }

    [Fact]
    public void SpanOperations_CountOccurrences_ShouldCountAccurately()
    {
        var testSpan = "a,b,c,d,e".AsSpan();
        var commaCount = SpanOperations.CountOccurrences(testSpan, ',');
        Assert.Equal(4, commaCount);

        var noMatchSpan = "abcde".AsSpan();
        var noMatchCount = SpanOperations.CountOccurrences(noMatchSpan, ',');
        Assert.Equal(0, noMatchCount);
    }

    [Fact]
    public void SpanOperations_TrimWhitespace_ShouldTrimCorrectly()
    {
        var testSpan = "  hello world  ".AsSpan();
        var (start, length) = SpanOperations.TrimWhitespace(testSpan);
        var trimmed = testSpan.Slice(start, length).ToString();
        Assert.Equal("hello world", trimmed);

        // Test all whitespace
        var allWhitespaceSpan = "   ".AsSpan();
        var (allStart, allLength) = SpanOperations.TrimWhitespace(allWhitespaceSpan);
        Assert.Equal(0, allLength);

        // Test no whitespace
        var noWhitespaceSpan = "hello".AsSpan();
        var (noStart, noLength) = SpanOperations.TrimWhitespace(noWhitespaceSpan);
        Assert.Equal(0, noStart);
        Assert.Equal(5, noLength);
    }
}