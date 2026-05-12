using System.Globalization;
using HeroParser.FixedWidths;
using HeroParser.SeparatedValues.Mapping;
using Xunit;

namespace HeroParser.Tests.Internal;

/// <summary>
/// Direct unit tests for FixedWidthByteSpanColumn and FixedWidthCharSpanColumn TryParse* methods
/// which previously had very low coverage (9-17%).
/// </summary>
[Trait("Category", "Unit")]
public class FixedWidthByteSpanColumnTests
{
    [Fact]
    public void Int32_16_64_Uint16_32_64()
    {
        Assert.True(new FixedWidthByteSpanColumn("42"u8).TryParseInt32(out int i32));
        Assert.Equal(42, i32);
        Assert.True(new FixedWidthByteSpanColumn("42"u8).TryParseInt16(out short i16));
        Assert.Equal(42, i16);
        Assert.True(new FixedWidthByteSpanColumn("42"u8).TryParseInt64(out long i64));
        Assert.Equal(42L, i64);
        Assert.True(new FixedWidthByteSpanColumn("42"u8).TryParseUInt16(out ushort u16));
        Assert.Equal(42, u16);
        Assert.True(new FixedWidthByteSpanColumn("42"u8).TryParseUInt32(out uint u32));
        Assert.Equal(42u, u32);
        Assert.True(new FixedWidthByteSpanColumn("42"u8).TryParseUInt64(out ulong u64));
        Assert.Equal(42ul, u64);
    }

    [Fact]
    public void Byte_Sbyte()
    {
        Assert.True(new FixedWidthByteSpanColumn("10"u8).TryParseByte(out byte b));
        Assert.Equal(10, b);
        Assert.True(new FixedWidthByteSpanColumn("-5"u8).TryParseSByte(out sbyte sb));
        Assert.Equal(-5, sb);
    }

    [Fact]
    public void FloatTypes()
    {
        Assert.True(new FixedWidthByteSpanColumn("3.14"u8).TryParseDouble(out var d));
        Assert.Equal(3.14, d, 5);
        Assert.True(new FixedWidthByteSpanColumn("3.14"u8).TryParseSingle(out var f));
        Assert.Equal(3.14f, f, 4);
        Assert.True(new FixedWidthByteSpanColumn("3.14"u8).TryParseDecimal(out var m));
        Assert.Equal(3.14m, m);
    }

    [Fact]
    public void Boolean()
    {
        Assert.True(new FixedWidthByteSpanColumn("true"u8).TryParseBoolean(out var b));
        Assert.True(b);
    }

    [Fact]
    public void Guid()
    {
        Assert.True(new FixedWidthByteSpanColumn("12345678-1234-1234-1234-123456789012"u8).TryParseGuid(out var g));
        Assert.NotEqual(System.Guid.Empty, g);
    }

    [Fact]
    public void DateTime_Variants()
    {
        Assert.True(new FixedWidthByteSpanColumn("20240115"u8).TryParseDateTime(out var dt1, "yyyyMMdd"));
        Assert.Equal(new DateTime(2024, 1, 15), dt1);
        Assert.True(new FixedWidthByteSpanColumn("20240115"u8).TryParseDateTime(out var dt2, "yyyyMMdd", CultureInfo.InvariantCulture));
        Assert.Equal(new DateTime(2024, 1, 15), dt2);
    }

    [Fact]
    public void DateTimeOffset_Variants()
    {
        Assert.True(new FixedWidthByteSpanColumn("2024-01-15T00:00:00+00:00"u8).TryParseDateTimeOffset(out var dto));
        Assert.Equal(2024, dto.Year);
        Assert.True(new FixedWidthByteSpanColumn("20240115"u8).TryParseDateTimeOffset(out var dto2, "yyyyMMdd"));
        Assert.Equal(2024, dto2.Year);
    }

    [Fact]
    public void DateOnly_Variants()
    {
        Assert.True(new FixedWidthByteSpanColumn("2024-01-15"u8).TryParseDateOnly(out var d1));
        Assert.True(new FixedWidthByteSpanColumn("20240115"u8).TryParseDateOnly(out var d2, "yyyyMMdd"));
        Assert.Equal(d1, d2);
    }

    [Fact]
    public void TimeOnly_Variants()
    {
        Assert.True(new FixedWidthByteSpanColumn("10:30"u8).TryParseTimeOnly(out var t1));
        Assert.True(new FixedWidthByteSpanColumn("1030"u8).TryParseTimeOnly(out var t2, "HHmm"));
        Assert.Equal(t1, t2);
    }

    [Fact]
    public void Parse_And_TryParse_Generic()
    {
        var parsed = new FixedWidthByteSpanColumn("42"u8).Parse<int>();
        Assert.Equal(42, parsed);
        Assert.True(new FixedWidthByteSpanColumn("42"u8).TryParse<int>(out int v));
        Assert.Equal(42, v);
    }

