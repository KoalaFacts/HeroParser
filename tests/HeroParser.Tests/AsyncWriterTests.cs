using HeroParser.SeparatedValues;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Writing;
using System.Text;
using Xunit;

namespace HeroParser.Tests;

/// <summary>
/// Tests for async CSV writer functionality using CsvAsyncStreamWriter.
/// </summary>
// Run async writer tests sequentially to avoid ArrayPool race conditions
[Collection("AsyncWriterTests")]
public class AsyncWriterTests
{
    #region Test Models

    public class TestRecord
    {
        public string? Name { get; set; }
        public int Age { get; set; }
        public string? City { get; set; }
    }

    #endregion

    #region Basic Async Writing Tests

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task CsvAsyncStreamWriter_WriteRowAsync_WritesCorrectly()
    {
        using var ms = new MemoryStream();
        await using var writer = new CsvAsyncStreamWriter(ms, CsvWriterOptions.Default, Encoding.UTF8, leaveOpen: true);

        await writer.WriteRowAsync(["Alice", "30", "NYC"], TestContext.Current.CancellationToken);
        await writer.WriteRowAsync(["Bob", "25", "LA"], TestContext.Current.CancellationToken);
        await writer.FlushAsync(TestContext.Current.CancellationToken);

        ms.Position = 0;
        using var reader = new StreamReader(ms);
        var csv = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);

        Assert.Contains("Alice", csv);
        Assert.Contains("30", csv);
        Assert.Contains("Bob", csv);
        Assert.Contains("25", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task CsvAsyncStreamWriter_WriteFieldAsync_WritesCorrectly()
    {
        using var ms = new MemoryStream();
        await using var writer = new CsvAsyncStreamWriter(ms, CsvWriterOptions.Default, Encoding.UTF8, leaveOpen: true);

        await writer.WriteFieldAsync("Header1", TestContext.Current.CancellationToken);
        await writer.WriteFieldAsync("Header2", TestContext.Current.CancellationToken);
        await writer.WriteFieldAsync("Header3", TestContext.Current.CancellationToken);
        await writer.EndRowAsync(TestContext.Current.CancellationToken);

        await writer.WriteFieldAsync("Value1", TestContext.Current.CancellationToken);
        await writer.WriteFieldAsync("Value2", TestContext.Current.CancellationToken);
        await writer.WriteFieldAsync("Value3", TestContext.Current.CancellationToken);
        await writer.EndRowAsync(TestContext.Current.CancellationToken);

        await writer.FlushAsync(TestContext.Current.CancellationToken);

        ms.Position = 0;
        using var reader = new StreamReader(ms);
        var csv = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);

        Assert.Contains("Header1,Header2,Header3", csv);
        Assert.Contains("Value1,Value2,Value3", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task WriteToFileAsync_WritesRecordsToFile()
    {
        var records = ToAsyncEnumerable([
            new TestRecord { Name = "Alice", Age = 30, City = "NYC" },
            new TestRecord { Name = "Bob", Age = 25, City = "LA" }
        ]);

        var tempPath = Path.Combine(Path.GetTempPath(), $"async_writer_test_{Guid.NewGuid()}.csv");

        try
        {
            await Csv.WriteToFileAsync(tempPath, records, cancellationToken: TestContext.Current.CancellationToken);

            var csv = await File.ReadAllTextAsync(tempPath, TestContext.Current.CancellationToken);

            Assert.Contains("Name", csv);
            Assert.Contains("Age", csv);
            Assert.Contains("Alice", csv);
            Assert.Contains("30", csv);
            Assert.Contains("Bob", csv);
            Assert.Contains("25", csv);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task WriteToStreamAsync_WritesRecordsToStream()
    {
        var records = ToAsyncEnumerable([
            new TestRecord { Name = "Charlie", Age = 35, City = "SF" }
        ]);

        using var ms = new MemoryStream();
        await Csv.WriteToStreamAsync(ms, records, leaveOpen: true, cancellationToken: TestContext.Current.CancellationToken);

        ms.Position = 0;
        using var reader = new StreamReader(ms);
        var csv = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);

        Assert.Contains("Charlie", csv);
        Assert.Contains("35", csv);
        Assert.Contains("SF", csv);
    }

    #endregion

    #region Cancellation Tests

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task CsvAsyncStreamWriter_CancellationToken_ThrowsWhenCancelled()
    {
        using var cts = new CancellationTokenSource();
        using var ms = new MemoryStream();
        await using var writer = new CsvAsyncStreamWriter(ms, CsvWriterOptions.Default, Encoding.UTF8, leaveOpen: true);

        // Cancel immediately
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await writer.WriteRowAsync(["test"], cts.Token);
        });
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task WriteToFileAsync_CancellationToken_ThrowsWhenCancelled()
    {
        var records = SlowAsyncEnumerable();
        var tempPath = Path.Combine(Path.GetTempPath(), $"async_cancel_test_{Guid.NewGuid()}.csv");

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(10));

        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                await Csv.WriteToFileAsync(tempPath, records, cancellationToken: cts.Token);
            });
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    #endregion

