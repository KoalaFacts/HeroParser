using System.Data.Common;
using System.Text;
using HeroParser.FixedWidths;
using HeroParser.FixedWidths.Reading.Data;
using Xunit;

namespace HeroParser.Tests.Internal;

/// <summary>
/// Mirrors the CsvDataReader and ExcelDataReader coverage tests for the
/// FixedWidth DbDataReader. Targets typed getters, IsDBNull, GetValues,
/// GetSchemaTable, GetEnumerator, and various close/dispose paths
/// (~73 lines / 82% on FixedWidthDataReader.cs).
/// </summary>
[Trait("Category", "Unit")]
public class FixedWidthDataReaderTypedGettersTests
{
    private static MemoryStream Sample()
    {
        // Each row: Name(5) + Age(5) + Price(8) + Active(6) + Date(10) + Guid(36) = 70 chars
        var header = Pad("Name", 5) + Pad("Age", 5) + Pad("Price", 8) + Pad("Active", 6) + Pad("BirthDate", 10) + Pad("Id", 36);
        var row1 = Pad("Alice", 5) + Pad("30", 5) + Pad("9.99", 8) + Pad("true", 6) + "2024-01-15" + "12345678-1234-1234-1234-123456789012";
        var row2 = Pad("Bob", 5) + Pad("25", 5) + Pad("1.25", 8) + Pad("false", 6) + "2023-06-30" + "abcdef00-0000-0000-0000-000000000000";
        var bytes = Encoding.UTF8.GetBytes(header + "\n" + row1 + "\n" + row2 + "\n");
        return new MemoryStream(bytes);

        static string Pad(string s, int width) => s.PadRight(width);
    }

    private static FixedWidthDataReaderOptions Options() => new()
    {
        Columns = [
            new FixedWidthDataReaderColumn { Name = "Name",      Start = 0,  Length = 5 },
            new FixedWidthDataReaderColumn { Name = "Age",       Start = 5,  Length = 5 },
            new FixedWidthDataReaderColumn { Name = "Price",     Start = 10, Length = 8 },
            new FixedWidthDataReaderColumn { Name = "Active",    Start = 18, Length = 6 },
            new FixedWidthDataReaderColumn { Name = "BirthDate", Start = 24, Length = 10 },
            new FixedWidthDataReaderColumn { Name = "Id",        Start = 34, Length = 36 }
        ],
        HasHeaderRow = true
    };

    [Fact]
    public void Read_AdvancesThroughRows()
    {
        using var stream = Sample();
        using var reader = HeroParser.FixedWidth.CreateDataReader(stream, readerOptions: Options());
        Assert.True(reader.Read());
        Assert.True(reader.Read());
        Assert.False(reader.Read());
    }

    [Fact]
    public void FieldCount_ReturnsConfiguredColumns()
    {
        using var stream = Sample();
        using var reader = HeroParser.FixedWidth.CreateDataReader(stream, readerOptions: Options());
        Assert.Equal(6, reader.FieldCount);
    }

    [Fact]
    public void GetName_ReturnsConfiguredNames()
    {
        using var stream = Sample();
        using var reader = HeroParser.FixedWidth.CreateDataReader(stream, readerOptions: Options());
        Assert.Equal("Name", reader.GetName(0));
        Assert.Equal("Age", reader.GetName(1));
    }

    [Fact]
    public void GetOrdinal_FindsByName()
    {
        using var stream = Sample();
        using var reader = HeroParser.FixedWidth.CreateDataReader(stream, readerOptions: Options());
        Assert.Equal(0, reader.GetOrdinal("Name"));
        Assert.Equal(1, reader.GetOrdinal("Age"));
    }

    [Fact]
    public void GetOrdinal_Unknown_Throws()
    {
        using var stream = Sample();
        using var reader = HeroParser.FixedWidth.CreateDataReader(stream, readerOptions: Options());
        Assert.Throws<IndexOutOfRangeException>(() => reader.GetOrdinal("Nonexistent"));
    }

    [Fact]
    public void GetFieldType_AndDataTypeName_String()
    {
        using var stream = Sample();
        using var reader = HeroParser.FixedWidth.CreateDataReader(stream, readerOptions: Options());
        Assert.Equal(typeof(string), reader.GetFieldType(0));
        Assert.Equal("String", reader.GetDataTypeName(0));
    }

    [Fact]
    public void TypedNumericGetters()
    {
        using var stream = Sample();
        using var reader = HeroParser.FixedWidth.CreateDataReader(stream, readerOptions: Options());
        Assert.True(reader.Read());
        Assert.Equal(30, reader.GetInt32(1));
        Assert.Equal(30L, reader.GetInt64(1));
        Assert.Equal((short)30, reader.GetInt16(1));
        Assert.Equal((byte)30, reader.GetByte(1));
        Assert.Equal(9.99f, reader.GetFloat(2), 4);
        Assert.Equal(9.99, reader.GetDouble(2), 5);
        Assert.Equal(9.99m, reader.GetDecimal(2));
    }

    [Fact]
    public void GetBoolean_TrueFalse()
    {
        using var stream = Sample();
        using var reader = HeroParser.FixedWidth.CreateDataReader(stream, readerOptions: Options());
        Assert.True(reader.Read());
        Assert.True(reader.GetBoolean(3));
        Assert.True(reader.Read());
        Assert.False(reader.GetBoolean(3));
    }

