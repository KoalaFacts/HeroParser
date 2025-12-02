using HeroParser.FixedWidths;
using HeroParser.FixedWidths.Records.Binding;
using HeroParser.FixedWidths.Writing;
using System.Runtime.CompilerServices;
using System.Text;
using Xunit;

namespace HeroParser.Tests.FixedWidths;

/// <summary>
/// Tests for async fixed-width writer functionality.
/// </summary>
public class FixedWidthAsyncWriterTests
{
    #region Test Models

    public class TestRecord
    {
        [FixedWidthColumn(Start = 0, Length = 20)]
        public string? Name { get; set; }

        [FixedWidthColumn(Start = 20, Length = 5, Alignment = FieldAlignment.Right, PadChar = '0')]
        public int Age { get; set; }

        [FixedWidthColumn(Start = 25, Length = 15)]
        public string? City { get; set; }
    }

    #endregion

    #region WriteToFileAsync Tests

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task WriteToFileAsync_IEnumerable_WritesRecordsToFile()
    {
        var records = new[]
        {
            new TestRecord { Name = "Alice", Age = 30, City = "NYC" },
            new TestRecord { Name = "Bob", Age = 25, City = "LA" }
        };

        var tempPath = Path.Combine(Path.GetTempPath(), $"fixedwidth_async_test_{Guid.NewGuid()}.dat");

        try
        {
            await FixedWidth.WriteToFileAsync(tempPath, records, cancellationToken: TestContext.Current.CancellationToken);

            var content = await File.ReadAllTextAsync(tempPath, TestContext.Current.CancellationToken);

            Assert.Contains("Alice", content);
            Assert.Contains("00030", content); // Right-aligned with zeros
            Assert.Contains("NYC", content);
            Assert.Contains("Bob", content);
            Assert.Contains("00025", content);
            Assert.Contains("LA", content);
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
    public async Task WriteToFileAsync_IAsyncEnumerable_WritesRecordsToFile()
    {
        var records = ToAsyncEnumerable([
            new TestRecord { Name = "Charlie", Age = 35, City = "SF" },
            new TestRecord { Name = "Diana", Age = 28, City = "Boston" }
        ]);

        var tempPath = Path.Combine(Path.GetTempPath(), $"fixedwidth_async_enum_test_{Guid.NewGuid()}.dat");

        try
        {
            await FixedWidth.WriteToFileAsync(tempPath, records, cancellationToken: TestContext.Current.CancellationToken);

            var content = await File.ReadAllTextAsync(tempPath, TestContext.Current.CancellationToken);

            Assert.Contains("Charlie", content);
            Assert.Contains("00035", content);
            Assert.Contains("SF", content);
            Assert.Contains("Diana", content);
            Assert.Contains("00028", content);
            Assert.Contains("Boston", content);
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

    #region WriteToStreamAsync Tests

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task WriteToStreamAsync_IEnumerable_WritesRecordsToStream()
    {
        var records = new[]
        {
            new TestRecord { Name = "Eve", Age = 40, City = "Seattle" }
        };

        using var ms = new MemoryStream();
        await FixedWidth.WriteToStreamAsync(ms, records, leaveOpen: true, cancellationToken: TestContext.Current.CancellationToken);

        ms.Position = 0;
        using var reader = new StreamReader(ms);
        var content = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);

        Assert.Contains("Eve", content);
        Assert.Contains("00040", content);
        Assert.Contains("Seattle", content);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task WriteToStreamAsync_IAsyncEnumerable_WritesRecordsToStream()
    {
        var records = ToAsyncEnumerable([
            new TestRecord { Name = "Frank", Age = 45, City = "Denver" }
        ]);

        using var ms = new MemoryStream();
        await FixedWidth.WriteToStreamAsync(ms, records, leaveOpen: true, cancellationToken: TestContext.Current.CancellationToken);

        ms.Position = 0;
        using var reader = new StreamReader(ms);
        var content = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);

        Assert.Contains("Frank", content);
        Assert.Contains("00045", content);
        Assert.Contains("Denver", content);
    }

    #endregion

    #region Cancellation Tests

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task WriteToFileAsync_CancellationToken_ThrowsWhenCancelled()
    {
        var records = SlowAsyncEnumerable();
        var tempPath = Path.Combine(Path.GetTempPath(), $"fixedwidth_async_cancel_test_{Guid.NewGuid()}.dat");

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(10));

        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                await FixedWidth.WriteToFileAsync(tempPath, records, cancellationToken: cts.Token);
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

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task WriteToStreamAsync_CancellationToken_ThrowsWhenCancelled()
    {
        var records = SlowAsyncEnumerable();

        using var cts = new CancellationTokenSource();
        using var ms = new MemoryStream();
        cts.CancelAfter(TimeSpan.FromMilliseconds(10));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await FixedWidth.WriteToStreamAsync(ms, records, leaveOpen: true, cancellationToken: cts.Token);
        });
    }

    #endregion

    #region Large Dataset Tests

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task WriteToStreamAsync_LargeDataset_StreamsEfficiently()
    {
        const int recordCount = 10_000;
        var records = GenerateLargeAsyncDataset(recordCount);

        using var ms = new MemoryStream();

        await FixedWidth.WriteToStreamAsync(
            ms,
            records,
            leaveOpen: true,
            cancellationToken: TestContext.Current.CancellationToken);

        ms.Position = 0;
        using var reader = new StreamReader(ms);
        var content = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);

        // Verify we got all records (each record is on a new line)
        var lineCount = content.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
        Assert.True(lineCount >= recordCount, $"Expected at least {recordCount} lines, got {lineCount}");
    }

