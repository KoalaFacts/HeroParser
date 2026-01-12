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
}