    #region Large Dataset Streaming Tests

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task CsvAsyncStreamWriter_LargeDataset_StreamsEfficiently()
    {
        const int recordCount = 10_000;
        var records = GenerateLargeAsyncDataset(recordCount);

        using var ms = new MemoryStream();
        var options = new CsvWriterOptions { WriteHeader = true };

        await Csv.WriteToStreamAsync(
            ms,
            records,
            options,
            leaveOpen: true,
            cancellationToken: TestContext.Current.CancellationToken);

        ms.Position = 0;
        using var reader = new StreamReader(ms);
        var csv = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);

        // Verify we got all records
        var lineCount = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
        Assert.True(lineCount >= recordCount, $"Expected at least {recordCount} lines, got {lineCount}");
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task CsvAsyncStreamWriter_VeryLargeFields_HandlesCorrectly()
    {
        var largeField = new string('X', 100_000);
        var records = ToAsyncEnumerable([
            new TestRecord { Name = largeField, Age = 1, City = "Test" }
        ]);

        using var ms = new MemoryStream();
        await Csv.WriteToStreamAsync(
            ms,
            records,
            leaveOpen: true,
            cancellationToken: TestContext.Current.CancellationToken);

        ms.Position = 0;
        using var reader = new StreamReader(ms);
        var csv = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);

