using System.Globalization;
using HeroParser.SeparatedValues.Reading.Rows;
using Xunit;

namespace HeroParser.Tests.Internal;

/// <summary>
/// Pins <see cref="CsvColumn{T}"/>'s behaviour for an element type it does not support.
///
/// The struct is generic over <c>unmanaged, IEquatable&lt;T&gt;</c> but only implements
/// <see cref="char"/> and <see cref="byte"/>; every accessor ends in a fallback for
/// anything else. Those fallbacks are the contract for a caller who instantiates the
/// type with something else, and none of them had ever run — so a fallback that threw
/// where its neighbours returned false would not have been noticed.
/// </summary>
[Trait("Category", "Unit")]
public class CsvColumnUnsupportedElementTests
{
    private static CsvColumn<int> Column() => new([1, 2, 3]);

    [Fact]
    public void SpanAccessors_StillWork()
    {
        var column = Column();
        Assert.Equal(3, column.Length);
        Assert.False(column.IsEmpty);
        Assert.Equal(1, column.Span[0]);
    }

    [Fact]
    public void EmptyColumn_ReportsEmpty() => Assert.True(new CsvColumn<int>([]).IsEmpty);

    [Fact]
    public void ToString_Throws() => Assert.Throws<NotSupportedException>(() => Column().ToString());

    [Fact]
    public void Parse_Throws() => Assert.Throws<NotSupportedException>(() => Column().Parse<int>());

    [Fact]
    public void TryParse_ReturnsFalse()
    {
        Assert.False(Column().TryParse<int>(out var result));
        Assert.Equal(0, result);
    }

    [Fact]
    public void EveryNumericTryParse_ReturnsFalse()
    {
        var column = Column();
        Assert.False(column.TryParseInt16(out _));
        Assert.False(column.TryParseInt32(out _));
        Assert.False(column.TryParseInt64(out _));
        Assert.False(column.TryParseUInt16(out _));
        Assert.False(column.TryParseUInt32(out _));
        Assert.False(column.TryParseUInt64(out _));
        Assert.False(column.TryParseByte(out _));
        Assert.False(column.TryParseSByte(out _));
        Assert.False(column.TryParseSingle(out _));
        Assert.False(column.TryParseDouble(out _));
        Assert.False(column.TryParseDecimal(out _));
    }

    [Fact]
    public void BooleanGuidAndEnumTryParse_ReturnFalse()
    {
        var column = Column();
        Assert.False(column.TryParseBoolean(out _));
        Assert.False(column.TryParseGuid(out _));
        Assert.False(column.TryParseEnum<DayOfWeek>(out _));
    }

    [Fact]
    public void EveryDateTryParse_ReturnsFalse()
    {
        var column = Column();
        Assert.False(column.TryParseDateTime(out _));
        Assert.False(column.TryParseDateTime(out _, CultureInfo.InvariantCulture));
        Assert.False(column.TryParseDateTime(out _, "yyyy-MM-dd", CultureInfo.InvariantCulture));
        Assert.False(column.TryParseDateTime(out _, "yyyy-MM-dd"));
        Assert.False(column.TryParseDateTimeOffset(out _));
        Assert.False(column.TryParseDateTimeOffset(out _, CultureInfo.InvariantCulture));
        Assert.False(column.TryParseDateTimeOffset(out _, "yyyy-MM-dd", CultureInfo.InvariantCulture));
        Assert.False(column.TryParseDateTimeOffset(out _, "yyyy-MM-dd"));
        Assert.False(column.TryParseDateOnly(out _));
        Assert.False(column.TryParseDateOnly(out _, CultureInfo.InvariantCulture));
        Assert.False(column.TryParseDateOnly(out _, "yyyy-MM-dd", CultureInfo.InvariantCulture));
        Assert.False(column.TryParseDateOnly(out _, "yyyy-MM-dd"));
        Assert.False(column.TryParseTimeOnly(out _));
        Assert.False(column.TryParseTimeOnly(out _, CultureInfo.InvariantCulture));
        Assert.False(column.TryParseTimeOnly(out _, "HH:mm", CultureInfo.InvariantCulture));
        Assert.False(column.TryParseTimeOnly(out _, "HH:mm"));
    }

    [Fact]
    public void TryParseTimeZoneInfo_ReturnsFalse() => Assert.False(Column().TryParseTimeZoneInfo(out _));

    [Fact]
    public void TryParseTimeZoneInfo_OnAnEmptyColumn_ReturnsFalse()
        => Assert.False(new CsvColumn<int>([]).TryParseTimeZoneInfo(out _));

    [Fact]
    public void EqualsString_ReturnsFalse()
    {
        Assert.False(Column().Equals("123"));
        Assert.False(Column().Equals(null));
    }

    [Fact]
    public void Unquote_ReturnsTheSpanUntouched()
    {
        // With no known quote character for the element type, there is nothing to strip.
        var column = Column();
        Assert.Equal(3, column.Unquote().Length);
        Assert.Equal(3, column.Unquote(0).Length);
    }

    [Fact]
    public void Unquote_WithAMatchingSentinel_StripsIt()
    {
        // The explicit overload is element-type agnostic: it strips whatever it is given.
        var column = new CsvColumn<int>([9, 1, 2, 9]);
        Assert.Equal(2, column.Unquote(9).Length);
    }

    [Fact]
    public void UnquoteToString_Throws()
    {
        Assert.Throws<NotSupportedException>(() => Column().UnquoteToString());
        Assert.Throws<NotSupportedException>(() => Column().UnquoteToString(0));
        Assert.Throws<NotSupportedException>(() => Column().UnquoteToString(0, null));
    }
}
