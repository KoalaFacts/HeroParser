using HeroParser.SeparatedValues;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Reading.Rows;
using System.Globalization;
using System.Runtime.Intrinsics.X86;
using System.Text;
using Xunit;

namespace HeroParser.Tests;

public class BasicTestsUtf8
{
    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void SimpleCsv_ParsesCorrectly()
    {
        var csv = "a,b,c\n1,2,3";
        var reader = CreateReader(csv);

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
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ForeachLoop_Works()
    {
        var csv = "a,b\n1,2\n3,4";
        int rowCount = 0;

        foreach (var row in CreateReader(csv))
        {
            rowCount++;
            Assert.Equal(2, row.ColumnCount);
        }

        Assert.Equal(3, rowCount);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void EmptyCsv_ReturnsNoRows()
    {
        var csv = "";
        var reader = CreateReader(csv);
        Assert.False(reader.MoveNext());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void SingleColumn_ParsesCorrectly()
    {
        var csv = "a\nb\nc";
        var reader = CreateReader(csv);

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
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void EmptyFields_ParsedAsEmpty()
    {
        var csv = "a,,c\n,b,\n,,";
        var reader = CreateReader(csv);

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
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CustomDelimiter_Tab()
    {
        var csv = "a\tb\tc\n1\t2\t3";
        var options = new CsvReadOptions { Delimiter = '\t' };
        var reader = CreateReader(csv, options);

        Assert.True(reader.MoveNext());
        var row = reader.Current;
        Assert.Equal(3, row.ColumnCount);
        Assert.Equal("a", row[0].ToString());
        Assert.Equal("b", row[1].ToString());
        Assert.Equal("c", row[2].ToString());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CustomDelimiter_Pipe()
    {
        var csv = "a|b|c";
        var options = new CsvReadOptions { Delimiter = '|' };
        var reader = CreateReader(csv, options);

        Assert.True(reader.MoveNext());
        Assert.Equal(3, reader.Current.ColumnCount);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void LineEndings_CRLF()
    {
        var csv = "a,b\r\n1,2\r\n3,4";
        var reader = CreateReader(csv);

        int count = 0;
        foreach (var row in reader)
        {
            count++;
            Assert.Equal(2, row.ColumnCount);
        }
        Assert.Equal(3, count);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void LineEndings_LF()
    {
        var csv = "a,b\n1,2\n3,4";
        var reader = CreateReader(csv);

        int count = 0;
        foreach (var row in reader)
        {
            count++;
        }
        Assert.Equal(3, count);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void LineEndings_CR()
    {
        var csv = "a,b\r1,2\r3,4";
        var reader = CreateReader(csv);

        int count = 0;
        foreach (var row in reader)
        {
            count++;
        }
        Assert.Equal(3, count);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void EmptyLines_AreSkipped()
    {
        var csv = "a,b\n\n1,2\n\n\n3,4\n\n";
        var reader = CreateReader(csv);

        int count = 0;
        foreach (var row in reader)
        {
            count++;
        }
        Assert.Equal(3, count);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void TypeParsing_Int()
    {
        var csv = "123,456";
        var reader = CreateReader(csv);
        reader.MoveNext();
        var row = reader.Current;

        Assert.True(row[0].TryParseInt32(out int val1));
        Assert.Equal(123, val1);

        Assert.True(row[1].TryParseInt32(out int val2));
        Assert.Equal(456, val2);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void TypeParsing_Byte()
    {
        var csv = "255";
        var reader = CreateReader(csv);
        reader.MoveNext();
        var row = reader.Current;

        Assert.True(row[0].TryParseByte(out byte b));
        Assert.Equal((byte)255, b);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void TypeParsing_SmallerIntegers()
    {
        var csv = "127,32767,65535,255,-128,-32768";
        var reader = CreateReader(csv);
        reader.MoveNext();
        var row = reader.Current;

        Assert.True(row[0].TryParseSByte(out var sb));
        Assert.Equal(sbyte.MaxValue, sb);

        Assert.True(row[1].TryParseInt16(out var i16));
        Assert.Equal(short.MaxValue, i16);

        Assert.True(row[2].TryParseUInt16(out var u16));
        Assert.Equal(ushort.MaxValue, u16);

        Assert.True(row[3].TryParseByte(out var b));
        Assert.Equal(byte.MaxValue, b);

        Assert.True(row[4].TryParseSByte(out var sbNeg));
        Assert.Equal(sbyte.MinValue, sbNeg);

        Assert.True(row[5].TryParseInt16(out var i16Neg));
        Assert.Equal(short.MinValue, i16Neg);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void TypeParsing_UInt32_UInt64_Float()
    {
        var csv = "4294967295,18446744073709551615,3.5";
        var reader = CreateReader(csv);
        reader.MoveNext();

        Assert.True(reader.Current[0].TryParseUInt32(out uint u32));
        Assert.Equal(uint.MaxValue, u32);

        Assert.True(reader.Current[1].TryParseUInt64(out ulong u64));
        Assert.Equal(ulong.MaxValue, u64);

        Assert.True(reader.Current[2].TryParseSingle(out float f));
        Assert.Equal(3.5f, f);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void TypeParsing_Enum()
    {
        var csv = "Sunday,monday";
        var reader = CreateReader(csv);
        reader.MoveNext();

        Assert.True(reader.Current[0].TryParseEnum<DayOfWeek>(out var d1));
        Assert.Equal(DayOfWeek.Sunday, d1);

        Assert.True(reader.Current[1].TryParseEnum<DayOfWeek>(out var d2));
        Assert.Equal(DayOfWeek.Monday, d2);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void TypeParsing_Double()
    {
        var csv = "3.14,2.71";
        var reader = CreateReader(csv);
        reader.MoveNext();
        var row = reader.Current;

        Assert.True(row[0].TryParseDouble(out double val1));
        Assert.Equal(3.14, val1, precision: 2);

        Assert.True(row[1].TryParseDouble(out double val2));
        Assert.Equal(2.71, val2, precision: 2);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public void TypeParsing_DateOnly_TimeOnly_TimeZone_CultureAware()
    {
        var csv = "31.12.2024 13:45;31.12.2024;13:45;UTC;31.12.2024 13:45 +01:00";
        var options = new CsvReadOptions { Delimiter = ';' };
        var culture = CultureInfo.GetCultureInfo("de-DE");

        var reader = CreateReader(csv, options);
        Assert.True(reader.MoveNext());
        var row = reader.Current;

        Assert.True(row[0].TryParseDateTime(out var dt, culture));
        Assert.Equal(2024, dt.Year);
        Assert.Equal(12, dt.Month);
        Assert.True(row[0].TryParseDateTime(out var dtExact, "dd.MM.yyyy HH:mm"));
        Assert.Equal(dt, dtExact);
        Assert.True(row[0].TryParseDateTime(out var dtExactShorthand, "dd.MM.yyyy HH:mm"));
        Assert.Equal(dt, dtExactShorthand);

        Assert.True(row[1].TryParseDateOnly(out var dateOnly, culture));
        Assert.Equal(new DateOnly(2024, 12, 31), dateOnly);
        Assert.True(row[1].TryParseDateOnly(out var dateOnlyExact, "dd.MM.yyyy"));
        Assert.Equal(dateOnly, dateOnlyExact);

        Assert.True(row[2].TryParseTimeOnly(out var timeOnly, culture));
        Assert.Equal(new TimeOnly(13, 45), timeOnly);
        Assert.True(row[2].TryParseTimeOnly(out var timeOnlyExact, "HH:mm"));
        Assert.Equal(timeOnly, timeOnlyExact);

        Assert.True(row[3].TryParseTimeZoneInfo(out var tz));
        Assert.Equal("UTC", tz.Id);

        Assert.True(row[4].TryParseDateTimeOffset(out var dto, culture));
        Assert.Equal(2024, dto.Year);
        Assert.Equal(12, dto.Month);
        Assert.True(row[4].TryParseDateTimeOffset(out var dtoExact, "dd.MM.yyyy HH:mm zzz"));
        Assert.Equal(dto, dtoExact);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public void TypeParsing_DateOnly_TimeOnly_TimeZone_CultureAware_Utf8()
    {
        var csv = "31.12.2024 13:45;31.12.2024;13:45;UTC;31.12.2024 13:45 +01:00";
        var options = new CsvReadOptions { Delimiter = ';' };
        var culture = CultureInfo.GetCultureInfo("de-DE");

        using var reader = Csv.ReadFromByteSpan(Encoding.UTF8.GetBytes(csv), options);
        Assert.True(reader.MoveNext());
        var row = reader.Current;

        Assert.True(row[0].TryParseDateTime(out var dt, culture));
        Assert.Equal(2024, dt.Year);
        Assert.Equal(12, dt.Month);
        Assert.True(row[0].TryParseDateTime(out var dtExactShorthand, "dd.MM.yyyy HH:mm", culture));
        Assert.Equal(dt, dtExactShorthand);

        Assert.True(row[1].TryParseDateOnly(out var dateOnly, culture));
        Assert.Equal(new DateOnly(2024, 12, 31), dateOnly);
        Assert.True(row[1].TryParseDateOnly(out var dateOnlyExact, "dd.MM.yyyy", culture));
        Assert.Equal(dateOnly, dateOnlyExact);

        Assert.True(row[2].TryParseTimeOnly(out var timeOnly, culture));
        Assert.Equal(new TimeOnly(13, 45), timeOnly);
        Assert.True(row[2].TryParseTimeOnly(out var timeOnlyExact, "HH:mm", culture));
        Assert.Equal(timeOnly, timeOnlyExact);

        Assert.True(row[3].TryParseTimeZoneInfo(out var tz));
        Assert.Equal("UTC", tz.Id);

        Assert.True(row[4].TryParseDateTimeOffset(out var dto, culture));
        Assert.Equal(2024, dto.Year);
        Assert.Equal(12, dto.Month);
        Assert.True(row[4].TryParseDateTimeOffset(out var dtoExact, "dd.MM.yyyy HH:mm zzz", culture));
        Assert.Equal(dto, dtoExact);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public void TooManyColumns_ThrowsException()
    {
        var csv = "a,b,c,d,e";
        var options = new CsvReadOptions { MaxColumnCount = 3 };

        CsvException? ex = null;
        try
        {
            var reader = CreateReader(csv, options);
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
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public void TooManyColumns_ThrowsException_WithSimd()
    {
        if (!Avx2.IsSupported)
            return;

        var csv = BuildRow(40);
        var options = new CsvReadOptions
        {
            MaxColumnCount = 3,
            EnableQuotedFields = false,
            UseSimdIfAvailable = true
        };

        CsvException? ex = null;
        try
        {
            var reader = CreateReader(csv, options);
            reader.MoveNext();
            _ = reader.Current.ColumnCount;
        }
        catch (CsvException e)
        {
            ex = e;
        }

        Assert.NotNull(ex);
        Assert.Equal(CsvErrorCode.TooManyColumns, ex.ErrorCode);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public void TooManyRows_ThrowsException()
    {
        var csv = "a\nb\nc\nd";
        var options = new CsvReadOptions { MaxRowCount = 2 };

        CsvException? ex = null;
        try
        {
            var reader = CreateReader(csv, options);
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
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public void InvalidDelimiter_ThrowsException()
    {
        var options = new CsvReadOptions { Delimiter = '€' }; // Non-ASCII
        var ex = Assert.Throws<CsvException>(() => CreateReader("test", options));
        Assert.Equal(CsvErrorCode.InvalidDelimiter, ex.ErrorCode);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public void NullCsv_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => CreateReader(null!));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ToStringArray_Works()
    {
        var csv = "a,b,c";
        var reader = CreateReader(csv);
        reader.MoveNext();

        var array = reader.Current.ToStringArray();
        Assert.Equal(new[] { "a", "b", "c" }, array);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public void OutOfBoundsAccess_ThrowsIndexOutOfRangeException()
    {
        var csv = "a,b,c";
        var reader = CreateReader(csv);
        reader.MoveNext();

        // Negative index should throw
        IndexOutOfRangeException? ex1 = null;
        try
        {
            var _ = reader.Current[-1];
        }
        catch (IndexOutOfRangeException e)
        {
            ex1 = e;
        }
        Assert.NotNull(ex1);
        Assert.Contains("out of range", ex1.Message);

        // Index beyond column count should throw
        IndexOutOfRangeException? ex2 = null;
        try
        {
            var _ = reader.Current[3];
        }
        catch (IndexOutOfRangeException e)
        {
            ex2 = e;
        }
        Assert.NotNull(ex2);
        Assert.Contains("out of range", ex2.Message);
        Assert.Contains("Column count is 3", ex2.Message);
    }

    private static string BuildRow(int columnCount)
    {
        var builder = new StringBuilder(columnCount * 2);
        for (int i = 0; i < columnCount; i++)
        {
            if (i > 0)
                builder.Append(',');
            builder.Append('a');
        }
        return builder.ToString();
    }

    private static CsvRowReader<byte> CreateReader(string csv, CsvReadOptions? options = null)
    {
        var bytes = Encoding.UTF8.GetBytes(csv);
        return Csv.ReadFromByteSpan(bytes, options);
    }
}

