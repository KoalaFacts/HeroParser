using System.Globalization;
using System.Text;
using HeroParser.FixedWidths;
using HeroParser.FixedWidths.Mapping;
using Xunit;

namespace HeroParser.Tests.Culture;

/// <summary>
/// Fixed-Width counterpart of <see cref="AsciiMinusCultureTests"/>: under fa-IR, ar-SA and he-IL (negative sign is a
/// minus with bidirectional marks) every numeric column must accept the ASCII <c>-</c> as well as the culture's own sign,
/// on the generated, runtime and fluent-map binders, for text and UTF-8 stream input.
/// </summary>
[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
public class FixedWidthAsciiMinusCultureTests
{
    public static TheoryData<string> BidiMinusCultures => ["fa-IR", "ar-SA", "he-IL"];

    [GenerateBinder]
    public class GeneratedLine
    {
        [PositionalMap(Start = 0, Length = 8)]
        public string Name { get; set; } = "";

        [PositionalMap(Start = 8, Length = 6, Alignment = FieldAlignment.Right)]
        public int Quantity { get; set; }

        [PositionalMap(Start = 14, Length = 8, Alignment = FieldAlignment.Right)]
        public decimal Price { get; set; }

        [PositionalMap(Start = 22, Length = 8, Alignment = FieldAlignment.Right)]
        public double? Ratio { get; set; }
    }

    public class RuntimeLine
    {
        [PositionalMap(Start = 0, Length = 8)]
        public string Name { get; set; } = "";

        [PositionalMap(Start = 8, Length = 6, Alignment = FieldAlignment.Right)]
        public int Quantity { get; set; }

        [PositionalMap(Start = 14, Length = 8, Alignment = FieldAlignment.Right)]
        public decimal Price { get; set; }

        [PositionalMap(Start = 22, Length = 8, Alignment = FieldAlignment.Right)]
        public double? Ratio { get; set; }
    }

    public class MappedLine
    {
        public string Name { get; set; } = "";
        public int Quantity { get; set; }
        public decimal Price { get; set; }
        public double? Ratio { get; set; }
    }

    private static string Text(string cultureName, string sign = "-")
    {
        var separator = CultureInfo.GetCultureInfo(cultureName).NumberFormat.NumberDecimalSeparator;
        return "Widget  "
            + $"{sign}2".PadLeft(6)
            + $"{sign}1{separator}5".PadLeft(8)
            + $"{sign}0{separator}25".PadLeft(8)
            + "\n";
    }

    private static FixedWidthMap<MappedLine> Map()
    {
        var map = new FixedWidthMap<MappedLine>();
        map.Map(r => r.Name, c => c.Start(0).Length(8))
           .Map(r => r.Quantity, c => c.Start(8).Length(6).Alignment(FieldAlignment.Right))
           .Map(r => r.Price, c => c.Start(14).Length(8).Alignment(FieldAlignment.Right))
           .Map(r => r.Ratio, c => c.Start(22).Length(8).Alignment(FieldAlignment.Right));
        return map;
    }

    private static void AssertValues(int quantity, decimal price, double? ratio)
    {
        Assert.Equal(-2, quantity);
        Assert.Equal(-1.5m, price);
        Assert.Equal(-0.25, ratio);
    }

    [Theory]
    [MemberData(nameof(BidiMinusCultures))]
    public void GeneratedBinder_FromText_AsciiMinus_Parses(string cultureName)
    {
        var record = Assert.Single(FixedWidth.Read<GeneratedLine>().WithCulture(cultureName).FromText(Text(cultureName)).Records);
        AssertValues(record.Quantity, record.Price, record.Ratio);
    }

    [Theory]
    [MemberData(nameof(BidiMinusCultures))]
    public void GeneratedBinder_FromStream_AsciiMinus_Parses(string cultureName)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(Text(cultureName)));
        var record = Assert.Single(FixedWidth.Read<GeneratedLine>().WithCulture(cultureName).FromStream(stream).Records);
        AssertValues(record.Quantity, record.Price, record.Ratio);
    }

    [Theory]
    [MemberData(nameof(BidiMinusCultures))]
    public void RuntimeBinder_FromText_AsciiMinus_Parses(string cultureName)
    {
        var record = Assert.Single(FixedWidth.Read<RuntimeLine>().WithCulture(cultureName).FromText(Text(cultureName)).Records);
        AssertValues(record.Quantity, record.Price, record.Ratio);
    }

    [Theory]
    [MemberData(nameof(BidiMinusCultures))]
    public void MapBinder_FromText_AsciiMinus_Parses(string cultureName)
    {
        var record = Assert.Single(FixedWidth.Read<MappedLine>().WithMap(Map()).WithCulture(cultureName).FromText(Text(cultureName)).Records);
        AssertValues(record.Quantity, record.Price, record.Ratio);
    }

    [Theory]
    [MemberData(nameof(BidiMinusCultures))]
    public void MapBinder_FromStream_AsciiMinus_Parses(string cultureName)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(Text(cultureName)));
        var record = Assert.Single(FixedWidth.Read<MappedLine>().WithMap(Map()).WithCulture(cultureName).FromStream(stream).Records);
        AssertValues(record.Quantity, record.Price, record.Ratio);
    }

    [Theory]
    [MemberData(nameof(BidiMinusCultures))]
    public void GeneratedBinder_CultureNegativeSign_StillParses(string cultureName)
    {
        var sign = CultureInfo.GetCultureInfo(cultureName).NumberFormat.NegativeSign;
        var record = Assert.Single(FixedWidth.Read<GeneratedLine>().WithCulture(cultureName).FromText(Text(cultureName, sign)).Records);
        AssertValues(record.Quantity, record.Price, record.Ratio);
    }

    [Fact]
    public void GeneratedBinder_HyphenInsideValue_StillFails()
    {
        var text = "Widget  " + "1-2".PadLeft(6) + "1".PadLeft(8) + "1".PadLeft(8) + "\n";
        Assert.ThrowsAny<Exception>(() => FixedWidth.Read<GeneratedLine>().WithCulture("he-IL").FromText(text));
    }
}
