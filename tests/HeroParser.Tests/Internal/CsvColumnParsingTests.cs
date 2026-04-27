using System.Globalization;
using HeroParser.FixedWidths.Mapping;
using HeroParser.SeparatedValues.Reading.Rows;
using Xunit;

namespace HeroParser.Tests.Internal;

/// <summary>
/// Direct unit tests for <see cref="CsvColumn{T}"/> TryParse* methods across both
/// byte and char storage. These exercise parsing paths that source-generated
/// binders call into but that were not directly tested.
/// </summary>
[Trait("Category", "Unit")]
public class CsvColumnParsingTests
{
    // ───── byte-backed column ─────

    [Fact]
    public void Byte_TryParseInt32_Ok_AndFail()
    {
        Assert.True(new CsvColumn<byte>("42"u8).TryParseInt32(out var v));
        Assert.Equal(42, v);
        Assert.False(new CsvColumn<byte>("abc"u8).TryParseInt32(out _));
    }

    [Fact]
    public void Byte_TryParseInt16_Ok_AndFail()
    {
        Assert.True(new CsvColumn<byte>("-5"u8).TryParseInt16(out var v));
        Assert.Equal(-5, v);
        Assert.False(new CsvColumn<byte>("foo"u8).TryParseInt16(out _));
    }

    [Fact]
    public void Byte_TryParseUInt16_AndUInt32_AndUInt64()
    {
        Assert.True(new CsvColumn<byte>("5"u8).TryParseUInt16(out ushort u16));
        Assert.Equal(5, u16);
        Assert.True(new CsvColumn<byte>("5"u8).TryParseUInt32(out uint u32));
        Assert.Equal(5u, u32);
        Assert.True(new CsvColumn<byte>("5"u8).TryParseUInt64(out ulong u64));
        Assert.Equal(5ul, u64);
    }

    [Fact]
    public void Byte_TryParseInt64_Ok_AndFail()
    {
        Assert.True(new CsvColumn<byte>("9000000000"u8).TryParseInt64(out var v));
        Assert.Equal(9_000_000_000L, v);
        Assert.False(new CsvColumn<byte>(""u8).TryParseInt64(out _));
    }

    [Fact]
    public void Byte_TryParseDouble_Single_Decimal()
    {
        Assert.True(new CsvColumn<byte>("3.14"u8).TryParseDouble(out var d));
        Assert.Equal(3.14, d, 5);
        Assert.True(new CsvColumn<byte>("3.14"u8).TryParseSingle(out var f));
        Assert.Equal(3.14f, f, 4);
        Assert.True(new CsvColumn<byte>("3.14"u8).TryParseDecimal(out var m));
        Assert.Equal(3.14m, m);
        Assert.False(new CsvColumn<byte>("x"u8).TryParseDecimal(out _));
    }

    [Fact]
    public void Byte_TryParseBoolean_Ok_AndFail()
    {
        Assert.True(new CsvColumn<byte>("true"u8).TryParseBoolean(out var b));
        Assert.True(b);
        Assert.True(new CsvColumn<byte>("false"u8).TryParseBoolean(out b));
        Assert.False(b);
        Assert.False(new CsvColumn<byte>("yes"u8).TryParseBoolean(out _));
    }

    [Fact]
    public void Byte_TryParseDateTime_Fails_OnBadInput()
    {
        Assert.False(new CsvColumn<byte>("not a date"u8).TryParseDateTime(out _));
    }

    [Fact]
    public void Byte_TryParseDateTime_WithFormat_Ok()
    {
        Assert.True(new CsvColumn<byte>("20240115"u8).TryParseDateTime(out var dt, "yyyyMMdd"));
        Assert.Equal(new DateTime(2024, 1, 15), dt);
    }

    [Fact]
    public void Byte_TryParseDateTimeOffset_Ok()
    {
        Assert.True(new CsvColumn<byte>("2024-01-15T00:00:00+00:00"u8).TryParseDateTimeOffset(out var dto));
        Assert.Equal(2024, dto.Year);
        Assert.True(new CsvColumn<byte>("20240115"u8).TryParseDateTimeOffset(out var dto2, "yyyyMMdd"));
        Assert.Equal(2024, dto2.Year);
        Assert.True(new CsvColumn<byte>("20240115"u8).TryParseDateTimeOffset(out var dto3, "yyyyMMdd", CultureInfo.InvariantCulture));
        Assert.Equal(2024, dto3.Year);
    }

