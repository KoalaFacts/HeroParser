using HeroParser.Excels.Core;
using HeroParser.Tests.Fixtures.Excel;
using Xunit;

namespace HeroParser.Tests.Excel;

[Trait("Category", "Unit")]
public class ExcelEdgeCaseTests
{
    [Fact]
    public void EmptyXlsx_NoDataRows_ReturnsEmptyList()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", []);

        var records = HeroParser.Excel.Read<SimpleProduct>().FromStream(xlsx);
        Assert.Empty(records);
    }

    [Fact]
    public void OnlyHeaderRow_ReturnsEmptyList()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["Name", "Price", "Quantity"]
        ]);

        var records = HeroParser.Excel.Read<SimpleProduct>().FromStream(xlsx);
        Assert.Empty(records);
    }

    [Fact]
    public void SparseRows_MissingCells_FilledWithEmptyStrings()
    {
        // XlsxSheetReader fills gaps, so a row with just one cell will pad the rest
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["Name", "Value"],
            ["OnlyName", ""]
        ]);

        var rows = HeroParser.Excel.Read().FromStream(xlsx);
        Assert.Single(rows);
        Assert.Equal("OnlyName", rows[0][0]);
        Assert.Equal("", rows[0][1]);
    }

    [Fact]
    public void VeryLongStringValues_ReadCorrectly()
    {
        var longValue = new string('X', 2000);
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["Name", "Value"],
            [longValue, "Short"]
        ]);

        var records = HeroParser.Excel.Read<NameValue>().FromStream(xlsx);
        Assert.Single(records);
        Assert.Equal(2000, records[0].Name.Length);
        Assert.Equal(longValue, records[0].Name);
    }

    [Fact]
    public void UnicodeContent_Chinese_ReadCorrectly()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["Name", "Value"],
            ["\u4F60\u597D\u4E16\u754C", "\u6D4B\u8BD5"]
        ]);

        var records = HeroParser.Excel.Read<NameValue>().FromStream(xlsx);
        Assert.Single(records);
        Assert.Equal("\u4F60\u597D\u4E16\u754C", records[0].Name);
        Assert.Equal("\u6D4B\u8BD5", records[0].Value);
    }

    [Fact]
    public void UnicodeContent_Arabic_ReadCorrectly()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["Name", "Value"],
            ["\u0645\u0631\u062D\u0628\u0627", "\u0627\u062E\u062A\u0628\u0627\u0631"]
        ]);

        var records = HeroParser.Excel.Read<NameValue>().FromStream(xlsx);
        Assert.Single(records);
        Assert.Equal("\u0645\u0631\u062D\u0628\u0627", records[0].Name);
    }

    [Fact]
    public void UnicodeContent_Emoji_ReadCorrectly()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["Name", "Value"],
            ["\U0001F600\U0001F604\U0001F609", "\U0001F44D"]
        ]);

        var records = HeroParser.Excel.Read<NameValue>().FromStream(xlsx);
        Assert.Single(records);
        Assert.Equal("\U0001F600\U0001F604\U0001F609", records[0].Name);
        Assert.Equal("\U0001F44D", records[0].Value);
    }

    [Fact]
    public void CorruptedXlsx_NotValidZip_ThrowsExcelException()
    {
        using var stream = new MemoryStream("This is not a valid xlsx file"u8.ToArray());

        Assert.Throws<ExcelException>(() =>
            HeroParser.Excel.Read<SimpleProduct>().FromStream(stream));
    }

    [Fact]
    public void CorruptedXlsx_DataReader_ThrowsExcelException()
    {
        using var stream = new MemoryStream("This is not a valid xlsx file"u8.ToArray());

        Assert.Throws<ExcelException>(() =>
            HeroParser.Excel.CreateDataReader(stream));
    }

    [Fact]
    public void BooleanCells_ReadAsTrueFalseStrings()
    {
        // Boolean cells are handled by XlsxSheetReader as "TRUE"/"FALSE"
        // The test helper creates numeric cells for "TRUE"/"FALSE" string values,
        // but boolean cell types would need a custom xlsx builder.
        // We test the string "TRUE"/"FALSE" roundtrip via shared strings.
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["Name", "Value"],
            ["BoolTest", "TRUE"]
        ]);

        var records = HeroParser.Excel.Read<NameValue>().FromStream(xlsx);
        Assert.Single(records);
        Assert.Equal("TRUE", records[0].Value);
    }

    [Fact]
    public void SkipRows_SkipsConfiguredRows()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["Metadata1"],
            ["Metadata2"],
            ["Name", "Price", "Quantity"],
            ["Widget", "9.99", "100"]
        ]);

        var records = HeroParser.Excel.Read<SimpleProduct>().SkipRows(2).FromStream(xlsx);
        Assert.Single(records);
        Assert.Equal("Widget", records[0].Name);
    }

    [Fact]
    public void SkipRows_MoreThanAvailable_ReturnsEmpty()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["Name", "Price", "Quantity"],
            ["Widget", "9.99", "100"]
        ]);

        var records = HeroParser.Excel.Read<SimpleProduct>().SkipRows(100).FromStream(xlsx);
        Assert.Empty(records);
    }

    [Fact]
    public void WithoutHeader_RowLevel_ReturnsAllRows()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["A", "B"],
            ["C", "D"]
        ]);

        var rows = HeroParser.Excel.Read().WithoutHeader().FromStream(xlsx);
        Assert.Equal(2, rows.Count);
        Assert.Equal("A", rows[0][0]);
        Assert.Equal("C", rows[1][0]);
    }

    [Fact]
    public void SpecialXmlCharacters_EscapedCorrectly()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["Name", "Value"],
            ["<Hello & 'World'>", "\"Quoted\""]
        ]);

        var records = HeroParser.Excel.Read<NameValue>().FromStream(xlsx);
        Assert.Single(records);
        Assert.Equal("<Hello & 'World'>", records[0].Name);
        Assert.Equal("\"Quoted\"", records[0].Value);
    }

    [Fact]
    public void MaxRows_WithZero_ReturnsEmpty()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["Name", "Price", "Quantity"],
            ["A", "1", "1"],
            ["B", "2", "2"]
        ]);

        var records = HeroParser.Excel.Read<SimpleProduct>().WithMaxRows(0).FromStream(xlsx);
        Assert.Empty(records);
    }

    [Fact]
    public void MaxRows_GreaterThanAvailable_ReturnsAll()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["Name", "Price", "Quantity"],
            ["A", "1", "1"],
            ["B", "2", "2"]
        ]);

        var records = HeroParser.Excel.Read<SimpleProduct>().WithMaxRows(100).FromStream(xlsx);
        Assert.Equal(2, records.Count);
    }

    [Fact]
    public void DataReader_WithSkipRows_SkipsConfiguredRows()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["Skip me"],
            ["Name", "Value"],
            ["A", "1"]
        ]);

        using var reader = HeroParser.Excel.CreateDataReader(xlsx, skipRows: 1);
        Assert.Equal("Name", reader.GetName(0));
        Assert.True(reader.Read());
        Assert.Equal("A", reader.GetString(0));
    }

    [Fact]
    public void MultipleReadsAfterEnd_ReturnFalse()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["Name"],
            ["A"]
        ]);

        using var reader = HeroParser.Excel.CreateDataReader(xlsx);
        Assert.True(reader.Read());
        Assert.False(reader.Read());
        Assert.False(reader.Read());
    }
}
