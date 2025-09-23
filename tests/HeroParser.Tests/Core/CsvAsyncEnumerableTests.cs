#if NET6_0_OR_GREATER
using HeroParser.Configuration;
using System.Text;

namespace HeroParser.Tests.Core;

/// <summary>
/// Tests for IAsyncEnumerable methods in Csv API (available in .NET 6+).
/// </summary>
public class CsvAsyncEnumerableTests
{
    private const string SimpleCsv = "Name,Age,City\nJohn,25,Boston\nJane,30,Seattle\nBob,35,Portland";

    [Fact]
    public async Task FromStringAsync_WithIAsyncEnumerable_StreamsCorrectly()
    {
        // Arrange
        var recordCount = 0;
        string[]? firstRecord = null;

        // Act
        await foreach (var record in Csv.StreamContent(SimpleCsv, cancellationToken: TestContext.Current.CancellationToken))
        {
            recordCount++;
            if (firstRecord == null)
                firstRecord = record;
        }

        // Assert
        Assert.Equal(3, recordCount);
        Assert.NotNull(firstRecord);
        Assert.Equal("John", firstRecord[0]);
        Assert.Equal("25", firstRecord[1]);
        Assert.Equal("Boston", firstRecord[2]);
    }

    [Fact]
    public async Task FromFileAsync_WithFileReader_StreamsCorrectly()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, SimpleCsv, TestContext.Current.CancellationToken);
            var records = new List<string[]>();

            // Act
            using var reader = Csv.OpenFile(tempFile);
            var allRecords = await reader.ReadAllAsync(TestContext.Current.CancellationToken);
            foreach (var record in allRecords)
            {
                records.Add(record);
            }

            // Assert
            Assert.Equal(3, records.Count);
            Assert.Equal("Jane", records[1][0]);
            Assert.Equal("30", records[1][1]);
            Assert.Equal("Seattle", records[1][2]);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task FromReaderAsync_WithTextReader_StreamsCorrectly()
    {
        // Arrange
        var bytes = Encoding.UTF8.GetBytes(SimpleCsv);
        var records = new List<string[]>();

        // Act
        await foreach (var record in Csv.StreamContent(bytes, cancellationToken: TestContext.Current.CancellationToken))
        {
            records.Add(record);
        }

        // Assert
        Assert.Equal(3, records.Count);
        Assert.Equal("Bob", records[2][0]);
        Assert.Equal("35", records[2][1]);
        Assert.Equal("Portland", records[2][2]);
    }

    [Fact]
    public async Task FromStreamAsync_WithMemoryStream_StreamsCorrectly()
    {
        // Arrange
        var bytes = Encoding.UTF8.GetBytes(SimpleCsv);
        using var stream = new MemoryStream(bytes);
        var recordCount = 0;

        // Act
        using var reader = Csv.OpenStream(stream);
        var records = await reader.ReadAllAsync(TestContext.Current.CancellationToken);
        foreach (var record in records)
        {
            recordCount++;
            Assert.Equal(3, record.Length); // Each record should have 3 fields
        }

        // Assert
        Assert.Equal(3, recordCount);
    }

    [Fact]
    public async Task FromBytesAsync_WithIAsyncEnumerable_StreamsCorrectly()
    {
        // Arrange
        var bytes = Encoding.UTF8.GetBytes(SimpleCsv);
        var records = new List<string[]>();

        // Act
        await foreach (var record in Csv.StreamContent(bytes, cancellationToken: TestContext.Current.CancellationToken))
        {
            records.Add(record);
        }

        // Assert
        Assert.Equal(3, records.Count);
        Assert.Equal("John", records[0][0]);
        Assert.Equal("Jane", records[1][0]);
        Assert.Equal("Bob", records[2][0]);
    }

    [Fact]
    public async Task FromBytesAsync_WithMemory_StreamsCorrectly()
    {
        // Arrange
        var bytes = Encoding.UTF8.GetBytes(SimpleCsv);
        var memory = new ReadOnlyMemory<byte>(bytes);
        var records = new List<string[]>();

        // Act
        await foreach (var record in Csv.StreamContent(memory, cancellationToken: TestContext.Current.CancellationToken))
        {
            records.Add(record);
        }

        // Assert
        Assert.Equal(3, records.Count);
        Assert.Equal("Boston", records[0][2]);
        Assert.Equal("Seattle", records[1][2]);
        Assert.Equal("Portland", records[2][2]);
    }

    [Fact]
    public async Task FromStringAsync_CanBreakEarly()
    {
        // Arrange
        var recordCount = 0;

        // Act
        await foreach (var record in Csv.StreamContent(SimpleCsv, cancellationToken: TestContext.Current.CancellationToken))
        {
            recordCount++;
            if (recordCount == 2)
                break; // Break after 2 records
        }

        // Assert
        Assert.Equal(2, recordCount); // Should only process 2 records
    }

    [Fact]
    public async Task FromStringAsync_WithCancellation_StopProcessing()
    {
        // Arrange
        var largeCsv = string.Join("\n",
            Enumerable.Range(0, 1000).Select(i => $"Name{i},{i},City{i}"));
        largeCsv = "Name,Age,City\n" + largeCsv;

        using var cts = new CancellationTokenSource();
        var processedCount = 0;

        // Act
        try
        {
            await foreach (var record in Csv.StreamContent(largeCsv, configuration: null, cancellationToken: cts.Token))
            {
                processedCount++;
                if (processedCount == 10)
                {
                    cts.Cancel(); // Cancel after 10 records
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        // Assert
        Assert.Equal(10, processedCount);
    }

    [Fact]
    public async Task FromStringAsync_WithConfiguration_AppliesSettings()
    {
        // Arrange
        var csv = "Name;Age;City\nJohn;25;Boston\nJane;30;Seattle";
        var config = new CsvReadConfiguration { Delimiter = ';' };
        var records = new List<string[]>();

        // Act
        await foreach (var record in Csv.StreamContent(csv, config, TestContext.Current.CancellationToken))
        {
            records.Add(record);
        }

        // Assert
        Assert.Equal(2, records.Count);
        Assert.Equal("John", records[0][0]);
        Assert.Equal("25", records[0][1]);
        Assert.Equal("Boston", records[0][2]);
    }

    [Fact]
    public async Task FromStringAsync_EmptyInput_ReturnsNoRecords()
    {
        // Arrange
        var recordCount = 0;

        // Act
        await foreach (var record in Csv.StreamContent("", cancellationToken: TestContext.Current.CancellationToken))
        {
            recordCount++;
        }

        // Assert
        Assert.Equal(0, recordCount);
    }

    [Fact]
    public async Task FromStringAsync_HeaderOnly_ReturnsNoRecords()
    {
        // Arrange
        var csv = "Name,Age,City";
        var recordCount = 0;

        // Act
        await foreach (var record in Csv.StreamContent(csv, cancellationToken: TestContext.Current.CancellationToken))
        {
            recordCount++;
        }

        // Assert
        Assert.Equal(0, recordCount);
    }

    [Fact]
    public async Task FromBytesAsync_DifferentEncoding_ParsesCorrectly()
    {
        // Arrange
        var bytes = Encoding.UTF32.GetBytes(SimpleCsv);
        var records = new List<string[]>();

        // Act
        await foreach (var record in Csv.StreamContent(bytes, encoding: Encoding.UTF32, cancellationToken: TestContext.Current.CancellationToken))
        {
            records.Add(record);
        }

        // Assert
        Assert.Equal(3, records.Count);
        Assert.Equal("Jane", records[1][0]);
        Assert.Equal("30", records[1][1]);
    }

    [Fact]
    public async Task FromStringAsync_NullInput_ThrowsImmediately()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await foreach (var record in Csv.StreamContent((string)null!, cancellationToken: TestContext.Current.CancellationToken))
            {
                // Should throw before entering the loop
            }
        });
    }

    [Fact]
    public void FromFileAsync_NullInput_ThrowsImmediately()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
        {
            var reader = Csv.OpenFile(null!);
        });
    }
}
#endif