    [Fact]
    public void ToString_ByteSpan_Length_IsEmpty()
    {
        var col = new FixedWidthByteSpanColumn("café"u8);
        Assert.Equal("café", col.ToString());
        Assert.Equal("café"u8.ToArray().Length, col.Length);
        Assert.False(col.IsEmpty);
        Assert.True(new FixedWidthByteSpanColumn(default).IsEmpty);
    }
}

[Trait("Category", "Unit")]
public class FixedWidthCharSpanColumnTests
{
    [Fact]
    public void Int_Float_Decimal_Boolean_Guid()
    {
        Assert.True(new FixedWidthCharSpanColumn("42".AsSpan()).TryParseInt32(out int i));
        Assert.Equal(42, i);
        Assert.True(new FixedWidthCharSpanColumn("42".AsSpan()).TryParseInt64(out long l));
        Assert.Equal(42L, l);
        Assert.True(new FixedWidthCharSpanColumn("3.14".AsSpan()).TryParseDouble(out var d));
        Assert.Equal(3.14, d, 5);
        Assert.True(new FixedWidthCharSpanColumn("3.14".AsSpan()).TryParseDecimal(out var m));
        Assert.Equal(3.14m, m);
        Assert.True(new FixedWidthCharSpanColumn("true".AsSpan()).TryParseBoolean(out var b));
        Assert.True(b);
        Assert.True(new FixedWidthCharSpanColumn("12345678-1234-1234-1234-123456789012".AsSpan()).TryParseGuid(out var g));
        Assert.NotEqual(System.Guid.Empty, g);
    }

    [Fact]
    public void DateTime_DateOnly_TimeOnly_Variants()
    {
        Assert.True(new FixedWidthCharSpanColumn("2024-01-15".AsSpan()).TryParseDateTime(out var dt));
        Assert.Equal(new DateTime(2024, 1, 15), dt);
        Assert.True(new FixedWidthCharSpanColumn("20240115".AsSpan()).TryParseDateTime(out var dt2, "yyyyMMdd"));
        Assert.Equal(dt, dt2);
        Assert.True(new FixedWidthCharSpanColumn("2024-01-15".AsSpan()).TryParseDateOnly(out var date));
        Assert.Equal(new DateOnly(2024, 1, 15), date);
        Assert.True(new FixedWidthCharSpanColumn("10:30".AsSpan()).TryParseTimeOnly(out var time));
        Assert.Equal(new TimeOnly(10, 30), time);
    }

    [Fact]
    public void CharSpan_Length_IsEmpty_ToString()
    {
        var col = new FixedWidthCharSpanColumn("hello".AsSpan());
        Assert.Equal("hello", col.ToString());
        Assert.Equal(5, col.Length);
        Assert.False(col.IsEmpty);
        Assert.True(new FixedWidthCharSpanColumn(default).IsEmpty);
    }

    [Fact]
    public void Parse_And_TryParse_Generic()
    {
        Assert.Equal(42, new FixedWidthCharSpanColumn("42".AsSpan()).Parse<int>());
        Assert.True(new FixedWidthCharSpanColumn("42".AsSpan()).TryParse<int>(out int v));
        Assert.Equal(42, v);
    }
}

[Trait("Category", "Unit")]
public class FixedWidthExceptionTests
{
    [Fact]
    public void Ctor_ErrorCodeAndMessage()
    {
        var ex = new FixedWidthException(FixedWidthErrorCode.FieldOutOfBounds, "msg");
        Assert.Equal(FixedWidthErrorCode.FieldOutOfBounds, ex.ErrorCode);
        Assert.Equal("msg", ex.Message);
        Assert.Null(ex.Record);
    }

    [Fact]
    public void Ctor_WithRecord()
    {
        var ex = new FixedWidthException(FixedWidthErrorCode.FieldOutOfBounds, "msg", record: 5);
        Assert.Equal(5, ex.Record);
    }

    [Fact]
    public void Ctor_WithInnerException()
    {
        var inner = new InvalidOperationException("inner");
        var ex = new FixedWidthException(FixedWidthErrorCode.TooManyRecords, "msg", innerException: inner);
        Assert.Same(inner, ex.InnerException);
    }

    [Fact]
    public void Ctor_WithFieldDetails()
    {
        var ex = new FixedWidthException(
            FixedWidthErrorCode.FieldOutOfBounds, "msg", record: 3, fieldStart: 10, fieldLength: 5, fieldValue: "X");
        Assert.Equal(3, ex.Record);
        Assert.Equal(10, ex.FieldStart);
        Assert.Equal(5, ex.FieldLength);
        Assert.Equal("X", ex.FieldValue);
    }

