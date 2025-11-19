using Xunit;

namespace HeroParser.Tests;

public class BasicTests
{
    [Fact]
    public void SimpleCsv_ParsesCorrectly()
    {
        var csv = "a,b,c\n1,2,3";
        var reader = Csv.ReadFromText(csv);

        Assert.True(reader.MoveNext());
        var row1 = reader.Current;
        Assert.Equal(3, row1.ColumnCount);
        Assert.Equal("a", row1[0].ToString());
        Assert.Equal("b", row1[1].ToString());
        Assert.Equal("c", row1[2].ToString());

        Assert.True(reader.MoveNext());
        var row2 = reader.Current;
        Assert.Equal(3, row2.ColumnCount);
        Assert.Equal("1", row2[0].ToString());
        Assert.Equal("2", row2[1].ToString());
        Assert.Equal("3", row2[2].ToString());

        Assert.False(reader.MoveNext());
    }

    [Fact]
    public void ForeachLoop_Works()
    {
        var csv = "a,b\n1,2\n3,4";
        int rowCount = 0;

        foreach (var row in Csv.ReadFromText(csv))
        {
            rowCount++;
            Assert.Equal(2, row.ColumnCount);
        }

        Assert.Equal(3, rowCount);
    }

    [Fact]
    public void EmptyCsv_ReturnsNoRows()
    {
        var csv = "";
        var reader = Csv.ReadFromText(csv);
        Assert.False(reader.MoveNext());
    }

    [Fact]
    public void SingleColumn_ParsesCorrectly()
    {
        var csv = "a\nb\nc";
        var reader = Csv.ReadFromText(csv);

        Assert.True(reader.MoveNext());
        Assert.Equal(1, reader.Current.ColumnCount);
        Assert.Equal("a", reader.Current[0].ToString());

        Assert.True(reader.MoveNext());
        Assert.Equal("b", reader.Current[0].ToString());

        Assert.True(reader.MoveNext());
        Assert.Equal("c", reader.Current[0].ToString());

        Assert.False(reader.MoveNext());
    }

    [Fact]
    public void EmptyFields_ParsedAsEmpty()
    {
        var csv = "a,,c\n,b,\n,,";
        var reader = Csv.ReadFromText(csv);

        Assert.True(reader.MoveNext());
        var row1 = reader.Current;
        Assert.Equal("a", row1[0].ToString());
        Assert.Equal("", row1[1].ToString());
        Assert.Equal("c", row1[2].ToString());

        Assert.True(reader.MoveNext());
        var row2 = reader.Current;
        Assert.Equal("", row2[0].ToString());
        Assert.Equal("b", row2[1].ToString());
        Assert.Equal("", row2[2].ToString());

        Assert.True(reader.MoveNext());
        var row3 = reader.Current;
        Assert.Equal("", row3[0].ToString());
        Assert.Equal("", row3[1].ToString());
        Assert.Equal("", row3[2].ToString());
    }

    [Fact]
    public void CustomDelimiter_Tab()
    {
        var csv = "a\tb\tc\n1\t2\t3";
        var options = new CsvParserOptions { Delimiter = '\t' };
        var reader = Csv.ReadFromText(csv, options);

        Assert.True(reader.MoveNext());
        var row = reader.Current;
        Assert.Equal(3, row.ColumnCount);
        Assert.Equal("a", row[0].ToString());
        Assert.Equal("b", row[1].ToString());
        Assert.Equal("c", row[2].ToString());
    }

    [Fact]
    public void CustomDelimiter_Pipe()
    {
        var csv = "a|b|c";
        var options = new CsvParserOptions { Delimiter = '|' };
        var reader = Csv.ReadFromText(csv, options);

        Assert.True(reader.MoveNext());
        Assert.Equal(3, reader.Current.ColumnCount);
    }

