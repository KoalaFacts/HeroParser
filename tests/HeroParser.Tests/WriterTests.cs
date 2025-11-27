using HeroParser.SeparatedValues;
using HeroParser.SeparatedValues.Records;
using HeroParser.SeparatedValues.Records.Binding;
using HeroParser.SeparatedValues.Writing;
using System.Globalization;
using System.Text;
using Xunit;

namespace HeroParser.Tests;

public class WriterTests
{
    #region Basic Writing

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void WriteRow_SimpleStrings_WritesCorrectly()
    {
        using var sw = new StringWriter();
        using var writer = new CsvStreamWriter(sw, CsvWriterOptions.Default, leaveOpen: true);

        writer.WriteRow("a", "b", "c");
        writer.Flush();

        Assert.Equal("a,b,c\r\n", sw.ToString());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void WriteRow_MultipleRows_WritesCorrectly()
    {
        using var sw = new StringWriter();
        using var writer = new CsvStreamWriter(sw, CsvWriterOptions.Default, leaveOpen: true);

        writer.WriteRow("a", "b", "c");
        writer.WriteRow("1", "2", "3");
        writer.Flush();

        Assert.Equal("a,b,c\r\n1,2,3\r\n", sw.ToString());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void WriteField_IndividualFields_WritesCorrectly()
    {
        using var sw = new StringWriter();
        using var writer = new CsvStreamWriter(sw, CsvWriterOptions.Default, leaveOpen: true);

        writer.WriteField("a");
        writer.WriteField("b");
        writer.WriteField("c");
        writer.EndRow();
        writer.Flush();

        Assert.Equal("a,b,c\r\n", sw.ToString());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void WriteRow_EmptyFields_WritesCorrectly()
    {
        using var sw = new StringWriter();
        using var writer = new CsvStreamWriter(sw, CsvWriterOptions.Default, leaveOpen: true);

        writer.WriteRow("a", "", "c");
        writer.Flush();

        Assert.Equal("a,,c\r\n", sw.ToString());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void WriteRow_NullFields_WritesEmptyString()
    {
        using var sw = new StringWriter();
        using var writer = new CsvStreamWriter(sw, CsvWriterOptions.Default, leaveOpen: true);

        writer.WriteRow("a", null, "c");
        writer.Flush();

        Assert.Equal("a,,c\r\n", sw.ToString());
    }

    #endregion

    #region Quoting Behavior

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void WriteRow_FieldContainsComma_QuotesField()
    {
        using var sw = new StringWriter();
        using var writer = new CsvStreamWriter(sw, CsvWriterOptions.Default, leaveOpen: true);

        writer.WriteRow("a,b", "c");
        writer.Flush();

        Assert.Equal("\"a,b\",c\r\n", sw.ToString());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void WriteRow_FieldContainsQuote_EscapesQuote()
    {
        using var sw = new StringWriter();
        using var writer = new CsvStreamWriter(sw, CsvWriterOptions.Default, leaveOpen: true);

        writer.WriteRow("a\"b", "c");
        writer.Flush();

        Assert.Equal("\"a\"\"b\",c\r\n", sw.ToString());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void WriteRow_FieldContainsNewline_QuotesField()
    {
        using var sw = new StringWriter();
        using var writer = new CsvStreamWriter(sw, CsvWriterOptions.Default, leaveOpen: true);

        writer.WriteRow("a\nb", "c");
        writer.Flush();

        Assert.Equal("\"a\nb\",c\r\n", sw.ToString());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void WriteRow_FieldContainsCRLF_QuotesField()
    {
        using var sw = new StringWriter();
        using var writer = new CsvStreamWriter(sw, CsvWriterOptions.Default, leaveOpen: true);

        writer.WriteRow("a\r\nb", "c");
        writer.Flush();

        Assert.Equal("\"a\r\nb\",c\r\n", sw.ToString());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void QuoteStyle_Always_QuotesAllFields()
    {
        var options = new CsvWriterOptions { QuoteStyle = QuoteStyle.Always };
        using var sw = new StringWriter();
        using var writer = new CsvStreamWriter(sw, options, leaveOpen: true);

        writer.WriteRow("a", "b", "c");
        writer.Flush();

        Assert.Equal("\"a\",\"b\",\"c\"\r\n", sw.ToString());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void QuoteStyle_Never_DoesNotQuote()
    {
        var options = new CsvWriterOptions { QuoteStyle = QuoteStyle.Never };
        using var sw = new StringWriter();
        using var writer = new CsvStreamWriter(sw, options, leaveOpen: true);

        writer.WriteRow("a,b", "c");
        writer.Flush();

        Assert.Equal("a,b,c\r\n", sw.ToString());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void QuoteStyle_WhenNeeded_OnlyQuotesSpecialFields()
    {
        var options = new CsvWriterOptions { QuoteStyle = QuoteStyle.WhenNeeded };
        using var sw = new StringWriter();
        using var writer = new CsvStreamWriter(sw, options, leaveOpen: true);

        writer.WriteRow("normal", "with,comma", "also normal");
        writer.Flush();

        Assert.Equal("normal,\"with,comma\",also normal\r\n", sw.ToString());
    }

    #endregion

    #region Custom Delimiters

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CustomDelimiter_Tab_WritesCorrectly()
    {
        var options = new CsvWriterOptions { Delimiter = '\t' };
        using var sw = new StringWriter();
        using var writer = new CsvStreamWriter(sw, options, leaveOpen: true);

        writer.WriteRow("a", "b", "c");
        writer.Flush();

        Assert.Equal("a\tb\tc\r\n", sw.ToString());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CustomDelimiter_Semicolon_WritesCorrectly()
    {
        var options = new CsvWriterOptions { Delimiter = ';' };
        using var sw = new StringWriter();
        using var writer = new CsvStreamWriter(sw, options, leaveOpen: true);

        writer.WriteRow("a", "b", "c");
        writer.Flush();

        Assert.Equal("a;b;c\r\n", sw.ToString());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CustomDelimiter_Pipe_WritesCorrectly()
    {
        var options = new CsvWriterOptions { Delimiter = '|' };
        using var sw = new StringWriter();
        using var writer = new CsvStreamWriter(sw, options, leaveOpen: true);

        writer.WriteRow("a", "b", "c");
        writer.Flush();

        Assert.Equal("a|b|c\r\n", sw.ToString());
    }

    #endregion

    #region Custom Newlines

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CustomNewLine_LF_WritesCorrectly()
    {
        var options = new CsvWriterOptions { NewLine = "\n" };
        using var sw = new StringWriter();
        using var writer = new CsvStreamWriter(sw, options, leaveOpen: true);

        writer.WriteRow("a", "b", "c");
        writer.Flush();

        Assert.Equal("a,b,c\n", sw.ToString());
    }

    #endregion

    #region Type Formatting

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void WriteRow_IntegerValues_FormatsCorrectly()
    {
        using var sw = new StringWriter();
        using var writer = new CsvStreamWriter(sw, CsvWriterOptions.Default, leaveOpen: true);

        writer.WriteRow(1, 2, 3);
        writer.Flush();

        Assert.Equal("1,2,3\r\n", sw.ToString());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void WriteRow_DoubleValues_FormatsCorrectly()
    {
        using var sw = new StringWriter();
        using var writer = new CsvStreamWriter(sw, CsvWriterOptions.Default, leaveOpen: true);

        writer.WriteRow(3.14, 2.71);
        writer.Flush();

        Assert.Equal("3.14,2.71\r\n", sw.ToString());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void WriteRow_DateTimeWithFormat_FormatsCorrectly()
    {
        var options = new CsvWriterOptions { DateTimeFormat = "yyyy-MM-dd" };
        using var sw = new StringWriter();
        using var writer = new CsvStreamWriter(sw, options, leaveOpen: true);

        writer.WriteRow(new DateTime(2024, 12, 31));
        writer.Flush();

        Assert.Equal("2024-12-31\r\n", sw.ToString());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void WriteRow_NullValue_WritesConfiguredNullString()
    {
        var options = new CsvWriterOptions { NullValue = "NULL" };
        using var sw = new StringWriter();
        using var writer = new CsvStreamWriter(sw, options, leaveOpen: true);

        // Use object[] to trigger formatted value path that applies NullValue
        writer.WriteRow(new object?[] { "a", null, "c" });
        writer.Flush();

        Assert.Equal("a,NULL,c\r\n", sw.ToString());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public void WriteRow_CultureAware_FormatsNumbers()
    {
        var options = new CsvWriterOptions
        {
            Delimiter = ';',
            Culture = CultureInfo.GetCultureInfo("de-DE")
        };
        using var sw = new StringWriter();
        using var writer = new CsvStreamWriter(sw, options, leaveOpen: true);

        writer.WriteRow(1234.56);
        writer.Flush();

        Assert.Equal("1234,56\r\n", sw.ToString());
    }

    #endregion

    #region Record Writing

    public class TestPerson
    {
        public string? Name { get; set; }
        public int Age { get; set; }
        public string? City { get; set; }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public void SerializeRecords_SimpleType_WritesCorrectly()
    {
        var records = new[]
        {
            new TestPerson { Name = "Alice", Age = 30, City = "New York" },
            new TestPerson { Name = "Bob", Age = 25, City = "London" }
        };

        var csv = Csv.Write<TestPerson>().ToText(records);

        Assert.Contains("Name", csv);
        Assert.Contains("Age", csv);
        Assert.Contains("City", csv);
        Assert.Contains("Alice", csv);
        Assert.Contains("30", csv);
        Assert.Contains("Bob", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public void SerializeRecords_WithoutHeader_NoHeaderRow()
    {
        var records = new[]
        {
            new TestPerson { Name = "Alice", Age = 30, City = "New York" }
        };

        var csv = Csv.Write<TestPerson>().WithoutHeader().ToText(records);

        Assert.DoesNotContain("Name", csv);
        Assert.Contains("Alice", csv);
    }

    public class PersonWithColumn
    {
        [CsvColumn(Name = "Full Name")]
        public string? Name { get; set; }

        [CsvColumn(Name = "Years Old")]
        public int Age { get; set; }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public void SerializeRecords_WithCsvColumnAttribute_UsesCustomNames()
    {
        var records = new[]
        {
            new PersonWithColumn { Name = "Alice", Age = 30 }
        };

        var csv = Csv.Write<PersonWithColumn>().ToText(records);

        Assert.Contains("Full Name", csv);
        Assert.Contains("Years Old", csv);
    }

    #endregion

    #region Fluent Builder API

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FluentBuilder_ChainedMethods_Work()
    {
        var records = new[]
        {
            new TestPerson { Name = "Alice", Age = 30, City = "New York" }
        };

        var csv = Csv.Write<TestPerson>()
            .WithDelimiter(';')
            .WithNewLine("\n")
            .AlwaysQuote()
            .ToText(records);

        Assert.Contains(";", csv);
        Assert.DoesNotContain(",", csv);
        Assert.EndsWith("\n", csv);
        Assert.Contains("\"Alice\"", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public void CreateWriter_ManualWriting_Works()
    {
        using var sw = new StringWriter();
        using var writer = Csv.Write().CreateWriter(sw, leaveOpen: true);

        writer.WriteRow("Name", "Age");
        writer.WriteRow("Alice", "30");
        writer.Flush();

        Assert.Contains("Name,Age", sw.ToString());
        Assert.Contains("Alice,30", sw.ToString());
    }

    #endregion

    #region Stream and File Writing

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public void ToStream_WritesToStream()
    {
        var records = new[]
        {
            new TestPerson { Name = "Alice", Age = 30, City = "New York" }
        };

        using var ms = new MemoryStream();
        Csv.Write<TestPerson>().ToStream(ms, records);

        ms.Position = 0;
        using var reader = new StreamReader(ms);
        var csv = reader.ReadToEnd();

        Assert.Contains("Alice", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public void ToFile_WritesToFile()
    {
        var records = new[]
        {
            new TestPerson { Name = "Alice", Age = 30, City = "New York" }
        };

        var tempPath = Path.GetTempFileName();
        try
        {
            Csv.Write<TestPerson>().ToFile(tempPath, records);

            var csv = File.ReadAllText(tempPath);
            Assert.Contains("Alice", csv);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    #endregion

    #region Round-Trip Tests

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public void RoundTrip_SimpleData_Preserves()
    {
        var original = new[]
        {
            new TestPerson { Name = "Alice", Age = 30, City = "New York" },
            new TestPerson { Name = "Bob", Age = 25, City = "London" }
        };

        var csv = Csv.Write<TestPerson>().ToText(original);
        var parsed = Csv.DeserializeRecords<TestPerson>(csv).ToList();

        Assert.Equal(2, parsed.Count);
        Assert.Equal("Alice", parsed[0].Name);
        Assert.Equal(30, parsed[0].Age);
        Assert.Equal("New York", parsed[0].City);
        Assert.Equal("Bob", parsed[1].Name);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public void RoundTrip_SpecialCharacters_Preserves()
    {
        using var sw = new StringWriter();
        using var writer = new CsvStreamWriter(sw, CsvWriterOptions.Default, leaveOpen: true);

        // Test without newlines in fields (simpler case)
        writer.WriteRow("hello", "world");
        writer.WriteRow("a,b", "c\"d");
        writer.Flush();

        var csv = sw.ToString();
        var reader = Csv.ReadFromText(csv);

        // First row - plain values
        Assert.True(reader.MoveNext());
        Assert.Equal("hello", reader.Current[0].UnquoteToString());
        Assert.Equal("world", reader.Current[1].UnquoteToString());

        // Second row - quoted values with special chars (use UnquoteToString for unquoted values)
        Assert.True(reader.MoveNext());
        Assert.Equal("a,b", reader.Current[0].UnquoteToString());
        Assert.Equal("c\"d", reader.Current[1].UnquoteToString());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public void RoundTrip_EmptyFields_Preserves()
    {
        using var sw = new StringWriter();
        using var writer = new CsvStreamWriter(sw, CsvWriterOptions.Default, leaveOpen: true);

        writer.WriteRow("a", "", "c");
        writer.WriteRow("", "b", "");
        writer.Flush();

        var csv = sw.ToString();
        var reader = Csv.ReadFromText(csv);

        Assert.True(reader.MoveNext());
        Assert.Equal("a", reader.Current[0].ToString());
        Assert.Equal("", reader.Current[1].ToString());
        Assert.Equal("c", reader.Current[2].ToString());

        Assert.True(reader.MoveNext());
        Assert.Equal("", reader.Current[0].ToString());
        Assert.Equal("b", reader.Current[1].ToString());
        Assert.Equal("", reader.Current[2].ToString());
    }

    #endregion

    #region Edge Cases

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void WriteRow_EmptyRow_WritesNewline()
    {
        using var sw = new StringWriter();
        using var writer = new CsvStreamWriter(sw, CsvWriterOptions.Default, leaveOpen: true);

        writer.EndRow();
        writer.Flush();

        Assert.Equal("\r\n", sw.ToString());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void WriteRow_SingleField_WritesCorrectly()
    {
        using var sw = new StringWriter();
        using var writer = new CsvStreamWriter(sw, CsvWriterOptions.Default, leaveOpen: true);

        writer.WriteRow("single");
        writer.Flush();

        Assert.Equal("single\r\n", sw.ToString());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void WriteRow_VeryLongField_WritesCorrectly()
    {
        using var sw = new StringWriter();
        using var writer = new CsvStreamWriter(sw, CsvWriterOptions.Default, leaveOpen: true);

        var longValue = new string('x', 100000);
        writer.WriteRow(longValue);
        writer.Flush();

        var result = sw.ToString();
        Assert.Contains(longValue, result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void WriteRow_UnicodeCharacters_WritesCorrectly()
    {
        using var sw = new StringWriter();
        using var writer = new CsvStreamWriter(sw, CsvWriterOptions.Default, leaveOpen: true);

        writer.WriteRow("日本語", "中文", "한국어");
        writer.Flush();

        var result = sw.ToString();
        Assert.Contains("日本語", result);
        Assert.Contains("中文", result);
        Assert.Contains("한국어", result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void WriteRow_EmptyCollection_WritesNothing()
    {
        var records = Array.Empty<TestPerson>();
        var csv = Csv.Write<TestPerson>().WithoutHeader().ToText(records);

        Assert.Equal("", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void WriteRow_EmptyCollectionWithHeader_WritesOnlyHeader()
    {
        var records = Array.Empty<TestPerson>();
        var csv = Csv.Write<TestPerson>().WithHeader().ToText(records);

        Assert.Contains("Name", csv);
        Assert.Contains("Age", csv);
        var lines = csv.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
        Assert.Single(lines);
    }

    #endregion

    #region Disposal and Resource Management

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Dispose_FlushesBuffer()
    {
        var sw = new StringWriter();
        var writer = new CsvStreamWriter(sw, CsvWriterOptions.Default, leaveOpen: true);

        writer.WriteRow("a", "b", "c");
        writer.Dispose();

        Assert.Equal("a,b,c\r\n", sw.ToString());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Dispose_LeaveOpenTrue_DoesNotDisposeUnderlying()
    {
        var sw = new StringWriter();
        using (var writer = new CsvStreamWriter(sw, CsvWriterOptions.Default, leaveOpen: true))
        {
            writer.WriteRow("test");
        }

        // Should still be able to write to StringWriter
        sw.Write("more");
        Assert.Contains("more", sw.ToString());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void WriteAfterDispose_Throws()
    {
        var sw = new StringWriter();
        var writer = new CsvStreamWriter(sw, CsvWriterOptions.Default, leaveOpen: true);
        writer.Dispose();

        Assert.Throws<ObjectDisposedException>(() => writer.WriteRow("test"));
    }

    #endregion

    #region Options Validation

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void InvalidDelimiter_ThrowsException()
    {
        var options = new CsvWriterOptions { Delimiter = '€' }; // Non-ASCII

        using var sw = new StringWriter();
        Assert.Throws<CsvException>(() => new CsvStreamWriter(sw, options));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void DelimiterEqualsQuote_ThrowsException()
    {
        var options = new CsvWriterOptions { Delimiter = '"', Quote = '"' };

        using var sw = new StringWriter();
        Assert.Throws<CsvException>(() => new CsvStreamWriter(sw, options));
    }

    #endregion
}
