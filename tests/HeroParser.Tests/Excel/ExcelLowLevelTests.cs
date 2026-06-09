using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;
using HeroParser.Excels.Core;
using HeroParser.Excels.Xlsx;
using Xunit;

namespace HeroParser.Tests.Excel;

[Trait("Category", "Integration")]
public class ExcelLowLevelTests
{
    [Fact]
    public void StartRow_WithOutlineLevel_WritesOutlineLevelAttribute()
    {
        using var ms = new MemoryStream();
        using (var writer = new XlsxWriter(ms, leaveOpen: true))
        {
            using var sheet = writer.StartSheet("OutlineTest");
            sheet.StartRow(1, outlineLevel: 3);
            sheet.WriteCellString(1, "Outlined Row");
            sheet.EndRow();
        }

        ms.Position = 0;
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
        var entry = archive.GetEntry("xl/worksheets/sheet1.xml");
        Assert.NotNull(entry);

        using var entryStream = entry.Open();
        using var reader = new StreamReader(entryStream, Encoding.UTF8);
        var xml = reader.ReadToEnd();

        // Should contain outlineLevel="3"
        Assert.Contains("outlineLevel=\"3\"", xml);
    }

    [Fact]
    public void MergeCells_WritesMergeCellsElement()
    {
        using var ms = new MemoryStream();
        using (var writer = new XlsxWriter(ms, leaveOpen: true))
        {
            using var sheet = writer.StartSheet("MergeTest");
            sheet.StartRow(1);
            sheet.WriteCellString(1, "Merged Header");
            sheet.EndRow();

            // Merge A1:B2 using coordinates (1-based startCol, startRow, endCol, endRow)
            sheet.MergeCells(1, 1, 2, 2);
            // Merge C3:D3 using string notation
            sheet.MergeCells("C3:D3");
        }

        ms.Position = 0;
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
        var entry = archive.GetEntry("xl/worksheets/sheet1.xml");
        Assert.NotNull(entry);

        using var entryStream = entry.Open();
        using var reader = new StreamReader(entryStream, Encoding.UTF8);
        var xml = reader.ReadToEnd();

        // Should contain mergeCells elements in correct order and format
        Assert.Contains("<mergeCells count=\"2\">", xml);
        Assert.Contains("<mergeCell ref=\"A1:B2\"/>", xml);
        Assert.Contains("<mergeCell ref=\"C3:D3\"/>", xml);
        Assert.Contains("</mergeCells>", xml);

        // Ensure mergeCells is written after sheetData
        int sheetDataCloseIndex = xml.IndexOf("</sheetData>");
        int mergeCellsOpenIndex = xml.IndexOf("<mergeCells");
        Assert.True(sheetDataCloseIndex < mergeCellsOpenIndex, "mergeCells should be written after sheetData closes");
    }

    [Fact]
    public void CellWriters_WithCustomStyleIndex_WritesStyleAttribute()
    {
        using var ms = new MemoryStream();
        using (var writer = new XlsxWriter(ms, leaveOpen: true))
        {
            using var sheet = writer.StartSheet("StyleTest");
            sheet.StartRow(1);

            // Custom style index 2 on string cell
            sheet.WriteCellString(1, "Styled String", styleIndex: 2);

            // Custom style index 3 on number cell
            sheet.WriteCellNumber(2, 42.5, styleIndex: 3);

            // Custom style index 4 on boolean cell
            sheet.WriteCellBoolean(3, true, styleIndex: 4);

            // Custom style index 5 on date cell
            sheet.WriteCellDate(4, new System.DateTime(2026, 6, 9), styleIndex: 5);

            // Custom style index 6 on empty cell
            sheet.WriteCellEmpty(5, styleIndex: 6);

            sheet.EndRow();
        }

        ms.Position = 0;
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
        var entry = archive.GetEntry("xl/worksheets/sheet1.xml");
        Assert.NotNull(entry);

        using var entryStream = entry.Open();
        using var reader = new StreamReader(entryStream, Encoding.UTF8);
        var xml = reader.ReadToEnd();

        // Verify that cell references are written with the correct style index attributes
        // A1 (String, style 2): <c r="A1" s="2" t="s">
        Assert.Contains("r=\"A1\" s=\"2\" t=\"s\"", xml);

        // B1 (Number, style 3): <c r="B1" s="3">
        Assert.Contains("r=\"B1\" s=\"3\"", xml);

        // C1 (Boolean, style 4): <c r="C1" s="4" t="b">
        Assert.Contains("r=\"C1\" s=\"4\" t=\"b\"", xml);

        // D1 (Date, style 5): <c r="D1" s="5">
        Assert.Contains("r=\"D1\" s=\"5\"", xml);

        // E1 (Empty, style 6): <c r="E1" s="6" />
        Assert.Contains("r=\"E1\" s=\"6\" />", xml);
    }

