using System.Globalization;
using HeroParser.Vectors;
using Xunit;

namespace HeroParser.Tests;

public class VectorParserTests
{
    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ParseFloats_BracketedCommaSeparated()
    {
        float[] result = VectorParser.ParseFloats("[0.1, 0.2, 0.3]");
        Assert.Equal([0.1f, 0.2f, 0.3f], result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ParseFloats_NoBrackets()
    {
        float[] result = VectorParser.ParseFloats("0.1,0.2,0.3");
        Assert.Equal([0.1f, 0.2f, 0.3f], result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ParseFloats_SpaceSeparated()
    {
        float[] result = VectorParser.ParseFloats("0.1 0.2 0.3");
        Assert.Equal([0.1f, 0.2f, 0.3f], result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ParseFloats_SemicolonSeparated()
    {
        float[] result = VectorParser.ParseFloats("0.1;0.2;0.3");
        Assert.Equal([0.1f, 0.2f, 0.3f], result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ParseFloats_MultipleWhitespaceCollapsed()
    {
        float[] result = VectorParser.ParseFloats("[0.1  0.2,   0.3]");
        Assert.Equal([0.1f, 0.2f, 0.3f], result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ParseFloats_EmptyBrackets_YieldsEmptyArray()
    {
        float[] result = VectorParser.ParseFloats("[]");
        Assert.Empty(result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ParseFloats_EmptyString_YieldsEmptyArray()
    {
        float[] result = VectorParser.ParseFloats(string.Empty);
        Assert.Empty(result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ParseFloats_NegativeAndScientific()
    {
        float[] result = VectorParser.ParseFloats("[-1.5, 2.0e-3, 0.0]");
        Assert.Equal([-1.5f, 0.002f, 0.0f], result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void TryParseFloats_GarbageInput_ReturnsFalse()
    {
        bool ok = VectorParser.TryParseFloats("[foo, bar]", out float[]? result);
        Assert.False(ok);
        Assert.Null(result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ParseFloats_GarbageInput_Throws()
    {
        Assert.Throws<FormatException>(() => VectorParser.ParseFloats("not a vector"));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ParseDoubles_RoundtripsFullPrecision()
    {
        double[] result = VectorParser.ParseDoubles("[0.123456789012345, -1.0e100]");
        Assert.Equal(2, result.Length);
        Assert.Equal(0.123456789012345, result[0], precision: 15);
        Assert.Equal(-1.0e100, result[1]);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ParseFloats_RespectsCulture()
    {
        // German locale uses comma as decimal separator. Comma is also a separator in our grammar, so
        // German-formatted values must use whitespace between elements to disambiguate.
        var deDe = CultureInfo.GetCultureInfo("de-DE");
        bool ok = VectorParser.TryParseFloats("0,1 0,2 0,3", deDe, out float[]? result);
        Assert.True(ok);
        Assert.NotNull(result);
        Assert.Equal([0.1f, 0.2f, 0.3f], result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void VectorConverters_FloatArray_IsExposedForRegistration()
    {
        // The static converter instance is the integration point with CsvRecordReaderBuilder.RegisterConverter.
        // Verifying the delegate is callable and produces the expected result is sufficient — exhaustive
        // builder-side integration is exercised by CsvReaderBuilderTests.
        bool ok = VectorConverters.FloatArray("[1, 2, 3]", CultureInfo.InvariantCulture, null, out float[]? result);
        Assert.True(ok);
        Assert.NotNull(result);
        Assert.Equal([1f, 2f, 3f], result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void VectorConverters_DoubleArray_HandlesScientificNotation()
    {
        bool ok = VectorConverters.DoubleArray("1e10, 2e-5", CultureInfo.InvariantCulture, null, out double[]? result);
        Assert.True(ok);
        Assert.NotNull(result);
        Assert.Equal([1e10, 2e-5], result);
    }
}
