using HeroParser.Conversion;
using HeroParser.FixedWidths;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Writing;
using Xunit;

namespace HeroParser.Tests;

/// <summary>
/// Formula-injection (CWE-1236) tests for the FixedWidth-to-CSV converter, covering finding H4.
/// Verifies that trimmed fixed-width fields whose first non-pad character is a formula trigger
/// are escaped according to the configured <see cref="CsvInjectionProtection"/>.
/// </summary>
public class FixedWidthToCsvInjectionTests
{
    [Theory]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    [InlineData("=SUM(A1)")]
    [InlineData("@cmd")]
    [InlineData("\tx")]
    [InlineData("\rx")]
    [InlineData("-cmd")]
    [InlineData("+cmd")]
    public void Default_DangerousField_IsEscapedWithApostropheInQuotes(string dangerous)
    {
        // Build a fixed-width row whose single field has the dangerous value padded to width 20.
        string padded = dangerous.PadRight(20);
        FixedWidthFieldDefinition[] columns =
        [
            new FixedWidthFieldDefinition("Value", 20, FieldAlignment.Left),
        ];

        var csv = FixedWidthToCsvConverter.Convert(padded, columns);

        // Headers row is "Value" (one column), data row should be quoted with apostrophe prefix.
        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.Equal("Value", lines[0]);
        Assert.Equal("\"'" + dangerous + "\"", lines[1]);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void None_DangerousField_IsWrittenVerbatim()
    {
        const string dangerous = "=SUM(A1)";
        string padded = dangerous.PadRight(20);
        FixedWidthFieldDefinition[] columns =
        [
            new FixedWidthFieldDefinition("Value", 20, FieldAlignment.Left),
        ];
        var options = new FixedWidthToCsvOptions { InjectionProtection = CsvInjectionProtection.None };

        var csv = FixedWidthToCsvConverter.Convert(padded, columns, options);

        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(dangerous, lines[1]);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Reject_DangerousField_ThrowsCsvException()
    {
        const string dangerous = "=SUM(A1)";
        string padded = dangerous.PadRight(20);
        FixedWidthFieldDefinition[] columns =
        [
            new FixedWidthFieldDefinition("Value", 20, FieldAlignment.Left),
        ];
        var options = new FixedWidthToCsvOptions { InjectionProtection = CsvInjectionProtection.Reject };

        var ex = Assert.Throws<CsvException>(() => FixedWidthToCsvConverter.Convert(padded, columns, options));
        Assert.Equal(CsvErrorCode.InjectionDetected, ex.ErrorCode);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Sanitize_DangerousField_HasDangerousPrefixStripped()
    {
        const string dangerous = "=SUM(A1)";
        string padded = dangerous.PadRight(20);
        FixedWidthFieldDefinition[] columns =
        [
            new FixedWidthFieldDefinition("Value", 20, FieldAlignment.Left),
        ];
        var options = new FixedWidthToCsvOptions { InjectionProtection = CsvInjectionProtection.Sanitize };

        var csv = FixedWidthToCsvConverter.Convert(padded, columns, options);

        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("SUM(A1)", lines[1]);
    }

    // Numeric-looking values starting with '-' or '+' are safe and must remain unquoted/unescaped.
    [Theory]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    [InlineData("-42")]
    [InlineData("+1.5")]
    [InlineData("hello")]
    public void Default_BenignField_IsPreservedVerbatim(string benign)
    {
        string padded = benign.PadRight(20);
        FixedWidthFieldDefinition[] columns =
        [
            new FixedWidthFieldDefinition("Value", 20, FieldAlignment.Left),
        ];

        var csv = FixedWidthToCsvConverter.Convert(padded, columns);

        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(benign, lines[1]);
    }
}
