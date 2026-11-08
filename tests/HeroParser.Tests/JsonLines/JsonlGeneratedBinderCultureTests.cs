using System.Globalization;
using HeroParser.JsonLines;
using Xunit;

namespace HeroParser.Tests.JsonLines;

/// <summary>
/// The source-generated JSONL binder must read <see cref="DateOnly"/> and <see cref="TimeOnly"/>
/// JSON strings the same way in every current culture, like System.Text.Json does.
/// </summary>
[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
[Collection("CultureSensitive")]
public class JsonlGeneratedBinderCultureTests
{
    [GenerateBinder]
    public class Shift
    {
        public DateOnly Day { get; set; }
        public TimeOnly Start { get; set; }
    }

    private const string JSONL = """{"Day":"2024-01-15","Start":"13:45:30"}""";

    private static Shift ReadUnder(string cultureName)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            using var reader = Jsonl.Read<Shift>().FromText(JSONL);
            return Assert.Single(reader.ToList());
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData("")] // invariant: control
    [InlineData("th-TH")] // Thai Buddhist calendar: year 2024 BE is 1481 CE
    [InlineData("de-DE")]
    public void GeneratedBinder_IsoDateAndTime_ReadIdenticallyInEveryCulture(string cultureName)
    {
        Shift shift = ReadUnder(cultureName);

        Assert.Equal(new DateOnly(2024, 1, 15), shift.Day);
        Assert.Equal(new TimeOnly(13, 45, 30), shift.Start);
    }

    [Fact]
    public void GeneratedBinder_MatchesSystemTextJson_UnderNonGregorianCulture()
    {
        var expected = System.Text.Json.JsonSerializer.Deserialize<Shift>(JSONL)!;

        Shift shift = ReadUnder("th-TH");

        Assert.Equal(expected.Day, shift.Day);
        Assert.Equal(expected.Start, shift.Start);
    }
}
