using System.Globalization;
using System.IO.Pipelines;
using System.Text;
using HeroParser.FixedWidths;
using HeroParser.JsonLines;
using HeroParser.JsonLines.Reading;
using HeroParser.JsonLines.Writing;
using HeroParser.SeparatedValues.Core;
using Xunit;

namespace HeroParser.Tests.Validation;

/// <summary>
/// Size and limit error messages are fixed English text, so their grouped numbers use the
/// invariant culture whatever the current culture is: under de-DE a limit reads "1,048,576",
/// not "1.048.576" (or "1 048 576" under fr-FR).
/// </summary>
[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
public class LimitMessageCultureTests
{
    private static T WithCulture<T>(string cultureName, Func<T> action)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            return action();
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    private static async Task<T> WithCultureAsync<T>(string cultureName, Func<Task<T>> action)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            return await action();
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    [InlineData("en-US")]
    public void CsvReadOptions_MaxColumnCount_UsesInvariantNumbers(string cultureName)
    {
        var ex = WithCulture(cultureName, () =>
            Assert.Throws<CsvException>(() => new CsvReadOptions { MaxColumnCount = 20_000 }.Validate()));

        Assert.Equal("MaxColumnCount cannot exceed 10,000, got 20,000", ex.Message);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    [InlineData("en-US")]
    public void CsvReadOptions_MaxInputSize_UsesInvariantNumbers(string cultureName)
    {
        var ex = WithCulture(cultureName, () =>
            Assert.Throws<CsvException>(() => new CsvReadOptions { MaxInputSize = 1_000 }.ValidateInputSize(123_456)));

        Assert.StartsWith("Input size (123,456 bytes) exceeds maximum allowed (1,000 bytes). ", ex.Message);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    [InlineData("en-US")]
    public void CsvException_SizeLimits_UseInvariantNumbers(string cultureName)
    {
        var (row, input) = WithCulture(cultureName, () =>
            (CsvException.RowSizeLimitExceeded(1_048_576, isUtf8: true).Message,
             CsvException.InputSizeLimitExceeded(2_500_000, isUtf8: false).Message));

        Assert.Equal("Row exceeds maximum size of 1,048,576 bytes. Ensure rows have proper line endings.", row);
        Assert.Equal("Schema inference exceeds maximum scanned input size of 2,500,000 characters.", input);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    [InlineData("en-US")]
    public void FixedWidthReadOptions_MaxInputSize_UsesInvariantNumbers(string cultureName)
    {
        var ex = WithCulture(cultureName, () =>
            Assert.Throws<FixedWidthException>(() => new FixedWidthReadOptions { MaxInputSize = 1_000 }.ValidateInputSize(123_456)));

        Assert.StartsWith("Input size (123,456 bytes) exceeds maximum allowed (1,000 bytes). ", ex.Message);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    [InlineData("en-US")]
    public async Task Jsonl_PipeMaxLineSize_UsesInvariantNumbers(string cultureName)
    {
        byte[] data = Encoding.UTF8.GetBytes("{\"Name\":\"" + new string('a', 3_000) + "\"}\n");

        var ex = await WithCultureAsync(cultureName, () => Assert.ThrowsAsync<JsonlException>(async () =>
        {
            using var stream = new MemoryStream(data);
            await foreach (JsonlLine line in Jsonl.ReadLinesAsync(
                PipeReader.Create(stream),
                new JsonlReadOptions { MaxLineSizeBytes = 1_500 },
                cancellationToken: TestContext.Current.CancellationToken))
            {
                _ = line;
            }
        }));

        Assert.Contains("MaxLineSizeBytes of 1,500 bytes.", ex.Message);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    [InlineData("en-US")]
    public void JsonlStreamWriter_MaxOutputSize_UsesInvariantNumbers(string cultureName)
    {
        var ex = WithCulture(cultureName, () => Assert.Throws<JsonlException>(() =>
        {
            using var stream = new MemoryStream();
            using var writer = new JsonlStreamWriter(stream, new JsonlWriteOptions { MaxOutputSize = 1_500 });
            writer.WriteRecord(new string('a', 2_000));
        }));

        Assert.Matches(@"^Output size of 2,\d{3} bytes exceeds MaxOutputSize of 1,500 bytes\.$", ex.Message);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    [InlineData("en-US")]
    public void JsonlStreamWriter_MaxRowCount_UsesInvariantNumbers(string cultureName)
    {
        var ex = WithCulture(cultureName, () => Assert.Throws<JsonlException>(() =>
        {
            using var stream = new MemoryStream();
            using var writer = new JsonlStreamWriter(stream, new JsonlWriteOptions { MaxRowCount = 1_000 });
            for (var i = 0; i <= 1_000; i++)
                writer.WriteRecord(i);
        }));

        Assert.Equal("MaxRowCount of 1,000 exceeded.", ex.Message);
    }
}
