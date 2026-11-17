using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Mapping;
using HeroParser.Tests.Excel;
using HeroParser.Tests.Fixtures.Excel;
using Xunit;

namespace HeroParser.Tests.Errors;

/// <summary>
/// A parse failure's message names the row, column and value once each; the value is truncated by
/// <see cref="CsvException"/> and must not also be interpolated, untruncated, into the message text.
/// </summary>
[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
public class ParseErrorMessageTests
{
    private const string CSV = "Name,Price,Quantity\nWidget,abc,1\n";

    [Fact]
    public void GeneratedCharBinder_ParseError_StatesValueOnce()
    {
        var ex = Assert.Throws<CsvException>(() =>
        {
            foreach (var _ in Csv.Read<SimpleProduct>().FromText(CSV)) { }
        });

        AssertStatedOnce(ex, "Row 2, Column 2:", "Value: 'abc'");
        Assert.Equal("abc", ex.FieldValue);
    }

    [Fact]
    public void GeneratedByteBinder_ParseError_StatesValueOnce()
    {
        var ex = Assert.Throws<CsvException>(() =>
        {
            foreach (var _ in Csv.Read<SimpleProduct>().FromText(CSV, out _)) { }
        });

        AssertStatedOnce(ex, "Row 2, Column 2:", "Value: 'abc'");
    }

    [Fact]
    public void DescriptorBinder_ParseError_StatesValueOnce()
    {
        var map = new CsvMap<SimpleProduct>();
        map.Map(p => p.Name, c => c.Name("Name"))
           .Map(p => p.Price, c => c.Name("Price"))
           .Map(p => p.Quantity, c => c.Name("Quantity"));

        var ex = Assert.Throws<CsvException>(() =>
        {
            foreach (var _ in Csv.Read<SimpleProduct>().WithMap(map).FromText(CSV)) { }
        });

        AssertStatedOnce(ex, "Row 2, Column 2:", "Value: 'abc'");
        Assert.Contains("Price", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DescriptorBinder_LongValue_IsTruncatedInMessage()
    {
        var longValue = new string('x', 5000);
        var map = new CsvMap<SimpleProduct>();
        map.Map(p => p.Price, c => c.Name("Price"));

        var ex = Assert.Throws<CsvException>(() =>
        {
            foreach (var _ in Csv.Read<SimpleProduct>().WithMap(map).FromText($"Price\n{longValue}\n")) { }
        });

        Assert.True(ex.Message.Length < 300, $"Message is {ex.Message.Length} characters long.");
    }

    [Fact]
    public void ExcelRead_ParseError_StatesValueOnce()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["Name", "Price", "Quantity"],
            ["Widget", "abc", "1"]
        ]);

        var ex = Assert.Throws<CsvException>(() => HeroParser.Excel.Read<SimpleProduct>().FromStream(xlsx));

        AssertStatedOnce(ex, "Row 2, Column 2:", "Value: 'abc'");
    }

    private static void AssertStatedOnce(CsvException ex, params string[] fragments)
    {
        foreach (var fragment in fragments)
        {
            Assert.Equal(1, CountOccurrences(ex.Message, fragment));
        }

        Assert.Equal(1, CountOccurrences(ex.Message, "abc"));
    }

    private static int CountOccurrences(string text, string fragment)
    {
        int count = 0;
        for (int i = text.IndexOf(fragment, StringComparison.Ordinal); i >= 0; i = text.IndexOf(fragment, i + fragment.Length, StringComparison.Ordinal))
            count++;
        return count;
    }
}