    [Fact]
    public void Byte_TryParseDateOnly_Ok_AndFormat()
    {
        Assert.True(new CsvColumn<byte>("2024-01-15"u8).TryParseDateOnly(out var d1));
        Assert.Equal(new DateOnly(2024, 1, 15), d1);
        Assert.True(new CsvColumn<byte>("20240115"u8).TryParseDateOnly(out var d2, "yyyyMMdd"));
        Assert.Equal(new DateOnly(2024, 1, 15), d2);
        Assert.True(new CsvColumn<byte>("20240115"u8).TryParseDateOnly(out var d3, "yyyyMMdd", CultureInfo.InvariantCulture));
        Assert.Equal(new DateOnly(2024, 1, 15), d3);
    }

    [Fact]
    public void Byte_TryParseTimeOnly_Ok_AndFormat()
    {
        Assert.True(new CsvColumn<byte>("10:30"u8).TryParseTimeOnly(out var t1));
        Assert.Equal(new TimeOnly(10, 30), t1);
        Assert.True(new CsvColumn<byte>("1030"u8).TryParseTimeOnly(out var t2, "HHmm"));
        Assert.Equal(new TimeOnly(10, 30), t2);
    }

    [Fact]
    public void Byte_TryParseTimeZoneInfo_Ok_AndFail()
    {
        var id = OperatingSystem.IsWindows() ? "UTC" : "UTC";
        Assert.True(new CsvColumn<byte>(System.Text.Encoding.UTF8.GetBytes(id)).TryParseTimeZoneInfo(out var tz));
        Assert.NotNull(tz);
        Assert.False(new CsvColumn<byte>("NoSuchTZ"u8).TryParseTimeZoneInfo(out _));
        Assert.False(new CsvColumn<byte>(""u8).TryParseTimeZoneInfo(out _));
    }

    [Fact]
    public void Byte_TryParseGuid_Ok_AndFail()
    {
        Assert.True(new CsvColumn<byte>("12345678-1234-1234-1234-123456789012"u8).TryParseGuid(out var g));
        Assert.NotEqual(Guid.Empty, g);
        Assert.False(new CsvColumn<byte>("nope"u8).TryParseGuid(out _));
    }

    [Fact]
    public void Byte_Parse_Generic()
    {
        var parsed = new CsvColumn<byte>("42"u8).Parse<int>();
        Assert.Equal(42, parsed);
        Assert.True(new CsvColumn<byte>("42"u8).TryParse<int>(out int v));
        Assert.Equal(42, v);
    }

    [Fact]
    public void Byte_ToString_DecodesUtf8()
    {
        Assert.Equal("café", new CsvColumn<byte>("café"u8).ToString());
    }

    [Fact]
    public void Byte_IsEmpty_And_Length()
    {
        Assert.True(new CsvColumn<byte>(""u8).IsEmpty);
        Assert.Equal(3, new CsvColumn<byte>("abc"u8).Length);
    }

    // ───── char-backed column ─────

    [Fact]
    public void Char_TryParseInt32_Ok_AndFail()
    {
        Assert.True(new CsvColumn<char>("42".AsSpan()).TryParseInt32(out var v));
        Assert.Equal(42, v);
        Assert.False(new CsvColumn<char>("abc".AsSpan()).TryParseInt32(out _));
    }

    [Fact]
    public void Char_AllIntegerTypes()
    {
        Assert.True(new CsvColumn<char>("5".AsSpan()).TryParseInt16(out short s));
        Assert.Equal(5, s);
        Assert.True(new CsvColumn<char>("5".AsSpan()).TryParseInt64(out long l));
        Assert.Equal(5L, l);
        Assert.True(new CsvColumn<char>("5".AsSpan()).TryParseUInt16(out ushort us));
        Assert.Equal(5, us);
        Assert.True(new CsvColumn<char>("5".AsSpan()).TryParseUInt32(out uint u));
        Assert.Equal(5u, u);
        Assert.True(new CsvColumn<char>("5".AsSpan()).TryParseUInt64(out ulong ul));
        Assert.Equal(5ul, ul);
    }

    [Fact]
    public void Char_AllFloatTypes()
    {
        Assert.True(new CsvColumn<char>("3.14".AsSpan()).TryParseDouble(out var d));
        Assert.Equal(3.14, d, 5);
        Assert.True(new CsvColumn<char>("3.14".AsSpan()).TryParseSingle(out var f));
        Assert.Equal(3.14f, f, 4);
        Assert.True(new CsvColumn<char>("3.14".AsSpan()).TryParseDecimal(out var m));
        Assert.Equal(3.14m, m);
    }

