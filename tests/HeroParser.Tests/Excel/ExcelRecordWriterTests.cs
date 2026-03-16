using HeroParser.Excels.Core;
using HeroParser.Excels.Writing;
using HeroParser.Excels.Xlsx;
using Xunit;

namespace HeroParser.Tests.Excel;

[Trait("Category", "Unit")]
public class ExcelRecordWriterTests
{
    // Writes SimpleProduct records via ExcelRecordWriter and reads them back with Excel.Read<T>
    // to verify the round-trip preserves all field values.
    [Fact]
    public void WriteRecords_SimpleProducts_RoundTripPreservesValues()
    {
        var products = new List<SimpleProduct>
        {
            new() { Name = "Widget", Price = 9.99m, Quantity = 100 },
            new() { Name = "Gadget", Price = 24.95m, Quantity = 50 },
        };

        var ms = WriteToStream(products);

        var records = HeroParser.Excel.Read<SimpleProduct>().FromStream(ms);

        Assert.Equal(2, records.Count);
        Assert.Equal("Widget", records[0].Name);
        Assert.Equal(9.99m, records[0].Price);
        Assert.Equal(100, records[0].Quantity);
        Assert.Equal("Gadget", records[1].Name);
        Assert.Equal(24.95m, records[1].Price);
        Assert.Equal(50, records[1].Quantity);
    }

    // Writes a record with a null string property and verifies the cell is read back as empty.
    [Fact]
    public void WriteRecords_NullableStringField_WritesEmptyCell()
    {
        var records = new List<NullableProduct>
        {
            new() { Name = null, Price = 1.0m },
            new() { Name = "Real", Price = 2.0m },
        };

        var ms = WriteNullableToStream(records);

        // Read back raw rows to inspect the cell values (NullableProduct has no [GenerateBinder]).
        // Row 0 is the header; rows 1+ are data.
        var rows = HeroParser.Excel.Read().FromStream(ms);

        Assert.Equal(2, rows.Count); // two data rows (header excluded by default)
        Assert.Equal("", rows[0][0]); // null Name → empty cell → empty string
        Assert.Equal("1.0", rows[0][1]); // Price = 1.0m → decimal written as string to preserve precision
        Assert.Equal("Real", rows[1][0]);
        Assert.Equal("2.0", rows[1][1]);
    }

    // Verifies that WriteRecords with an empty sequence produces only a header row.
    [Fact]
    public void WriteRecords_EmptySequence_WritesHeaderOnly()
    {
        var ms = WriteToStream([]);

        var records = HeroParser.Excel.Read<SimpleProduct>().FromStream(ms);
        Assert.Empty(records);
    }

    // Verifies that WriteRecords with WriteHeader = false produces no header row,
    // so reading with hasHeaderRow=true (default) yields no results, but
    // we can verify the raw row count is correct.
    [Fact]
    public void WriteRecords_WithHeaderDisabled_WritesDataRowsOnly()
    {
        var products = new List<SimpleProduct>
        {
            new() { Name = "A", Price = 1m, Quantity = 1 },
        };

        var ms = new MemoryStream();
        using (var xlsxWriter = new XlsxWriter(ms, leaveOpen: true))
        {
            using var sheet = xlsxWriter.StartSheet("Sheet1");
            var writer = new ExcelRecordWriter<SimpleProduct>(new ExcelWriteOptions { WriteHeader = false });
            writer.WriteRecords(sheet, products);
            sheet.Close();
        }

        ms.Position = 0;

        // Read raw rows without header assumption — there should be exactly 1 row (the data row)
        var rows = HeroParser.Excel.Read().WithoutHeader().FromStream(ms);
        Assert.Single(rows);
    }

    // Helper: writes SimpleProduct list to a MemoryStream using ExcelRecordWriter + XlsxWriter.
    private static MemoryStream WriteToStream(IEnumerable<SimpleProduct> products)
    {
        var ms = new MemoryStream();
        using (var xlsxWriter = new XlsxWriter(ms, leaveOpen: true))
        {
            using var sheet = xlsxWriter.StartSheet("Sheet1");
            var writer = new ExcelRecordWriter<SimpleProduct>();
            writer.WriteRecords(sheet, products);
            sheet.Close();
        }

        ms.Position = 0;
        return ms;
    }

    // Helper: writes NullableProduct list to a MemoryStream using ExcelRecordWriter + XlsxWriter.
    private static MemoryStream WriteNullableToStream(IEnumerable<NullableProduct> records)
    {
        var ms = new MemoryStream();
        using (var xlsxWriter = new XlsxWriter(ms, leaveOpen: true))
        {
            using var sheet = xlsxWriter.StartSheet("Sheet1");
            var writer = new ExcelRecordWriter<NullableProduct>();
            writer.WriteRecords(sheet, records);
            sheet.Close();
        }

        ms.Position = 0;
        return ms;
    }
}

/// <summary>A product record with a nullable Name for testing empty-cell handling.</summary>
public class NullableProduct
{
    /// <summary>Gets or sets the product name (nullable).</summary>
    [TabularMap(Name = "Name")]
    public string? Name { get; set; }

    /// <summary>Gets or sets the product price.</summary>
    [TabularMap(Name = "Price")]
    public decimal Price { get; set; }
}