    [Fact]
    public void Builder_WithFluentStyles_AppliesStylesToHeadersAndColumns()
    {
        var records = new List<StyledRecord>
        {
            new() { Name = "John", Score = 95.5 },
            new() { Name = "Jane", Score = 88.0 }
        };

        var headerStyle = ExcelStyle.Create()
            .WithFont(f => f.WithName("Arial").WithBold().WithColor("FF0000"))
            .WithFill(fill => fill.WithSolidColor("FFFF00")); // Yellow background

        var scoreStyle = ExcelStyle.Create()
            .WithFont(f => f.WithItalic())
            .WithNumberFormat("0.0")
            .WithAlignment(a => a.WithHorizontal(ExcelHorizontalAlignment.Center));

        var bytes = HeroParser.Excel.Write<StyledRecord>()
            .WithHeaderStyle(headerStyle)
            .WithColumnStyle(r => r.Score, scoreStyle)
            .ToBytes(records);

        using var ms = new MemoryStream(bytes);
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read);

        // 1. Verify sheet1.xml cell styling indices
        var sheetEntry = archive.GetEntry("xl/worksheets/sheet1.xml");
        Assert.NotNull(sheetEntry);
        using (var sheetStream = sheetEntry.Open())
        using (var reader = new StreamReader(sheetStream, Encoding.UTF8))
        {
            var sheetXml = reader.ReadToEnd();
            // A1: Name header (style 2)
            // B1: Score header (style 2)
            // A2: Name data (no custom column style -> default style 0)
            // B2: Score data (style 3)
            Assert.Contains("r=\"A1\" s=\"2\" t=\"s\"", sheetXml);
            Assert.Contains("r=\"B1\" s=\"2\" t=\"s\"", sheetXml);
            Assert.Contains("r=\"A2\" t=\"s\"", sheetXml);
            Assert.Contains("r=\"B2\" s=\"3\"", sheetXml);
        }

        // 2. Verify styles.xml generated stylesheet
        var stylesEntry = archive.GetEntry("xl/styles.xml");
        Assert.NotNull(stylesEntry);
        using (var stylesStream = stylesEntry.Open())
        using (var reader = new StreamReader(stylesStream, Encoding.UTF8))
        {
            var stylesXml = reader.ReadToEnd();
            // Should contain our custom Arial font
            Assert.Contains("<name val=\"Arial\" />", stylesXml);
            // Should contain bold tag
            Assert.Contains("<b />", stylesXml);
            // Should contain custom colors
            Assert.Contains("rgb=\"FFFF0000\"", stylesXml); // Red font
            Assert.Contains("rgb=\"FFFFFF00\"", stylesXml); // Yellow fill fgColor

            // Alignment: horizontal="center"
            Assert.Contains("horizontal=\"center\"", stylesXml);
        }
    }

    [Fact]
    public void Builder_WithMergeCells_AppliesExplicitCellMerges()
    {
        var records = new List<StyledRecord>
        {
            new() { Name = "John", Score = 95.5 },
            new() { Name = "Jane", Score = 88.0 }
        };

        var bytes = HeroParser.Excel.Write<StyledRecord>()
            .WithMergeCells("A2:B2")
            .WithMergeCells(1, 3, 2, 3)
            .ToBytes(records);

        using var ms = new MemoryStream(bytes);
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
        var sheetEntry = archive.GetEntry("xl/worksheets/sheet1.xml");
        Assert.NotNull(sheetEntry);

        using var sheetStream = sheetEntry.Open();
        using var reader = new StreamReader(sheetStream, Encoding.UTF8);
        var xml = reader.ReadToEnd();

        Assert.Contains("<mergeCell ref=\"A2:B2\"/>", xml);
        Assert.Contains("<mergeCell ref=\"A3:B3\"/>", xml);
    }

    [Fact]
    public void Builder_WithMergeDuplicates_AppliesColumnDuplicateMerges()
    {
        var records = new List<MergedRecord>
        {
            new() { Category = "Category1", Item = "Item1" },
            new() { Category = "Category1", Item = "Item2" },
            new() { Category = "Category2", Item = "Item3" }
        };

        var bytes = HeroParser.Excel.Write<MergedRecord>()
            .WithMergeDuplicates(x => x.Category)
            .ToBytes(records);

        using var ms = new MemoryStream(bytes);
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
        var sheetEntry = archive.GetEntry("xl/worksheets/sheet1.xml");
        Assert.NotNull(sheetEntry);

        using var sheetStream = sheetEntry.Open();
        using var reader = new StreamReader(sheetStream, Encoding.UTF8);
        var xml = reader.ReadToEnd();

        // Row 2 Category: Category1 (A2)
        // Row 3 Category: Category1 (A3) -> duplicate, should be merged A2:A3
        // Row 4 Category: Category2 (A4) -> different
        Assert.Contains("<mergeCell ref=\"A2:A3\"/>", xml);

        // A2 should contain Category1
        Assert.Contains("r=\"A2\" t=\"s\"", xml);
        // A3 should be empty/blank
        Assert.Contains("r=\"A3\" />", xml);
        // A4 should contain Category2
        Assert.Contains("r=\"A4\" t=\"s\"", xml);
    }
}

public class StyledRecord
{
    public string Name { get; set; } = "";
    public double Score { get; set; }
}

public class MergedRecord
{
    public string Category { get; set; } = "";
    public string Item { get; set; } = "";
}
