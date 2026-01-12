using System.Data;
using System.Text;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Reading.Data;
using Xunit;

namespace HeroParser.Tests;

public class CsvDataReaderTests
{
    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void DataReader_WithHeader_ReadsValues()
    {
        var csv = "Name,Age,City\r\nAlice,30,NYC\r\nBob,25,LA\r\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        using var reader = Csv.CreateDataReader(stream);

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
    public void DataReader_NoHeader_UsesProvidedColumnNames()
    {
        var csv = "1,2\r\n3,4\r\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        var options = new CsvDataReaderOptions
        {
            HasHeaderRow = false,
            ColumnNames = ["A", "B"]
        };

        using var reader = Csv.CreateDataReader(stream, readerOptions: options);

        Assert.Equal(2, reader.FieldCount);
        Assert.Equal("A", reader.GetName(0));
        Assert.Equal("B", reader.GetName(1));

        Assert.True(reader.Read());
        Assert.Equal("1", reader.GetValue(0));
        Assert.Equal("2", reader.GetValue(1));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void DataReader_NullValues_ReturnsDbNull()
    {
        var csv = "Name,Age\r\nAlice,NULL\r\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        var options = new CsvDataReaderOptions
        {
            NullValues = ["NULL"]
        };

        using var reader = Csv.CreateDataReader(stream, readerOptions: options);

        Assert.True(reader.Read());
        Assert.False(reader.IsDBNull(0));
        Assert.True(reader.IsDBNull(1));
        Assert.Equal(DBNull.Value, reader.GetValue(1));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void DataReader_AllowMissingColumns_ReturnsDbNull()
    {
        var csv = "1\r\n2\r\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        var options = new CsvDataReaderOptions
        {
            HasHeaderRow = false,
            ColumnNames = ["A", "B"],
            AllowMissingColumns = true
        };

        using var reader = Csv.CreateDataReader(stream, readerOptions: options);

        Assert.True(reader.Read());
        Assert.Equal("1", reader.GetValue(0));
        Assert.True(reader.IsDBNull(1));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void DataReader_MissingColumns_ThrowsWhenNotAllowed()
    {
        var csv = "1\r\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        var options = new CsvDataReaderOptions
        {
            HasHeaderRow = false,
            ColumnNames = ["A", "B"],
            AllowMissingColumns = false
        };

        using var reader = Csv.CreateDataReader(stream, readerOptions: options);

        Assert.Throws<CsvException>(() => reader.Read());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void DataReader_DataTableLoad_Works()
    {
        var csv = "Name,Age\r\nAlice,30\r\nBob,25\r\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        using var reader = Csv.CreateDataReader(stream);
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
    public void DataReader_CaseSensitiveHeaders_RespectsCase()
    {
        var csv = "Name,Age\r\nAlice,30\r\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        var options = new CsvDataReaderOptions
        {
            CaseSensitiveHeaders = true
        };

        using var reader = Csv.CreateDataReader(stream, readerOptions: options);

        Assert.Equal(0, reader.GetOrdinal("Name"));
        Assert.Throws<IndexOutOfRangeException>(() => reader.GetOrdinal("name"));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void DataReader_SkipRows_SkipsBeforeHeader()
    {
        var csv = "Skip,Me\r\nName,Age\r\nAlice,30\r\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        var options = new CsvDataReaderOptions
        {
            SkipRows = 1
        };

        using var reader = Csv.CreateDataReader(stream, readerOptions: options);

        Assert.Equal(2, reader.FieldCount);
        Assert.Equal("Name", reader.GetName(0));
        Assert.True(reader.Read());
        Assert.Equal("Alice", reader.GetValue(0));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void DataReader_TypedGetters_ParseValues()
    {
        var guid = Guid.NewGuid();
        var csv = $"Int,Byte,Short,Long,Float,Double,Decimal,Bool,Char,Date,Guid\r\n" +
                  $"1,2,3,4,1.5,2.5,3.5,true,X,2024-01-02,{guid}\r\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        using var reader = Csv.CreateDataReader(stream);

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
        var csv = "Value\r\nHello\r\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        using var reader = Csv.CreateDataReader(stream);

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
}
