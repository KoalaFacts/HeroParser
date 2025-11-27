#pragma warning disable xUnit1051 // Calls to methods which accept CancellationToken should use TestContext.Current.CancellationToken

using HeroParser.SeparatedValues;
using HeroParser.SeparatedValues.Records.Binding;
using HeroParser.SeparatedValues.Writing;
using System.Runtime.CompilerServices;
using System.Text;
using Xunit;

namespace HeroParser.Tests;

public class AsyncEnumerableWriterTests
{
    #region Test Models

    private record Person(string Name, int Age, string City);

    private record Product(int Id, string Name, decimal Price);

    #endregion

    #region WriteToTextAsync Tests

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task WriteToTextAsync_SimpleRecords_WritesCorrectly()
    {
        // Arrange
        var records = CreateAsyncEnumerable(
            new Person("Alice", 30, "New York"),
            new Person("Bob", 25, "London"),
            new Person("Charlie", 35, "Tokyo")
        );

        // Act
        var csv = await Csv.WriteToTextAsync(records);

        // Assert
        var expected = "Name,Age,City\r\n" +
                      "Alice,30,New York\r\n" +
                      "Bob,25,London\r\n" +
                      "Charlie,35,Tokyo\r\n";
        Assert.Equal(expected, csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task WriteToTextAsync_EmptyAsyncEnumerable_WritesHeaderOnly()
    {
        // Arrange
        var records = CreateAsyncEnumerable<Person>();

        // Act
        var csv = await Csv.WriteToTextAsync(records);

        // Assert
        Assert.Equal("Name,Age,City\r\n", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task WriteToTextAsync_WithoutHeader_WritesDataOnly()
    {
        // Arrange
        var records = CreateAsyncEnumerable(
            new Person("Alice", 30, "New York"),
            new Person("Bob", 25, "London")
        );

        var options = new CsvWriterOptions { WriteHeader = false };

        // Act
        var csv = await Csv.WriteToTextAsync(records, options);

        // Assert
        var expected = "Alice,30,New York\r\n" +
                      "Bob,25,London\r\n";
        Assert.Equal(expected, csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task WriteToTextAsync_WithCancellation_ThrowsWhenCancelled()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var records = CreateAsyncEnumerableWithDelay(
            new Person("Alice", 30, "New York"),
            new Person("Bob", 25, "London")
        );

        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await Csv.WriteToTextAsync(records, cancellationToken: cts.Token)
        );
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task WriteToTextAsync_WithCustomDelimiter_WritesCorrectly()
    {
        // Arrange
        var records = CreateAsyncEnumerable(
            new Person("Alice", 30, "New York"),
            new Person("Bob", 25, "London")
        );

        var options = new CsvWriterOptions { Delimiter = ';' };

        // Act
        var csv = await Csv.WriteToTextAsync(records, options);

        // Assert
        var expected = "Name;Age;City\r\n" +
                      "Alice;30;New York\r\n" +
                      "Bob;25;London\r\n";
        Assert.Equal(expected, csv);
    }

    #endregion

    #region WriteToFileAsync Tests

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task WriteToFileAsync_SimpleRecords_WritesCorrectly()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        try
        {
            var records = CreateAsyncEnumerable(
                new Person("Alice", 30, "New York"),
                new Person("Bob", 25, "London")
            );

            // Act
            await Csv.WriteToFileAsync(tempFile, records);

            // Assert
            var content = await File.ReadAllTextAsync(tempFile);
            var expected = "Name,Age,City\r\n" +
                          "Alice,30,New York\r\n" +
                          "Bob,25,London\r\n";
            Assert.Equal(expected, content);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task WriteToFileAsync_WithCancellation_ThrowsWhenCancelled()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        try
        {
            var cts = new CancellationTokenSource();
            var records = CreateAsyncEnumerableWithDelay(
                new Person("Alice", 30, "New York"),
                new Person("Bob", 25, "London")
            );

            cts.Cancel();

            // Act & Assert
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () => await Csv.WriteToFileAsync(tempFile, records, cancellationToken: cts.Token)
            );
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task WriteToFileAsync_WithEncoding_UsesCorrectEncoding()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        try
        {
            var records = CreateAsyncEnumerable(
                new Person("Müller", 30, "München"),
                new Person("François", 25, "Paris")
            );

            // Act
            await Csv.WriteToFileAsync(tempFile, records, encoding: Encoding.UTF8);

            // Assert
            var content = await File.ReadAllTextAsync(tempFile, Encoding.UTF8);
            Assert.Contains("Müller", content);
            Assert.Contains("François", content);
            Assert.Contains("München", content);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    #endregion

    #region WriteToStreamAsync Tests

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task WriteToStreamAsync_SimpleRecords_WritesCorrectly()
    {
        // Arrange
        var stream = new MemoryStream();
        var records = CreateAsyncEnumerable(
            new Person("Alice", 30, "New York"),
            new Person("Bob", 25, "London")
        );

        // Act
        await Csv.WriteToStreamAsync(stream, records, leaveOpen: true);
        stream.Position = 0;

        // Assert
        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync();
        var expected = "Name,Age,City\r\n" +
                      "Alice,30,New York\r\n" +
                      "Bob,25,London\r\n";
        Assert.Equal(expected, content);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task WriteToStreamAsync_LeaveOpenFalse_DisposesStream()
    {
        // Arrange
        var stream = new MemoryStream();
        var records = CreateAsyncEnumerable(
            new Person("Alice", 30, "New York")
        );

        // Act
        await Csv.WriteToStreamAsync(stream, records, leaveOpen: false);

        // Assert
        Assert.Throws<ObjectDisposedException>(() => stream.Position = 0);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task WriteToStreamAsync_LeaveOpenTrue_KeepsStreamOpen()
    {
        // Arrange
        var stream = new MemoryStream();
        var records = CreateAsyncEnumerable(
            new Person("Alice", 30, "New York")
        );

        // Act
        await Csv.WriteToStreamAsync(stream, records, leaveOpen: true);

        // Assert
        stream.Position = 0; // Should not throw
        Assert.True(stream.CanRead);
    }

    #endregion

    #region CsvWriterBuilder Async Tests

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task CsvWriterBuilder_ToTextAsync_WritesCorrectly()
    {
        // Arrange
        var records = CreateAsyncEnumerable(
            new Person("Alice", 30, "New York"),
            new Person("Bob", 25, "London")
        );

        // Act
        var csv = await Csv.Write<Person>()
            .WithDelimiter(';')
            .WithHeader()
            .ToTextAsync(records);

        // Assert
        var expected = "Name;Age;City\r\n" +
                      "Alice;30;New York\r\n" +
                      "Bob;25;London\r\n";
        Assert.Equal(expected, csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task CsvWriterBuilder_ToFileAsync_WritesCorrectly()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        try
        {
            var records = CreateAsyncEnumerable(
                new Person("Alice", 30, "New York"),
                new Person("Bob", 25, "London")
            );

            // Act
            await Csv.Write<Person>()
                .WithDelimiter(';')
                .ToFileAsync(tempFile, records);

            // Assert
            var content = await File.ReadAllTextAsync(tempFile);
            Assert.Contains("Name;Age;City", content);
            Assert.Contains("Alice;30;New York", content);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task CsvWriterBuilder_ToStreamAsync_WritesCorrectly()
    {
        // Arrange
        var stream = new MemoryStream();
        var records = CreateAsyncEnumerable(
            new Person("Alice", 30, "New York"),
            new Person("Bob", 25, "London")
        );

        // Act
        await Csv.Write<Person>()
            .WithDelimiter('|')
            .ToStreamAsync(stream, records, leaveOpen: true);
        stream.Position = 0;

        // Assert
        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync();
        Assert.Contains("Name|Age|City", content);
        Assert.Contains("Alice|30|New York", content);
    }

    #endregion

    #region Simulated Database-Like Async Source Tests

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task WriteToTextAsync_SimulatedDatabaseSource_WritesCorrectly()
    {
        // Arrange
        var records = SimulateDatabaseQueryAsync();

        // Act
        var csv = await Csv.WriteToTextAsync(records);

        // Assert
        Assert.Contains("Id,Name,Price", csv);
        Assert.Contains("Product 1", csv);
        Assert.Contains("Product 10", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task WriteToTextAsync_LargeAsyncSource_HandlesMemoryEfficiently()
    {
        // Arrange - simulate a large dataset that would be problematic if buffered
        var records = SimulateLargeDatabaseQueryAsync(10000);

        // Act
        var csv = await Csv.WriteToTextAsync(records);

        // Assert
        var lineCount = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
        Assert.Equal(10001, lineCount); // 10000 records + 1 header
    }

    #endregion

    #region Cancellation Mid-Stream Tests

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task WriteToTextAsync_CancellationMidStream_ThrowsOperationCanceledException()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var records = CreateAsyncEnumerableWithCancellationTrigger(cts, cancelAfterCount: 5);

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await Csv.WriteToTextAsync(records, cancellationToken: cts.Token)
        );
    }

    #endregion

    #region Error Propagation Tests

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task WriteToTextAsync_ErrorInAsyncSource_PropagatesException()
    {
        // Arrange
        var records = CreateAsyncEnumerableWithError();

        // Act & Assert
        var ex = await Assert.ThrowsAnyAsync<Exception>(
            async () => await Csv.WriteToTextAsync(records)
        );
        Assert.Contains("Simulated error", ex.Message);
    }

    #endregion

    #region Special Characters and Quoting Tests

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task WriteToTextAsync_RecordsWithQuotes_EscapesCorrectly()
    {
        // Arrange
        var records = CreateAsyncEnumerable(
            new Person("Alice \"The Great\"", 30, "New York"),
            new Person("Bob", 25, "London")
        );

        // Act
        var csv = await Csv.WriteToTextAsync(records);

        // Assert
        Assert.Contains("\"Alice \"\"The Great\"\"\"", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task WriteToTextAsync_RecordsWithCommas_QuotesFields()
    {
        // Arrange
        var records = CreateAsyncEnumerable(
            new Person("Smith, John", 30, "New York"),
            new Person("Bob", 25, "London, UK")
        );

        // Act
        var csv = await Csv.WriteToTextAsync(records);

        // Assert
        Assert.Contains("\"Smith, John\"", csv);
        Assert.Contains("\"London, UK\"", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task WriteToTextAsync_RecordsWithNewlines_QuotesFields()
    {
        // Arrange
        var records = CreateAsyncEnumerable(
            new Person("Alice\nSmith", 30, "New York"),
            new Person("Bob", 25, "London")
        );

        // Act
        var csv = await Csv.WriteToTextAsync(records);

        // Assert
        Assert.Contains("\"Alice\nSmith\"", csv);
    }

    #endregion

    #region Helper Methods

    private static async IAsyncEnumerable<T> CreateAsyncEnumerable<T>(params T[] items)
    {
        foreach (var item in items)
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

    private static async IAsyncEnumerable<Person> CreateAsyncEnumerableWithCancellationTrigger(
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
            yield return new Person($"Person {i}", 20 + i, "City");
        }
    }

    private static async IAsyncEnumerable<Person> CreateAsyncEnumerableWithError()
    {
        yield return new Person("Alice", 30, "New York");
        await Task.Yield();
        throw new InvalidOperationException("Simulated error in async enumerable");
    }

    private static async IAsyncEnumerable<Product> SimulateDatabaseQueryAsync()
    {
        // Simulate database query with async delay
        for (int i = 1; i <= 10; i++)
        {
            await Task.Delay(1); // Simulate database latency
            yield return new Product(i, $"Product {i}", i * 10.5m);
        }
    }

    private static async IAsyncEnumerable<Product> SimulateLargeDatabaseQueryAsync(int count)
    {
        for (int i = 1; i <= count; i++)
        {
            if (i % 1000 == 0)
            {
                await Task.Yield(); // Periodically yield to avoid blocking
            }
            yield return new Product(i, $"Product {i}", i * 10.5m);
        }
    }

    #endregion
}
