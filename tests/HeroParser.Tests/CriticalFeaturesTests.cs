using HeroParser.SeparatedValues;
using HeroParser.SeparatedValues.Records;
using Xunit;

namespace HeroParser.Tests;

public class CriticalFeaturesTests
{
    #region Comment Line Tests

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CommentLines_SkippedCorrectly()
    {
        var csv = "# This is a comment\na,b,c\n1,2,3\n# Another comment\n4,5,6";
        var options = new CsvParserOptions { CommentCharacter = '#' };
        var reader = Csv.ReadFromText(csv, options);

        Assert.True(reader.MoveNext());
        var row1 = reader.Current;
        Assert.Equal("a", row1[0].ToString());

        Assert.True(reader.MoveNext());
        var row2 = reader.Current;
        Assert.Equal("1", row2[0].ToString());

        Assert.True(reader.MoveNext());
        var row3 = reader.Current;
        Assert.Equal("4", row3[0].ToString());

        Assert.False(reader.MoveNext());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CommentLines_WithLeadingWhitespace_SkippedCorrectly()
    {
        var csv = "  # Comment with leading spaces\na,b,c\n\t# Comment with leading tab\n1,2,3";
        var options = new CsvParserOptions { CommentCharacter = '#' };
        var reader = Csv.ReadFromText(csv, options);

        Assert.True(reader.MoveNext());
        var row1 = reader.Current;
        Assert.Equal("a", row1[0].ToString());

        Assert.True(reader.MoveNext());
        var row2 = reader.Current;
        Assert.Equal("1", row2[0].ToString());

        Assert.False(reader.MoveNext());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CommentCharacter_NotAtLineStart_NotTreatedAsComment()
    {
        var csv = "a,#b,c\n1,#2,3";
        var options = new CsvParserOptions { CommentCharacter = '#' };
        var reader = Csv.ReadFromText(csv, options);

        Assert.True(reader.MoveNext());
        var row1 = reader.Current;
        Assert.Equal("a", row1[0].ToString());
        Assert.Equal("#b", row1[1].ToString());
        Assert.Equal("c", row1[2].ToString());

        Assert.True(reader.MoveNext());
        var row2 = reader.Current;
        Assert.Equal("1", row2[0].ToString());
        Assert.Equal("#2", row2[1].ToString());
        Assert.Equal("3", row2[2].ToString());

        Assert.False(reader.MoveNext());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CommentCharacter_SameAsDelimiter_ThrowsException()
    {
        var options = new CsvParserOptions { CommentCharacter = ',' };
        var ex = Assert.Throws<CsvException>(() => options.Validate());
        Assert.Equal(CsvErrorCode.InvalidOptions, ex.ErrorCode);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CommentCharacter_SameAsQuote_ThrowsException()
    {
        var options = new CsvParserOptions { CommentCharacter = '"' };
        var ex = Assert.Throws<CsvException>(() => options.Validate());
        Assert.Equal(CsvErrorCode.InvalidOptions, ex.ErrorCode);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CommentCharacter_NonAscii_ThrowsException()
    {
        var options = new CsvParserOptions { CommentCharacter = '€' };
        var ex = Assert.Throws<CsvException>(() => options.Validate());
        Assert.Equal(CsvErrorCode.InvalidOptions, ex.ErrorCode);
    }

    #endregion

    #region Trim Fields Tests

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void TrimFields_RemovesWhitespace()
    {
        var csv = " a , b , c \n 1 , 2 , 3 ";
        var options = new CsvParserOptions { TrimFields = true };
        var reader = Csv.ReadFromText(csv, options);

        Assert.True(reader.MoveNext());
        var row1 = reader.Current;
        Assert.Equal("a", row1[0].ToString());
        Assert.Equal("b", row1[1].ToString());
        Assert.Equal("c", row1[2].ToString());

        Assert.True(reader.MoveNext());
        var row2 = reader.Current;
        Assert.Equal("1", row2[0].ToString());
        Assert.Equal("2", row2[1].ToString());
        Assert.Equal("3", row2[2].ToString());

        Assert.False(reader.MoveNext());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void TrimFields_WithTabs_RemovesWhitespace()
    {
        var csv = "\ta\t,\tb\t,\tc\t\n\t1\t,\t2\t,\t3\t";
        var options = new CsvParserOptions { TrimFields = true };
        var reader = Csv.ReadFromText(csv, options);

        Assert.True(reader.MoveNext());
        var row1 = reader.Current;
        Assert.Equal("a", row1[0].ToString());
        Assert.Equal("b", row1[1].ToString());
        Assert.Equal("c", row1[2].ToString());

        Assert.True(reader.MoveNext());
        var row2 = reader.Current;
        Assert.Equal("1", row2[0].ToString());
        Assert.Equal("2", row2[1].ToString());
        Assert.Equal("3", row2[2].ToString());

        Assert.False(reader.MoveNext());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void TrimFields_DoesNotAffectQuotedFields()
    {
        var csv = " \" a \" , b , c ";
        var options = new CsvParserOptions { TrimFields = true };
        var reader = Csv.ReadFromText(csv, options);

        Assert.True(reader.MoveNext());
        var row = reader.Current;
        Assert.Equal("\" a \"", row[0].ToString());
        Assert.Equal("b", row[1].ToString());
        Assert.Equal("c", row[2].ToString());

        Assert.False(reader.MoveNext());
    }

    #endregion

    #region Null Values Tests

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void NullValues_RecognizesNullStrings()
    {
        var csv = "Name,Age\nAlice,25\nBob,NULL\nCharlie,N/A";
        var recordOptions = new CsvRecordOptions { NullValues = new[] { "NULL", "N/A" } };
        var reader = Csv.ParseRecords<PersonWithNullableAge>(csv, recordOptions);

        var records = new List<PersonWithNullableAge>();
        foreach (var record in reader)
        {
            records.Add(record);
        }

        Assert.Equal(3, records.Count);
        Assert.Equal("Alice", records[0].Name);
        Assert.Equal(25, records[0].Age);

        Assert.Equal("Bob", records[1].Name);
        Assert.Null(records[1].Age);

        Assert.Equal("Charlie", records[2].Name);
        Assert.Null(records[2].Age);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void NullValues_CaseSensitive()
    {
        var csv = "Name,Age\nAlice,\nBob,NULL";
        var recordOptions = new CsvRecordOptions { NullValues = new[] { "NULL" } };
        var reader = Csv.ParseRecords<PersonWithNullableAge>(csv, recordOptions);

        var records = new List<PersonWithNullableAge>();
        foreach (var record in reader)
        {
            records.Add(record);
        }

        Assert.Equal(2, records.Count);
        Assert.Equal("Alice", records[0].Name);
        Assert.Null(records[0].Age); // Empty string, parsed as null for nullable int

        Assert.Equal("Bob", records[1].Name);
        Assert.Null(records[1].Age); // NULL matches the null value list
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void NullValues_EmptyStringAsNull()
    {
        var csv = "Name,Age\nAlice,25\nBob,\nCharlie,30";
        var recordOptions = new CsvRecordOptions { NullValues = new[] { "" } };
        var reader = Csv.ParseRecords<PersonWithNullableAge>(csv, recordOptions);

        var records = new List<PersonWithNullableAge>();
        foreach (var record in reader)
        {
            records.Add(record);
        }

        Assert.Equal(3, records.Count);
        Assert.Equal("Alice", records[0].Name);
        Assert.Equal(25, records[0].Age);

        Assert.Equal("Bob", records[1].Name);
        Assert.Null(records[1].Age);

        Assert.Equal("Charlie", records[2].Name);
        Assert.Equal(30, records[2].Age);
    }

    private class PersonWithNullableAge
    {
        public string Name { get; set; } = string.Empty;
        public int? Age { get; set; }
    }

    #endregion

    #region CSV Writer Tests

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvWriter_WritesSimpleRow()
    {
        using var stringWriter = new StringWriter();
        using var csvWriter = new CsvWriter(stringWriter);

        csvWriter.WriteRow("a", "b", "c");
        csvWriter.WriteRow("1", "2", "3");

        var result = stringWriter.ToString();
        Assert.Equal("a,b,c\r\n1,2,3\r\n", result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvWriter_QuotesFieldsWithDelimiter()
    {
        using var stringWriter = new StringWriter();
        using var csvWriter = new CsvWriter(stringWriter);

        csvWriter.WriteRow("a,b", "c", "d");

        var result = stringWriter.ToString();
        Assert.Equal("\"a,b\",c,d\r\n", result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvWriter_QuotesFieldsWithQuotes()
    {
        using var stringWriter = new StringWriter();
        using var csvWriter = new CsvWriter(stringWriter);

        csvWriter.WriteRow("a\"b", "c", "d");

        var result = stringWriter.ToString();
        Assert.Equal("\"a\"\"b\",c,d\r\n", result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvWriter_QuotesFieldsWithNewlines()
    {
        using var stringWriter = new StringWriter();
        using var csvWriter = new CsvWriter(stringWriter);

        csvWriter.WriteRow("a\nb", "c", "d");

        var result = stringWriter.ToString();
        Assert.Equal("\"a\nb\",c,d\r\n", result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvWriter_AlwaysQuote_QuotesAllFields()
    {
        var options = new CsvWriterOptions { AlwaysQuote = true };
        using var stringWriter = new StringWriter();
        using var csvWriter = new CsvWriter(stringWriter, options);

        csvWriter.WriteRow("a", "b", "c");

        var result = stringWriter.ToString();
        Assert.Equal("\"a\",\"b\",\"c\"\r\n", result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvWriter_CustomDelimiter()
    {
        var options = new CsvWriterOptions { Delimiter = '\t' };
        using var stringWriter = new StringWriter();
        using var csvWriter = new CsvWriter(stringWriter, options);

        csvWriter.WriteRow("a", "b", "c");

        var result = stringWriter.ToString();
        Assert.Equal("a\tb\tc\r\n", result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvWriter_CustomNewline()
    {
        var options = new CsvWriterOptions { NewLine = "\n" };
        using var stringWriter = new StringWriter();
        using var csvWriter = new CsvWriter(stringWriter, options);

        csvWriter.WriteRow("a", "b", "c");

        var result = stringWriter.ToString();
        Assert.Equal("a,b,c\n", result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvWriter_NullValues_WrittenAsEmpty()
    {
        using var stringWriter = new StringWriter();
        using var csvWriter = new CsvWriter(stringWriter);

        csvWriter.WriteRow("a", null, "c");

        var result = stringWriter.ToString();
        Assert.Equal("a,,c\r\n", result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvWriter_WriteToString_Works()
    {
        var rows = new[]
        {
            new[] { "a", "b", "c" },
            new[] { "1", "2", "3" }
        };

        var result = Csv.WriteToString(rows);
        Assert.Equal("a,b,c\r\n1,2,3\r\n", result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvWriter_RoundTrip_Works()
    {
        var original = "a,b,c\r\n1,2,3\r\n";

        // Read
        var reader = Csv.ReadFromText(original);
        var rows = new List<List<string>>();
        while (reader.MoveNext())
        {
            var row = new List<string>();
            for (int i = 0; i < reader.Current.ColumnCount; i++)
            {
                row.Add(reader.Current[i].ToString());
            }
            rows.Add(row);
        }

        // Write
        var result = Csv.WriteToString(rows);

        Assert.Equal(original, result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvWriter_WriterOptions_SameAsDelimiter_ThrowsException()
    {
        var options = new CsvWriterOptions { Delimiter = ',', Quote = ',' };
        var ex = Assert.Throws<CsvException>(() => options.Validate());
        Assert.Equal(CsvErrorCode.InvalidOptions, ex.ErrorCode);
    }

    #endregion
}
