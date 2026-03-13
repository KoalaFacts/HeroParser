using System.IO.Compression;
using HeroParser.Excels.Core;
using HeroParser.Excels.Xlsx;
using Xunit;

namespace HeroParser.Tests.Excel;

[Trait("Category", "Unit")]
public class XlsxWorkbookTests
{
    [Fact]
    public void Parse_ThreeSheets_ReturnsAllSheets()
    {
        using var archive = CreateArchive(
            workbookXml: """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                          xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets>
                    <sheet name="Sheet1" sheetId="1" r:id="rId1" />
                    <sheet name="Sheet2" sheetId="2" r:id="rId2" />
                    <sheet name="Data" sheetId="3" r:id="rId3" />
                  </sheets>
                </workbook>
                """,
            relsXml: """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml" />
                  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet2.xml" />
                  <Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet3.xml" />
                </Relationships>
                """);

        var workbook = XlsxWorkbook.Parse(archive);
        Assert.Equal(3, workbook.Sheets.Count);
        Assert.Equal("Sheet1", workbook.Sheets[0].Name);
        Assert.Equal("Sheet2", workbook.Sheets[1].Name);
        Assert.Equal("Data", workbook.Sheets[2].Name);
    }

    [Fact]
    public void GetSheetByName_ExistingSheet_ReturnsCorrectEntry()
    {
        using var archive = CreateArchiveWithThreeSheets();
        var workbook = XlsxWorkbook.Parse(archive);

        var sheet = workbook.GetSheetByName("Sheet2");
        Assert.Equal("Sheet2", sheet.Name);
        Assert.Equal(1, sheet.Index);
        Assert.Equal("xl/worksheets/sheet2.xml", sheet.Path);
    }

    [Fact]
    public void GetSheetByIndex_FirstSheet_ReturnsCorrectEntry()
    {
        using var archive = CreateArchiveWithThreeSheets();
        var workbook = XlsxWorkbook.Parse(archive);

        var sheet = workbook.GetSheetByIndex(0);
        Assert.Equal("Sheet1", sheet.Name);
        Assert.Equal(0, sheet.Index);
    }

    [Fact]
    public void GetFirstSheet_ReturnsFirstSheet()
    {
        using var archive = CreateArchiveWithThreeSheets();
        var workbook = XlsxWorkbook.Parse(archive);

        var sheet = workbook.GetFirstSheet();
        Assert.Equal("Sheet1", sheet.Name);
    }

    [Fact]
    public void GetSheetByName_MissingSheet_ThrowsExcelException()
    {
        using var archive = CreateArchiveWithThreeSheets();
        var workbook = XlsxWorkbook.Parse(archive);

        var ex = Assert.Throws<ExcelException>(() => workbook.GetSheetByName("NonExistent"));
        Assert.Contains("NonExistent", ex.Message);
    }

    [Fact]
    public void GetSheetByIndex_OutOfRange_ThrowsExcelException()
    {
        using var archive = CreateArchiveWithThreeSheets();
        var workbook = XlsxWorkbook.Parse(archive);

        Assert.Throws<ExcelException>(() => workbook.GetSheetByIndex(5));
        Assert.Throws<ExcelException>(() => workbook.GetSheetByIndex(-1));
    }

    [Fact]
    public void GetFirstSheet_EmptyWorkbook_ThrowsExcelException()
    {
        using var archive = CreateArchive(
            workbookXml: """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                          xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets />
                </workbook>
                """,
            relsXml: """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships" />
                """);

        var workbook = XlsxWorkbook.Parse(archive);
        var ex = Assert.Throws<ExcelException>(() => { _ = workbook.GetFirstSheet(); });
        Assert.Contains("no sheets", ex.Message);
    }

    [Fact]
    public void Parse_SheetPaths_ResolvedCorrectly()
    {
        using var archive = CreateArchiveWithThreeSheets();
        var workbook = XlsxWorkbook.Parse(archive);

        Assert.Equal("xl/worksheets/sheet1.xml", workbook.Sheets[0].Path);
        Assert.Equal("xl/worksheets/sheet2.xml", workbook.Sheets[1].Path);
        Assert.Equal("xl/worksheets/sheet3.xml", workbook.Sheets[2].Path);
    }

    private static ZipArchive CreateArchiveWithThreeSheets()
    {
        return CreateArchive(
            workbookXml: """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                          xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets>
                    <sheet name="Sheet1" sheetId="1" r:id="rId1" />
                    <sheet name="Sheet2" sheetId="2" r:id="rId2" />
                    <sheet name="Data" sheetId="3" r:id="rId3" />
                  </sheets>
                </workbook>
                """,
            relsXml: """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml" />
                  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet2.xml" />
                  <Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet3.xml" />
                </Relationships>
                """);
    }

    private static ZipArchive CreateArchive(string workbookXml, string relsXml)
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddEntry(zip, "xl/workbook.xml", workbookXml);
            AddEntry(zip, "xl/_rels/workbook.xml.rels", relsXml);
        }
        ms.Position = 0;
        return new ZipArchive(ms, ZipArchiveMode.Read);
    }

    private static void AddEntry(ZipArchive zip, string entryName, string content)
    {
        var entry = zip.CreateEntry(entryName);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }
}
