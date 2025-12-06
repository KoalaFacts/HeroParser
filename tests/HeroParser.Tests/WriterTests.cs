using HeroParser.SeparatedValues;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Reading;
using HeroParser.SeparatedValues.Reading.Records;
using HeroParser.SeparatedValues.Reading.Shared;
using HeroParser.SeparatedValues.Writing;
using System.Globalization;
using System.Text;
using Xunit;

namespace HeroParser.Tests;

// Run writer tests sequentially to avoid ArrayPool race conditions
[Collection("AsyncWriterTests")]
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

    [CsvGenerateBinder]
    internal class TestPerson
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

        var csv = Csv.WriteToText(records);

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

        var options = new CsvWriterOptions { WriteHeader = false };
        var csv = Csv.WriteToText(records, options);

        Assert.DoesNotContain("Name", csv);
        Assert.Contains("Alice", csv);
    }

    [CsvGenerateBinder]
    internal class PersonWithColumn
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

        var csv = Csv.WriteToText(records);

        Assert.Contains("Full Name", csv);
        Assert.Contains("Years Old", csv);
    }

    #endregion

    #region Low-Level Writer API

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public void CreateWriter_ManualWriting_Works()
    {
        using var sw = new StringWriter();
        using var writer = Csv.CreateWriter(sw, leaveOpen: true);

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
        Csv.WriteToStream(ms, records);

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
            Csv.WriteToFile(tempPath, records);

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

        var csv = Csv.WriteToText(original);
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

    [CsvGenerateBinder]
    internal class AllTypesRecord
    {
        public int IntValue { get; set; }
        public double DoubleValue { get; set; }
        public decimal DecimalValue { get; set; }
        public bool BoolValue { get; set; }
        public DateTime DateTimeValue { get; set; }
        public string? StringValue { get; set; }
        public int? NullableInt { get; set; }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public void RoundTrip_AllDataTypes_Preserves()
    {
        var original = new[]
        {
            new AllTypesRecord
            {
                IntValue = 42,
                DoubleValue = 3.14159,
                DecimalValue = 123.45m,
                BoolValue = true,
                DateTimeValue = new DateTime(2024, 12, 25, 10, 30, 0),
                StringValue = "Hello",
                NullableInt = 100
            },
            new AllTypesRecord
            {
                IntValue = -999,
                DoubleValue = 0.0,
                DecimalValue = 0m,
                BoolValue = false,
                DateTimeValue = new DateTime(2000, 1, 1),
                StringValue = null,
                NullableInt = null
            }
        };

        var csv = Csv.WriteToText(original);
        // Use NullValues to treat empty strings as null (CSV doesn't distinguish null vs empty string)
        var recordOptions = new CsvRecordOptions { NullValues = [""] };
        var parsed = Csv.DeserializeRecords<AllTypesRecord>(csv, recordOptions).ToList();

        Assert.Equal(2, parsed.Count);

        // First record
        Assert.Equal(42, parsed[0].IntValue);
        Assert.Equal(3.14159, parsed[0].DoubleValue, 5);
        Assert.Equal(123.45m, parsed[0].DecimalValue);
        Assert.True(parsed[0].BoolValue);
        Assert.Equal(new DateTime(2024, 12, 25, 10, 30, 0), parsed[0].DateTimeValue);
        Assert.Equal("Hello", parsed[0].StringValue);
        Assert.Equal(100, parsed[0].NullableInt);

        // Second record
        Assert.Equal(-999, parsed[1].IntValue);
        Assert.Equal(0.0, parsed[1].DoubleValue);
        Assert.Equal(0m, parsed[1].DecimalValue);
        Assert.False(parsed[1].BoolValue);
        Assert.Null(parsed[1].StringValue);
        Assert.Null(parsed[1].NullableInt);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public void RoundTrip_NewlinesInFields_Preserves()
    {
        using var sw = new StringWriter();
        using var writer = new CsvStreamWriter(sw, CsvWriterOptions.Default, leaveOpen: true);

        writer.WriteRow("line1\nline2", "normal");
        writer.WriteRow("a\r\nb\r\nc", "d");
        writer.Flush();

        var csv = sw.ToString();

        // Must enable AllowNewlinesInsideQuotes to parse multi-line fields
        var parserOptions = new CsvParserOptions { AllowNewlinesInsideQuotes = true };
        var reader = Csv.ReadFromText(csv, parserOptions);

        Assert.True(reader.MoveNext());
        Assert.Equal("line1\nline2", reader.Current[0].UnquoteToString());
        Assert.Equal("normal", reader.Current[1].UnquoteToString());

        Assert.True(reader.MoveNext());
        Assert.Equal("a\r\nb\r\nc", reader.Current[0].UnquoteToString());
        Assert.Equal("d", reader.Current[1].UnquoteToString());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public void RoundTrip_UnicodeCharacters_Preserves()
    {
        var original = new[]
        {
            new TestPerson { Name = "日本語テスト", Age = 25, City = "東京" },
            new TestPerson { Name = "中文测试", Age = 30, City = "北京" },
            new TestPerson { Name = "한국어테스트", Age = 35, City = "서울" },
            new TestPerson { Name = "Emoji 😀🎉", Age = 40, City = "Test 🌍" }
        };

        var csv = Csv.WriteToText(original);
        var parsed = Csv.DeserializeRecords<TestPerson>(csv).ToList();

        Assert.Equal(4, parsed.Count);
        Assert.Equal("日本語テスト", parsed[0].Name);
        Assert.Equal("東京", parsed[0].City);
        Assert.Equal("中文测试", parsed[1].Name);
        Assert.Equal("北京", parsed[1].City);
        Assert.Equal("한국어테스트", parsed[2].Name);
        Assert.Equal("서울", parsed[2].City);
        Assert.Equal("Emoji 😀🎉", parsed[3].Name);
        Assert.Equal("Test 🌍", parsed[3].City);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public void RoundTrip_CustomDelimiter_Preserves()
    {
        var original = new[]
        {
            new TestPerson { Name = "Alice", Age = 30, City = "New York" },
            new TestPerson { Name = "Bob,Jr", Age = 25, City = "London" }
        };

        var options = new CsvWriterOptions { Delimiter = ';' };
        var csv = Csv.WriteToText(original, options);

        var parserOptions = new CsvParserOptions { Delimiter = ';' };
        var parsed = Csv.DeserializeRecords<TestPerson>(csv, parserOptions: parserOptions).ToList();

        Assert.Equal(2, parsed.Count);
        Assert.Equal("Alice", parsed[0].Name);
        Assert.Equal("Bob,Jr", parsed[1].Name); // Comma should be preserved (not delimiter)
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public void RoundTrip_TabDelimiter_Preserves()
    {
        var original = new[]
        {
            new TestPerson { Name = "Alice", Age = 30, City = "New York" },
            new TestPerson { Name = "Bob", Age = 25, City = "London" }
        };

        var options = new CsvWriterOptions { Delimiter = '\t' };
        var csv = Csv.WriteToText(original, options);

        var parserOptions = new CsvParserOptions { Delimiter = '\t' };
        var parsed = Csv.DeserializeRecords<TestPerson>(csv, parserOptions: parserOptions).ToList();

        Assert.Equal(2, parsed.Count);
        Assert.Equal("Alice", parsed[0].Name);
        Assert.Equal("New York", parsed[0].City);
        Assert.Equal("Bob", parsed[1].Name);
        Assert.Equal("London", parsed[1].City);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public void RoundTrip_FieldWithDelimiter_Preserves()
    {
        // Test that a field containing the delimiter is properly quoted and round-trips
        using var sw = new StringWriter();
        using var writer = new CsvStreamWriter(sw, new CsvWriterOptions { Delimiter = '\t' }, leaveOpen: true);

        writer.WriteRow("header1", "header2");
        writer.WriteRow("normal", "value\twith\ttabs");
        writer.Flush();

        var csv = sw.ToString();

        // Verify the tab-containing value is quoted in output
        Assert.Contains("\"value\twith\ttabs\"", csv);

        // Read back with tab delimiter
        var parserOptions = new CsvParserOptions { Delimiter = '\t' };
        var reader = Csv.ReadFromText(csv, parserOptions);

        Assert.True(reader.MoveNext()); // header
        Assert.True(reader.MoveNext()); // data row
        Assert.Equal("normal", reader.Current[0].ToString());
        Assert.Equal("value\twith\ttabs", reader.Current[1].UnquoteToString());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public void RoundTrip_AlwaysQuoted_Preserves()
    {
        // Use low-level writer/reader to test quoting since DeserializeRecords
        // expects unquoted header names for binding
        using var sw = new StringWriter();
        using var writer = new CsvStreamWriter(sw, new CsvWriterOptions { QuoteStyle = QuoteStyle.Always }, leaveOpen: true);

        writer.WriteRow("Alice", "30", "NYC");
        writer.WriteRow("Bob", "25", "LA");
        writer.Flush();

        var csv = sw.ToString();

        // Verify all fields are quoted
        Assert.Contains("\"Alice\"", csv);
        Assert.Contains("\"30\"", csv);
        Assert.Contains("\"NYC\"", csv);

        // Read back and verify values are preserved
        var reader = Csv.ReadFromText(csv);

        Assert.True(reader.MoveNext());
        Assert.Equal("Alice", reader.Current[0].UnquoteToString());
        Assert.Equal("30", reader.Current[1].UnquoteToString());
        Assert.Equal("NYC", reader.Current[2].UnquoteToString());

        Assert.True(reader.MoveNext());
        Assert.Equal("Bob", reader.Current[0].UnquoteToString());
        Assert.Equal("25", reader.Current[1].UnquoteToString());
        Assert.Equal("LA", reader.Current[2].UnquoteToString());
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
        var options = new CsvWriterOptions { WriteHeader = false };
        var csv = Csv.WriteToText(records, options);

        Assert.Equal("", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void WriteRow_EmptyCollectionWithHeader_WritesOnlyHeader()
    {
        var records = Array.Empty<TestPerson>();
        var csv = Csv.WriteToText(records);

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