    [Fact]
    public void Ctor_WithSourceLineNumber()
    {
        var ex = new FixedWidthException(FixedWidthErrorCode.TooManyRecords, "msg", record: 3, sourceLineNumber: 10);
        Assert.Equal(3, ex.Record);
        Assert.Equal(10, ex.SourceLineNumber);
    }

    [Fact]
    public void Ctor_WithFieldNameAndDetails()
    {
        var ex = new FixedWidthException(
            FixedWidthErrorCode.FieldOutOfBounds, "msg", record: 3,
            fieldName: "Age", fieldStart: 10, fieldLength: 5, fieldValue: "X");
        Assert.Equal("Age", ex.FieldName);
        Assert.Equal(3, ex.Record);
    }

    [Fact]
    public void Ctor_FullDetails()
    {
        var ex = new FixedWidthException(
            FixedWidthErrorCode.FieldOutOfBounds, "msg", record: 3, sourceLineNumber: 7,
            fieldName: "Age", fieldStart: 10, fieldLength: 5, fieldValue: "X");
        Assert.Equal(3, ex.Record);
        Assert.Equal(7, ex.SourceLineNumber);
        Assert.Equal("Age", ex.FieldName);
        Assert.Equal(10, ex.FieldStart);
        Assert.Equal(5, ex.FieldLength);
        Assert.Equal("X", ex.FieldValue);
    }
}

/// <summary>
/// Tests <see cref="SpanParserFactory.GetParser{T}"/> for every supported type variant,
/// mirroring the Utf8SpanParserFactory coverage but for the char-based code path.
/// </summary>
#pragma warning disable IL2026, IL3050
[Trait("Category", "Unit")]
public class SpanParserFactoryTests
{
    private enum Size { Small, Medium, Large }

    [Fact]
    public void GetParser_String()
    {
        var parser = SpanParserFactory.GetParser<string>();
        Assert.Equal("hi", parser("hi".AsSpan(), CultureInfo.InvariantCulture));
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
    public void GetParser_Value_AndNullable(Type t)
    {
        var method = typeof(SpanParserFactory).GetMethod(nameof(SpanParserFactory.GetParser))!;
        Assert.NotNull(method.MakeGenericMethod(t).Invoke(null, null));
        Assert.NotNull(method.MakeGenericMethod(typeof(Nullable<>).MakeGenericType(t)).Invoke(null, null));
    }

    [Fact]
    public void GetParser_Int_Parses()
    {
        var parser = SpanParserFactory.GetParser<int>();
        Assert.Equal(42, parser("42".AsSpan(), CultureInfo.InvariantCulture));
    }

    [Fact]
    public void GetParser_NullableInt_EmptyReturnsNull()
    {
        var parser = SpanParserFactory.GetParser<int?>();
        Assert.Null(parser("".AsSpan(), CultureInfo.InvariantCulture));
        Assert.Null(parser("   ".AsSpan(), CultureInfo.InvariantCulture));
        Assert.Equal(5, parser("5".AsSpan(), CultureInfo.InvariantCulture));
    }

    [Fact]
    public void GetParser_Enum_And_Nullable()
    {
        var parser = SpanParserFactory.GetParser<Size>();
        Assert.Equal(Size.Large, parser("Large".AsSpan(), CultureInfo.InvariantCulture));
        var nparser = SpanParserFactory.GetParser<Size?>();
        Assert.Null(nparser("".AsSpan(), CultureInfo.InvariantCulture));
    }

    [Fact]
    public void GetParser_Bool_DateTime_DateOnly_TimeOnly_Guid()
    {
        Assert.True(SpanParserFactory.GetParser<bool>()("true".AsSpan(), CultureInfo.InvariantCulture));
        Assert.Equal(new DateTime(2024, 1, 15),
            SpanParserFactory.GetParser<DateTime>()("2024-01-15".AsSpan(), CultureInfo.InvariantCulture));
        Assert.Equal(new DateOnly(2024, 1, 15),
            SpanParserFactory.GetParser<DateOnly>()("2024-01-15".AsSpan(), CultureInfo.InvariantCulture));
        Assert.Equal(new TimeOnly(10, 30),
            SpanParserFactory.GetParser<TimeOnly>()("10:30".AsSpan(), CultureInfo.InvariantCulture));
        Assert.NotEqual(System.Guid.Empty,
            SpanParserFactory.GetParser<Guid>()("12345678-1234-1234-1234-123456789012".AsSpan(), CultureInfo.InvariantCulture));
    }

    [Fact]
    public void GetParser_UnsupportedType_Throws()
    {
        Assert.Throws<NotSupportedException>(SpanParserFactory.GetParser<Uri>);
    }
}
#pragma warning restore IL2026, IL3050