    #endregion

    #region Options Tests

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task WriteToStreamAsync_CustomNewLine_AppliesCorrectly()
    {
        var records = new[]
        {
            new TestRecord { Name = "Test1", Age = 1, City = "A" },
            new TestRecord { Name = "Test2", Age = 2, City = "B" }
        };

        var options = new FixedWidthWriterOptions { NewLine = "\n" };

        using var ms = new MemoryStream();
        await FixedWidth.WriteToStreamAsync(ms, records, options, leaveOpen: true, cancellationToken: TestContext.Current.CancellationToken);

        ms.Position = 0;
        using var reader = new StreamReader(ms);
        var content = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);

        // Check that \r\n is not present (just \n)
        Assert.DoesNotContain("\r\n", content);
        Assert.Contains("\n", content);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task WriteToStreamAsync_MaxRowCount_ThrowsWhenExceeded()
    {
        var records = ToAsyncEnumerable(Enumerable.Range(1, 100).Select(i =>
            new TestRecord { Name = $"Name{i}", Age = i, City = $"City{i}" }));

        var options = new FixedWidthWriterOptions { MaxRowCount = 10 };

        using var ms = new MemoryStream();

        await Assert.ThrowsAsync<FixedWidthException>(async () =>
        {
            await FixedWidth.WriteToStreamAsync(ms, records, options, leaveOpen: true, cancellationToken: TestContext.Current.CancellationToken);
        });
    }

    #endregion

