using System.Globalization;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Writing;
using Xunit;

namespace HeroParser.Tests.Internal;

/// <summary>
/// Covers <see cref="CsvStreamWriter"/>'s row overloads, its size limits, and every
/// injection-protection mode.
///
/// Injection protection is a security feature: a field starting with '=' is a formula to
/// a spreadsheet, and each mode is a different promise about what happens to it. Only the
/// default was exercised, so three of the four modes — including the one that rejects the
/// field outright — had never been shown to do anything.
/// </summary>
[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
public class CsvStreamWriterRowAndInjectionTests
{
    private static string Write(Action<CsvStreamWriter> write, CsvWriteOptions? options = null)
    {
        using var text = new StringWriter(CultureInfo.InvariantCulture);
        using (var writer = new CsvStreamWriter(text, options, leaveOpen: true))
        {
            write(writer);
        }
        return text.ToString().TrimEnd('\r', '\n');
    }

    // ---- row overloads ---------------------------------------------------------

    [Fact]
    public void WriteRow_Params_FormatsEveryValueType()
    {
        string row = Write(w => w.WriteRow(
            "text", 1, 2L, 3.5d, 4.5f, 5.25m, true, new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Unspecified), Guid.Empty, null));

        Assert.StartsWith("text,1,2,3.5,4.5,5.25,", row, StringComparison.Ordinal);
        Assert.Contains("2024", row, StringComparison.Ordinal);
        Assert.EndsWith(",", row, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteRow_Span_MatchesTheParamsOverload()
    {
        object?[] values = ["a", 1, true];
        Assert.Equal(Write(w => w.WriteRow(values)), Write(w => w.WriteRow(new ReadOnlySpan<object?>(values))));
    }

    [Fact]
    public void WriteRow_TooManyColumns_IsRejected()
    {
        var options = new CsvWriteOptions { MaxColumnCount = 2 };
        object?[] values = ["a", "b", "c"];

        var fromParams = Assert.Throws<CsvException>(() => Write(w => w.WriteRow(values), options));
        Assert.Equal(CsvErrorCode.TooManyColumnsWritten, fromParams.ErrorCode);

        var fromSpan = Assert.Throws<CsvException>(() => Write(w => w.WriteRow(new ReadOnlySpan<object?>(values)), options));
        Assert.Equal(CsvErrorCode.TooManyColumnsWritten, fromSpan.ErrorCode);
    }

    [Fact]
    public void WriteRow_NullValue_UsesTheConfiguredNullValue()
    {
        var options = new CsvWriteOptions { NullValue = "NULL" };
        object?[] values = ["a", null, "c"];

        Assert.Equal("a,NULL,c", Write(w => w.WriteRow(values), options));
    }

    // ---- limits ----------------------------------------------------------------

    [Fact]
    public void OversizedField_IsRejected()
    {
        var options = new CsvWriteOptions { MaxFieldSize = 4 };
        var ex = Assert.Throws<CsvException>(() => Write(w => w.WriteField("abcdefghij"), options));

        Assert.Equal(CsvErrorCode.FieldSizeExceeded, ex.ErrorCode);
    }

    [Fact]
    public void EmptyField_IsQuotedWhenEverythingIsQuoted()
    {
        var options = new CsvWriteOptions { QuoteStyle = QuoteStyle.Always };
        string row = Write(w =>
        {
            w.WriteField(string.Empty);
            w.WriteField("x");
            w.EndRow();
        }, options);

        Assert.Equal("\"\",\"x\"", row);
    }

    [Fact]
    public void EmptyField_IsBareByDefault()
    {
        string row = Write(w =>
        {
            w.WriteField(string.Empty);
            w.WriteField("x");
            w.EndRow();
        });

        Assert.Equal(",x", row);
    }

    // ---- injection protection --------------------------------------------------

    [Theory]
    [InlineData("=SUM(A1)")]
    [InlineData("@cmd")]
    [InlineData("+SUM(A1)")]
    [InlineData("-cmd")]
    [InlineData("\tlead")]
    public void DangerousPrefixes_AreEscapedByDefault(string field)
    {
        // The default mode quotes the field and prefixes it, so a spreadsheet reads it as text.
        string row = Write(w => w.WriteField(field));

        Assert.StartsWith("\"'", row, StringComparison.Ordinal);
        Assert.Contains(field, row, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("+1")]
    [InlineData("-.5")]
    [InlineData("+")]
    [InlineData("-")]
    public void SignedNumbers_AreNotTreatedAsFormulas(string field)
    {
        // A leading sign followed by a digit or a decimal point is a number or a phone
        // number, not a formula — mangling those would corrupt ordinary data.
        Assert.Equal(field, Write(w => w.WriteField(field)));
    }

    [Fact]
    public void EscapeWithTab_PrefixesWithATab()
    {
        var options = new CsvWriteOptions { InjectionProtection = CsvInjectionProtection.EscapeWithTab };
        string row = Write(w => w.WriteField("=SUM(A1)"), options);

        Assert.StartsWith("\"\t", row, StringComparison.Ordinal);
    }

    [Fact]
    public void Sanitize_StripsTheDangerousPrefix()
    {
        var options = new CsvWriteOptions { InjectionProtection = CsvInjectionProtection.Sanitize };
        Assert.Equal("SUM(A1)", Write(w => w.WriteField("=SUM(A1)"), options));
    }

    [Fact]
    public void Reject_RefusesTheField()
    {
        var options = new CsvWriteOptions { InjectionProtection = CsvInjectionProtection.Reject };
        var ex = Assert.Throws<CsvException>(() => Write(w => w.WriteField("=SUM(A1)"), options));

        Assert.Equal(CsvErrorCode.InjectionDetected, ex.ErrorCode);
    }

    [Fact]
    public void None_LeavesTheFieldAlone()
    {
        var options = new CsvWriteOptions { InjectionProtection = CsvInjectionProtection.None };
        Assert.Equal("=SUM(A1)", Write(w => w.WriteField("=SUM(A1)"), options));
    }

    [Fact]
    public void AdditionalDangerousCharacters_AreHonoured()
    {
        var options = new CsvWriteOptions
        {
            InjectionProtection = CsvInjectionProtection.Sanitize,
            AdditionalDangerousChars = new HashSet<char> { '%' },
        };

        Assert.Equal("done", Write(w => w.WriteField("%done"), options));
    }

    [Fact]
    public void SafeField_IsUntouchedByProtection()
        => Assert.Equal("ordinary", Write(w => w.WriteField("ordinary")));

    [Fact]
    public void Culture_IsExposedForCallersThatFormatThemselves()
    {
        using var text = new StringWriter(CultureInfo.InvariantCulture);
        using var writer = new CsvStreamWriter(text, new CsvWriteOptions { Culture = CultureInfo.GetCultureInfo("fr-FR") }, leaveOpen: true);

        Assert.Equal("fr-FR", writer.Culture.Name);
    }
}
