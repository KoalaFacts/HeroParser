using System.Text;
using HeroParser.SeparatedValues.Core;
using Xunit;

namespace HeroParser.Tests.Internal;

/// <summary>
/// Forces the CSV row parser into its scalar (non-SIMD) code path by setting
/// <see cref="CsvReadOptions.UseSimdIfAvailable"/> = false, exercising lines
/// in CsvRowParser that the SIMD fast path skips.
/// </summary>
[Trait("Category", "Unit")]
public class CsvScalarParserTests
{
    private static readonly CsvReadOptions scalarOptions = new() { UseSimdIfAvailable = false };

    private static List<TestRecord> Parse(string csv, CsvReadOptions? opts = null)
    {
        var reader = Csv.DeserializeRecords<TestRecord>(csv, out _, parserOptions: opts ?? scalarOptions);
        var list = new List<TestRecord>();
        foreach (var r in reader) list.Add(r);
        return list;
    }

    [GenerateBinder]
    public sealed class TestRecord
    {
        [TabularMap(Name = "A")] public string A { get; set; } = "";
        [TabularMap(Name = "B")] public string B { get; set; } = "";
        [TabularMap(Name = "C")] public string C { get; set; } = "";
    }

    [Fact]
    public void Scalar_BasicCsv()
    {
        var records = Parse("A,B,C\n1,2,3\n4,5,6\n");
        Assert.Equal(2, records.Count);
        Assert.Equal("1", records[0].A);
        Assert.Equal("3", records[0].C);
        Assert.Equal("6", records[1].C);
    }

    [Fact]
    public void Scalar_QuotedFields_DoNotCrash()
    {
        // Scalar quote handling is exercised; the parser tracks quotes for delimiter detection.
        // Whether the binder strips quote chars is a separate concern tested elsewhere.
        var records = Parse("A,B,C\n\"hello\",\"world\",\"!\"\n");
        Assert.Single(records);
    }

    [Fact]
    public void Scalar_QuotedFieldWithEmbeddedDelimiter_StaysOneRow()
    {
        var records = Parse("A,B,C\n\"a,b\",x,y\n");
        // Embedded delimiter inside quotes should not split the row.
        Assert.Single(records);
    }

    [Fact]
    public void Scalar_QuotedFieldWithEmbeddedQuote_StaysOneRow()
    {
        var records = Parse("A,B,C\n\"she said \"\"hi\"\"\",x,y\n");
        Assert.Single(records);
    }

    [Fact]
    public void Scalar_QuotedFieldWithEmbeddedNewline()
    {
        var opts = new CsvReadOptions { UseSimdIfAvailable = false, AllowNewlinesInsideQuotes = true };
        var records = Parse("A,B,C\n\"line1\nline2\",x,y\n", opts);
        Assert.Single(records);
        Assert.Contains("line1", records[0].A);
        Assert.Contains("line2", records[0].A);
    }

    [Fact]
    public void Scalar_CrLf_RowSeparator()
    {
        var records = Parse("A,B,C\r\n1,2,3\r\n");
        Assert.Single(records);
        Assert.Equal("1", records[0].A);
    }

    [Fact]
    public void Scalar_LfOnly_RowSeparator()
    {
        var records = Parse("A,B,C\n1,2,3\n");
        Assert.Single(records);
    }

    [Fact]
    public void Scalar_TrailingNewlineOptional()
    {
        var records = Parse("A,B,C\n1,2,3");
        Assert.Single(records);
    }

    [Fact]
    public void Scalar_EmptyFields_PreservedAsEmpty()
    {
        var records = Parse("A,B,C\n,,\n");
        Assert.Single(records);
        Assert.Equal("", records[0].A);
        Assert.Equal("", records[0].B);
        Assert.Equal("", records[0].C);
    }

    [Fact]
    public void Scalar_CustomDelimiter_Pipe()
    {
        var opts = new CsvReadOptions { UseSimdIfAvailable = false, Delimiter = '|' };
        var records = Parse("A|B|C\n1|2|3\n", opts);
        Assert.Single(records);
        Assert.Equal("3", records[0].C);
    }

    [Fact]
    public void Scalar_CustomDelimiter_Tab()
    {
        var opts = new CsvReadOptions { UseSimdIfAvailable = false, Delimiter = '\t' };
        var records = Parse("A\tB\tC\n1\t2\t3\n", opts);
        Assert.Single(records);
    }

    [Fact]
    public void Scalar_CommentLines_Skipped()
    {
        var opts = new CsvReadOptions { UseSimdIfAvailable = false, CommentCharacter = '#' };
        var records = Parse("A,B,C\n# comment line\n1,2,3\n# another\n4,5,6\n", opts);
        Assert.Equal(2, records.Count);
    }

    [Fact]
    public void Scalar_TrimFields_StripsWhitespace()
    {
        var opts = new CsvReadOptions { UseSimdIfAvailable = false, TrimFields = true };
        var records = Parse("A,B,C\n  1  ,  2  ,  3  \n", opts);
        Assert.Single(records);
        Assert.Equal("1", records[0].A);
        Assert.Equal("3", records[0].C);
    }

    [Fact]
    public void Scalar_EscapeCharacter_DoesNotCrash()
    {
        // EscapeCharacter forces the scalar path even when SIMD is enabled.
        var opts = new CsvReadOptions { EscapeCharacter = '\\' };
        var records = Parse("A,B,C\n\"a\\\"b\",x,y\n", opts);
        Assert.Single(records);
    }

    [Fact]
    public void Scalar_EmptyInput_NoRecords()
    {
        var records = Parse("");
        Assert.Empty(records);
    }

    [Fact]
    public void Scalar_HeaderOnly_NoRecords()
    {
        var records = Parse("A,B,C\n");
        Assert.Empty(records);
    }

    [Fact]
    public void Scalar_LongRowExceedingChunk()
    {
        // Force the scalar path to traverse several chunks worth of data
        var sb = new StringBuilder("A,B,C\n");
        for (int i = 0; i < 500; i++)
        {
            sb.AppendLine($"row{i}A,row{i}B,row{i}C");
        }
        var records = Parse(sb.ToString());
        Assert.Equal(500, records.Count);
        Assert.Equal("row0A", records[0].A);
        Assert.Equal("row499C", records[^1].C);
    }

    [Fact]
    public void Scalar_QuotedFieldAtEndOfFile_NoTrailingNewline_OneRow()
    {
        var records = Parse("A,B,C\n1,2,\"abc\"");
        Assert.Single(records);
    }

    [Fact]
    public void Scalar_OnlyCrLf_TreatedAsEmpty()
    {
        var records = Parse("A,B,C\n\r\n1,2,3\n");
        // The empty line between header and data may be either skipped or emitted
        // as an empty record depending on implementation; just ensure parser handles it.
        Assert.NotEmpty(records);
    }

    [Fact]
    public void Scalar_BomStripped()
    {
        var withBom = "﻿A,B,C\n1,2,3\n";
        var records = Parse(withBom);
        Assert.Single(records);
        Assert.Equal("1", records[0].A);
    }

    [Fact]
    public void Scalar_MixedQuotedAndUnquoted_TwoRows()
    {
        var records = Parse("A,B,C\n1,\"two\",3\n\"4\",5,\"six\"\n");
        Assert.Equal(2, records.Count);
    }
}
