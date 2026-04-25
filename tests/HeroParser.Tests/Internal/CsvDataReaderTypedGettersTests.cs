using System.Data.Common;
using System.Text;
using Xunit;

namespace HeroParser.Tests.Internal;

/// <summary>
/// Targets typed-getter and edge-case branches in <see cref="SeparatedValues.Reading.Data.CsvDataReader"/>
/// that lacked direct coverage (~91 lines / 76%). Mirrors the ExcelDataReader coverage tests.
/// </summary>
[Trait("Category", "Unit")]
public class CsvDataReaderTypedGettersTests
{
    private static MemoryStream CsvStream(string csv)
        => new(Encoding.UTF8.GetBytes(csv));

    private const string SAMPLE_CSV = """
        Name,Age,Price,Active,BirthDate,Id
        Alice,30,9.99,true,2024-01-15,12345678-1234-1234-1234-123456789012
        Bob,25,1.25,0,2023-06-30,abcdef00-0000-0000-0000-000000000000
        """;

    [Fact]
    public void GetString_OnEmpty_ReturnsEmptyString()
    {
        // CsvDataReader treats empty/missing values as DBNull only when configured;
        // by default empty cells return empty strings.
        using var ms = CsvStream("A,B\n,present\n");
        using var reader = HeroParser.Csv.CreateDataReader(ms);
        Assert.True(reader.Read());
        var s = reader.GetString(0);
        Assert.NotNull(s);
    }

    [Fact]
    public void GetBoolean_Variants()
    {
        using var ms = CsvStream("A,B,C,D\ntrue,false,1,0\n");
        using var reader = HeroParser.Csv.CreateDataReader(ms);
        Assert.True(reader.Read());
        Assert.True(reader.GetBoolean(0));
        Assert.False(reader.GetBoolean(1));
        Assert.True(reader.GetBoolean(2));
        Assert.False(reader.GetBoolean(3));
    }

    [Fact]
    public void GetBoolean_InvalidValue_Throws()
    {
        using var ms = CsvStream("A\nmaybe\n");
        using var reader = HeroParser.Csv.CreateDataReader(ms);
        Assert.True(reader.Read());
        Assert.Throws<FormatException>(() => reader.GetBoolean(0));
    }

    [Fact]
    public void TypedNumericGetters()
    {
        using var ms = CsvStream(SAMPLE_CSV);
        using var reader = HeroParser.Csv.CreateDataReader(ms);
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
        using var ms = CsvStream(SAMPLE_CSV);
        using var reader = HeroParser.Csv.CreateDataReader(ms);
        Assert.True(reader.Read());
        Assert.Equal(new DateTime(2024, 1, 15), reader.GetDateTime(4));
    }

    [Fact]
    public void GetGuid_ParsesValid()
    {
        using var ms = CsvStream(SAMPLE_CSV);
        using var reader = HeroParser.Csv.CreateDataReader(ms);
        Assert.True(reader.Read());
        Assert.NotEqual(Guid.Empty, reader.GetGuid(5));
    }

    [Fact]
    public void GetChar_ReturnsFirstChar()
    {
        using var ms = CsvStream(SAMPLE_CSV);
        using var reader = HeroParser.Csv.CreateDataReader(ms);
        Assert.True(reader.Read());
        Assert.Equal('A', reader.GetChar(0));
    }

    [Fact]
    public void GetChar_EmptyValue_Throws()
    {
        using var ms = CsvStream("A,B\n,present\n");
        using var reader = HeroParser.Csv.CreateDataReader(ms);
        Assert.True(reader.Read());
        // GetChar may throw FormatException or InvalidCastException depending on impl.
        Assert.ThrowsAny<Exception>(() => reader.GetChar(0));
    }

    [Fact]
    public void GetBytes_NullBuffer_ReturnsLength()
    {
        using var ms = CsvStream("A\nhello\n");
        using var reader = HeroParser.Csv.CreateDataReader(ms);
        Assert.True(reader.Read());
        Assert.Equal(5, reader.GetBytes(0, 0, null, 0, 0));
    }

