using HeroParser.SeparatedValues;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Reading.Rows;
using System.Text;
using Xunit;

namespace HeroParser.Tests;

public class Rfc4180TestsUtf8
{
    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public void QuotedField_WithDelimiter()
    {
        var csv = "a,\"b,c\",d";
        var reader = CreateReader(csv);

        Assert.True(reader.MoveNext());
        var row = reader.Current;

        Assert.Equal(3, row.ColumnCount);
        Assert.Equal("a", row[0].ToString());
        Assert.Equal("\"b,c\"", row[1].ToString());
        Assert.Equal("d", row[2].ToString());

        // Test unquoting
        Assert.Equal("b,c", row[1].UnquoteToString());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public void QuotedField_WithEscapedQuotes()
    {
        var csv = "a,\"b\"\"c\",d";
        var reader = CreateReader(csv);

        Assert.True(reader.MoveNext());
        var row = reader.Current;

        Assert.Equal(3, row.ColumnCount);
        Assert.Equal("a", row[0].ToString());
        Assert.Equal("\"b\"\"c\"", row[1].ToString());
        Assert.Equal("d", row[2].ToString());

        // Test unquoting with escaped quotes
        Assert.Equal("b\"c", row[1].UnquoteToString());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public void QuotedField_Empty()
    {
        var csv = "a,\"\",c";
        var reader = CreateReader(csv);

        Assert.True(reader.MoveNext());
        var row = reader.Current;

        Assert.Equal(3, row.ColumnCount);
        Assert.Equal("a", row[0].ToString());
        Assert.Equal("\"\"", row[1].ToString());
        Assert.Equal("c", row[2].ToString());

        // Test unquoting empty quoted field
        Assert.Equal("", row[1].UnquoteToString());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public void QuotedField_OnlyQuotes()
    {
        var csv = "\"\"\"\"";
        var reader = CreateReader(csv);

        Assert.True(reader.MoveNext());
        var row = reader.Current;

        Assert.Equal(1, row.ColumnCount);
        Assert.Equal("\"\"\"\"", row[0].ToString());

        // Test unquoting - should unescape to single quote
        Assert.Equal("\"", row[0].UnquoteToString());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public void MixedQuotedAndUnquoted()
    {
        var csv = "unquoted,\"quoted value\",123,\"another, quoted\"";
        var reader = CreateReader(csv);

        Assert.True(reader.MoveNext());
        var row = reader.Current;

        Assert.Equal(4, row.ColumnCount);
        Assert.Equal("unquoted", row[0].ToString());
        Assert.Equal("\"quoted value\"", row[1].ToString());
        Assert.Equal("123", row[2].ToString());
        Assert.Equal("\"another, quoted\"", row[3].ToString());

        // Test unquoting
        Assert.Equal("unquoted", row[0].UnquoteToString());
        Assert.Equal("quoted value", row[1].UnquoteToString());
        Assert.Equal("123", row[2].UnquoteToString());
        Assert.Equal("another, quoted", row[3].UnquoteToString());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public void ComplexEscaping()
    {
        var csv = "\"a\"\"b\"\"c\",\"x,y,z\",\"\"\"quoted\"\"\"";
        var reader = CreateReader(csv);

        Assert.True(reader.MoveNext());
        var row = reader.Current;

        Assert.Equal(3, row.ColumnCount);

        // Test unescaping
        Assert.Equal("a\"b\"c", row[0].UnquoteToString());
        Assert.Equal("x,y,z", row[1].UnquoteToString());
        Assert.Equal("\"quoted\"", row[2].UnquoteToString());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public void Rfc4180Example()
    {
        // Example from RFC 4180
        var csv = "field1,\"field2\",field3\n" +
                  "aaa,\"b,bb\",ccc\n" +
                  "zzz,\"y\"\"yy\",xxx";

        var reader = CreateReader(csv);

        // Row 1
        Assert.True(reader.MoveNext());
        var row1 = reader.Current;
        Assert.Equal(3, row1.ColumnCount);
        Assert.Equal("field1", row1[0].ToString());
        Assert.Equal("field2", row1[1].UnquoteToString());
        Assert.Equal("field3", row1[2].ToString());

        // Row 2
        Assert.True(reader.MoveNext());
        var row2 = reader.Current;
        Assert.Equal(3, row2.ColumnCount);
        Assert.Equal("aaa", row2[0].ToString());
        Assert.Equal("b,bb", row2[1].UnquoteToString());
        Assert.Equal("ccc", row2[2].ToString());

        // Row 3
        Assert.True(reader.MoveNext());
        var row3 = reader.Current;
        Assert.Equal(3, row3.ColumnCount);
        Assert.Equal("zzz", row3[0].ToString());
        Assert.Equal("y\"yy", row3[1].UnquoteToString());
        Assert.Equal("xxx", row3[2].ToString());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public void CustomQuoteCharacter()
    {
        var options = new CsvReadOptions
        {
            Delimiter = ',',
            Quote = '\''
        };

        var csv = "a,'b,c',d";
        var reader = CreateReader(csv, options);

        Assert.True(reader.MoveNext());
        var row = reader.Current;

        Assert.Equal(3, row.ColumnCount);
        Assert.Equal("a", row[0].ToString());
        Assert.Equal("'b,c'", row[1].ToString());
        Assert.Equal("d", row[2].ToString());

        // Test unquoting with custom quote char
        Assert.Equal("b,c", row[1].UnquoteToString((byte)'\''));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public void UnquoteMethod_PreservesOriginalWhenNotQuoted()
    {
        var csv = "abc,def,ghi";
        var reader = CreateReader(csv);

        Assert.True(reader.MoveNext());
        var row = reader.Current;

        // Unquote should return same value when not quoted
        Assert.Equal("abc", Encoding.UTF8.GetString(row[0].Unquote()));
        Assert.Equal("def", Encoding.UTF8.GetString(row[1].Unquote()));
        Assert.Equal("ghi", Encoding.UTF8.GetString(row[2].Unquote()));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public void EmptyFields_AreSupported()
    {
        // RFC 4180: Empty fields should be allowed
        var csv = "a,,c\n,b,\n,,";
        var rows = new List<string[]>();

        foreach (var row in CreateReader(csv))
        {
            rows.Add(row.ToStringArray());
        }

        Assert.Equal(3, rows.Count);
        Assert.Equal(new[] { "a", "", "c" }, rows[0]);
        Assert.Equal(new[] { "", "b", "" }, rows[1]);
        Assert.Equal(new[] { "", "", "" }, rows[2]);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public void Spaces_ArePartOfField()
    {
        // RFC 4180: Spaces are considered part of a field and should not be ignored
        var csv = " a , b ,c ";
        var reader = CreateReader(csv);

        Assert.True(reader.MoveNext());
        var row = reader.Current;

        Assert.Equal(3, row.ColumnCount);
        Assert.Equal(" a ", row[0].ToString());
        Assert.Equal(" b ", row[1].ToString());
        Assert.Equal("c ", row[2].ToString());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public void NewlinesInQuotes_DefaultThrows()
    {
        var csv = "a,\"b\nc\",d";
        using var reader = CreateReader(csv);

        CsvException? ex = null;
        try
        {
            reader.MoveNext();
        }
        catch (CsvException e)
        {
            ex = e;
        }

        Assert.NotNull(ex);
        Assert.Equal(CsvErrorCode.ParseError, ex!.ErrorCode);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public void NewlinesInQuotes_AllowedWhenOptedIn()
    {
        var options = new CsvReadOptions { AllowNewlinesInsideQuotes = true };
        var csv = "a,\"b\nc\",d\n1,2,3";

        var reader = CreateReader(csv, options);

        Assert.True(reader.MoveNext());
        var row1 = reader.Current;
        Assert.Equal(3, row1.ColumnCount);
        Assert.Equal("\"b\nc\"", row1[1].ToString());
        Assert.Equal("b\nc", row1[1].UnquoteToString());

        Assert.True(reader.MoveNext());
        var row2 = reader.Current;
        Assert.Equal(new[] { "1", "2", "3" }, row2.ToStringArray());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public void DisableQuotes_TreatsQuoteCharacterAsData()
    {
        var options = new CsvReadOptions { EnableQuotedFields = false };
        var csv = "a,\"b,c\",d";

        var reader = CreateReader(csv, options);
        Assert.True(reader.MoveNext());

        var row = reader.Current;
        Assert.Equal(4, row.ColumnCount);
        Assert.Equal(new[] { "a", "\"b", "c\"", "d" }, row.ToStringArray());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void DisablingQuotes_CannotEnableNewlinesInQuotes()
    {
        var options = new CsvReadOptions
        {
            EnableQuotedFields = false,
            AllowNewlinesInsideQuotes = true
        };

        var ex = Assert.Throws<CsvException>(() => CreateReader("a", options));
        Assert.Equal(CsvErrorCode.InvalidOptions, ex.ErrorCode);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public void Utf8_LargeDataWithQuotes_ParsesCorrectly()
    {
        // Replicates benchmark scenario: 10k rows, 25 columns, 50% quoted fields
        const int rows = 10_000;
        const int columns = 25;

        var sb = new StringBuilder();
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                if (c > 0) sb.Append(',');

                string value = $"value_{r}_{c}";
                // Quote 50% of fields
                if ((r * columns + c) % 2 == 0)
                    sb.Append('"').Append(value).Append('"');
                else
                    sb.Append(value);
            }
            sb.AppendLine();
        }

        var csv = sb.ToString();

        var options = new CsvReadOptions
        {
            MaxColumnCount = 1_000,
            MaxRowCount = 1_000_000,
            EnableQuotedFields = true,
            AllowNewlinesInsideQuotes = true
        };

        var reader = CreateReader(csv, options);

        int totalRows = 0;
        int totalColumns = 0;
        foreach (var row in reader)
        {
            totalRows++;
            totalColumns += row.ColumnCount;

            // Spot check some values
            if (totalRows == 1)
            {
                Assert.Equal(columns, row.ColumnCount);
                Assert.Equal("value_0_0", row[0].UnquoteToString()); // quoted
                Assert.Equal("value_0_1", row[1].ToString());        // unquoted
            }
        }

        Assert.Equal(rows, totalRows);
        Assert.Equal(rows * columns, totalColumns);
    }

    private static CsvRowReader<byte> CreateReader(string csv, CsvReadOptions? options = null)
    {
        var bytes = Encoding.UTF8.GetBytes(csv);
        return Csv.ReadFromByteSpan(bytes, options);
    }
}