    [Fact]
    public void LineEndings_CRLF()
    {
        var csv = "a,b\r\n1,2\r\n3,4";
        var reader = Csv.ReadFromText(csv);

        int count = 0;
        foreach (var row in reader)
        {
            count++;
            Assert.Equal(2, row.ColumnCount);
        }
        Assert.Equal(3, count);
    }

    [Fact]
    public void LineEndings_LF()
    {
        var csv = "a,b\n1,2\n3,4";
        var reader = Csv.ReadFromText(csv);

        int count = 0;
        foreach (var row in reader)
        {
            count++;
        }
        Assert.Equal(3, count);
    }

    [Fact]
    public void LineEndings_CR()
    {
        var csv = "a,b\r1,2\r3,4";
        var reader = Csv.ReadFromText(csv);

        int count = 0;
        foreach (var row in reader)
        {
            count++;
        }
        Assert.Equal(3, count);
    }

    [Fact]
    public void EmptyLines_AreSkipped()
    {
        var csv = "a,b\n\n1,2\n\n\n3,4\n\n";
        var reader = Csv.ReadFromText(csv);

        int count = 0;
        foreach (var row in reader)
        {
            count++;
        }
        Assert.Equal(3, count);
    }

    [Fact]
    public void TypeParsing_Int()
    {
        var csv = "123,456";
        var reader = Csv.ReadFromText(csv);
        reader.MoveNext();
        var row = reader.Current;

        Assert.True(row[0].TryParseInt32(out int val1));
        Assert.Equal(123, val1);

        Assert.True(row[1].TryParseInt32(out int val2));
        Assert.Equal(456, val2);
    }

    [Fact]
    public void TypeParsing_Double()
    {
        var csv = "3.14,2.71";
        var reader = Csv.ReadFromText(csv);
        reader.MoveNext();
        var row = reader.Current;

        Assert.True(row[0].TryParseDouble(out double val1));
        Assert.Equal(3.14, val1, precision: 2);

        Assert.True(row[1].TryParseDouble(out double val2));
        Assert.Equal(2.71, val2, precision: 2);
    }

    [Fact]
    public void TooManyColumns_ThrowsException()
    {
        var csv = "a,b,c,d,e";
        var options = new CsvParserOptions { MaxColumns = 3 };

        CsvException? ex = null;
        try
        {
            var reader = Csv.ReadFromText(csv, options);
            reader.MoveNext();
            // Access columns to trigger parsing
            var count = reader.Current.ColumnCount;
        }
        catch (CsvException e)
        {
            ex = e;
        }

        Assert.NotNull(ex);
        Assert.Equal(CsvErrorCode.TooManyColumns, ex.ErrorCode);
    }

    [Fact]
    public void TooManyRows_ThrowsException()
    {
        var csv = "a\nb\nc\nd";
        var options = new CsvParserOptions { MaxRows = 2 };

        CsvException? ex = null;
        try
        {
            var reader = Csv.ReadFromText(csv, options);
            Assert.True(reader.MoveNext()); // Row 1
            Assert.True(reader.MoveNext()); // Row 2
            reader.MoveNext(); // Row 3 - should throw
        }
        catch (CsvException e)
        {
            ex = e;
        }

        Assert.NotNull(ex);
        Assert.Equal(CsvErrorCode.TooManyRows, ex.ErrorCode);
    }

    [Fact]
    public void InvalidDelimiter_ThrowsException()
    {
        var options = new CsvParserOptions { Delimiter = '€' }; // Non-ASCII
        var ex = Assert.Throws<CsvException>(() => Csv.ReadFromText("test", options));
        Assert.Equal(CsvErrorCode.InvalidDelimiter, ex.ErrorCode);
    }

    [Fact]
    public void NullCsv_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => Csv.ReadFromText(null!));
    }

    [Fact]
    public void ToStringArray_Works()
    {
        var csv = "a,b,c";
        var reader = Csv.ReadFromText(csv);
        reader.MoveNext();

        var array = reader.Current.ToStringArray();
        Assert.Equal(new[] { "a", "b", "c" }, array);
    }
}