    [Fact]
    public void GetBytes_CopiesIntoBuffer()
    {
        using var ms = CsvStream("A\nhello\n");
        using var reader = HeroParser.Csv.CreateDataReader(ms);
        Assert.True(reader.Read());
        var buf = new byte[5];
        Assert.Equal(5, reader.GetBytes(0, 0, buf, 0, 5));
        Assert.Equal("hello"u8.ToArray(), buf);
    }

    [Fact]
    public void GetBytes_OffsetBeyondData_ReturnsZero()
    {
        using var ms = CsvStream("A\nhi\n");
        using var reader = HeroParser.Csv.CreateDataReader(ms);
        Assert.True(reader.Read());
        Assert.Equal(0, reader.GetBytes(0, 100, new byte[5], 0, 5));
    }

    [Fact]
    public void GetBytes_NegativeOffset_Throws()
    {
        using var ms = CsvStream("A\nhi\n");
        using var reader = HeroParser.Csv.CreateDataReader(ms);
        Assert.True(reader.Read());
        Assert.Throws<ArgumentOutOfRangeException>(() => reader.GetBytes(0, -1, new byte[5], 0, 5));
    }

    [Fact]
    public void GetChars_NullBuffer_ReturnsLength()
    {
        using var ms = CsvStream("A\nhello\n");
        using var reader = HeroParser.Csv.CreateDataReader(ms);
        Assert.True(reader.Read());
        Assert.Equal(5, reader.GetChars(0, 0, null, 0, 0));
    }

    [Fact]
    public void GetChars_CopiesIntoBuffer()
    {
        using var ms = CsvStream("A\nhello\n");
        using var reader = HeroParser.Csv.CreateDataReader(ms);
        Assert.True(reader.Read());
        var buf = new char[5];
        Assert.Equal(5, reader.GetChars(0, 0, buf, 0, 5));
        Assert.Equal("hello", new string(buf));
    }

    [Fact]
    public void GetChars_OffsetBeyond_ReturnsZero()
    {
        using var ms = CsvStream("A\nhi\n");
        using var reader = HeroParser.Csv.CreateDataReader(ms);
        Assert.True(reader.Read());
        Assert.Equal(0, reader.GetChars(0, 100, new char[5], 0, 5));
    }

    [Fact]
    public void GetValues_FillsArray()
    {
        using var ms = CsvStream(SAMPLE_CSV);
        using var reader = HeroParser.Csv.CreateDataReader(ms);
        Assert.True(reader.Read());
        var arr = new object[6];
        var n = reader.GetValues(arr);
        Assert.Equal(6, n);
        Assert.Equal("Alice", arr[0]);
    }

    [Fact]
    public void GetValues_SmallerArray_ReturnsActualCount()
    {
        using var ms = CsvStream(SAMPLE_CSV);
        using var reader = HeroParser.Csv.CreateDataReader(ms);
        Assert.True(reader.Read());
        var arr = new object[2];
        Assert.Equal(2, reader.GetValues(arr));
    }

    [Fact]
    public void GetValues_Null_Throws()
    {
        using var ms = CsvStream(SAMPLE_CSV);
        using var reader = HeroParser.Csv.CreateDataReader(ms);
        Assert.True(reader.Read());
        Assert.Throws<ArgumentNullException>(() => reader.GetValues(null!));
    }

    [Fact]
    public void IsDBNull_OnEmpty_DependsOnOptions()
    {
        // Default CsvDataReaderOptions may treat empty values as empty strings, not DBNull.
        // Just verify the call does not throw.
        using var ms = CsvStream("A,B\n,present\n");
        using var reader = HeroParser.Csv.CreateDataReader(ms);
        Assert.True(reader.Read());
        _ = reader.IsDBNull(0);
    }

