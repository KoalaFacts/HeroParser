using System.Globalization;
using HeroParser.SeparatedValues.Reading.Records.MultiSchema;
using HeroParser.SeparatedValues.Reading.Shared;
using Xunit;

namespace HeroParser.Tests;

/// <summary>
/// Integer discriminators are matched against the raw CSV text, which is machine-formatted ASCII.
/// They must be formatted with the invariant culture, not the current one: cultures such as sv-SE
/// (ICU) use U+2212 MINUS SIGN as the negative sign, so "-1" would otherwise never match.
/// </summary>
[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
public class IntegerDiscriminatorCultureTests
{
    private static CultureInfo CreateUnicodeMinusCulture()
    {
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.NumberFormat.NegativeSign = "−";
        return culture;
    }

    private static T WithCulture<T>(CultureInfo culture, Func<T> action)
    {
        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = culture;
        try
        {
            return action();
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void MapRecord_NegativeIntegerDiscriminator_MatchesAsciiMinusUnderUnicodeMinusCulture()
    {
        const string csv = """
            RecordType,Value1
            -1,Header
            5,Item
            """;

        var results = WithCulture(CreateUnicodeMinusCulture(), () =>
        {
            var list = new List<object>();
            foreach (var record in Csv.Read()
                .WithMultiSchema()
                .WithDiscriminator("RecordType")
                .MapRecord<MultiSchemaTests.FileHeader>(-1)
                .MapRecord<MultiSchemaTests.BatchItem>(5)
                .OnUnmatchedRow(UnmatchedRowBehavior.Throw)
                .FromText(csv))
            {
                list.Add(record);
            }
            return list;
        });

        Assert.Equal(2, results.Count);
        Assert.Equal("Header", Assert.IsType<MultiSchemaTests.FileHeader>(results[0]).Value1);
        Assert.Equal("Item", Assert.IsType<MultiSchemaTests.BatchItem>(results[1]).Value1);
    }

    [Fact]
    public void CsvSchemaMappingAttribute_IntegerConstructor_FormatsWithInvariantCulture()
    {
        var attribute = WithCulture(CreateUnicodeMinusCulture(), () => new CsvSchemaMappingAttribute(-7, typeof(object)));

        Assert.Equal("-7", attribute.Discriminator);
    }

    [Fact]
    public void DiscriminatorKey_FromInt_FormatsWithInvariantCulture()
    {
        var key = WithCulture(CreateUnicodeMinusCulture(), () => DiscriminatorKey.FromInt(-1));

        Assert.True(DiscriminatorKey.TryCreate("-1".AsSpan(), out var expected));
        Assert.Equal(expected, key);
    }
}
