using System.Data.Common;
using HeroParser.Tests.Fixtures.Excel;
using Xunit;

namespace HeroParser.Tests.Excel;

/// <summary>
/// Targets typed-getter and edge-case branches in <see cref="Excels.Reading.Data.ExcelDataReader"/>
/// that lacked direct coverage (~153 lines / 55%).
/// </summary>
[Trait("Category", "Unit")]
public class ExcelDataReaderTypedGettersTests
{
    private static MemoryStream Sample()
        => ExcelTestHelper.CreateXlsx("Sheet1", [
            ["Name", "Age", "Price", "Active", "BirthDate", "Id"],
            ["Alice", "30", "9.99", "true", "2024-01-15", "12345678-1234-1234-1234-123456789012"],
            ["Bob", "25", "1.25", "0", "2023-06-30", "abcdef00-0000-0000-0000-000000000000"]
        ]);

    [Fact]
    public void GetString_Throws_OnDbNull()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["A"], [""]
        ]);
        using var reader = HeroParser.Excel.CreateDataReader(xlsx);
        Assert.True(reader.Read());
        Assert.Throws<InvalidCastException>(() => reader.GetString(0));
    }

    [Fact]
    public void GetBoolean_Variants_TrueFalseOneZero()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["A", "B", "C", "D"],
            ["true", "false", "1", "0"]
        ]);
        using var reader = HeroParser.Excel.CreateDataReader(xlsx);
        Assert.True(reader.Read());
        Assert.True(reader.GetBoolean(0));
        Assert.False(reader.GetBoolean(1));
        Assert.True(reader.GetBoolean(2));
        Assert.False(reader.GetBoolean(3));
    }

    [Fact]
    public void GetBoolean_InvalidValue_Throws()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["A"], ["maybe"]
        ]);
        using var reader = HeroParser.Excel.CreateDataReader(xlsx);
        Assert.True(reader.Read());
        Assert.Throws<FormatException>(() => reader.GetBoolean(0));
    }

    [Fact]
    public void GetBoolean_SingleCharInvalid_Throws()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["A"], ["x"]
        ]);
        using var reader = HeroParser.Excel.CreateDataReader(xlsx);
        Assert.True(reader.Read());
        Assert.Throws<FormatException>(() => reader.GetBoolean(0));
    }

    [Fact]
    public void TypedNumericGetters()
    {
        using var xlsx = Sample();
        using var reader = HeroParser.Excel.CreateDataReader(xlsx);
        Assert.True(reader.Read());

        Assert.Equal((byte)30, reader.GetByte(1));
        Assert.Equal((short)30, reader.GetInt16(1));
        Assert.Equal(30, reader.GetInt32(1));
        Assert.Equal(30L, reader.GetInt64(1));
        Assert.Equal(9.99f, reader.GetFloat(2), 4);
        Assert.Equal(9.99, reader.GetDouble(2), 5);
        Assert.Equal(9.99m, reader.GetDecimal(2));
    }

    [Fact]
    public void GetDateTime_ParsesIso()
    {
        using var xlsx = Sample();
        using var reader = HeroParser.Excel.CreateDataReader(xlsx);
        Assert.True(reader.Read());
        Assert.Equal(new DateTime(2024, 1, 15), reader.GetDateTime(4));
    }

    [Fact]
    public void GetGuid_ParsesValid()
    {
        using var xlsx = Sample();
        using var reader = HeroParser.Excel.CreateDataReader(xlsx);
        Assert.True(reader.Read());
        Assert.NotEqual(Guid.Empty, reader.GetGuid(5));
    }

    [Fact]
    public void GetChar_ReturnsFirstChar()
    {
        using var xlsx = Sample();
        using var reader = HeroParser.Excel.CreateDataReader(xlsx);
        Assert.True(reader.Read());
        Assert.Equal('A', reader.GetChar(0));
    }

    [Fact]
    public void GetChar_EmptyValue_Throws()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [["A"], [""]]);
        using var reader = HeroParser.Excel.CreateDataReader(xlsx);
        Assert.True(reader.Read());
        Assert.Throws<InvalidCastException>(() => reader.GetChar(0));
    }

    [Fact]
    public void GetBytes_NullBuffer_ReturnsLength()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [["A"], ["hello"]]);
        using var reader = HeroParser.Excel.CreateDataReader(xlsx);
        Assert.True(reader.Read());
        Assert.Equal(5, reader.GetBytes(0, 0, null, 0, 0));
    }

    [Fact]
    public void GetBytes_CopiesIntoBuffer()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [["A"], ["hello"]]);
        using var reader = HeroParser.Excel.CreateDataReader(xlsx);
        Assert.True(reader.Read());
        var buf = new byte[5];
        var got = reader.GetBytes(0, 0, buf, 0, 5);
        Assert.Equal(5, got);
        Assert.Equal("hello"u8.ToArray(), buf);
    }

    [Fact]
    public void GetBytes_OffsetBeyondData_ReturnsZero()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [["A"], ["hi"]]);
        using var reader = HeroParser.Excel.CreateDataReader(xlsx);
        Assert.True(reader.Read());
        var buf = new byte[5];
        Assert.Equal(0, reader.GetBytes(0, 100, buf, 0, 5));
    }

    [Fact]
    public void GetBytes_NegativeOffset_Throws()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [["A"], ["hi"]]);
        using var reader = HeroParser.Excel.CreateDataReader(xlsx);
        Assert.True(reader.Read());
        Assert.Throws<ArgumentOutOfRangeException>(() => reader.GetBytes(0, -1, new byte[5], 0, 5));
    }

    [Fact]
    public void GetChars_NullBuffer_ReturnsLength()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [["A"], ["hello"]]);
        using var reader = HeroParser.Excel.CreateDataReader(xlsx);
        Assert.True(reader.Read());
        Assert.Equal(5, reader.GetChars(0, 0, null, 0, 0));
    }

    [Fact]
    public void GetChars_CopiesIntoBuffer()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [["A"], ["hello"]]);
        using var reader = HeroParser.Excel.CreateDataReader(xlsx);
        Assert.True(reader.Read());
        var buf = new char[5];
        Assert.Equal(5, reader.GetChars(0, 0, buf, 0, 5));
        Assert.Equal("hello", new string(buf));
    }

    [Fact]
    public void GetChars_OffsetBeyond_ReturnsZero()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [["A"], ["hi"]]);
        using var reader = HeroParser.Excel.CreateDataReader(xlsx);
        Assert.True(reader.Read());
        Assert.Equal(0, reader.GetChars(0, 100, new char[5], 0, 5));
    }

    [Fact]
    public void GetChars_NegativeOffset_Throws()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [["A"], ["hi"]]);
        using var reader = HeroParser.Excel.CreateDataReader(xlsx);
        Assert.True(reader.Read());
        Assert.Throws<ArgumentOutOfRangeException>(() => reader.GetChars(0, -1, new char[5], 0, 5));
    }

    [Fact]
    public void GetValues_FillsArray()
    {
        using var xlsx = Sample();
        using var reader = HeroParser.Excel.CreateDataReader(xlsx);
        Assert.True(reader.Read());
        var arr = new object[6];
        var n = reader.GetValues(arr);
        Assert.Equal(6, n);
        Assert.Equal("Alice", arr[0]);
    }

    [Fact]
    public void GetValues_SmallerArray_ReturnsActualCount()
    {
        using var xlsx = Sample();
        using var reader = HeroParser.Excel.CreateDataReader(xlsx);
        Assert.True(reader.Read());
        var arr = new object[2];
        Assert.Equal(2, reader.GetValues(arr));
    }

    [Fact]
    public void GetValues_Null_Throws()
    {
        using var xlsx = Sample();
        using var reader = HeroParser.Excel.CreateDataReader(xlsx);
        Assert.True(reader.Read());
        Assert.Throws<ArgumentNullException>(() => reader.GetValues(null!));
    }

    [Fact]
    public void IsDBNull_EmptyCell_True()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [["A"], [""]]);
        using var reader = HeroParser.Excel.CreateDataReader(xlsx);
        Assert.True(reader.Read());
        Assert.True(reader.IsDBNull(0));
    }

    [Fact]
    public void IsDBNull_PopulatedCell_False()
    {
        using var xlsx = Sample();
        using var reader = HeroParser.Excel.CreateDataReader(xlsx);
        Assert.True(reader.Read());
        Assert.False(reader.IsDBNull(0));
    }

    [Fact]
    public void Indexer_ByOrdinal_AndByName()
    {
        using var xlsx = Sample();
        using var reader = HeroParser.Excel.CreateDataReader(xlsx);
        Assert.True(reader.Read());
        Assert.Equal("Alice", reader[0]);
        Assert.Equal("Alice", reader["Name"]);
    }

    [Fact]
    public void GetOrdinal_ReturnsIndex()
    {
        using var xlsx = Sample();
        using var reader = HeroParser.Excel.CreateDataReader(xlsx);
        Assert.True(reader.Read());
        Assert.Equal(0, reader.GetOrdinal("Name"));
        Assert.Equal(1, reader.GetOrdinal("Age"));
    }

    [Fact]
    public void GetOrdinal_UnknownColumn_Throws()
    {
        using var xlsx = Sample();
        using var reader = HeroParser.Excel.CreateDataReader(xlsx);
        Assert.True(reader.Read());
        Assert.Throws<IndexOutOfRangeException>(() => reader.GetOrdinal("Nonexistent"));
    }

    [Fact]
    public void GetName_ReturnsHeader()
    {
        using var xlsx = Sample();
        using var reader = HeroParser.Excel.CreateDataReader(xlsx);
        Assert.True(reader.Read());
        Assert.Equal("Name", reader.GetName(0));
    }

    [Fact]
    public void GetFieldType_ReturnsString()
    {
        using var xlsx = Sample();
        using var reader = HeroParser.Excel.CreateDataReader(xlsx);
        Assert.True(reader.Read());
        Assert.Equal(typeof(string), reader.GetFieldType(0));
        Assert.Equal("String", reader.GetDataTypeName(0));
    }

    [Fact]
    public void NextResult_ReturnsFalse()
    {
        using var xlsx = Sample();
        using var reader = HeroParser.Excel.CreateDataReader(xlsx);
        Assert.False(reader.NextResult());
    }

    [Fact]
    public void Depth_AndRecordsAffected()
    {
        using var xlsx = Sample();
        using var reader = HeroParser.Excel.CreateDataReader(xlsx);
        Assert.Equal(0, reader.Depth);
        Assert.Equal(-1, reader.RecordsAffected);
    }

    [Fact]
    public void HasRows_TrueWhenDataExists()
    {
        using var xlsx = Sample();
        using var reader = HeroParser.Excel.CreateDataReader(xlsx);
        Assert.True(reader.HasRows);
    }

    [Fact]
    public void HasRows_FalseForHeaderOnly()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [["A", "B"]]);
        using var reader = HeroParser.Excel.CreateDataReader(xlsx);
        Assert.False(reader.HasRows);
    }

    [Fact]
    public void Close_MarksClosed()
    {
        using var xlsx = Sample();
        var reader = HeroParser.Excel.CreateDataReader(xlsx);
        Assert.False(reader.IsClosed);
        reader.Close();
        Assert.True(reader.IsClosed);
    }

    [Fact]
    public void AccessAfterClose_Throws()
    {
        using var xlsx = Sample();
        var reader = HeroParser.Excel.CreateDataReader(xlsx);
        reader.Close();
        Assert.Throws<InvalidOperationException>(() => reader.Read());
    }

    [Fact]
    public void GetSchemaTable_ReturnsTableWithRowPerColumn()
    {
        using var xlsx = Sample();
        using var reader = HeroParser.Excel.CreateDataReader(xlsx);
        var schema = reader.GetSchemaTable();
        Assert.NotNull(schema);
        Assert.Equal(reader.FieldCount, schema.Rows.Count);
    }

    [Fact]
    public void GetEnumerator_IteratesAllRows()
    {
        using var xlsx = Sample();
        using var reader = HeroParser.Excel.CreateDataReader(xlsx);
        var rowCount = 0;
        foreach (var _ in reader)
        {
            rowCount++;
        }
        Assert.Equal(2, rowCount);
    }

    [Fact]
    public void Read_BeforeAccess_ThrowsInvalidOperation()
    {
        using var xlsx = Sample();
        using var reader = HeroParser.Excel.CreateDataReader(xlsx);
        // Did not call Read() yet
        Assert.Throws<InvalidOperationException>(() => reader.GetString(0));
    }

    [Fact]
    public void OutOfRangeOrdinal_Throws()
    {
        using var xlsx = Sample();
        using var reader = HeroParser.Excel.CreateDataReader(xlsx);
        Assert.True(reader.Read());
        Assert.Throws<IndexOutOfRangeException>(() => reader.GetString(99));
    }

    [Fact]
    public void DbDataReader_InheritedAsBaseType()
    {
        using var xlsx = Sample();
        DbDataReader reader = HeroParser.Excel.CreateDataReader(xlsx);
        Assert.True(reader.Read());
        Assert.Equal("Alice", reader.GetString(0));
        reader.Dispose();
    }
}
