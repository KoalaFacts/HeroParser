using System.Globalization;
using HeroParser.FixedWidths;
using HeroParser.FixedWidths.Writing;
using Xunit;

namespace HeroParser.Tests.Internal;

/// <summary>
/// Covers <see cref="FixedWidthStreamWriter.WriteField(object?, int, FieldAlignment?, char?, string?)"/>.
///
/// That overload dispatches on the runtime type of a boxed value, and its switch is what
/// the record writer funnels every property through. None of its arms had run, so a type
/// landing on the wrong one — or on the ToString fallback, losing its format — would have
/// gone unnoticed.
/// </summary>
[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
public class FixedWidthStreamWriterValueTests
{
    private static string Write(object? value, int width = 12, string? format = null, FieldAlignment? alignment = null, char? padChar = null)
    {
        using var text = new StringWriter(CultureInfo.InvariantCulture);
        using (var writer = new FixedWidthStreamWriter(text, leaveOpen: true))
        {
            writer.WriteField(value, width, alignment, padChar, format);
            writer.EndRow();
        }
        return text.ToString().TrimEnd('\r', '\n');
    }

    [Fact]
    public void String_IsWrittenAsIs() => Assert.Equal("abc         ", Write("abc"));

    [Fact]
    public void Null_UsesTheConfiguredNullValue() => Assert.Equal(new string(' ', 12), Write(null));

    [Theory]
    [InlineData(42, "42")]
    [InlineData(9007199254740993L, "9007199254740993")]
    [InlineData(1.5d, "1.5")]
    [InlineData(2.5f, "2.5")]
    [InlineData(true, "True")]
    [InlineData(false, "False")]
    [InlineData((byte)7, "7")]
    [InlineData((short)-3, "-3")]
    [InlineData(8u, "8")]
    [InlineData(9ul, "9")]
    public void PrimitiveTypes_LandOnTheirOwnSwitchArm(object value, string expected)
        => Assert.Equal(expected, Write(value, width: 20).TrimEnd());

    [Fact]
    public void Decimal_IsFormatted() => Assert.Equal("3.25", Write(3.25m, width: 20).TrimEnd());

    [Fact]
    public void DateTime_HonoursAFormat()
        => Assert.Equal("2024-03-17", Write(new DateTime(2024, 3, 17, 0, 0, 0, DateTimeKind.Unspecified), width: 20, format: "yyyy-MM-dd").TrimEnd());

    [Fact]
    public void DateTimeOffset_HonoursAFormat()
        => Assert.Equal("2024-03-17", Write(new DateTimeOffset(2024, 3, 17, 0, 0, 0, TimeSpan.Zero), width: 20, format: "yyyy-MM-dd").TrimEnd());

    [Fact]
    public void Guid_IsFormatted()
    {
        var value = new Guid("11112222-3333-4444-5555-666677778888");
        Assert.Equal(value.ToString("N", CultureInfo.InvariantCulture), Write(value, width: 40, format: "N").TrimEnd());
    }

    [Fact]
    public void SpanFormattableType_UsesTheSpanPath()
    {
        // TimeSpan is ISpanFormattable but has no dedicated switch arm, so it exercises
        // the generic span-formatting fallback rather than the ToString one.
        Assert.Equal("01:02:03", Write(new TimeSpan(1, 2, 3), width: 20).TrimEnd());
    }

    [Fact]
    public void FormattableType_UsesItsFormat()
    {
        // An IFormattable that is not ISpanFormattable falls back to ToString(format).
        Assert.Equal("formatted:X", Write(new OnlyFormattable(), width: 20, format: "X").TrimEnd());
    }

    [Fact]
    public void PlainObject_FallsBackToToString()
        => Assert.Equal("plain", Write(new PlainObject(), width: 20).TrimEnd());

    [Fact]
    public void Alignment_AndPadCharacterAreHonoured()
    {
        Assert.Equal("00000000abc", Write("abc", width: 11, alignment: FieldAlignment.Right, padChar: '0'));
        Assert.Equal("abc00000000", Write("abc", width: 11, alignment: FieldAlignment.Left, padChar: '0'));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveWidth_IsRejected(int width)
    {
        using var text = new StringWriter(CultureInfo.InvariantCulture);
        using var writer = new FixedWidthStreamWriter(text, leaveOpen: true);

        Assert.Throws<ArgumentOutOfRangeException>(() => writer.WriteField("x", width));
        Assert.Throws<ArgumentOutOfRangeException>(() => writer.WriteField("x".AsSpan(), width));
    }

    [Fact]
    public void Disposed_WriterRejectsFurtherWrites()
    {
        using var text = new StringWriter(CultureInfo.InvariantCulture);
        var writer = new FixedWidthStreamWriter(text, leaveOpen: true);
        writer.Dispose();

        Assert.Throws<ObjectDisposedException>(() => writer.WriteField("x", 4));
    }

    private sealed class OnlyFormattable : IFormattable
    {
        public string ToString(string? format, IFormatProvider? formatProvider) => $"formatted:{format}";
    }

    private sealed class PlainObject
    {
        public override string ToString() => "plain";
    }
}
