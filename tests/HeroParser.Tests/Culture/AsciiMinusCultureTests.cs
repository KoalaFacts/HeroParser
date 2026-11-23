using System.Globalization;
using HeroParser.SeparatedValues.Mapping;
using HeroParser.Tests.Excel;
using Xunit;

namespace HeroParser.Tests.Culture;

/// <summary>
/// fa-IR, ar-SA and he-IL write the negative sign as a minus with bidirectional marks. CSV text from those locales
/// (and .xlsx numeric cells) usually carries the ASCII <c>-</c>, which integer columns already accept; decimal and
/// floating-point columns must accept it too, alongside the culture's own sign.
/// </summary>
[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
public class AsciiMinusCultureTests
{
    public static TheoryData<string> BidiMinusCultures => ["fa-IR", "ar-SA", "he-IL"];

    private static string Csv(string cultureName, string sign = "-")
    {
        var separator = CultureInfo.GetCultureInfo(cultureName).NumberFormat.NumberDecimalSeparator;
        return $"Name,Price,Quantity\nWidget,{sign}1{separator}5,-2\n";
    }

    [Theory]
    [MemberData(nameof(BidiMinusCultures))]
    public void GeneratedCharBinder_AsciiMinus_ParsesNegativeDecimal(string cultureName)
    {
        var records = new List<SimpleProduct>();
        foreach (var record in HeroParser.Csv.Read<SimpleProduct>().WithCulture(cultureName).FromText(Csv(cultureName)))
            records.Add(record);

        Assert.Equal(-1.5m, Assert.Single(records).Price);
        Assert.Equal(-2, records[0].Quantity);
    }

    [Theory]
    [MemberData(nameof(BidiMinusCultures))]
    public void GeneratedByteBinder_AsciiMinus_ParsesNegativeDecimal(string cultureName)
    {
        var records = new List<SimpleProduct>();
        foreach (var record in HeroParser.Csv.Read<SimpleProduct>().WithCulture(cultureName).FromText(Csv(cultureName), out _))
            records.Add(record);

        Assert.Equal(-1.5m, Assert.Single(records).Price);
    }

    [Theory]
    [MemberData(nameof(BidiMinusCultures))]
    public void DescriptorBinder_AsciiMinus_ParsesNegativeDecimal(string cultureName)
    {
        var map = new CsvMap<SimpleProduct>();
        map.Map(p => p.Name, c => c.Name("Name"))
           .Map(p => p.Price, c => c.Name("Price"))
           .Map(p => p.Quantity, c => c.Name("Quantity"));

        var records = new List<SimpleProduct>();
        foreach (var record in HeroParser.Csv.Read<SimpleProduct>().WithMap(map).WithCulture(cultureName).FromText(Csv(cultureName)))
            records.Add(record);

        Assert.Equal(-1.5m, Assert.Single(records).Price);
        Assert.Equal(-2, records[0].Quantity);
    }

    [Theory]
    [MemberData(nameof(BidiMinusCultures))]
    public void GeneratedCharBinder_CultureNegativeSign_StillParses(string cultureName)
    {
        var sign = CultureInfo.GetCultureInfo(cultureName).NumberFormat.NegativeSign;

        var records = new List<SimpleProduct>();
        foreach (var record in HeroParser.Csv.Read<SimpleProduct>().WithCulture(cultureName).FromText(Csv(cultureName, sign)))
            records.Add(record);

        Assert.Equal(-1.5m, Assert.Single(records).Price);
    }

    [Fact]
    public void GeneratedCharBinder_HyphenInsideValue_StillFails()
    {
        Assert.ThrowsAny<Exception>(() =>
        {
            foreach (var _ in HeroParser.Csv.Read<SimpleProduct>().WithCulture("he-IL").FromText("Name,Price,Quantity\nWidget,1-5,2\n")) { }
        });
    }
}