    [Fact]
    public void Char_TryParseBoolean()
    {
        Assert.True(new CsvColumn<char>("true".AsSpan()).TryParseBoolean(out var b));
        Assert.True(b);
        Assert.True(new CsvColumn<char>("false".AsSpan()).TryParseBoolean(out b));
        Assert.False(b);
    }

    [Fact]
    public void Char_TryParseDateTime_Variants()
    {
        Assert.True(new CsvColumn<char>("2024-01-15".AsSpan()).TryParseDateTime(out var dt1));
        Assert.Equal(new DateTime(2024, 1, 15), dt1);
        Assert.True(new CsvColumn<char>("20240115".AsSpan()).TryParseDateTime(out var dt2, "yyyyMMdd"));
        Assert.Equal(new DateTime(2024, 1, 15), dt2);
        Assert.True(new CsvColumn<char>("20240115".AsSpan()).TryParseDateTime(out var dt3, "yyyyMMdd", CultureInfo.InvariantCulture));
        Assert.Equal(new DateTime(2024, 1, 15), dt3);
    }

    [Fact]
    public void Char_TryParseDateTimeOffset_Variants()
    {
        Assert.True(new CsvColumn<char>("2024-01-15T00:00:00+00:00".AsSpan()).TryParseDateTimeOffset(out var dto1));
        Assert.Equal(2024, dto1.Year);
        Assert.True(new CsvColumn<char>("20240115".AsSpan()).TryParseDateTimeOffset(out var dto2, "yyyyMMdd"));
        Assert.Equal(2024, dto2.Year);
        Assert.True(new CsvColumn<char>("20240115".AsSpan()).TryParseDateTimeOffset(out var dto3, "yyyyMMdd", CultureInfo.InvariantCulture));
        Assert.Equal(2024, dto3.Year);
    }

    [Fact]
    public void Char_TryParseDateOnly_Variants()
    {
        Assert.True(new CsvColumn<char>("2024-01-15".AsSpan()).TryParseDateOnly(out var d1));
        Assert.Equal(new DateOnly(2024, 1, 15), d1);
        Assert.True(new CsvColumn<char>("20240115".AsSpan()).TryParseDateOnly(out var d2, "yyyyMMdd"));
        Assert.Equal(new DateOnly(2024, 1, 15), d2);
        Assert.True(new CsvColumn<char>("20240115".AsSpan()).TryParseDateOnly(out var d3, "yyyyMMdd", CultureInfo.InvariantCulture));
        Assert.Equal(new DateOnly(2024, 1, 15), d3);
    }

    [Fact]
    public void Char_TryParseTimeOnly_Variants()
    {
        Assert.True(new CsvColumn<char>("10:30".AsSpan()).TryParseTimeOnly(out var t1));
        Assert.Equal(new TimeOnly(10, 30), t1);
        Assert.True(new CsvColumn<char>("1030".AsSpan()).TryParseTimeOnly(out var t2, "HHmm"));
        Assert.Equal(new TimeOnly(10, 30), t2);
        Assert.True(new CsvColumn<char>("1030".AsSpan()).TryParseTimeOnly(out var t3, "HHmm", CultureInfo.InvariantCulture));
        Assert.Equal(new TimeOnly(10, 30), t3);
    }

    [Fact]
    public void Char_TryParseTimeZoneInfo_Ok_AndFail()
    {
        Assert.True(new CsvColumn<char>("UTC".AsSpan()).TryParseTimeZoneInfo(out var tz));
        Assert.NotNull(tz);
        Assert.False(new CsvColumn<char>("NoSuchZone".AsSpan()).TryParseTimeZoneInfo(out _));
    }

    [Fact]
    public void Char_TryParseGuid()
    {
        Assert.True(new CsvColumn<char>("12345678-1234-1234-1234-123456789012".AsSpan()).TryParseGuid(out var g));
        Assert.NotEqual(Guid.Empty, g);
    }

    [Fact]
    public void Char_ToString_ReturnsString()
    {
        Assert.Equal("hello", new CsvColumn<char>("hello".AsSpan()).ToString());
    }

    [Fact]
    public void Char_Parse_Generic()
    {
        var parsed = new CsvColumn<char>("7".AsSpan()).Parse<int>();
        Assert.Equal(7, parsed);
    }
}

/// <summary>
/// Tests <see cref="Utf8SpanParserFactory.GetParser{T}"/> for every supported type
/// (including nullable and enum variants) to exercise factory dispatch paths.
/// </summary>
#pragma warning disable IL2026, IL3050 // Fluent mapping uses reflection / MakeGenericMethod
[Trait("Category", "Unit")]
public class Utf8SpanParserFactoryTests
{
    private enum Color { Red, Green, Blue }

