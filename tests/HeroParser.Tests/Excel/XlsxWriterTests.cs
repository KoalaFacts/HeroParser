using HeroParser.Excels.Xlsx;
using Xunit;

namespace HeroParser.Tests.Excel;

[Trait("Category", "Unit")]
public class XlsxWriterTests
{
    // Writes a sheet with a header row and two data rows containing string and number cells,
    // then reads it back with XlsxReader to verify the values survive the round-trip.
    [Fact]
    public void RoundTrip_StringAndNumberCells_ValuesPreserved()
    {
        using var ms = new MemoryStream();

        using (var writer = new XlsxWriter(ms, leaveOpen: true))
        {
            using var sheet = writer.StartSheet("Sheet1");
            sheet.WriteHeaderRow(["Name", "Score"]);

            sheet.StartRow(2);
            sheet.WriteCellString(1, "Alice");
            sheet.WriteCellNumber(2, 42.5);
            sheet.EndRow();

            sheet.StartRow(3);
            sheet.WriteCellString(1, "Bob");
            sheet.WriteCellNumber(2, 99);
            sheet.EndRow();

            sheet.Close();
        }

        ms.Position = 0;

        using var reader = new XlsxReader(ms);
        var sheetInfo = reader.Workbook.Sheets[0];
        using var sheetReader = reader.OpenSheet(sheetInfo);

        var headerRow = sheetReader.ReadNextRow();
        Assert.NotNull(headerRow);
        Assert.Equal("Name", headerRow[0]);
        Assert.Equal("Score", headerRow[1]);

        var row2 = sheetReader.ReadNextRow();
        Assert.NotNull(row2);
        Assert.Equal("Alice", row2[0]);
        Assert.Equal("42.5", row2[1]);

        var row3 = sheetReader.ReadNextRow();
        Assert.NotNull(row3);
        Assert.Equal("Bob", row3[0]);
        Assert.Equal("99", row3[1]);

        Assert.Null(sheetReader.ReadNextRow());
    }

    // Writes a sheet with no rows and verifies that reading it back yields no rows.
    [Fact]
    public void RoundTrip_EmptySheet_ReturnsNoRows()
    {
        using var ms = new MemoryStream();

        using (var writer = new XlsxWriter(ms, leaveOpen: true))
        {
            using var sheet = writer.StartSheet("EmptySheet");
            sheet.Close();
        }

        ms.Position = 0;

        using var reader = new XlsxReader(ms);
        var sheetInfo = reader.Workbook.Sheets[0];
        using var sheetReader = reader.OpenSheet(sheetInfo);

        Assert.Null(sheetReader.ReadNextRow());
    }

    // Verifies that writing the same string value multiple times results in a single
    // entry in the shared string table (deduplication).
    [Fact]
    public void SharedStringTable_DuplicateStrings_StoredOnce()
    {
        var table = new XlsxSharedStringTable();

        int idx1 = table.GetOrAdd("Hello");
        int idx2 = table.GetOrAdd("World");
        int idx3 = table.GetOrAdd("Hello"); // duplicate

        Assert.Equal(0, idx1);
        Assert.Equal(1, idx2);
        Assert.Equal(0, idx3); // same index as first "Hello"
        Assert.Equal(2, table.Count);
        Assert.Equal(["Hello", "World"], table.Strings);
    }

    // Verifies that two identical strings written to separate cells in a sheet
    // are deduplicated and read back with the correct value.
    [Fact]
    public void RoundTrip_DuplicateStrings_ReadBackCorrectly()
    {
        using var ms = new MemoryStream();

        using (var writer = new XlsxWriter(ms, leaveOpen: true))
        {
            using var sheet = writer.StartSheet("Sheet1");
            sheet.WriteHeaderRow(["Category", "Label"]);

            sheet.StartRow(2);
            sheet.WriteCellString(1, "Alpha");
            sheet.WriteCellString(2, "Alpha"); // same string
            sheet.EndRow();

            sheet.Close();
        }

        ms.Position = 0;

        using var reader = new XlsxReader(ms);
        var sheetInfo = reader.Workbook.Sheets[0];
        using var sheetReader = reader.OpenSheet(sheetInfo);

        sheetReader.ReadNextRow(); // header row
        var dataRow = sheetReader.ReadNextRow();
        Assert.NotNull(dataRow);
        Assert.Equal("Alpha", dataRow[0]);
        Assert.Equal("Alpha", dataRow[1]);
    }