    #region Builder Pattern Async Tests

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task Builder_ToFileAsync_WritesCorrectly()
    {
        var records = ToAsyncEnumerable([
            new TestRecord { Name = "Grace", Age = 33, City = "Miami" }
        ]);

        var tempPath = Path.Combine(Path.GetTempPath(), $"fixedwidth_async_builder_test_{Guid.NewGuid()}.dat");

        try
        {
            await FixedWidth.Write<TestRecord>()
                .WithPadChar(' ')
                .ToFileAsync(tempPath, records, TestContext.Current.CancellationToken);

            var content = await File.ReadAllTextAsync(tempPath, TestContext.Current.CancellationToken);

            Assert.Contains("Grace", content);
            Assert.Contains("00033", content);
            Assert.Contains("Miami", content);
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
    public async Task Builder_ToStreamAsync_WritesCorrectly()
    {
        var records = ToAsyncEnumerable([
            new TestRecord { Name = "Henry", Age = 29, City = "Portland" }
        ]);

        using var ms = new MemoryStream();
        await FixedWidth.Write<TestRecord>()
            .WithPadChar(' ')
            .ToStreamAsync(ms, records, leaveOpen: true, cancellationToken: TestContext.Current.CancellationToken);

        ms.Position = 0;
        using var reader = new StreamReader(ms);
        var content = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);

        Assert.Contains("Henry", content);
        Assert.Contains("00029", content);
        Assert.Contains("Portland", content);
    }

    #endregion

    #region Edge Cases

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task WriteToStreamAsync_EmptyRecords_WritesNoRecordData()
    {
        var records = Array.Empty<TestRecord>();

        using var ms = new MemoryStream();
        await FixedWidth.WriteToStreamAsync(ms, records, leaveOpen: true, cancellationToken: TestContext.Current.CancellationToken);

        // May contain BOM or preamble bytes, but no actual record data
        ms.Position = 0;
        using var reader = new StreamReader(ms, Encoding.UTF8);
        var content = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);

        Assert.True(string.IsNullOrWhiteSpace(content), $"Expected no record content but got: '{content}'");
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task WriteToStreamAsync_UnicodeCharacters_WritesCorrectly()
    {
        var records = new[]
        {
            new TestRecord { Name = "日本語テスト", Age = 1, City = "東京" }
        };

        using var ms = new MemoryStream();
        await FixedWidth.WriteToStreamAsync(ms, records, leaveOpen: true, cancellationToken: TestContext.Current.CancellationToken);

        ms.Position = 0;
        using var reader = new StreamReader(ms, Encoding.UTF8);
        var content = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);

        Assert.Contains("日本語", content);
        Assert.Contains("東京", content);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task WriteToStreamAsync_NullValues_HandlesCorrectly()
    {
        var records = new[]
        {
            new TestRecord { Name = null, Age = 0, City = null }
        };

        using var ms = new MemoryStream();
        await FixedWidth.WriteToStreamAsync(ms, records, leaveOpen: true, cancellationToken: TestContext.Current.CancellationToken);

        // Should not throw
        Assert.True(ms.Length > 0);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task WriteToStreamAsync_LeaveOpenFalse_DisposesStream()
    {
        var records = new[]
        {
            new TestRecord { Name = "Test", Age = 1, City = "City" }
        };

        var ms = new MemoryStream();
        await FixedWidth.WriteToStreamAsync(ms, records, leaveOpen: false, cancellationToken: TestContext.Current.CancellationToken);

        // Stream should be disposed
        Assert.False(ms.CanRead);
        Assert.False(ms.CanWrite);
    }

    #endregion

    #region WriteToTextAsync Tests

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task WriteToTextAsync_IAsyncEnumerable_ReturnsCorrectString()
    {
        var records = ToAsyncEnumerable([
            new TestRecord { Name = "Alice", Age = 30, City = "NYC" },
            new TestRecord { Name = "Bob", Age = 25, City = "LA" }
        ]);

        var result = await FixedWidth.WriteToTextAsync(records, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains("Alice", result);
        Assert.Contains("00030", result);
        Assert.Contains("Bob", result);
        Assert.Contains("00025", result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task WriteToTextAsync_EmptyRecords_ReturnsEmptyString()
    {
        var records = ToAsyncEnumerable(Array.Empty<TestRecord>());

        var result = await FixedWidth.WriteToTextAsync(records, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(string.IsNullOrEmpty(result));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task WriteToTextAsync_WithOptions_AppliesOptions()
    {
        var records = ToAsyncEnumerable([
            new TestRecord { Name = "Test", Age = 1, City = "City" }
        ]);

        var options = new FixedWidthWriterOptions { NewLine = "\n" };
        var result = await FixedWidth.WriteToTextAsync(records, options, TestContext.Current.CancellationToken);

        Assert.DoesNotContain("\r\n", result);
        Assert.Contains("Test", result);
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
        await FixedWidth.Write<TestRecord>()
            .ToStreamAsyncStreaming(ms, records, leaveOpen: true, cancellationToken: TestContext.Current.CancellationToken);

        ms.Position = 0;
        using var reader = new StreamReader(ms);
        var content = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);

        Assert.Contains("Streaming1", content);
        Assert.Contains("00010", content);
        Assert.Contains("Streaming2", content);
        Assert.Contains("00020", content);
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
        await FixedWidth.Write<TestRecord>()
            .ToStreamAsyncStreaming(ms, records, leaveOpen: true, cancellationToken: TestContext.Current.CancellationToken);

        ms.Position = 0;
        using var reader = new StreamReader(ms);
        var content = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);

        Assert.Contains("DirectStream1", content);
        Assert.Contains("00015", content);
        Assert.Contains("DirectStream2", content);
        Assert.Contains("00025", content);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task Builder_ToStreamAsyncStreaming_LargeDataset_StreamsEfficiently()
    {
        const int recordCount = 5_000;
        var records = GenerateLargeAsyncDataset(recordCount);

        using var ms = new MemoryStream();
        await FixedWidth.Write<TestRecord>()
            .ToStreamAsyncStreaming(ms, records, leaveOpen: true, cancellationToken: TestContext.Current.CancellationToken);

        ms.Position = 0;
        using var reader = new StreamReader(ms);
        var content = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);

        var lineCount = content.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
        Assert.True(lineCount >= recordCount, $"Expected at least {recordCount} lines, got {lineCount}");
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
        await FixedWidth.Write<TestRecord>()
            .ToStreamAsyncStreaming(ms, records, leaveOpen: false, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(ms.CanRead);
        Assert.False(ms.CanWrite);
    }

    #endregion

    #region CreateWriter Tests (Alias for CreateStreamWriter)

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public void CreateWriter_TextWriter_ReturnsWriter()
    {
        using var sw = new StringWriter();
        using var writer = FixedWidth.CreateWriter(sw);

        writer.WriteField("Test", 10);
        writer.EndRow();
        writer.Flush();

        var result = sw.ToString();
        Assert.StartsWith("Test", result);
        // Writer defaults to CRLF ("\r\n") regardless of platform for RFC compliance
        Assert.Equal(10 + 2, result.Length);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public void CreateWriter_WithOptions_AppliesOptions()
    {
        using var sw = new StringWriter();
        var options = new FixedWidthWriterOptions
        {
            DefaultPadChar = '*',
            DefaultAlignment = FieldAlignment.Right
        };
        using var writer = FixedWidth.CreateWriter(sw, options);

        writer.WriteField("Hi", 5);
        writer.EndRow();
        writer.Flush();

        var result = sw.ToString();
        Assert.StartsWith("***Hi", result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public void CreateWriter_LeaveOpenTrue_DoesNotDisposeTextWriter()
    {
        var sw = new StringWriter();
        using (var writer = FixedWidth.CreateWriter(sw, leaveOpen: true))
        {
            writer.WriteField("Test", 10);
            writer.EndRow();
        }

        // Should still be able to write to StringWriter
        sw.Write("More");
        Assert.Contains("More", sw.ToString());
    }

    #endregion

    #region CreateStreamWriter(Stream) Tests

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public void CreateStreamWriter_Stream_ReturnsWriter()
    {
        using var ms = new MemoryStream();
        using var writer = FixedWidth.CreateStreamWriter(ms, leaveOpen: true);

        writer.WriteField("Hello", 10);
        writer.EndRow();
        writer.Flush();

        ms.Position = 0;
        using var reader = new StreamReader(ms);
        var result = reader.ReadToEnd();

        Assert.StartsWith("Hello", result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public void CreateStreamWriter_Stream_WithOptions_AppliesOptions()
    {
        using var ms = new MemoryStream();
        var options = new FixedWidthWriterOptions
        {
            DefaultPadChar = '-',
            DefaultAlignment = FieldAlignment.Center
        };
        using var writer = FixedWidth.CreateStreamWriter(ms, options, leaveOpen: true);

        writer.WriteField("Hi", 6);
        writer.EndRow();
        writer.Flush();

        ms.Position = 0;
        using var reader = new StreamReader(ms);
        var result = reader.ReadToEnd();

        Assert.Contains("--Hi--", result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public void CreateStreamWriter_Stream_LeaveOpenFalse_DisposesStream()
    {
        var ms = new MemoryStream();
        using (var writer = FixedWidth.CreateStreamWriter(ms, leaveOpen: false))
        {
            writer.WriteField("Test", 10);
            writer.EndRow();
        }

        Assert.False(ms.CanRead);
        Assert.False(ms.CanWrite);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public void CreateStreamWriter_Stream_LeaveOpenTrue_PreservesStream()
    {
        using var ms = new MemoryStream();
        using (var writer = FixedWidth.CreateStreamWriter(ms, leaveOpen: true))
        {
            writer.WriteField("Test", 10);
            writer.EndRow();
        }

        Assert.True(ms.CanRead);
        Assert.True(ms.CanWrite);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public void CreateStreamWriter_Stream_CustomEncoding_UsesEncoding()
    {
        using var ms = new MemoryStream();
        using var writer = FixedWidth.CreateStreamWriter(ms, encoding: Encoding.Unicode, leaveOpen: true);

        writer.WriteField("Test", 10);
        writer.EndRow();
        writer.Flush();

        ms.Position = 0;
        using var reader = new StreamReader(ms, Encoding.Unicode);
        var result = reader.ReadToEnd();

        Assert.StartsWith("Test", result);
    }

    #endregion

    #region CreateAsyncStreamWriter Tests

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task CreateAsyncStreamWriter_Stream_ReturnsWriter()
    {
        using var ms = new MemoryStream();
        await using var writer = FixedWidth.CreateAsyncStreamWriter(ms, leaveOpen: true);

        await writer.WriteFieldAsync("Hello", 10, TestContext.Current.CancellationToken);
        await writer.EndRowAsync(TestContext.Current.CancellationToken);
        await writer.FlushAsync(TestContext.Current.CancellationToken);

        ms.Position = 0;
        using var reader = new StreamReader(ms);
        var result = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);

        Assert.StartsWith("Hello", result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task CreateAsyncStreamWriter_WithAlignment_AppliesAlignment()
    {
        using var ms = new MemoryStream();
        await using var writer = FixedWidth.CreateAsyncStreamWriter(ms, leaveOpen: true);

        await writer.WriteFieldAsync("Hi", 5, FieldAlignment.Right, TestContext.Current.CancellationToken);
        await writer.EndRowAsync(TestContext.Current.CancellationToken);
        await writer.FlushAsync(TestContext.Current.CancellationToken);

        ms.Position = 0;
        using var reader = new StreamReader(ms);
        var result = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);

        Assert.StartsWith("   Hi", result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task CreateAsyncStreamWriter_WithOptions_AppliesOptions()
    {
        using var ms = new MemoryStream();
        var options = new FixedWidthWriterOptions
        {
            DefaultPadChar = '*',
            DefaultAlignment = FieldAlignment.Right
        };
        await using var writer = FixedWidth.CreateAsyncStreamWriter(ms, options, leaveOpen: true);

        await writer.WriteFieldAsync("Hi", 5, TestContext.Current.CancellationToken);
        await writer.EndRowAsync(TestContext.Current.CancellationToken);
        await writer.FlushAsync(TestContext.Current.CancellationToken);

        ms.Position = 0;
        using var reader = new StreamReader(ms);
        var result = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);

        Assert.StartsWith("***Hi", result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task CreateAsyncStreamWriter_LeaveOpenFalse_DisposesStream()
    {
        var ms = new MemoryStream();
        await using (var writer = FixedWidth.CreateAsyncStreamWriter(ms, leaveOpen: false))
        {
            await writer.WriteFieldAsync("Test", 10, TestContext.Current.CancellationToken);
            await writer.EndRowAsync(TestContext.Current.CancellationToken);
        }

        Assert.False(ms.CanRead);
        Assert.False(ms.CanWrite);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task CreateAsyncStreamWriter_LeaveOpenTrue_PreservesStream()
    {
        using var ms = new MemoryStream();
        await using (var writer = FixedWidth.CreateAsyncStreamWriter(ms, leaveOpen: true))
        {
            await writer.WriteFieldAsync("Test", 10, TestContext.Current.CancellationToken);
            await writer.EndRowAsync(TestContext.Current.CancellationToken);
        }

        Assert.True(ms.CanRead);
        Assert.True(ms.CanWrite);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task CreateAsyncStreamWriter_MultipleRows_WritesCorrectly()
    {
        using var ms = new MemoryStream();
        await using var writer = FixedWidth.CreateAsyncStreamWriter(ms, leaveOpen: true);

        await writer.WriteFieldAsync("Alice", 10, TestContext.Current.CancellationToken);
        await writer.WriteFieldAsync("30", 5, FieldAlignment.Right, TestContext.Current.CancellationToken);
        await writer.EndRowAsync(TestContext.Current.CancellationToken);

        await writer.WriteFieldAsync("Bob", 10, TestContext.Current.CancellationToken);
        await writer.WriteFieldAsync("25", 5, FieldAlignment.Right, TestContext.Current.CancellationToken);
        await writer.EndRowAsync(TestContext.Current.CancellationToken);

        await writer.FlushAsync(TestContext.Current.CancellationToken);

        ms.Position = 0;
        using var reader = new StreamReader(ms);
        var result = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);

        Assert.Contains("Alice", result);
        Assert.Contains("   30", result);
        Assert.Contains("Bob", result);
        Assert.Contains("   25", result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task CreateAsyncStreamWriter_FormattedValues_WritesCorrectly()
    {
        using var ms = new MemoryStream();
        await using var writer = FixedWidth.CreateAsyncStreamWriter(ms, leaveOpen: true);

        await writer.WriteFieldAsync(12345, 10, cancellationToken: TestContext.Current.CancellationToken);
        await writer.WriteFieldAsync(3.14159m, 10, format: "F2", cancellationToken: TestContext.Current.CancellationToken);
        await writer.EndRowAsync(TestContext.Current.CancellationToken);
        await writer.FlushAsync(TestContext.Current.CancellationToken);

        ms.Position = 0;
        using var reader = new StreamReader(ms);
        var result = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);

        Assert.Contains("12345", result);
        Assert.Contains("3.14", result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task CreateAsyncStreamWriter_NullValue_WritesNullValue()
    {
        using var ms = new MemoryStream();
        var options = new FixedWidthWriterOptions { NullValue = "NULL" };
        await using var writer = FixedWidth.CreateAsyncStreamWriter(ms, options, leaveOpen: true);

        await writer.WriteFieldAsync((object?)null, 10, cancellationToken: TestContext.Current.CancellationToken);
        await writer.EndRowAsync(TestContext.Current.CancellationToken);
        await writer.FlushAsync(TestContext.Current.CancellationToken);

        ms.Position = 0;
        using var reader = new StreamReader(ms);
        var result = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);

        Assert.StartsWith("NULL", result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task CreateAsyncStreamWriter_OverflowBehavior_Throws()
    {
        using var ms = new MemoryStream();
        var options = new FixedWidthWriterOptions { OverflowBehavior = OverflowBehavior.Throw };
        await using var writer = FixedWidth.CreateAsyncStreamWriter(ms, options, leaveOpen: true);

        await Assert.ThrowsAsync<FixedWidthException>(async () =>
        {
            await writer.WriteFieldAsync("This text is way too long", 5, TestContext.Current.CancellationToken);
        });
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task CreateAsyncStreamWriter_OverflowBehavior_Truncates()
    {
        using var ms = new MemoryStream();
        var options = new FixedWidthWriterOptions { OverflowBehavior = OverflowBehavior.Truncate };
        await using var writer = FixedWidth.CreateAsyncStreamWriter(ms, options, leaveOpen: true);

        await writer.WriteFieldAsync("Hello World", 5, TestContext.Current.CancellationToken);
        await writer.EndRowAsync(TestContext.Current.CancellationToken);
        await writer.FlushAsync(TestContext.Current.CancellationToken);

        ms.Position = 0;
        using var reader = new StreamReader(ms);
        var result = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);

        Assert.StartsWith("Hello", result);
        Assert.DoesNotContain("World", result.Split('\n')[0]);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task CreateAsyncStreamWriter_CenterAlignment_PadsBothSides()
    {
        using var ms = new MemoryStream();
        await using var writer = FixedWidth.CreateAsyncStreamWriter(ms, leaveOpen: true);

        await writer.WriteFieldAsync("Hi".AsMemory(), 6, FieldAlignment.Center, ' ', TestContext.Current.CancellationToken);
        await writer.EndRowAsync(TestContext.Current.CancellationToken);
        await writer.FlushAsync(TestContext.Current.CancellationToken);

        ms.Position = 0;
        using var reader = new StreamReader(ms);
        var result = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);

        Assert.StartsWith("  Hi  ", result);
    }

    #endregion

    #region Error Propagation Tests

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task WriteToTextAsync_ErrorInAsyncSource_PropagatesException()
    {
        // Arrange
        var records = CreateAsyncEnumerableWithError();

        // Act & Assert
        var ex = await Assert.ThrowsAnyAsync<Exception>(
            async () => await FixedWidth.WriteToTextAsync(records, cancellationToken: TestContext.Current.CancellationToken)
        );
        Assert.Contains("Simulated error", ex.Message);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task WriteToStreamAsync_ErrorInAsyncSource_PropagatesException()
    {
        // Arrange
        var records = CreateAsyncEnumerableWithError();
        using var ms = new MemoryStream();

        // Act & Assert
        var ex = await Assert.ThrowsAnyAsync<Exception>(
            async () => await FixedWidth.WriteToStreamAsync(ms, records, leaveOpen: true, cancellationToken: TestContext.Current.CancellationToken)
        );
        Assert.Contains("Simulated error", ex.Message);
    }

    #endregion

    #region Cancellation Mid-Stream Tests

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task WriteToTextAsync_CancellationMidStream_ThrowsOperationCanceledException()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, TestContext.Current.CancellationToken);
        var records = CreateAsyncEnumerableWithCancellationTrigger(cts, cancelAfterCount: 5, linkedCts.Token);

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await FixedWidth.WriteToTextAsync(records, cancellationToken: linkedCts.Token)
        );
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task WriteToStreamAsync_CancellationMidStream_ThrowsOperationCanceledException()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, TestContext.Current.CancellationToken);
        var records = CreateAsyncEnumerableWithCancellationTrigger(cts, cancelAfterCount: 5, linkedCts.Token);
        using var ms = new MemoryStream();

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await FixedWidth.WriteToStreamAsync(ms, records, leaveOpen: true, cancellationToken: linkedCts.Token)
        );
    }

    #endregion

    #region Simulated Database Source Tests

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task WriteToTextAsync_SimulatedDatabaseSource_WritesCorrectly()
    {
        // Arrange
        var records = SimulateDatabaseQueryAsync();

        // Act
        var result = await FixedWidth.WriteToTextAsync(records, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains("Product 1", result);
        Assert.Contains("Product 10", result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task WriteToStreamAsync_SimulatedDatabaseSource_WritesCorrectly()
    {
        // Arrange
        var records = SimulateDatabaseQueryAsync();
        using var ms = new MemoryStream();

        // Act
        await FixedWidth.WriteToStreamAsync(ms, records, leaveOpen: true, cancellationToken: TestContext.Current.CancellationToken);

        ms.Position = 0;
        using var reader = new StreamReader(ms);
        var content = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains("Product 1", content);
        Assert.Contains("Product 10", content);
    }

    #endregion

    #region Builder ToTextAsync Tests

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task Builder_ToTextAsync_IAsyncEnumerable_WritesCorrectly()
    {
        // Arrange
        var records = ToAsyncEnumerable([
            new TestRecord { Name = "Alice", Age = 30, City = "NYC" },
            new TestRecord { Name = "Bob", Age = 25, City = "LA" }
        ]);

        // Act
        var result = await FixedWidth.Write<TestRecord>()
            .WithPadChar(' ')
            .ToTextAsync(records, TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains("Alice", result);
        Assert.Contains("00030", result);
        Assert.Contains("Bob", result);
        Assert.Contains("00025", result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task Builder_ToTextAsync_IEnumerable_WritesCorrectly()
    {
        // Arrange
        var records = ToAsyncEnumerable([
            new TestRecord { Name = "Charlie", Age = 35, City = "SF" },
            new TestRecord { Name = "Diana", Age = 28, City = "Boston" }
        ]);

        // Act
        var result = await FixedWidth.Write<TestRecord>()
            .ToTextAsync(records, TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains("Charlie", result);
        Assert.Contains("00035", result);
        Assert.Contains("Diana", result);
        Assert.Contains("00028", result);
    }

    #endregion

    #region WriteToTextAsync Cancellation Tests

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task WriteToTextAsync_WithCancellation_ThrowsWhenCancelled()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var records = CreateAsyncEnumerableWithDelay(
            new TestRecord { Name = "Alice", Age = 30, City = "NYC" },
            new TestRecord { Name = "Bob", Age = 25, City = "LA" }
        );

        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await FixedWidth.WriteToTextAsync(records, cancellationToken: cts.Token)
        );
    }

    #endregion

    #region WriteToFileAsync Encoding Tests

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task WriteToFileAsync_WithEncoding_UsesCorrectEncoding()
    {
        // Arrange
        var tempPath = Path.Combine(Path.GetTempPath(), $"fixedwidth_encoding_test_{Guid.NewGuid()}.dat");
        try
        {
            var records = ToAsyncEnumerable([
                new TestRecord { Name = "Müller", Age = 30, City = "München" },
                new TestRecord { Name = "François", Age = 25, City = "Paris" }
            ]);

            // Act
            await FixedWidth.WriteToFileAsync(tempPath, records, encoding: Encoding.UTF8, cancellationToken: TestContext.Current.CancellationToken);

            // Assert
            var content = await File.ReadAllTextAsync(tempPath, Encoding.UTF8, TestContext.Current.CancellationToken);
            Assert.Contains("Müller", content);
            Assert.Contains("François", content);
            Assert.Contains("München", content);
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
    public async Task WriteToFileAsync_IEnumerable_WithEncoding_UsesCorrectEncoding()
    {
        // Arrange
        var tempPath = Path.Combine(Path.GetTempPath(), $"fixedwidth_encoding_sync_test_{Guid.NewGuid()}.dat");
        try
        {
            var records = new[]
            {
                new TestRecord { Name = "Müller", Age = 30, City = "München" },
                new TestRecord { Name = "François", Age = 25, City = "Paris" }
            };

            // Act
            await FixedWidth.WriteToFileAsync(tempPath, records, encoding: Encoding.UTF8, cancellationToken: TestContext.Current.CancellationToken);

            // Assert
            var content = await File.ReadAllTextAsync(tempPath, Encoding.UTF8, TestContext.Current.CancellationToken);
            Assert.Contains("Müller", content);
            Assert.Contains("François", content);
            Assert.Contains("München", content);
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

    #region Additional Test Models

    public class ProductRecord
    {
        [FixedWidthColumn(Start = 0, Length = 10, Alignment = FieldAlignment.Right, PadChar = '0')]
        public int Id { get; set; }

        [FixedWidthColumn(Start = 10, Length = 30)]
        public string? Name { get; set; }

        [FixedWidthColumn(Start = 40, Length = 15, Alignment = FieldAlignment.Right)]
        public decimal Price { get; set; }
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

    private static async IAsyncEnumerable<T> CreateAsyncEnumerableWithDelay<T>(params T[] items)
    {
        foreach (var item in items)
        {
            await Task.Delay(50);
            yield return item;
        }
    }

    private static async IAsyncEnumerable<TestRecord> CreateAsyncEnumerableWithCancellationTrigger(
        CancellationTokenSource cts,
        int cancelAfterCount,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        for (int i = 0; i < 100; i++)
        {
            if (i == cancelAfterCount)
            {
                cts.Cancel();
            }

            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return new TestRecord { Name = $"Person {i}", Age = 20 + i, City = "City" };
        }
    }

    private static async IAsyncEnumerable<TestRecord> CreateAsyncEnumerableWithError()
    {
        yield return new TestRecord { Name = "Alice", Age = 30, City = "NYC" };
        await Task.Yield();
        throw new InvalidOperationException("Simulated error in async enumerable");
    }

    private static async IAsyncEnumerable<ProductRecord> SimulateDatabaseQueryAsync()
    {
        // Simulate database query with async delay
        for (int i = 1; i <= 10; i++)
        {
            await Task.Delay(1); // Simulate database latency
            yield return new ProductRecord { Id = i, Name = $"Product {i}", Price = i * 10.5m };
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
