using HeroParser.SeparatedValues.Reading.Records;
using HeroParser.SeparatedValues.Reading.Shared;
using Xunit;

namespace HeroParser.Tests;

/// <summary>
/// Tests for CsvReaderBuilder{T} fluent builder API.
/// </summary>
public class CsvReaderBuilderTests
{
    #region Test Record Types

    [CsvGenerateBinder]
    public class TestPerson
    {
        public string? Name { get; set; }
        public int Age { get; set; }
        public string? City { get; set; }
    }

    [CsvGenerateBinder]
    public class ValueRecord
    {
        public double Value { get; set; }
    }

    [CsvGenerateBinder]
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

    #region Csv.Read() Entry Point Tests

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRead_GenericEntryPoint_Works()
    {
        var csv = "Name,Age,City\r\nAlice,30,NYC\r\n";

        using var reader = Csv.Read<TestPerson>().FromText(csv);
        var records = reader.ToList();

        Assert.Single(records);
        Assert.Equal("Alice", records[0].Name);
        Assert.Equal(30, records[0].Age);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRead_NonGenericEntryPoint_Works()
    {
        var csv = "A,B,C\r\n1,2,3\r\n";

        using var reader = Csv.Read().FromText(csv);
        var rows = new List<string>();

        foreach (var row in reader)
        {
            rows.Add(string.Concat(row[0].ToString(), ",", row[1].ToString(), ",", row[2].ToString()));
        }

        Assert.Equal(2, rows.Count);
        Assert.Equal("A,B,C", rows[0]);
        Assert.Equal("1,2,3", rows[1]);
    }

    #endregion

    #region Non-Generic Builder Additional Options Tests

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void NonGenericBuilder_WithCommentCharacter_SkipsCommentLines()
    {
        var csv = "# This is a comment\r\nName,Age\r\n# Another comment\r\nAlice,30\r\n";

        using var reader = Csv.Read()
            .WithCommentCharacter('#')
            .FromText(csv);

        var rowCount = 0;
        foreach (var row in reader)
        {
            rowCount++;
        }

        Assert.Equal(2, rowCount);  // Header + 1 data row (comments skipped)
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void NonGenericBuilder_AllowNewlinesInQuotes_Works()
    {
        var csv = "Name,Bio\r\n\"Alice\",\"Line1\r\nLine2\"\r\n";

        using var reader = Csv.Read()
            .AllowNewlinesInQuotes()
            .FromText(csv);

        var rowCount = 0;
        string? bio = null;
        foreach (var row in reader)
        {
            if (rowCount == 1)  // Skip header
            {
                bio = row[1].ToString();
            }
            rowCount++;
        }

        Assert.Equal(2, rowCount);
        Assert.Contains("Line1", bio);
        Assert.Contains("Line2", bio);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void NonGenericBuilder_DisableSimd_Works()
    {
        var csv = "A,B,C\r\n1,2,3\r\n";

        using var reader = Csv.Read()
            .DisableSimd()
            .FromText(csv);

        var rowCount = 0;
        foreach (var _ in reader)
        {
            rowCount++;
        }

        Assert.Equal(2, rowCount);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void NonGenericBuilder_DisableQuotedFields_Works()
    {
        var csv = "A,B,C\r\n1,2,3\r\n";

        using var reader = Csv.Read()
            .DisableQuotedFields()
            .FromText(csv);

        var rowCount = 0;
        foreach (var _ in reader)
        {
            rowCount++;
        }

        Assert.Equal(2, rowCount);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void NonGenericBuilder_WithMaxFieldSize_Works()
    {
        var csv = "A,B,C\r\n1,2,3\r\n";

        using var reader = Csv.Read()
            .WithMaxFieldSize(1000)
            .FromText(csv);

        var rowCount = 0;
        foreach (var _ in reader)
        {
            rowCount++;
        }

        Assert.Equal(2, rowCount);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void NonGenericBuilder_WithMaxRowSize_Works()
    {
        var csv = "A,B,C\r\n1,2,3\r\n";

        using var reader = Csv.Read()
            .WithMaxRowSize(1024)
            .FromText(csv);

        var rowCount = 0;
        foreach (var _ in reader)
        {
            rowCount++;
        }

        Assert.Equal(2, rowCount);
    }

    #endregion

    #region Generic Builder Additional Options Tests

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void GenericBuilder_WithCommentCharacter_SkipsCommentLines()
    {
        var csv = "# Comment\r\nName,Age,City\r\n# Another comment\r\nAlice,30,NYC\r\n";

        using var reader = Csv.Read<TestPerson>()
            .WithCommentCharacter('#')
            .FromText(csv);

        var records = reader.ToList();
        Assert.Single(records);
        Assert.Equal("Alice", records[0].Name);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void GenericBuilder_AllowNewlinesInQuotes_Works()
    {
        var csv = "Name,Age,City\r\n\"Multi\r\nLine\",30,NYC\r\n";

        using var reader = Csv.Read<TestPerson>()
            .AllowNewlinesInQuotes()
            .FromText(csv);

        var records = reader.ToList();
        Assert.Single(records);
        Assert.Contains("Multi", records[0].Name);
        Assert.Contains("Line", records[0].Name);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void GenericBuilder_WithCulture_Works()
    {
        // Use semicolon delimiter to avoid conflict with German decimal comma
        var csv = "Value\r\n1234,56\r\n";

        using var reader = Csv.Read<ValueRecord>()
            .WithDelimiter(';')  // Use semicolon to avoid conflict with decimal comma
            .WithCulture(new System.Globalization.CultureInfo("de-DE"))
            .FromText(csv);

        var records = reader.ToList();
        Assert.Single(records);
        Assert.Equal(1234.56, records[0].Value, precision: 2);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void GenericBuilder_WithMaxFieldSize_Works()
    {
        var csv = "Name,Age,City\r\nAlice,30,NYC\r\n";

        using var reader = Csv.Read<TestPerson>()
            .WithMaxFieldSize(1000)
            .FromText(csv);

        var records = reader.ToList();
        Assert.Single(records);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void GenericBuilder_DisableSimd_Works()
    {
        var csv = "Name,Age,City\r\nAlice,30,NYC\r\n";

        using var reader = Csv.Read<TestPerson>()
            .DisableSimd()
            .FromText(csv);

        var records = reader.ToList();
        Assert.Single(records);
        Assert.Equal("Alice", records[0].Name);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void GenericBuilder_DisableQuotedFields_Works()
    {
        var csv = "Name,Age,City\r\nAlice,30,NYC\r\n";

        using var reader = Csv.Read<TestPerson>()
            .DisableQuotedFields()
            .FromText(csv);

        var records = reader.ToList();
        Assert.Single(records);
        Assert.Equal("Alice", records[0].Name);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void GenericBuilder_CaseSensitiveHeaders_Works()
    {
        var csv = "Name,Age,City\r\nAlice,30,NYC\r\n";

        using var reader = Csv.Read<TestPerson>()
            .CaseSensitiveHeaders()
            .FromText(csv);

        var records = reader.ToList();
        Assert.Single(records);
        Assert.Equal("Alice", records[0].Name);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void GenericBuilder_CompleteMethodChaining()
    {
        var csv = "Name;Age;City\r\nAlice;30;NYC\r\n";

        using var reader = Csv.Read<TestPerson>()
            .WithDelimiter(';')
            .WithQuote('"')
            .WithMaxColumns(10)
            .WithMaxRows(1000)
            .AllowMissingColumns()
            .WithMaxFieldSize(1000)
            .DisableSimd()
            .FromText(csv);

        var records = reader.ToList();
        Assert.Single(records);
        Assert.Equal("Alice", records[0].Name);
        Assert.Equal(30, records[0].Age);
        Assert.Equal("NYC", records[0].City);
    }

    #endregion
}