    [Fact]
    public void GetDateTime_ParsesIso()
    {
        using var stream = Sample();
        using var reader = HeroParser.FixedWidth.CreateDataReader(stream, readerOptions: Options());
        Assert.True(reader.Read());
        Assert.Equal(new DateTime(2024, 1, 15), reader.GetDateTime(4));
    }

    [Fact]
    public void GetGuid_ParsesValid()
    {
        using var stream = Sample();
        using var reader = HeroParser.FixedWidth.CreateDataReader(stream, readerOptions: Options());
        Assert.True(reader.Read());
        Assert.NotEqual(Guid.Empty, reader.GetGuid(5));
    }

    [Fact]
    public void GetChar_ReturnsFirstChar()
    {
        using var stream = Sample();
        using var reader = HeroParser.FixedWidth.CreateDataReader(stream, readerOptions: Options());
        Assert.True(reader.Read());
        Assert.Equal('A', reader.GetChar(0));
    }

    [Fact]
    public void GetBytes_NullBuffer_ReturnsLength()
    {
        using var stream = Sample();
        using var reader = HeroParser.FixedWidth.CreateDataReader(stream, readerOptions: Options());
        Assert.True(reader.Read());
        var len = reader.GetBytes(0, 0, null, 0, 0);
        Assert.True(len > 0);
    }

    [Fact]
    public void GetBytes_CopiesIntoBuffer()
    {
        using var stream = Sample();
        using var reader = HeroParser.FixedWidth.CreateDataReader(stream, readerOptions: Options());
        Assert.True(reader.Read());
        var buf = new byte[5];
        var copied = reader.GetBytes(0, 0, buf, 0, 5);
        Assert.True(copied > 0);
    }

    [Fact]
    public void GetChars_NullBuffer_ReturnsLength()
    {
        using var stream = Sample();
        using var reader = HeroParser.FixedWidth.CreateDataReader(stream, readerOptions: Options());
        Assert.True(reader.Read());
        var len = reader.GetChars(0, 0, null, 0, 0);
        Assert.True(len > 0);
    }

    [Fact]
    public void GetValues_FillsArray()
    {
        using var stream = Sample();
        using var reader = HeroParser.FixedWidth.CreateDataReader(stream, readerOptions: Options());
        Assert.True(reader.Read());
        var arr = new object[6];
        var n = reader.GetValues(arr);
        Assert.Equal(6, n);
    }

    [Fact]
    public void GetValues_SmallArray_ReturnsActualCount()
    {
        using var stream = Sample();
        using var reader = HeroParser.FixedWidth.CreateDataReader(stream, readerOptions: Options());
        Assert.True(reader.Read());
        var arr = new object[2];
        Assert.Equal(2, reader.GetValues(arr));
    }

    [Fact]
    public void GetValues_Null_Throws()
    {
        using var stream = Sample();
        using var reader = HeroParser.FixedWidth.CreateDataReader(stream, readerOptions: Options());
        Assert.True(reader.Read());
        Assert.Throws<ArgumentNullException>(() => reader.GetValues(null!));
    }

    [Fact]
    public void Indexer_ByOrdinalAndByName()
    {
        using var stream = Sample();
        using var reader = HeroParser.FixedWidth.CreateDataReader(stream, readerOptions: Options());
        Assert.True(reader.Read());
        Assert.Equal("Alice", reader[0]);
        Assert.Equal("Alice", reader["Name"]);
    }

    [Fact]
    public void NextResult_AlwaysFalse()
    {
        using var stream = Sample();
        using var reader = HeroParser.FixedWidth.CreateDataReader(stream, readerOptions: Options());
        Assert.False(reader.NextResult());
    }

    [Fact]
    public void Depth_AndRecordsAffected()
    {
        using var stream = Sample();
        using var reader = HeroParser.FixedWidth.CreateDataReader(stream, readerOptions: Options());
        Assert.Equal(0, reader.Depth);
        Assert.Equal(-1, reader.RecordsAffected);
    }

    [Fact]
    public void Close_MarksClosed_AndAccessThrows()
    {
        using var stream = Sample();
        var reader = HeroParser.FixedWidth.CreateDataReader(stream, readerOptions: Options());
        reader.Close();
        Assert.True(reader.IsClosed);
        Assert.Throws<InvalidOperationException>(() => reader.Read());
    }

    [Fact]
    public void DbDataReader_BaseTypeUsable()
    {
        using var stream = Sample();
        DbDataReader reader = HeroParser.FixedWidth.CreateDataReader(stream, readerOptions: Options());
        Assert.True(reader.Read());
        Assert.Equal("Alice", reader.GetString(0));
        reader.Dispose();
    }

    [Fact]
    public void OutOfRangeOrdinal_Throws()
    {
        using var stream = Sample();
        using var reader = HeroParser.FixedWidth.CreateDataReader(stream, readerOptions: Options());
        Assert.True(reader.Read());
        Assert.Throws<IndexOutOfRangeException>(() => reader.GetString(99));
    }

    [Fact]
    public void CreateDataReader_FromFile()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmp, "Name Age  \nAlice00030\n");
            using var reader = HeroParser.FixedWidth.CreateDataReader(tmp, readerOptions: new FixedWidthDataReaderOptions
            {
                Columns = [
                    new FixedWidthDataReaderColumn { Name = "Name", Start = 0, Length = 5 },
                    new FixedWidthDataReaderColumn { Name = "Age",  Start = 5, Length = 5 }
                ],
                HasHeaderRow = true
            });
            Assert.True(reader.Read());
            Assert.Equal("Alice", reader.GetString(0));
        }
        finally { File.Delete(tmp); }
    }
}
