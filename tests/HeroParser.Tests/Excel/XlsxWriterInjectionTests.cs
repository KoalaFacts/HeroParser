using HeroParser.Excels.Core;
using HeroParser.Excels.Xlsx;
using Xunit;

namespace HeroParser.Tests.Excel;

/// <summary>
/// Tests for Excel formula-injection protection (CWE-1236) covering finding H3.
/// Verifies that <see cref="XlsxWriter"/> sanitises string cells per the configured
/// <see cref="ExcelInjectionProtection"/> setting.
/// </summary>
[Trait("Category", "Unit")]
public class XlsxWriterInjectionTests
{
    [Theory]
    [InlineData("=cmd|'/c calc'!A1")]
    [InlineData("=1+1")]
    [InlineData("@SUM(A1:A10)")]
    [InlineData("\tTAB-prefix")]
    [InlineData("+cmd|'/c calc'!A0")]
    public void Default_DangerousValue_IsPrefixedWithApostrophe(string dangerous)
    {
        var bytes = WriteSingleStringCell(dangerous, protection: null /* default */);

        using var reader = new XlsxReader(new MemoryStream(bytes));
        var sheet = reader.Workbook.Sheets[0];
        using var sheetReader = reader.OpenSheet(sheet);

        var row = sheetReader.ReadNextRow();
        Assert.NotNull(row);
        Assert.Equal("'" + dangerous, row[0]);
    }

    // OOXML normalises CR to LF on read (XML 1.0 spec), so a value containing '\r' cannot
    // round-trip byte-for-byte. We still need to verify '\r' triggers injection protection,
    // so this test asserts the post-normalisation form ("'\nCR-prefix") rather than the
    // original ("'\rCR-prefix").
    [Fact]
    public void Default_CarriageReturnPrefix_IsPrefixedWithApostropheAfterXmlNormalisation()
    {
        const string dangerous = "\rCR-prefix";
        const string expectedAfterXmlNormalise = "'\nCR-prefix";

        var bytes = WriteSingleStringCell(dangerous, protection: null);

        using var reader = new XlsxReader(new MemoryStream(bytes));
        var sheet = reader.Workbook.Sheets[0];
        using var sheetReader = reader.OpenSheet(sheet);

        var row = sheetReader.ReadNextRow();
        Assert.NotNull(row);
        Assert.Equal(expectedAfterXmlNormalise, row[0]);
    }

    // Smart detection (matching CsvStreamWriter's heuristic) treats '-' or '+' followed by
    // a digit as a number prefix (e.g. phone numbers "-555-1234") and leaves it unescaped.
    // This is intentional behaviour; the test documents it.
    [Theory]
    [InlineData("-2+3+cmd|'/c calc'!A0")]
    [InlineData("+1234")]
    public void Default_NumericLookingPrefix_IsNotEscaped(string numericLooking)
    {
        var bytes = WriteSingleStringCell(numericLooking, protection: null);

        using var reader = new XlsxReader(new MemoryStream(bytes));
        var sheet = reader.Workbook.Sheets[0];
        using var sheetReader = reader.OpenSheet(sheet);

        var row = sheetReader.ReadNextRow();
        Assert.NotNull(row);
        Assert.Equal(numericLooking, row[0]);
    }

    [Theory]
    [InlineData("hello")]
    [InlineData("-42")]
    [InlineData("+1.5")]
    [InlineData("")]
    public void Default_BenignValue_IsPreservedVerbatim(string benign)
    {
        var bytes = WriteSingleStringCell(benign, protection: null);

        using var reader = new XlsxReader(new MemoryStream(bytes));
        var sheet = reader.Workbook.Sheets[0];
        using var sheetReader = reader.OpenSheet(sheet);

        var row = sheetReader.ReadNextRow();
        Assert.NotNull(row);
        Assert.Equal(benign, row[0]);
    }

    [Fact]
    public void None_DangerousValue_IsPreservedVerbatim()
    {
        const string dangerous = "=cmd|'/c calc'!A1";
        var bytes = WriteSingleStringCell(dangerous, ExcelInjectionProtection.None);

        using var reader = new XlsxReader(new MemoryStream(bytes));
        var sheet = reader.Workbook.Sheets[0];
        using var sheetReader = reader.OpenSheet(sheet);

        var row = sheetReader.ReadNextRow();
        Assert.NotNull(row);
        Assert.Equal(dangerous, row[0]);
    }

    [Fact]
    public void Sanitize_DangerousValue_HasDangerousPrefixStripped()
    {
        const string dangerous = "=SUM(A1)";
        var bytes = WriteSingleStringCell(dangerous, ExcelInjectionProtection.Sanitize);

        using var reader = new XlsxReader(new MemoryStream(bytes));
        var sheet = reader.Workbook.Sheets[0];
        using var sheetReader = reader.OpenSheet(sheet);

        var row = sheetReader.ReadNextRow();
        Assert.NotNull(row);
        Assert.Equal("SUM(A1)", row[0]);
    }

    [Fact]
    public void Reject_DangerousValue_ThrowsExcelException()
    {
        const string dangerous = "=cmd|'/c calc'!A1";
        using var ms = new MemoryStream();

        using var writer = new XlsxWriter(ms, leaveOpen: true, injectionProtection: ExcelInjectionProtection.Reject);
        using var sheet = writer.StartSheet("Sheet1");
        sheet.StartRow(1);

        Assert.Throws<ExcelException>(() => sheet.WriteCellString(1, dangerous));
    }

    private static byte[] WriteSingleStringCell(string value, ExcelInjectionProtection? protection)
    {
        using var ms = new MemoryStream();
        XlsxWriter writer = protection.HasValue
            ? new XlsxWriter(ms, leaveOpen: true, injectionProtection: protection.Value)
            : new XlsxWriter(ms, leaveOpen: true);
        using (writer)
        using (var sheet = writer.StartSheet("Sheet1"))
        {
            sheet.StartRow(1);
            sheet.WriteCellString(1, value);
            sheet.EndRow();
            sheet.Close();
        }
        return ms.ToArray();
    }
}
