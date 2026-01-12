using System.Data;
using System.Text;
using HeroParser.FixedWidths;
using HeroParser.FixedWidths.Reading.Data;
using Xunit;

namespace HeroParser.Tests;

public class FixedWidthDataReaderTests
{
    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void DataReader_WithHeader_ReadsValues()
    {
        var data =
            "Name  AgeCity\r\n" +
            "Alice 30 NYC \r\n" +
            "Bob   25 LA  \r\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(data));

        var options = new FixedWidthDataReaderOptions
        {
            HasHeaderRow = true,
            Columns =
            [
                new FixedWidthDataReaderColumn { Start = 0, Length = 6 },
                new FixedWidthDataReaderColumn { Start = 6, Length = 3 },
                new FixedWidthDataReaderColumn { Start = 9, Length = 4 }
            ]
        };

        using var reader = FixedWidth.CreateDataReader(stream, readerOptions: options);

        Assert.Equal(3, reader.FieldCount);
        Assert.Equal("Name", reader.GetName(0));
        Assert.Equal(0, reader.GetOrdinal("Name"));
        Assert.Equal(0, reader.GetOrdinal("name"));

        Assert.True(reader.Read());
        Assert.Equal("Alice", reader.GetValue(0));
        Assert.Equal("30", reader.GetValue(1));
        Assert.Equal("NYC", reader.GetValue(2));

        Assert.True(reader.Read());
        Assert.Equal("Bob", reader.GetValue(0));
        Assert.Equal("25", reader.GetValue(1));
        Assert.Equal("LA", reader.GetValue(2));

        Assert.False(reader.Read());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void DataReader_NoHeader_UsesExplicitNames()
    {
        var data = "ABCDEF\r\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(data));

        var options = new FixedWidthDataReaderOptions
        {
            Columns =
            [
                new FixedWidthDataReaderColumn { Start = 0, Length = 3, Name = "A" },
                new FixedWidthDataReaderColumn { Start = 3, Length = 3, Name = "B" }
            ]
        };

        using var reader = FixedWidth.CreateDataReader(stream, readerOptions: options);

        Assert.Equal(2, reader.FieldCount);
        Assert.Equal("A", reader.GetName(0));
        Assert.Equal("B", reader.GetName(1));

        Assert.True(reader.Read());
        Assert.Equal("ABC", reader.GetValue(0));
        Assert.Equal("DEF", reader.GetValue(1));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void DataReader_NullValues_ReturnsDbNull()
    {
        var data = "JohnNULL\r\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(data));

        var options = new FixedWidthDataReaderOptions
        {
            NullValues = ["NULL"],
            Columns =
            [
                new FixedWidthDataReaderColumn { Start = 0, Length = 4, Name = "Name" },
                new FixedWidthDataReaderColumn { Start = 4, Length = 4, Name = "Code" }
            ]
        };

        using var reader = FixedWidth.CreateDataReader(stream, readerOptions: options);

        Assert.True(reader.Read());
        Assert.False(reader.IsDBNull(0));
        Assert.True(reader.IsDBNull(1));
        Assert.Equal(DBNull.Value, reader.GetValue(1));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void DataReader_AllowMissingColumns_ReturnsDbNull()
    {
        var data = "ABC\r\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(data));

        var options = new FixedWidthDataReaderOptions
        {
            AllowMissingColumns = true,
            Columns =
            [
                new FixedWidthDataReaderColumn { Start = 0, Length = 3, Name = "A" },
                new FixedWidthDataReaderColumn { Start = 3, Length = 3, Name = "B" }
            ]
        };

        using var reader = FixedWidth.CreateDataReader(stream, readerOptions: options);

        Assert.True(reader.Read());
        Assert.Equal("ABC", reader.GetValue(0));
        Assert.True(reader.IsDBNull(1));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void DataReader_MissingColumns_ThrowsWhenNotAllowed()
    {
        var data = "ABC\r\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(data));

        var options = new FixedWidthDataReaderOptions
        {
            AllowMissingColumns = false,
            Columns =
            [
                new FixedWidthDataReaderColumn { Start = 0, Length = 3, Name = "A" },
                new FixedWidthDataReaderColumn { Start = 3, Length = 3, Name = "B" }
            ]
        };

        using var reader = FixedWidth.CreateDataReader(stream, readerOptions: options);

        Assert.Throws<FixedWidthException>(() => reader.Read());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void DataReader_DataTableLoad_Works()
    {
        var data =
            "Name  AgeCity\r\n" +
            "Alice 30 NYC \r\n" +
            "Bob   25 LA  \r\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(data));

        var options = new FixedWidthDataReaderOptions
        {
            HasHeaderRow = true,
            Columns =
            [
                new FixedWidthDataReaderColumn { Start = 0, Length = 6 },
                new FixedWidthDataReaderColumn { Start = 6, Length = 3 },
                new FixedWidthDataReaderColumn { Start = 9, Length = 4 }
            ]
        };

        using var reader = FixedWidth.CreateDataReader(stream, readerOptions: options);
        var table = new DataTable();
        table.Load(reader);

        Assert.Equal(2, table.Rows.Count);
        Assert.Equal("Name", table.Columns[0].ColumnName);
        Assert.Equal("Age", table.Columns[1].ColumnName);
        Assert.Equal("Alice", table.Rows[0][0]);
        Assert.Equal("30", table.Rows[0][1]);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void DataReader_TypedGetters_ParseValues()
    {
        var guid = Guid.NewGuid();
        var values = new[]
        {
            "1",
            "2",
            "3",
            "4",
            "1.5",
            "2.5",
            "3.5",
            "true",
            "X",
            "2024-01-02",
            guid.ToString()
        };
        var lengths = new[] { 2, 2, 2, 2, 4, 4, 4, 5, 2, 10, 36 };

        var builder = new StringBuilder();
        for (int i = 0; i < values.Length; i++)
        {
            builder.Append(values[i].PadRight(lengths[i], ' '));
        }
        builder.Append("\r\n");

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(builder.ToString()));

        var options = new FixedWidthDataReaderOptions
        {
            Columns = FixedWidthDataReaderColumns.FromLengths(lengths)
        };

        using var reader = FixedWidth.CreateDataReader(stream, readerOptions: options);

        Assert.True(reader.Read());
        Assert.Equal(1, reader.GetInt32(0));
        Assert.Equal((byte)2, reader.GetByte(1));
        Assert.Equal((short)3, reader.GetInt16(2));
        Assert.Equal(4L, reader.GetInt64(3));
        Assert.Equal(1.5f, reader.GetFloat(4), 3);
        Assert.Equal(2.5d, reader.GetDouble(5), 3);
        Assert.Equal(3.5m, reader.GetDecimal(6));
        Assert.True(reader.GetBoolean(7));
        Assert.Equal('X', reader.GetChar(8));
        Assert.Equal(new DateTime(2024, 1, 2), reader.GetDateTime(9));
        Assert.Equal(guid, reader.GetGuid(10));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void DataReader_GetBytesAndChars_ReadsSlices()
    {
        var data = "Hello\r\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(data));

        var options = new FixedWidthDataReaderOptions
        {
            Columns =
            [
                new FixedWidthDataReaderColumn { Start = 0, Length = 5, Name = "Value" }
            ]
        };

        using var reader = FixedWidth.CreateDataReader(stream, readerOptions: options);

        Assert.True(reader.Read());
        Assert.Equal(5, reader.GetBytes(0, 0, null, 0, 0));

        var bytes = new byte[3];
        var bytesRead = reader.GetBytes(0, 1, bytes, 0, bytes.Length);
        Assert.Equal(3, bytesRead);
        Assert.Equal((byte)'e', bytes[0]);
        Assert.Equal((byte)'l', bytes[1]);
        Assert.Equal((byte)'l', bytes[2]);

        var chars = new char[2];
        var charsRead = reader.GetChars(0, 2, chars, 0, chars.Length);
        Assert.Equal(2, charsRead);
        Assert.Equal("ll", new string(chars));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void DataReader_RecordLength_Works()
    {
        var data = "Alice00030Bob  00025";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(data));

        var parserOptions = new FixedWidthParserOptions
        {
            RecordLength = 10
        };

        var options = new FixedWidthDataReaderOptions
        {
            Columns =
            [
                new FixedWidthDataReaderColumn { Start = 0, Length = 5, Name = "Name" },
                new FixedWidthDataReaderColumn { Start = 5, Length = 5, Name = "Age" }
            ]
        };

        using var reader = FixedWidth.CreateDataReader(stream, parserOptions, options);

        Assert.True(reader.Read());
        Assert.Equal("Alice", reader.GetValue(0));
        Assert.Equal("00030", reader.GetValue(1));

        Assert.True(reader.Read());
        Assert.Equal("Bob", reader.GetValue(0));
        Assert.Equal("00025", reader.GetValue(1));

        Assert.False(reader.Read());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void DataReader_AllowShortRows_ReturnsEmptyStringForMissing()
    {
        var data = "AB\r\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(data));

        var parserOptions = new FixedWidthParserOptions
        {
            AllowShortRows = true
        };

        var options = new FixedWidthDataReaderOptions
        {
            Columns =
            [
                new FixedWidthDataReaderColumn { Start = 0, Length = 2, Name = "A" },
                new FixedWidthDataReaderColumn { Start = 4, Length = 2, Name = "B" }
            ]
        };

        using var reader = FixedWidth.CreateDataReader(stream, parserOptions, options);

        Assert.True(reader.Read());
        Assert.Equal("AB", reader.GetValue(0));
        Assert.Equal(string.Empty, reader.GetValue(1));
        Assert.False(reader.IsDBNull(1));
    }
}