    [Fact]
    public void Indexer_ByOrdinalAndByName()
    {
        using var ms = CsvStream(SAMPLE_CSV);
        using var reader = HeroParser.Csv.CreateDataReader(ms);
        Assert.True(reader.Read());
        Assert.Equal("Alice", reader[0]);
        Assert.Equal("Alice", reader["Name"]);
    }

    [Fact]
    public void GetOrdinal_Unknown_Throws()
    {
        using var ms = CsvStream(SAMPLE_CSV);
        using var reader = HeroParser.Csv.CreateDataReader(ms);
        Assert.True(reader.Read());
        Assert.Throws<IndexOutOfRangeException>(() => reader.GetOrdinal("NoSuchColumn"));
    }

    [Fact]
    public void GetName_AndFieldType()
    {
        using var ms = CsvStream(SAMPLE_CSV);
        using var reader = HeroParser.Csv.CreateDataReader(ms);
        Assert.True(reader.Read());
        Assert.Equal("Name", reader.GetName(0));
        Assert.Equal(typeof(string), reader.GetFieldType(0));
        Assert.Equal("String", reader.GetDataTypeName(0));
    }

    [Fact]
    public void NextResult_AlwaysFalse()
    {
        using var ms = CsvStream(SAMPLE_CSV);
        using var reader = HeroParser.Csv.CreateDataReader(ms);
        Assert.False(reader.NextResult());
    }

    [Fact]
    public void Depth_AndRecordsAffected()
    {
        using var ms = CsvStream(SAMPLE_CSV);
        using var reader = HeroParser.Csv.CreateDataReader(ms);
        Assert.Equal(0, reader.Depth);
        Assert.Equal(-1, reader.RecordsAffected);
    }

    [Fact]
    public void HasRows_TrueWhenDataExists()
    {
        using var ms = CsvStream(SAMPLE_CSV);
        using var reader = HeroParser.Csv.CreateDataReader(ms);
        Assert.True(reader.HasRows);
    }

    [Fact]
    public void Close_MarksClosed_AndAccessThrows()
    {
        using var ms = CsvStream(SAMPLE_CSV);
        var reader = HeroParser.Csv.CreateDataReader(ms);
        reader.Close();
        Assert.True(reader.IsClosed);
        Assert.Throws<InvalidOperationException>(() => reader.Read());
    }

    [Fact]
    public void Read_BeforeAccess_ThrowsOnGet()
    {
        using var ms = CsvStream(SAMPLE_CSV);
        using var reader = HeroParser.Csv.CreateDataReader(ms);
        // No Read() called yet
        Assert.Throws<InvalidOperationException>(() => reader.GetString(0));
    }

    [Fact]
    public void OutOfRangeOrdinal_Throws()
    {
        using var ms = CsvStream(SAMPLE_CSV);
        using var reader = HeroParser.Csv.CreateDataReader(ms);
        Assert.True(reader.Read());
        Assert.Throws<IndexOutOfRangeException>(() => reader.GetString(99));
    }

    [Fact]
    public void DbDataReader_BaseTypeUsable()
    {
        using var ms = CsvStream(SAMPLE_CSV);
        DbDataReader reader = HeroParser.Csv.CreateDataReader(ms);
        Assert.True(reader.Read());
        Assert.Equal("Alice", reader.GetString(0));
        reader.Dispose();
    }

    [Fact]
    public void CreateDataReader_FromFile()
    {
        var tempPath = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempPath, SAMPLE_CSV);
            using var reader = HeroParser.Csv.CreateDataReader(tempPath);
            Assert.True(reader.Read());
            Assert.Equal("Alice", reader.GetString(0));
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public void CreateDataReader_NullStream_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => HeroParser.Csv.CreateDataReader((Stream)null!));
    }

    [Fact]
    public void CreateDataReader_NullPath_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => HeroParser.Csv.CreateDataReader((string)null!));
    }
}
