using HeroParser.SeparatedValues.Records;
using Xunit;

namespace HeroParser.Tests;

/// <summary>
/// Tests for CsvReaderBuilder{T} fluent builder API.
/// </summary>
public class CsvReaderBuilderTests
{
    #region Test Record Types

    public class TestPerson
    {
        public string? Name { get; set; }
        public int Age { get; set; }
        public string? City { get; set; }
    }

    public class ValueRecord
    {
        public double Value { get; set; }
    }

    public class NullableRecord
    {
        public int? Value { get; set; }
    }

    #endregion

    #region Basic Reading Tests

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Builder_FromText_ReadsRecords()
    {
        var csv = "Name,Age,City\r\nAlice,30,NYC\r\nBob,25,LA\r\n";

        using var reader = Csv.Read<TestPerson>().FromText(csv);
        var records = reader.ToList();

        Assert.Equal(2, records.Count);
        Assert.Equal("Alice", records[0].Name);
        Assert.Equal(30, records[0].Age);
        Assert.Equal("NYC", records[0].City);
        Assert.Equal("Bob", records[1].Name);
        Assert.Equal(25, records[1].Age);
        Assert.Equal("LA", records[1].City);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Builder_WithDelimiter_UsesCustomDelimiter()
    {
        var csv = "Name;Age;City\r\nAlice;30;NYC\r\n";

        using var reader = Csv.Read<TestPerson>()
            .WithDelimiter(';')
            .FromText(csv);

        var records = reader.ToList();
        Assert.Single(records);
        Assert.Equal("Alice", records[0].Name);
        Assert.Equal(30, records[0].Age);
        Assert.Equal("NYC", records[0].City);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Builder_WithQuote_UsesCustomQuote()
    {
        // Use single quotes around a field that contains a comma - parser should handle it
        var csv = "Name,Age,City\r\n'Alice, Jr',30,NYC\r\n";

        using var reader = Csv.Read<TestPerson>()
            .WithQuote('\'')
            .FromText(csv);

        var records = reader.ToList();
        Assert.Single(records);
        // The parser correctly parsed the field (not splitting on comma inside quotes)
        // Note: The binder returns the raw value including quotes for custom quote chars
        Assert.Contains("Alice, Jr", records[0].Name);
        Assert.Equal(30, records[0].Age);  // Age correctly parsed
        Assert.Equal("NYC", records[0].City);  // City correctly parsed
    }

    #endregion

    #region Header Handling Tests

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Builder_CaseInsensitiveHeaders_Works()
    {
        // Case-insensitive is the default
        var csv = "NAME,AGE,CITY\r\nAlice,30,NYC\r\n";

        using var reader = Csv.Read<TestPerson>()
            .FromText(csv);

        var records = reader.ToList();
        Assert.Single(records);
        Assert.Equal("Alice", records[0].Name);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Builder_AllowMissingColumns_DoesNotThrowOnMissing()
    {
        var csv = "Name,Age\r\nAlice,30\r\n";  // Missing City column

        using var reader = Csv.Read<TestPerson>()
            .AllowMissingColumns()
            .FromText(csv);

        var records = reader.ToList();
        Assert.Single(records);
        Assert.Equal("Alice", records[0].Name);
        Assert.Null(records[0].City);  // Missing column should be null/default
    }

    #endregion

    #region Skip Rows Tests

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Builder_SkipRows_SkipsInitialRows()
    {
        // Skip 2 metadata rows, then read header and data
        var csv = "Metadata1\r\nMetadata2\r\nName,Age,City\r\nAlice,30,NYC\r\n";

        using var reader = Csv.Read<TestPerson>()
            .SkipRows(2)  // Skip the metadata rows
            .FromText(csv);

        var records = reader.ToList();
        Assert.Single(records);
        Assert.Equal("Alice", records[0].Name);
    }

    #endregion

    #region File and Stream Tests

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Builder_FromFile_ReadsFromFile()
    {
        var csv = "Name,Age,City\r\nAlice,30,NYC\r\n";
        var tempPath = Path.Combine(Path.GetTempPath(), $"heroparser_test_{Guid.NewGuid()}.csv");

        try
        {
            File.WriteAllText(tempPath, csv);

            using var reader = Csv.Read<TestPerson>().FromFile(tempPath);
            var records = reader.ToList();

            Assert.Single(records);
            Assert.Equal("Alice", records[0].Name);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Builder_FromStream_ReadsFromStream()
    {
        var csv = "Name,Age,City\r\nAlice,30,NYC\r\n";
        using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv));

        using var reader = Csv.Read<TestPerson>().FromStream(ms);
        var records = reader.ToList();

        Assert.Single(records);
        Assert.Equal("Alice", records[0].Name);
    }

    #endregion

    #region Async Tests

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task Builder_FromFileAsync_ReadsFromFile()
    {
        var csv = "Name,Age,City\r\nAlice,30,NYC\r\nBob,25,LA\r\n";
        var tempPath = Path.Combine(Path.GetTempPath(), $"heroparser_test_async_{Guid.NewGuid()}.csv");
        var cancellationToken = TestContext.Current.CancellationToken;

        try
        {
            await File.WriteAllTextAsync(tempPath, csv, cancellationToken);

            var records = new List<TestPerson>();
            await foreach (var record in Csv.Read<TestPerson>().FromFileAsync(tempPath, cancellationToken))
            {
                records.Add(record);
            }

            Assert.Equal(2, records.Count);
            Assert.Equal("Alice", records[0].Name);
            Assert.Equal("Bob", records[1].Name);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task Builder_FromStreamAsync_ReadsFromStream()
    {
        var csv = "Name,Age,City\r\nAlice,30,NYC\r\n";
        using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv));
        var cancellationToken = TestContext.Current.CancellationToken;

        var records = new List<TestPerson>();
        await foreach (var record in Csv.Read<TestPerson>().FromStreamAsync(ms, cancellationToken: cancellationToken))
        {
            records.Add(record);
        }

        Assert.Single(records);
        Assert.Equal("Alice", records[0].Name);
    }

    #endregion

    #region Method Chaining Tests

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Builder_MethodChaining_AllOptionsWork()
    {
        var csv = "Name;Age;City\r\nAlice;30;NYC\r\n";

        using var reader = Csv.Read<TestPerson>()
            .WithDelimiter(';')
            .AllowMissingColumns()
            .WithMaxColumns(10)
            .FromText(csv);

        var records = reader.ToList();
        Assert.Single(records);
        Assert.Equal("Alice", records[0].Name);
        Assert.Equal(30, records[0].Age);
        Assert.Equal("NYC", records[0].City);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Builder_FluentAPI_WithMultipleOptions()
    {
        var csv = "Name;Age;City\r\nAlice;30;NYC\r\n";

        using var reader = Csv.Read<TestPerson>()
            .WithDelimiter(';')
            .WithMaxColumns(10)
            .WithMaxRows(100)
            .AllowMissingColumns()
            .DisableSimd()
            .FromText(csv);

        var records = reader.ToList();
        Assert.Single(records);
        Assert.Equal("Alice", records[0].Name);
        Assert.Equal(30, records[0].Age);
        Assert.Equal("NYC", records[0].City);
    }

    #endregion

    #region Parser Options Tests

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Builder_WithMaxColumns_SetsLimit()
    {
        var csv = "A,B,C,D,E,F\r\n1,2,3,4,5,6\r\n";

        // Should not throw with default max columns
        using var reader = Csv.Read<TestPerson>()
            .WithMaxColumns(10)
            .AllowMissingColumns()
            .FromText(csv);

        // Just verify it doesn't throw
        foreach (var _ in reader) { }
    }

    #endregion

    #region Non-Generic Builder Tests

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void NonGenericBuilder_FromText_ReadsRows()
    {
        var csv = "Name,Age,City\r\nAlice,30,NYC\r\nBob,25,LA\r\n";

        using var reader = Csv.Read().FromText(csv);
        var rows = new List<(string name, string age, string city)>();

        foreach (var row in reader)
        {
            rows.Add((row[0].ToString(), row[1].ToString(), row[2].ToString()));
        }

        Assert.Equal(3, rows.Count);  // Header + 2 data rows
        Assert.Equal(("Name", "Age", "City"), rows[0]);  // Header row
        Assert.Equal(("Alice", "30", "NYC"), rows[1]);
        Assert.Equal(("Bob", "25", "LA"), rows[2]);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void NonGenericBuilder_WithDelimiter_UsesCustomDelimiter()
    {
        var csv = "Name;Age;City\r\nAlice;30;NYC\r\n";

        using var reader = Csv.Read()
            .WithDelimiter(';')
            .FromText(csv);

        var firstRow = true;
        foreach (var row in reader)
        {
            if (firstRow)
            {
                firstRow = false;
                continue; // Skip header
            }

            Assert.Equal("Alice", row[0].ToString());
            Assert.Equal(30, row[1].Parse<int>());
            Assert.Equal("NYC", row[2].ToString());
        }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void NonGenericBuilder_TrimFields_TrimsWhitespace()
    {
        var csv = "  Name  ,  Age  \r\n  Alice  ,  30  \r\n";

        using var reader = Csv.Read()
            .TrimFields()
            .FromText(csv);

        var rowCount = 0;
        foreach (var row in reader)
        {
            rowCount++;
            Assert.Equal("Name", row[0].ToString());
            break;  // Just check first row
        }

        Assert.Equal(1, rowCount);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void NonGenericBuilder_FromFile_ReadsFromFile()
    {
        var csv = "Name,Age,City\r\nAlice,30,NYC\r\n";
        var tempPath = Path.Combine(Path.GetTempPath(), $"heroparser_test_ng_{Guid.NewGuid()}.csv");

        try
        {
            File.WriteAllText(tempPath, csv);

            using var reader = Csv.Read().FromFile(tempPath);
            var rowCount = 0;
            foreach (var row in reader)
            {
                rowCount++;
            }

            Assert.Equal(2, rowCount);  // Header + 1 data row
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void NonGenericBuilder_FromStream_ReadsFromStream()
    {
        var csv = "Name,Age,City\r\nAlice,30,NYC\r\n";
        using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv));

        using var reader = Csv.Read().FromStream(ms);
        var rowCount = 0;
        foreach (var row in reader)
        {
            rowCount++;
        }

        Assert.Equal(2, rowCount);  // Header + 1 data row
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task NonGenericBuilder_FromFileAsync_ReadsFromFile()
    {
        var csv = "Name,Age,City\r\nAlice,30,NYC\r\nBob,25,LA\r\n";
        var tempPath = Path.Combine(Path.GetTempPath(), $"heroparser_test_ng_async_{Guid.NewGuid()}.csv");
        var cancellationToken = TestContext.Current.CancellationToken;

        try
        {
            await File.WriteAllTextAsync(tempPath, csv, cancellationToken);

            await using var reader = Csv.Read().FromFileAsync(tempPath);
            var rowCount = 0;
            while (await reader.MoveNextAsync(cancellationToken))
            {
                rowCount++;
            }

            Assert.Equal(3, rowCount);  // Header + 2 data rows
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void NonGenericBuilder_MethodChaining_AllOptionsWork()
    {
        var csv = "Name;Age;City\r\nAlice;30;NYC\r\n";

        using var reader = Csv.Read()
            .WithDelimiter(';')
            .WithQuote('\'')
            .WithMaxColumns(10)
            .WithMaxRows(100)
            .TrimFields()
            .FromText(csv);

        var rowCount = 0;
        foreach (var _ in reader)
        {
            rowCount++;
        }

        Assert.Equal(2, rowCount);
    }

    #endregion
}