    [Fact]
    public void GetParser_String_Decodes()
    {
        var parser = Utf8SpanParserFactory.GetParser<string>();
        Assert.Equal("hi", parser("hi"u8, CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData(typeof(int))]
    [InlineData(typeof(long))]
    [InlineData(typeof(short))]
    [InlineData(typeof(byte))]
    [InlineData(typeof(decimal))]
    [InlineData(typeof(double))]
    [InlineData(typeof(float))]
    [InlineData(typeof(bool))]
    [InlineData(typeof(DateTime))]
    [InlineData(typeof(DateOnly))]
    [InlineData(typeof(TimeOnly))]
    [InlineData(typeof(Guid))]
    public void GetParser_ValueTypes_AndNullableVariants(Type t)
    {
        var method = typeof(Utf8SpanParserFactory).GetMethod(nameof(Utf8SpanParserFactory.GetParser))!;
        var valueParser = method.MakeGenericMethod(t).Invoke(null, null);
        Assert.NotNull(valueParser);
        var nullableT = typeof(Nullable<>).MakeGenericType(t);
        var nullableParser = method.MakeGenericMethod(nullableT).Invoke(null, null);
        Assert.NotNull(nullableParser);
    }

    [Fact]
    public void GetParser_Enum_AndNullableEnum()
    {
        var parser = Utf8SpanParserFactory.GetParser<Color>();
        Assert.Equal(Color.Red, parser("Red"u8, CultureInfo.InvariantCulture));
        var nparser = Utf8SpanParserFactory.GetParser<Color?>();
        Assert.Null(nparser(""u8, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void GetParser_Int_ParsesValid()
    {
        var parser = Utf8SpanParserFactory.GetParser<int>();
        Assert.Equal(42, parser("42"u8, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void GetParser_Int_ThrowsOnInvalid()
    {
        var parser = Utf8SpanParserFactory.GetParser<int>();
        Assert.Throws<FormatException>(() => parser("abc"u8, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void GetParser_Long_Short_Byte()
    {
        Assert.Equal(1L, Utf8SpanParserFactory.GetParser<long>()("1"u8, CultureInfo.InvariantCulture));
        Assert.Equal((short)1, Utf8SpanParserFactory.GetParser<short>()("1"u8, CultureInfo.InvariantCulture));
        Assert.Equal((byte)1, Utf8SpanParserFactory.GetParser<byte>()("1"u8, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void GetParser_Decimal_Double_Single()
    {
        Assert.Equal(3.14m, Utf8SpanParserFactory.GetParser<decimal>()("3.14"u8, CultureInfo.InvariantCulture));
        Assert.Equal(3.14, Utf8SpanParserFactory.GetParser<double>()("3.14"u8, CultureInfo.InvariantCulture), 5);
        Assert.Equal(3.14f, Utf8SpanParserFactory.GetParser<float>()("3.14"u8, CultureInfo.InvariantCulture), 4);
    }

    [Fact]
    public void GetParser_Bool_DateTime_DateOnly_TimeOnly_Guid()
    {
        Assert.True(Utf8SpanParserFactory.GetParser<bool>()("true"u8, CultureInfo.InvariantCulture));
        Assert.Equal(new DateTime(2024, 1, 15),
            Utf8SpanParserFactory.GetParser<DateTime>()("2024-01-15"u8, CultureInfo.InvariantCulture));
        Assert.Equal(new DateOnly(2024, 1, 15),
            Utf8SpanParserFactory.GetParser<DateOnly>()("2024-01-15"u8, CultureInfo.InvariantCulture));
        Assert.Equal(new TimeOnly(10, 30),
            Utf8SpanParserFactory.GetParser<TimeOnly>()("10:30"u8, CultureInfo.InvariantCulture));
        Assert.NotEqual(Guid.Empty,
            Utf8SpanParserFactory.GetParser<Guid>()("12345678-1234-1234-1234-123456789012"u8, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void GetParser_NullableInt_EmptyReturnsNull()
    {
        var parser = Utf8SpanParserFactory.GetParser<int?>();
        Assert.Null(parser(""u8, CultureInfo.InvariantCulture));
        Assert.Null(parser("   "u8, CultureInfo.InvariantCulture));
        Assert.Equal(42, parser("42"u8, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void GetParser_UnsupportedType_Throws()
    {
        Assert.Throws<NotSupportedException>(Utf8SpanParserFactory.GetParser<Uri>);
    }
}
#pragma warning restore IL2026, IL3050