    // Verifies that GetColumnLetter converts 0-based column indices to A1-notation letters.
    [Theory]
    [InlineData(0, "A")]
    [InlineData(1, "B")]
    [InlineData(25, "Z")]
    [InlineData(26, "AA")]
    [InlineData(27, "AB")]
    [InlineData(51, "AZ")]
    [InlineData(52, "BA")]
    public void GetColumnLetter_KnownValues_ReturnsExpected(int index, string expected)
    {
        Assert.Equal(expected, XlsxWriter.GetColumnLetter(index));
    }

    // Verifies that duplicate spans written to the shared string table are deduplicated.
    [Fact]
    public void SharedStringTable_DuplicateSpans_StoredOnce()
    {
        var table = new XlsxSharedStringTable();

        int idx1 = table.GetOrAdd("Hello".AsSpan());
        int idx2 = table.GetOrAdd("World".AsSpan());
        int idx3 = table.GetOrAdd("Hello".AsSpan()); // duplicate span

        Assert.Equal(0, idx1);
        Assert.Equal(1, idx2);
        Assert.Equal(0, idx3); // same index as first "Hello"
        Assert.Equal(2, table.Count);
        Assert.Equal(["Hello", "World"], table.Strings);
    }

    // Verifies that cell values written as ReadOnlySpan<char> are successfully preserved in round-trip.
    [Fact]
    public void RoundTrip_SpanStrings_ValuesPreserved()
    {
        using var ms = new MemoryStream();

        using (var writer = new XlsxWriter(ms, leaveOpen: true))
        {
            using var sheet = writer.StartSheet("Sheet1");
            sheet.StartRow(1);
            sheet.WriteCellString(1, "SpanValue1".AsSpan());
            sheet.WriteCellString(2, "SpanValue2".AsSpan());
            sheet.EndRow();
            sheet.Close();
        }

        ms.Position = 0;

        using var reader = new XlsxReader(ms);
        var sheetInfo = reader.Workbook.Sheets[0];
        using var sheetReader = reader.OpenSheet(sheetInfo);

        var dataRow = sheetReader.ReadNextRow();
        Assert.NotNull(dataRow);
        Assert.Equal("SpanValue1", dataRow[0]);
        Assert.Equal("SpanValue2", dataRow[1]);
    }

    // Verifies that Excel injection protection is correctly applied when writing spans.
    [Fact]
    public void RoundTrip_SpanStrings_InjectionProtection()
    {
        using var ms = new MemoryStream();

        using (var writer = new XlsxWriter(ms, leaveOpen: true, injectionProtection: HeroParser.Excels.Core.ExcelInjectionProtection.EscapeWithApostrophe))
        {
            using var sheet = writer.StartSheet("Sheet1");
            sheet.StartRow(1);
            sheet.WriteCellString(1, "=SUM(A1)".AsSpan()); // dangerous span
            sheet.EndRow();
            sheet.Close();
        }

        ms.Position = 0;

        using var reader = new XlsxReader(ms);
        var sheetInfo = reader.Workbook.Sheets[0];
        using var sheetReader = reader.OpenSheet(sheetInfo);

        var dataRow = sheetReader.ReadNextRow();
        Assert.NotNull(dataRow);
        Assert.Equal("'=SUM(A1)", dataRow[0]); // Escaped with apostrophe
    }
}
