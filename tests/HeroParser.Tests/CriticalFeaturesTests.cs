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

    #region CsvException Field Value Context Tests (#3)

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvException_IncludesFieldValue()
    {
        var csv = "Name,Age\nAlice,not_a_number";

        CsvException? ex = null;
        try
        {
            var reader = Csv.ParseRecords<PersonWithAge>(csv);
            while (reader.MoveNext()) { }
        }
        catch (CsvException e)
        {
            ex = e;
        }

        Assert.NotNull(ex);
        Assert.Equal(CsvErrorCode.ParseError, ex!.ErrorCode);
        Assert.Equal("not_a_number", ex.FieldValue);
        Assert.Contains("not_a_number", ex.Message);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvException_TruncatesLongFieldValue()
    {
        var longValue = new string('x', 150);
        var csv = $"Name,Age\nAlice,{longValue}";

        CsvException? ex = null;
        try
        {
            var reader = Csv.ParseRecords<PersonWithAge>(csv);
            while (reader.MoveNext()) { }
        }
        catch (CsvException e)
        {
            ex = e;
        }

        Assert.NotNull(ex);
        Assert.NotNull(ex!.FieldValue);
        Assert.Equal(103, ex.FieldValue!.Length); // 100 chars + "..."
        Assert.EndsWith("...", ex.FieldValue);
    }

    private class PersonWithAge
    {
        public string Name { get; set; } = string.Empty;
        public int Age { get; set; }
    }

    #endregion

    #region Error Handling Callbacks Tests (#4)

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void OnParseError_SkipRow_SkipsProblematicRows()
    {
        var csv = "Name,Age\nAlice,25\nBob,invalid\nCharlie,30";
        var skippedRows = new List<int>();
        var recordOptions = new CsvRecordOptions
        {
            OnParseError = ctx =>
            {
                skippedRows.Add(ctx.Row);
                return ParseErrorAction.SkipRow;
            }
        };

        var records = new List<PersonWithAge>();
        var reader = Csv.ParseRecords<PersonWithAge>(csv, recordOptions);
        while (reader.MoveNext())
        {
            records.Add(reader.Current);
        }

        Assert.Equal(2, records.Count);
        Assert.Equal("Alice", records[0].Name);
        Assert.Equal(25, records[0].Age);
        Assert.Equal("Charlie", records[1].Name);
        Assert.Equal(30, records[1].Age);

        Assert.Single(skippedRows);
        Assert.Equal(3, skippedRows[0]); // Row 3 (Bob,invalid) was skipped
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void OnParseError_UseDefault_UsesDefaultValue()
    {
        var csv = "Name,Age\nAlice,invalid";
        var recordOptions = new CsvRecordOptions
        {
            OnParseError = _ => ParseErrorAction.UseDefault
        };

        var records = new List<PersonWithAge>();
        var reader = Csv.ParseRecords<PersonWithAge>(csv, recordOptions);
        while (reader.MoveNext())
        {
            records.Add(reader.Current);
        }

        Assert.Single(records);
        Assert.Equal("Alice", records[0].Name);
        Assert.Equal(0, records[0].Age); // Default value for int
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void OnParseError_Throw_ThrowsException()
    {
        var csv = "Name,Age\nAlice,invalid";
        var recordOptions = new CsvRecordOptions
        {
            OnParseError = _ => ParseErrorAction.Throw
        };

        CsvException? ex = null;
        try
        {
            var reader = Csv.ParseRecords<PersonWithAge>(csv, recordOptions);
            while (reader.MoveNext()) { }
        }
        catch (CsvException e)
        {
            ex = e;
        }

        Assert.NotNull(ex);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void OnParseError_ContextContainsCorrectInfo()
    {
        var csv = "Name,Age\nAlice,bad_value";
        CsvParseErrorContext? capturedContext = null;
        var recordOptions = new CsvRecordOptions
        {
            OnParseError = ctx =>
            {
                capturedContext = ctx;
                return ParseErrorAction.SkipRow;
            }
        };

        var reader = Csv.ParseRecords<PersonWithAge>(csv, recordOptions);
        while (reader.MoveNext()) { }

        Assert.NotNull(capturedContext);
        Assert.Equal(2, capturedContext.Value.Row);
        Assert.Equal(2, capturedContext.Value.Column);
        Assert.Equal("Age", capturedContext.Value.MemberName);
        Assert.Equal(typeof(int), capturedContext.Value.TargetType);
        Assert.Equal("bad_value", capturedContext.Value.FieldValue);
    }

    #endregion

    #region Unterminated Quote Position Tests (#6)

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void UnterminatedQuote_IncludesPosition()
    {
        var csv = "Name,Value\nAlice,\"unclosed quote";

        CsvException? ex = null;
        try
        {
            var reader = Csv.ReadFromText(csv);
            while (reader.MoveNext()) { }
        }
        catch (CsvException e)
        {
            ex = e;
        }

        Assert.NotNull(ex);
        Assert.Equal(CsvErrorCode.ParseError, ex!.ErrorCode);
        Assert.NotNull(ex.QuoteStartPosition);
        Assert.Contains("quote started at position", ex.Message);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void UnterminatedQuote_PositionIsCorrect()
    {
        // "Hello is at position 5 (0-indexed, after "Name,")
        var csv = "Name,\"Hello";

        CsvException? ex = null;
        try
        {
            var reader = Csv.ReadFromText(csv);
            while (reader.MoveNext()) { }
        }
        catch (CsvException e)
        {
            ex = e;
        }

        Assert.NotNull(ex);
        Assert.NotNull(ex!.QuoteStartPosition);
        Assert.Equal(5, ex.QuoteStartPosition!.Value); // 0-based position of the quote
    }

    #endregion

    #region Duplicate Header Detection Tests (#9)

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void DetectDuplicateHeaders_ThrowsOnDuplicate()
    {
        var csv = "Name,Age,Name\nAlice,25,Bob";
        var recordOptions = new CsvRecordOptions { DetectDuplicateHeaders = true };

        CsvException? ex = null;
        try
        {
            var reader = Csv.ParseRecords<PersonWithAge>(csv, recordOptions);
            while (reader.MoveNext()) { }
        }
        catch (CsvException e)
        {
            ex = e;
        }

        Assert.NotNull(ex);
        Assert.Equal(CsvErrorCode.ParseError, ex!.ErrorCode);
        Assert.Contains("Duplicate header", ex.Message);
        Assert.Contains("Name", ex.Message);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void DetectDuplicateHeaders_CaseInsensitive()
    {
        var csv = "Name,Age,NAME\nAlice,25,Bob";
        var recordOptions = new CsvRecordOptions
        {
            DetectDuplicateHeaders = true,
            CaseSensitiveHeaders = false
        };

        CsvException? ex = null;
        try
        {
            var reader = Csv.ParseRecords<PersonWithAge>(csv, recordOptions);
            while (reader.MoveNext()) { }
        }
        catch (CsvException e)
        {
            ex = e;
        }

        Assert.NotNull(ex);
        Assert.Equal(CsvErrorCode.ParseError, ex!.ErrorCode);
        Assert.Contains("Duplicate header", ex.Message);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void DetectDuplicateHeaders_CaseSensitive_AllowsDifferentCase()
    {
        var csv = "Name,Age,NAME\nAlice,25,Bob";
        var recordOptions = new CsvRecordOptions
        {
            DetectDuplicateHeaders = true,
            CaseSensitiveHeaders = true,
            AllowMissingColumns = true
        };

        // Should not throw since Name != NAME when case-sensitive
        var records = new List<PersonWithAge>();
        var reader = Csv.ParseRecords<PersonWithAge>(csv, recordOptions);
        while (reader.MoveNext())
        {
            records.Add(reader.Current);
        }

        Assert.Single(records);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void DetectDuplicateHeaders_Disabled_AllowsDuplicates()
    {
        var csv = "Name,Age,Name\nAlice,25,Bob";
        var recordOptions = new CsvRecordOptions
        {
            DetectDuplicateHeaders = false, // Default
            AllowMissingColumns = true
        };

        // Should not throw
        var records = new List<PersonWithAge>();
        var reader = Csv.ParseRecords<PersonWithAge>(csv, recordOptions);
        while (reader.MoveNext())
        {
            records.Add(reader.Current);
        }

        Assert.Single(records);
        Assert.Equal("Alice", records[0].Name); // First occurrence wins
    }

    #endregion
}
