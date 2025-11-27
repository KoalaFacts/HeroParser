using HeroParser.SeparatedValues.Writing;
using Xunit;

namespace HeroParser.Tests;

/// <summary>
/// Tests for CsvWriterBuilder{T} and CsvWriterBuilder fluent builder APIs.
/// </summary>
public class CsvWriterBuilderTests
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

    public class DateRecord
    {
        public DateTime Date { get; set; }
    }

    #endregion

    #region CsvWriterBuilder<T> Tests

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Builder_WithDelimiter_UsesCustomDelimiter()
    {
        var records = new[] { new TestPerson { Name = "Alice", Age = 30, City = "NYC" } };

        var csv = new CsvWriterBuilder<TestPerson>()
            .WithDelimiter(';')
            .ToText(records);

        Assert.Contains(";", csv);
        Assert.DoesNotContain(",", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Builder_WithQuote_UsesCustomQuote()
    {
        var records = new[] { new TestPerson { Name = "Alice,Bob", Age = 30, City = "NYC" } };

        var csv = new CsvWriterBuilder<TestPerson>()
            .WithQuote('\'')
            .ToText(records);

        Assert.Contains("'Alice,Bob'", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Builder_WithNewLine_UsesCustomNewLine()
    {
        var records = new[] { new TestPerson { Name = "Alice", Age = 30, City = "NYC" } };

        var csv = new CsvWriterBuilder<TestPerson>()
            .WithNewLine("\n")
            .ToText(records);

        Assert.DoesNotContain("\r\n", csv);
        Assert.EndsWith("\n", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Builder_AlwaysQuote_QuotesAllFields()
    {
        var records = new[] { new TestPerson { Name = "Alice", Age = 30, City = "NYC" } };

        var csv = new CsvWriterBuilder<TestPerson>()
            .AlwaysQuote()
            .ToText(records);

        Assert.Contains("\"Alice\"", csv);
        Assert.Contains("\"30\"", csv);
        Assert.Contains("\"NYC\"", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Builder_NeverQuote_DoesNotQuoteFields()
    {
        var records = new[] { new TestPerson { Name = "Alice,Bob", Age = 30, City = "NYC" } };

        var csv = new CsvWriterBuilder<TestPerson>()
            .NeverQuote()
            .ToText(records);

        Assert.DoesNotContain("\"", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Builder_QuoteWhenNeeded_OnlyQuotesSpecialFields()
    {
        var records = new[] { new TestPerson { Name = "Alice,Bob", Age = 30, City = "NYC" } };

        var csv = new CsvWriterBuilder<TestPerson>()
            .QuoteWhenNeeded()
            .ToText(records);

        Assert.Contains("\"Alice,Bob\"", csv);
        Assert.Contains(",NYC\r\n", csv); // NYC should NOT be quoted (it's at end of row)
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Builder_WithoutHeader_DoesNotWriteHeader()
    {
        var records = new[] { new TestPerson { Name = "Alice", Age = 30, City = "NYC" } };

        var csv = new CsvWriterBuilder<TestPerson>()
            .WithoutHeader()
            .ToText(records);

        Assert.DoesNotContain("Name", csv);
        Assert.Contains("Alice", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Builder_WithHeader_WritesHeader()
    {
        var records = new[] { new TestPerson { Name = "Alice", Age = 30, City = "NYC" } };

        var csv = new CsvWriterBuilder<TestPerson>()
            .WithHeader()
            .ToText(records);

        Assert.Contains("Name", csv);
        Assert.Contains("Age", csv);
        Assert.Contains("City", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public void Builder_WithCulture_FormatsNumbersCorrectly()
    {
        var records = new[] { new ValueRecord { Value = 1234.56 } };

        var csv = new CsvWriterBuilder<ValueRecord>()
            .WithCulture("de-DE")
            .WithDelimiter(';') // Use semicolon to avoid conflict with German decimal comma
            .ToText(records);

        Assert.Contains("1234,56", csv); // German uses comma for decimal
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Builder_WithNullValue_UsesCustomNullRepresentation()
    {
        var records = new[] { new TestPerson { Name = null, Age = 30, City = "NYC" } };

        var csv = new CsvWriterBuilder<TestPerson>()
            .WithNullValue("NULL")
            .ToText(records);

        Assert.Contains("NULL", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Builder_WithDateTimeFormat_FormatsDateCorrectly()
    {
        var records = new[] { new DateRecord { Date = new DateTime(2024, 12, 31, 14, 30, 0) } };

        var csv = new CsvWriterBuilder<DateRecord>()
            .WithDateTimeFormat("yyyy-MM-dd")
            .ToText(records);

        Assert.Contains("2024-12-31", csv);
        Assert.DoesNotContain("14:30", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Builder_ToFile_WritesToFile()
    {
        var records = new[] { new TestPerson { Name = "Alice", Age = 30, City = "NYC" } };
        var tempPath = Path.GetTempFileName();

        try
        {
            new CsvWriterBuilder<TestPerson>()
                .WithDelimiter(';')
                .ToFile(tempPath, records);

            var csv = File.ReadAllText(tempPath);
            Assert.Contains("Alice", csv);
            Assert.Contains(";", csv);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Builder_ToStream_WritesToStream()
    {
        var records = new[] { new TestPerson { Name = "Alice", Age = 30, City = "NYC" } };

        using var ms = new MemoryStream();
        new CsvWriterBuilder<TestPerson>()
            .WithDelimiter('|')
            .ToStream(ms, records);

        ms.Position = 0;
        var csv = new StreamReader(ms).ReadToEnd();
        Assert.Contains("Alice", csv);
        Assert.Contains("|", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Builder_ToWriter_WritesToTextWriter()
    {
        var records = new[] { new TestPerson { Name = "Alice", Age = 30, City = "NYC" } };

        using var sw = new StringWriter();
        new CsvWriterBuilder<TestPerson>()
            .WithDelimiter('\t')
            .ToWriter(sw, records);

        var csv = sw.ToString();
        Assert.Contains("Alice", csv);
        Assert.Contains("\t", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Builder_MethodChaining_AllOptionsWork()
    {
        var records = new[] { new TestPerson { Name = "Alice", Age = 30, City = "NYC" } };

        var csv = new CsvWriterBuilder<TestPerson>()
            .WithDelimiter(';')
            .WithQuote('\'')
            .WithNewLine("\n")
            .AlwaysQuote()
            .WithoutHeader()
            .WithNullValue("N/A")
            .ToText(records);

        Assert.Contains(";", csv);
        Assert.Contains("'Alice'", csv);
        Assert.EndsWith("\n", csv);
        Assert.DoesNotContain("Name", csv);
    }

    #endregion

    #region CsvWriterBuilder (non-generic) Tests

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void NonGenericBuilder_CreateWriter_Works()
    {
        using var sw = new StringWriter();
        using var writer = new CsvWriterBuilder()
            .WithDelimiter(';')
            .CreateWriter(sw, leaveOpen: true);

        writer.WriteRow("A", "B", "C");
        writer.Flush();

        Assert.Contains("A;B;C", sw.ToString());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void NonGenericBuilder_AlwaysQuote_QuotesAllFields()
    {
        using var sw = new StringWriter();
        using var writer = new CsvWriterBuilder()
            .AlwaysQuote()
            .CreateWriter(sw, leaveOpen: true);

        writer.WriteRow("A", "B", "C");
        writer.Flush();

        Assert.Equal("\"A\",\"B\",\"C\"\r\n", sw.ToString());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void NonGenericBuilder_NeverQuote_NeverQuotesFields()
    {
        using var sw = new StringWriter();
        using var writer = new CsvWriterBuilder()
            .NeverQuote()
            .CreateWriter(sw, leaveOpen: true);

        writer.WriteRow("A,B", "C");
        writer.Flush();

        Assert.DoesNotContain("\"", sw.ToString());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void NonGenericBuilder_WithCustomNewLine_UsesIt()
    {
        using var sw = new StringWriter();
        using var writer = new CsvWriterBuilder()
            .WithNewLine("\n")
            .CreateWriter(sw, leaveOpen: true);

        writer.WriteRow("A", "B");
        writer.Flush();

        Assert.EndsWith("\n", sw.ToString());
        Assert.DoesNotContain("\r\n", sw.ToString());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void NonGenericBuilder_CreateStreamWriter_Works()
    {
        using var ms = new MemoryStream();
        using var writer = new CsvWriterBuilder()
            .WithDelimiter('|')
            .CreateStreamWriter(ms, leaveOpen: true);

        writer.WriteRow("X", "Y", "Z");
        writer.Flush();

        ms.Position = 0;
        var csv = new StreamReader(ms).ReadToEnd();
        Assert.Contains("X|Y|Z", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void NonGenericBuilder_CreateFileWriter_WritesToFile()
    {
        var tempPath = Path.GetTempFileName();

        try
        {
            using (var writer = new CsvWriterBuilder()
                .WithDelimiter('\t')
                .CreateFileWriter(tempPath))
            {
                writer.WriteRow("Col1", "Col2");
                writer.WriteRow("Val1", "Val2");
            }

            var csv = File.ReadAllText(tempPath);
            Assert.Contains("Col1\tCol2", csv);
            Assert.Contains("Val1\tVal2", csv);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void NonGenericBuilder_WithCulture_String_Works()
    {
        using var sw = new StringWriter();
        using var writer = new CsvWriterBuilder()
            .WithCulture("de-DE")
            .WithDelimiter(';')
            .CreateWriter(sw, leaveOpen: true);

        writer.WriteRow(1234.56);
        writer.Flush();

        Assert.Contains("1234,56", sw.ToString());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void NonGenericBuilder_MethodChaining_AllOptionsWork()
    {
        using var sw = new StringWriter();
        using var writer = new CsvWriterBuilder()
            .WithDelimiter(';')
            .WithQuote('\'')
            .WithNewLine("\n")
            .AlwaysQuote()
            .WithNullValue("NULL")
            .WithDateTimeFormat("yyyy-MM-dd")
            .CreateWriter(sw, leaveOpen: true);

        writer.WriteRow("A", "B");
        writer.Flush();

        var result = sw.ToString();
        Assert.Contains(";", result);
        Assert.Contains("'A'", result);
        Assert.EndsWith("\n", result);
    }

    #endregion

    #region Csv.Write() Entry Point Tests

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvWrite_GenericEntryPoint_Works()
    {
        var records = new[] { new TestPerson { Name = "Alice", Age = 30, City = "NYC" } };

        var csv = Csv.Write<TestPerson>()
            .WithDelimiter(';')
            .ToText(records);

        Assert.Contains("Alice;30;NYC", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvWrite_NonGenericEntryPoint_Works()
    {
        using var sw = new StringWriter();
        using var writer = Csv.Write()
            .WithDelimiter('|')
            .CreateWriter(sw, leaveOpen: true);

        writer.WriteRow("A", "B", "C");
        writer.Flush();

        Assert.Contains("A|B|C", sw.ToString());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvWrite_GenericEntryPoint_WithChaining()
    {
        var records = new[] { new TestPerson { Name = "Alice,Bob", Age = 30, City = null } };

        var csv = Csv.Write<TestPerson>()
            .WithDelimiter(';')
            .WithQuote('\'')
            .AlwaysQuote()
            .WithNullValue("N/A")
            .WithoutHeader()
            .ToText(records);

        Assert.Contains("'Alice,Bob'", csv);
        Assert.Contains("N/A", csv);
        Assert.DoesNotContain("Name", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvWrite_NonGenericEntryPoint_WithChaining()
    {
        using var sw = new StringWriter();
        using var writer = Csv.Write()
            .WithDelimiter('\t')
            .WithNewLine("\n")
            .AlwaysQuote()
            .CreateWriter(sw, leaveOpen: true);

        writer.WriteRow("X", "Y");
        writer.Flush();

        var result = sw.ToString();
        Assert.Contains("\"X\"\t\"Y\"", result);
        Assert.EndsWith("\n", result);
    }

    #endregion

    #region Async Terminal Method Tests

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task Builder_ToFileAsync_WritesToFile()
    {
        var records = ToAsyncEnumerable([new TestPerson { Name = "Alice", Age = 30, City = "NYC" }]);
        var tempPath = Path.Combine(Path.GetTempPath(), $"heroparser_writer_async_{Guid.NewGuid()}.csv");
        var cancellationToken = TestContext.Current.CancellationToken;

        try
        {
            await new CsvWriterBuilder<TestPerson>()
                .WithDelimiter(';')
                .ToFileAsync(tempPath, records, cancellationToken);

            var csv = await File.ReadAllTextAsync(tempPath, cancellationToken);
            Assert.Contains("Alice", csv);
            Assert.Contains(";", csv);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task Builder_ToStreamAsync_WritesToStream()
    {
        var records = ToAsyncEnumerable([new TestPerson { Name = "Bob", Age = 25, City = "LA" }]);
        var cancellationToken = TestContext.Current.CancellationToken;

        using var ms = new MemoryStream();
        await new CsvWriterBuilder<TestPerson>()
            .WithDelimiter('|')
            .ToStreamAsync(ms, records, leaveOpen: true, cancellationToken);

        ms.Position = 0;
        var csv = new StreamReader(ms).ReadToEnd();
        Assert.Contains("Bob|25|LA", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task CsvWrite_GenericEntryPoint_ToFileAsync()
    {
        TestPerson[] data =
        [
            new TestPerson { Name = "Alice", Age = 30, City = "NYC" },
            new TestPerson { Name = "Bob", Age = 25, City = "LA" }
        ];
        var records = ToAsyncEnumerable(data);
        var tempPath = Path.Combine(Path.GetTempPath(), $"heroparser_entry_async_{Guid.NewGuid()}.csv");
        var cancellationToken = TestContext.Current.CancellationToken;

        try
        {
            await Csv.Write<TestPerson>()
                .WithHeader()
                .ToFileAsync(tempPath, records, cancellationToken);

            var csv = await File.ReadAllTextAsync(tempPath, cancellationToken);
            Assert.Contains("Name,Age,City", csv);
            Assert.Contains("Alice,30,NYC", csv);
            Assert.Contains("Bob,25,LA", csv);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    private static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(IEnumerable<T> source)
    {
        foreach (var item in source)
        {
            await Task.Yield();
            yield return item;
        }
    }

    #endregion
}
