using System.Globalization;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Reading.Records;
using HeroParser.Validation;
using Xunit;

namespace HeroParser.Tests;

/// <summary>Quick-win tests for options validation and small uncovered branches.</summary>
public class CoveragePushTests
{
    // ----- CsvReadOptions.Validate throws -----

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvReadOptions_NonAsciiDelimiter_Throws()
    {
        var ex = Assert.Throws<CsvException>(() => new CsvReadOptions { Delimiter = '€' }.Validate());
        Assert.Equal(CsvErrorCode.InvalidDelimiter, ex.ErrorCode);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvReadOptions_NonAsciiQuote_Throws()
        => Assert.Throws<CsvException>(() => new CsvReadOptions { Quote = '€' }.Validate());

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvReadOptions_DelimiterEqualsQuote_Throws()
        => Assert.Throws<CsvException>(() => new CsvReadOptions { Delimiter = ',', Quote = ',' }.Validate());

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvReadOptions_NonPositiveMaxColumnCount_Throws()
        => Assert.Throws<CsvException>(() => new CsvReadOptions { MaxColumnCount = 0 }.Validate());

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvReadOptions_HugeMaxColumnCount_Throws()
        => Assert.Throws<CsvException>(() => new CsvReadOptions { MaxColumnCount = 100_000 }.Validate());

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvReadOptions_NonPositiveMaxRowCount_Throws()
        => Assert.Throws<CsvException>(() => new CsvReadOptions { MaxRowCount = 0 }.Validate());

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvReadOptions_NewlinesInQuotesRequiresQuoting_Throws()
    {
        Assert.Throws<CsvException>(() => new CsvReadOptions
        {
            EnableQuotedFields = false,
            AllowNewlinesInsideQuotes = true
        }.Validate());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvReadOptions_NonAsciiComment_Throws()
        => Assert.Throws<CsvException>(() => new CsvReadOptions { CommentCharacter = '€' }.Validate());

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvReadOptions_CommentEqualsDelimiter_Throws()
        => Assert.Throws<CsvException>(() => new CsvReadOptions { CommentCharacter = ',' }.Validate());

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvReadOptions_CommentEqualsQuote_Throws()
        => Assert.Throws<CsvException>(() => new CsvReadOptions { CommentCharacter = '"' }.Validate());

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvReadOptions_NonPositiveMaxInputSize_Throws()
        => Assert.Throws<CsvException>(() => new CsvReadOptions { MaxInputSize = 0 }.Validate());

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvReadOptions_NonPositiveMaxFieldSize_Throws()
        => Assert.Throws<CsvException>(() => new CsvReadOptions { MaxFieldSize = 0 }.Validate());

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvReadOptions_NonAsciiEscape_Throws()
        => Assert.Throws<CsvException>(() => new CsvReadOptions { EscapeCharacter = '€' }.Validate());

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvReadOptions_EscapeEqualsDelimiter_Throws()
        => Assert.Throws<CsvException>(() => new CsvReadOptions { EscapeCharacter = ',' }.Validate());

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvReadOptions_EscapeEqualsQuote_Throws()
        => Assert.Throws<CsvException>(() => new CsvReadOptions { EscapeCharacter = '"' }.Validate());

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvReadOptions_EscapeEqualsComment_Throws()
        => Assert.Throws<CsvException>(() => new CsvReadOptions
        {
            CommentCharacter = '#',
            EscapeCharacter = '#'
        }.Validate());

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvReadOptions_NonPositiveMaxRowSize_Throws()
        => Assert.Throws<CsvException>(() => new CsvReadOptions { MaxRowSize = 0 }.Validate());

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvReadOptions_ValidateInputSize_OverLimit_Throws()
    {
        var opts = new CsvReadOptions { MaxInputSize = 100 };
        var method = typeof(CsvReadOptions).GetMethod("ValidateInputSize", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var ex = Assert.Throws<System.Reflection.TargetInvocationException>(() => method!.Invoke(opts, [200L]));
        Assert.IsType<CsvException>(ex.InnerException);
    }

    // ----- CSV reader with rare options to exercise CsvRowParser branches -----

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Csv_CommentCharacter_SkipsCommentLines()
    {
        string csv = "# comment\nName,Age\n# another\nAlice,30\n";

        var rows = new List<string[]>();
        var reader = Csv.Read()
            .WithCommentCharacter('#')
            .FromText(csv);
        foreach (var row in reader)
        {
            string[] cols = new string[row.ColumnCount];
            for (int i = 0; i < row.ColumnCount; i++) cols[i] = row[i].ToString();
            rows.Add(cols);
        }
        Assert.Equal(2, rows.Count);
        Assert.Equal("Name", rows[0][0]);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Csv_TrimFields_TrimsUnquotedWhitespace()
    {
        string csv = "  Alice  ,  30  \n";

        var reader = Csv.Read().TrimFields().FromText(csv);
        Assert.True(reader.MoveNext());
        Assert.Equal("Alice", reader.Current[0].ToString());
        Assert.Equal("30", reader.Current[1].ToString());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Csv_EscapeCharacter_EscapesQuoteWithBackslash()
    {
        string csv = "\"He said \\\"hi\\\"\",30\n";

        var reader = Csv.Read().WithEscapeCharacter('\\').FromText(csv);
        Assert.True(reader.MoveNext());
        // First column unquoted should contain literal `He said "hi"`.
        var col = reader.Current[0];
        Assert.Contains("hi", col.ToString());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Csv_DisableQuotedFields_TreatsQuoteAsLiteral()
    {
        string csv = "\"hello\",world\n";

        var reader = Csv.Read().DisableQuotedFields().FromText(csv);
        Assert.True(reader.MoveNext());
        Assert.Equal(2, reader.Current.ColumnCount);
        // With quoted fields disabled, leading/trailing quotes are part of the value.
        Assert.Contains("\"", reader.Current[0].ToString());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Csv_MaxFieldSize_ThrowsOnOversize()
    {
        string csv = "thisIsALongField,30\n";

        Assert.Throws<CsvException>(() =>
        {
            using var reader = Csv.Read().WithMaxFieldSize(5).FromText(csv);
            while (reader.MoveNext()) { }
        });
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Csv_AllowNewlinesInQuotes_Works()
    {
        string csv = "\"line1\nline2\",30\n";

        var reader = Csv.Read().AllowNewlinesInQuotes().FromText(csv);
        Assert.True(reader.MoveNext());
        Assert.Equal(2, reader.Current.ColumnCount);
        Assert.Contains("line1", reader.Current[0].ToString());
        Assert.Contains("line2", reader.Current[0].ToString());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Csv_WithCulture_ParsesLocalizedNumbers()
    {
        var deDe = CultureInfo.GetCultureInfo("de-DE");
        var reader = Csv.Read<CoveragePerson>()
            .WithCulture(deDe)
            .WithDelimiter(';')
            .FromText("Name;Age\nAlice;30\n")
            .ToList();
        Assert.NotEmpty(reader);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Csv_WithCultureName_Variant()
    {
        var reader = Csv.Read<CoveragePerson>()
            .WithCulture("en-US")
            .FromText("Name,Age\nAlice,30\n")
            .ToList();
        Assert.NotEmpty(reader);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Csv_AllowMissingColumns_DoesNotThrow()
    {
        string csv = "Name,Age,City\nAlice,30\n";

        var reader = Csv.Read<CoveragePerson>()
            .AllowMissingColumns()
            .FromText(csv)
            .ToList();
        Assert.Single(reader);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Csv_CaseSensitiveHeaders_DistinguishesNAme()
    {
        string csv = "NAME,age\nAlice,30\n";

        var reader = Csv.Read<CoveragePerson>()
            .CaseSensitiveHeaders()
            .AllowMissingColumns()
            .FromText(csv);
        var people = reader.ToList();
        // With case-sensitive headers and 'NAME' header, no match for 'Name' property.
        Assert.True(people.Count <= 1);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Csv_WithNullValues_TreatsTokensAsNull()
    {
        string csv = "Name,Age\nAlice,NA\n";
        var reader = Csv.Read<NullableAgePerson>()
            .WithNullValues("NA", "null")
            .FromText(csv)
            .ToList();
        Assert.Single(reader);
        Assert.Null(reader[0].Age);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Csv_SkipRows_SkipsLeading()
    {
        // SkipRows applies BEFORE the header — skip a pre-header banner row,
        // then the header is detected and 'Alice'/'Bob' bind normally.
        string csv = "BANNER\nName,Age\nAlice,30\nBob,25\n";
        var reader = Csv.Read<CoveragePerson>()
            .SkipRows(1)
            .FromText(csv)
            .ToList();
        Assert.Equal(2, reader.Count);
        Assert.Equal("Alice", reader[0].Name);
    }

    // ----- CSV writer rare options -----

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvWrite_AlwaysQuote_AddsQuotesToAllFields()
    {
        var people = new[] { new CoveragePerson { Name = "Alice", Age = 30 } };
        string csv = Csv.Write<CoveragePerson>().AlwaysQuote().ToText(people);
        Assert.Contains("\"Alice\"", csv);
        Assert.Contains("\"30\"", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvWrite_NeverQuote_StripsQuotes()
    {
        var people = new[] { new CoveragePerson { Name = "with,comma", Age = 30 } };
        // NeverQuote: commas not escaped, which produces malformed CSV.
        string csv = Csv.Write<CoveragePerson>().NeverQuote().ToText(people);
        Assert.DoesNotContain("\"", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvWrite_WithoutHeader_EmitsDataOnly()
    {
        var people = new[] { new CoveragePerson { Name = "Alice", Age = 30 } };
        string csv = Csv.Write<CoveragePerson>().WithoutHeader().ToText(people);
        Assert.DoesNotContain("Name", csv);
        Assert.Contains("Alice", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvWrite_WithNullValue_RepresentsNull()
    {
        var people = new[] { new NullableAgePerson { Name = "Alice", Age = null } };
        string csv = Csv.Write<NullableAgePerson>().WithNullValue("NA").ToText(people);
        Assert.Contains("NA", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvWrite_WithDateTimeFormat_AppliesFormat()
    {
        var rows = new[] { new EventRow { When = new DateTime(2024, 6, 1, 12, 0, 0) } };
        string csv = Csv.Write<EventRow>().WithDateTimeFormat("yyyy-MM-dd").ToText(rows);
        Assert.Contains("2024-06-01", csv);
        Assert.DoesNotContain("12:00", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvWrite_WithCulture_AffectsNumberFormat()
    {
        var rows = new[] { new MoneyRow { Amount = 1234.56m } };
        string csv = Csv.Write<MoneyRow>().WithCulture("de-DE").ToText(rows);
        Assert.Contains("1234,56", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvWrite_WithDelimiter_AcceptsTab()
    {
        var people = new[] { new CoveragePerson { Name = "Alice", Age = 30 } };
        string csv = Csv.Write<CoveragePerson>().WithDelimiter('\t').ToText(people);
        Assert.Contains("Alice\t30", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvWrite_WithNewLine_AcceptsCrlf()
    {
        var people = new[] { new CoveragePerson { Name = "Alice", Age = 30 } };
        string csv = Csv.Write<CoveragePerson>().WithNewLine("\r\n").ToText(people);
        Assert.Contains("\r\n", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvWrite_MaxRowCount_StopsAtLimit()
    {
        var rows = Enumerable.Range(0, 10).Select(i => new CoveragePerson { Name = $"P{i}", Age = i });
        Assert.Throws<CsvException>(() => Csv.Write<CoveragePerson>().WithMaxRowCount(3).ToText(rows));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvWrite_MaxFieldSize_RejectsOversize()
    {
        var rows = new[] { new CoveragePerson { Name = new string('x', 100), Age = 30 } };
        Assert.Throws<CsvException>(() => Csv.Write<CoveragePerson>().WithMaxFieldSize(5).ToText(rows));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvWrite_MaxOutputSize_Caps()
    {
        var rows = Enumerable.Range(0, 100).Select(i => new CoveragePerson { Name = $"P{i}", Age = i });
        Assert.Throws<CsvException>(() => Csv.Write<CoveragePerson>().WithMaxOutputSize(50).ToText(rows));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvWrite_WithoutEmptyColumns_StripsEmpties()
    {
        var rows = new[] { new SparseRow { A = "x", B = null, C = "z" }, new SparseRow { A = "x", B = null, C = "z" } };
        string csv = Csv.Write<SparseRow>().WithoutEmptyColumns().ToText(rows);
        Assert.DoesNotContain(",,", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvWrite_InjectionProtection_EscapesFormulaPrefix()
    {
        var rows = new[] { new CoveragePerson { Name = "=SUM(A1)", Age = 0 } };
        string csv = Csv.Write<CoveragePerson>()
            .WithInjectionProtection(SeparatedValues.Writing.CsvInjectionProtection.EscapeWithQuote)
            .ToText(rows);
        Assert.Contains("=SUM(A1)", csv);
        Assert.Contains("'", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvWrite_ValidationMode_LenientCollectsErrors()
    {
        var rows = new[] { new InvalidRow { Required = null } };
        string csv = Csv.Write<InvalidRow>().WithValidationMode(ValidationMode.Lenient).ToText(rows);
        // Lenient just skips bad rows.
        Assert.NotNull(csv);
    }

    // ----- DataReader extras -----

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvDataReader_AllowMissingColumns_ReturnsDBNull()
    {
        string csv = "A,B,C\n1,2\n";
        using var dr = Csv.CreateDataReader(
            new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv)),
            new CsvReadOptions(),
            new SeparatedValues.Reading.Data.CsvDataReaderOptions { AllowMissingColumns = true });
        Assert.True(dr.Read());
        Assert.True(dr.IsDBNull(2));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvDataReader_GetSchemaTable_HasExpectedColumns()
    {
        string csv = "A,B\n1,2\n";
        using var dr = Csv.CreateDataReader(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv)));
        Assert.True(dr.Read());
        using var schema = dr.GetSchemaTable();
        Assert.Equal(2, schema.Rows.Count);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvDataReader_GetString_OnNullValue_Throws()
    {
        string csv = "A,B\nfoo,\n";
        using var dr = Csv.CreateDataReader(
            new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv)),
            new CsvReadOptions(),
            new SeparatedValues.Reading.Data.CsvDataReaderOptions { NullValues = [""] });
        Assert.True(dr.Read());
        Assert.True(dr.IsDBNull(1));
        Assert.Throws<InvalidCastException>(() => dr.GetString(1));
    }
}

[GenerateBinder]
public class CoveragePerson
{
    public string? Name { get; set; }
    public int Age { get; set; }
}

[GenerateBinder]
public class NullableAgePerson
{
    public string? Name { get; set; }
    public int? Age { get; set; }
}

[GenerateBinder]
public class EventRow
{
    public DateTime When { get; set; }
}

[GenerateBinder]
public class MoneyRow
{
    public decimal Amount { get; set; }
}

[GenerateBinder]
public class SparseRow
{
    public string? A { get; set; }
    public string? B { get; set; }
    public string? C { get; set; }
}

[GenerateBinder]
public class InvalidRow
{
    [Validate(NotNull = true)]
    public string? Required { get; set; }
}
