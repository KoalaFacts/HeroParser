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
        var ex = Assert.Throws<CsvException>(options.Validate);
        Assert.Equal(CsvErrorCode.InvalidOptions, ex.ErrorCode);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CommentCharacter_SameAsQuote_ThrowsException()
    {
        var options = new CsvParserOptions { CommentCharacter = '"' };
        var ex = Assert.Throws<CsvException>(options.Validate);
        Assert.Equal(CsvErrorCode.InvalidOptions, ex.ErrorCode);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CommentCharacter_NonAscii_ThrowsException()
    {
        var options = new CsvParserOptions { CommentCharacter = '€' };
        var ex = Assert.Throws<CsvException>(options.Validate);
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
        var recordOptions = new CsvRecordOptions { NullValues = ["NULL", "N/A"] };
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
        var recordOptions = new CsvRecordOptions { NullValues = ["NULL"] };
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
        var recordOptions = new CsvRecordOptions { NullValues = [""] };
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
}