        Assert.Contains(largeField, csv);
    }

    #endregion

    #region Disposal Tests

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task CsvAsyncStreamWriter_DisposeAsync_FlushesAndReleases()
    {
        var ms = new MemoryStream();
        var writer = new CsvAsyncStreamWriter(ms, CsvWriterOptions.Default, Encoding.UTF8, leaveOpen: true);

        await writer.WriteRowAsync(["test", "data"], TestContext.Current.CancellationToken);
        await writer.DisposeAsync();

        // Verify data was flushed
        ms.Position = 0;
        using var reader = new StreamReader(ms);
        var csv = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);

        Assert.Contains("test", csv);
        Assert.Contains("data", csv);

        // Verify stream is still usable (leaveOpen = true)
        Assert.True(ms.CanRead);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task CsvAsyncStreamWriter_LeaveOpenFalse_DisposesStream()
    {
        var ms = new MemoryStream();
        await using (var writer = new CsvAsyncStreamWriter(ms, CsvWriterOptions.Default, Encoding.UTF8, leaveOpen: false))
        {
            await writer.WriteRowAsync(["test"], TestContext.Current.CancellationToken);
        }

        // Stream should be disposed
        Assert.False(ms.CanRead);
        Assert.False(ms.CanWrite);
    }

    #endregion

    #region Format and Options Tests

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task CsvAsyncStreamWriter_CustomDelimiter_WritesCorrectly()
    {
        using var ms = new MemoryStream();
        var options = new CsvWriterOptions { Delimiter = ';' };
        await using var writer = new CsvAsyncStreamWriter(ms, options, Encoding.UTF8, leaveOpen: true);

        await writer.WriteRowAsync(["a", "b", "c"], TestContext.Current.CancellationToken);
        await writer.FlushAsync(TestContext.Current.CancellationToken);

        ms.Position = 0;
        using var reader = new StreamReader(ms);
        var csv = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);

        Assert.Contains("a;b;c", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task CsvAsyncStreamWriter_QuoteStyle_Always_QuotesAllFields()
    {
        using var ms = new MemoryStream();
        var options = new CsvWriterOptions { QuoteStyle = QuoteStyle.Always };
        await using var writer = new CsvAsyncStreamWriter(ms, options, Encoding.UTF8, leaveOpen: true);

        await writer.WriteRowAsync(["simple", "text"], TestContext.Current.CancellationToken);
        await writer.FlushAsync(TestContext.Current.CancellationToken);

        ms.Position = 0;
        using var reader = new StreamReader(ms);
        var csv = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);

        Assert.Contains("\"simple\",\"text\"", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task CsvAsyncStreamWriter_InjectionProtection_EscapesFormulas()
    {
        using var ms = new MemoryStream();
        var options = new CsvWriterOptions { InjectionProtection = CsvInjectionProtection.EscapeWithQuote };
        await using var writer = new CsvAsyncStreamWriter(ms, options, Encoding.UTF8, leaveOpen: true);

        await writer.WriteRowAsync(["=SUM(A1:A10)", "normal"], TestContext.Current.CancellationToken);
        await writer.FlushAsync(TestContext.Current.CancellationToken);

        ms.Position = 0;
        using var reader = new StreamReader(ms);
        var csv = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);

        // Should be quoted and prefixed with single quote
        Assert.Contains("\"'=SUM(A1:A10)\"", csv);
        Assert.Contains("normal", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task CsvAsyncStreamWriter_MaxOutputSize_ThrowsWhenExceeded()
    {
        using var ms = new MemoryStream();
        var options = new CsvWriterOptions { MaxOutputSize = 100 };
        var writer = new CsvAsyncStreamWriter(ms, options, Encoding.UTF8, leaveOpen: true);

        var exception = await Assert.ThrowsAsync<CsvException>(async () =>
        {
            for (int i = 0; i < 100; i++)
            {
                await writer.WriteRowAsync(["data", "data", "data", "data"], TestContext.Current.CancellationToken);
                // Force flush to trigger size check sooner
                await writer.FlushAsync(TestContext.Current.CancellationToken);
            }
        });

        Assert.Equal(CsvErrorCode.OutputSizeExceeded, exception.ErrorCode);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task CsvAsyncStreamWriter_MaxColumnCount_ThrowsWhenExceeded()
    {
        using var ms = new MemoryStream();
        var options = new CsvWriterOptions { MaxColumnCount = 3 };
        await using var writer = new CsvAsyncStreamWriter(ms, options, Encoding.UTF8, leaveOpen: true);

        await Assert.ThrowsAsync<CsvException>(async () =>
        {
            await writer.WriteRowAsync(["a", "b", "c", "d"], TestContext.Current.CancellationToken);
        });
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task CsvAsyncStreamWriter_MaxFieldSize_ThrowsWhenExceeded()
    {
        using var ms = new MemoryStream();
        var options = new CsvWriterOptions { MaxFieldSize = 10 };
        await using var writer = new CsvAsyncStreamWriter(ms, options, Encoding.UTF8, leaveOpen: true);

        await Assert.ThrowsAsync<CsvException>(async () =>
        {
            await writer.WriteRowAsync(["short", "this is a very long field that exceeds limit"], TestContext.Current.CancellationToken);
        });
    }

    #endregion

    #region Builder Pattern Tests

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task Builder_ToFileAsync_WritesCorrectly()
    {
        var records = ToAsyncEnumerable([
            new TestRecord { Name = "David", Age = 40, City = "Seattle" }
        ]);

        var tempPath = Path.Combine(Path.GetTempPath(), $"async_builder_test_{Guid.NewGuid()}.csv");

        try
        {
            await Csv.Write<TestRecord>()
                .WithDelimiter(',')
                .WithHeader()
                .ToFileAsync(tempPath, records, TestContext.Current.CancellationToken);

            var csv = await File.ReadAllTextAsync(tempPath, TestContext.Current.CancellationToken);

            Assert.Contains("Name", csv);
            Assert.Contains("David", csv);
            Assert.Contains("40", csv);
            Assert.Contains("Seattle", csv);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task DirectAsyncStreamWriter_WritesManually()
    {
        using var ms = new MemoryStream();
        await using var writer = new CsvAsyncStreamWriter(ms, CsvWriterOptions.Default, Encoding.UTF8, leaveOpen: true);

        await writer.WriteRowAsync(["Header1", "Header2"], TestContext.Current.CancellationToken);
        await writer.WriteRowAsync(["Value1", "Value2"], TestContext.Current.CancellationToken);
        await writer.FlushAsync(TestContext.Current.CancellationToken);

        ms.Position = 0;
        using var reader = new StreamReader(ms);
        var csv = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);

        Assert.Contains("Header1,Header2", csv);
        Assert.Contains("Value1,Value2", csv);
    }

    #endregion

    #region Edge Cases

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task CsvAsyncStreamWriter_EmptyRow_WritesNewline()
    {
        using var ms = new MemoryStream();
        await using var writer = new CsvAsyncStreamWriter(ms, CsvWriterOptions.Default, Encoding.UTF8, leaveOpen: true);

        await writer.EndRowAsync(TestContext.Current.CancellationToken);
        await writer.FlushAsync(TestContext.Current.CancellationToken);

        ms.Position = 0;
        using var reader = new StreamReader(ms);
        var csv = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);

        Assert.Equal("\r\n", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task CsvAsyncStreamWriter_UnicodeCharacters_WritesCorrectly()
    {
        using var ms = new MemoryStream();
        await using var writer = new CsvAsyncStreamWriter(ms, CsvWriterOptions.Default, Encoding.UTF8, leaveOpen: true);

        await writer.WriteRowAsync(["日本語", "中文", "한국어", "Emoji 😀"], TestContext.Current.CancellationToken);
        await writer.FlushAsync(TestContext.Current.CancellationToken);

        ms.Position = 0;
        using var reader = new StreamReader(ms, Encoding.UTF8);
        var csv = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);

        Assert.Contains("日本語", csv);
        Assert.Contains("中文", csv);
        Assert.Contains("한국어", csv);
        Assert.Contains("😀", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task CsvAsyncStreamWriter_FieldsWithQuotesAndDelimiters_EscapesCorrectly()
    {
        using var ms = new MemoryStream();
        await using var writer = new CsvAsyncStreamWriter(ms, CsvWriterOptions.Default, Encoding.UTF8, leaveOpen: true);

        await writer.WriteRowAsync(["a,b", "c\"d", "e\r\nf"], TestContext.Current.CancellationToken);
        await writer.FlushAsync(TestContext.Current.CancellationToken);

        ms.Position = 0;
        using var reader = new StreamReader(ms);
        var csv = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);

        Assert.Contains("\"a,b\"", csv);
        Assert.Contains("\"c\"\"d\"", csv);
        Assert.Contains("\"e\r\nf\"", csv);
    }

    #endregion

    #region CreateStreamWriter(Stream) Tests

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public void CreateStreamWriter_Stream_ReturnsWriter()
    {
        using var ms = new MemoryStream();
        using var writer = Csv.CreateStreamWriter(ms, leaveOpen: true);

        writer.WriteRow(["Hello", "World"]);
        writer.Flush();

        ms.Position = 0;
        using var reader = new StreamReader(ms);
        var result = reader.ReadToEnd();

        Assert.Contains("Hello", result);
        Assert.Contains("World", result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public void CreateStreamWriter_Stream_WithOptions_AppliesOptions()
    {
        using var ms = new MemoryStream();
        var options = new CsvWriterOptions { Delimiter = ';' };
        using var writer = Csv.CreateStreamWriter(ms, options, leaveOpen: true);

        writer.WriteRow(["A", "B", "C"]);
        writer.Flush();

        ms.Position = 0;
        using var reader = new StreamReader(ms);
        var result = reader.ReadToEnd();

        Assert.Contains("A;B;C", result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public void CreateStreamWriter_Stream_LeaveOpenFalse_DisposesStream()
    {
        var ms = new MemoryStream();
        using (var writer = Csv.CreateStreamWriter(ms, leaveOpen: false))
        {
            writer.WriteRow(["Test"]);
        }

        Assert.False(ms.CanRead);
        Assert.False(ms.CanWrite);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public void CreateStreamWriter_Stream_LeaveOpenTrue_PreservesStream()
    {
        using var ms = new MemoryStream();
        using (var writer = Csv.CreateStreamWriter(ms, leaveOpen: true))
        {
            writer.WriteRow(["Test"]);
        }

        Assert.True(ms.CanRead);
        Assert.True(ms.CanWrite);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public void CreateStreamWriter_Stream_CustomEncoding_UsesEncoding()
    {
        using var ms = new MemoryStream();
        using var writer = Csv.CreateStreamWriter(ms, encoding: Encoding.Unicode, leaveOpen: true);

        writer.WriteRow(["Test"]);
        writer.Flush();

        ms.Position = 0;
        using var reader = new StreamReader(ms, Encoding.Unicode);
        var result = reader.ReadToEnd();

        Assert.Contains("Test", result);
    }

    #endregion

    #region CreateAsyncStreamWriter Tests

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task CreateAsyncStreamWriter_Stream_ReturnsWriter()
    {
        using var ms = new MemoryStream();
        await using var writer = Csv.CreateAsyncStreamWriter(ms, leaveOpen: true);

        await writer.WriteRowAsync(["Hello", "World"], TestContext.Current.CancellationToken);
        await writer.FlushAsync(TestContext.Current.CancellationToken);

        ms.Position = 0;
        using var reader = new StreamReader(ms);
        var result = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);

        Assert.Contains("Hello", result);
        Assert.Contains("World", result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task CreateAsyncStreamWriter_WithOptions_AppliesOptions()
    {
        using var ms = new MemoryStream();
        var options = new CsvWriterOptions { Delimiter = ';' };
        await using var writer = Csv.CreateAsyncStreamWriter(ms, options, leaveOpen: true);

        await writer.WriteRowAsync(["A", "B", "C"], TestContext.Current.CancellationToken);
        await writer.FlushAsync(TestContext.Current.CancellationToken);

        ms.Position = 0;
        using var reader = new StreamReader(ms);
        var result = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);

        Assert.Contains("A;B;C", result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task CreateAsyncStreamWriter_LeaveOpenFalse_DisposesStream()
    {
        var ms = new MemoryStream();
        await using (var writer = Csv.CreateAsyncStreamWriter(ms, leaveOpen: false))
        {
            await writer.WriteRowAsync(["Test"], TestContext.Current.CancellationToken);
        }

        Assert.False(ms.CanRead);
        Assert.False(ms.CanWrite);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task CreateAsyncStreamWriter_LeaveOpenTrue_PreservesStream()
    {
        using var ms = new MemoryStream();
        await using (var writer = Csv.CreateAsyncStreamWriter(ms, leaveOpen: true))
        {
            await writer.WriteRowAsync(["Test"], TestContext.Current.CancellationToken);
        }

        Assert.True(ms.CanRead);
        Assert.True(ms.CanWrite);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task CreateAsyncStreamWriter_MultipleRows_WritesCorrectly()
    {
        using var ms = new MemoryStream();
        await using var writer = Csv.CreateAsyncStreamWriter(ms, leaveOpen: true);

        await writer.WriteRowAsync(["Alice", "30", "NYC"], TestContext.Current.CancellationToken);
        await writer.WriteRowAsync(["Bob", "25", "LA"], TestContext.Current.CancellationToken);
        await writer.FlushAsync(TestContext.Current.CancellationToken);

        ms.Position = 0;
        using var reader = new StreamReader(ms);
        var result = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);

        Assert.Contains("Alice", result);
        Assert.Contains("Bob", result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task CreateAsyncStreamWriter_WriteFieldAsync_WritesFields()
    {
        using var ms = new MemoryStream();
        await using var writer = Csv.CreateAsyncStreamWriter(ms, leaveOpen: true);

        await writer.WriteFieldAsync("Field1", TestContext.Current.CancellationToken);
        await writer.WriteFieldAsync("Field2", TestContext.Current.CancellationToken);
        await writer.WriteFieldAsync("Field3", TestContext.Current.CancellationToken);
        await writer.EndRowAsync(TestContext.Current.CancellationToken);
        await writer.FlushAsync(TestContext.Current.CancellationToken);

        ms.Position = 0;
        using var reader = new StreamReader(ms);
        var result = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);

        Assert.Contains("Field1,Field2,Field3", result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task CreateAsyncStreamWriter_QuoteStyleAlways_QuotesAllFields()
    {
        using var ms = new MemoryStream();
        var options = new CsvWriterOptions { QuoteStyle = QuoteStyle.Always };
        await using var writer = Csv.CreateAsyncStreamWriter(ms, options, leaveOpen: true);

        await writer.WriteRowAsync(["simple", "text"], TestContext.Current.CancellationToken);
        await writer.FlushAsync(TestContext.Current.CancellationToken);

        ms.Position = 0;
        using var reader = new StreamReader(ms);
        var result = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);

        Assert.Contains("\"simple\",\"text\"", result);
    }

    #endregion

    #region ToStreamAsyncStreaming Tests

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task Builder_ToStreamAsyncStreaming_IAsyncEnumerable_WritesCorrectly()
    {
        var records = ToAsyncEnumerable([
            new TestRecord { Name = "Streaming1", Age = 10, City = "City1" },
            new TestRecord { Name = "Streaming2", Age = 20, City = "City2" }
        ]);

        using var ms = new MemoryStream();
        await Csv.Write<TestRecord>()
            .WithHeader()
            .ToStreamAsyncStreaming(ms, records, leaveOpen: true, cancellationToken: TestContext.Current.CancellationToken);

        ms.Position = 0;
        using var reader = new StreamReader(ms);
        var content = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);

        Assert.Contains("Streaming1", content);
        Assert.Contains("10", content);
        Assert.Contains("Streaming2", content);
        Assert.Contains("20", content);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task Builder_ToStreamAsyncStreaming_IEnumerable_WritesCorrectly()
    {
        var records = new[]
        {
            new TestRecord { Name = "DirectStream1", Age = 15, City = "TestCity1" },
            new TestRecord { Name = "DirectStream2", Age = 25, City = "TestCity2" }
        };

        using var ms = new MemoryStream();
        await Csv.Write<TestRecord>()
            .WithHeader()
            .ToStreamAsyncStreaming(ms, records, leaveOpen: true, cancellationToken: TestContext.Current.CancellationToken);

        ms.Position = 0;
        using var reader = new StreamReader(ms);
        var content = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);

        Assert.Contains("DirectStream1", content);
        Assert.Contains("15", content);
        Assert.Contains("DirectStream2", content);
        Assert.Contains("25", content);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task Builder_ToStreamAsyncStreaming_LeaveOpenFalse_ClosesStream()
    {
        var records = new[]
        {
            new TestRecord { Name = "Test", Age = 1, City = "City" }
        };

        var ms = new MemoryStream();
        await Csv.Write<TestRecord>()
            .ToStreamAsyncStreaming(ms, records, leaveOpen: false, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(ms.CanRead);
        Assert.False(ms.CanWrite);
    }

    #endregion

    #region Helper Methods

    private static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(IEnumerable<T> source)
    {
        foreach (var item in source)
        {
            await Task.Yield();
            yield return item;
        }
    }

    private static async IAsyncEnumerable<TestRecord> SlowAsyncEnumerable()
    {
        for (int i = 0; i < 1000; i++)
        {
            await Task.Delay(10);
            yield return new TestRecord { Name = $"Name{i}", Age = i, City = $"City{i}" };
        }
    }

    private static async IAsyncEnumerable<TestRecord> GenerateLargeAsyncDataset(int count)
    {
        for (int i = 0; i < count; i++)
        {
            if (i % 100 == 0)
            {
                await Task.Yield();
            }
            yield return new TestRecord
            {
                Name = $"Name{i}",
                Age = i % 100,
                City = $"City{i % 10}"
            };
        }
    }

    #endregion
}
