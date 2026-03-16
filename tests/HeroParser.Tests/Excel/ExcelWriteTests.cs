using HeroParser.Excels.Core;
using HeroParser.Tests.Validation;
using HeroParser.Validation;
using Xunit;

namespace HeroParser.Tests.Excel;

/// <summary>
/// Integration tests for the Excel write API: builder fluent interface, facade static methods,
/// header control, sheet naming, culture formatting, and MaxRowCount enforcement.
/// </summary>
[Trait("Category", "Integration")]
public class ExcelWriteTests
{
    // ──────────────────────────────────────────────
    // Round-trip via builder API
    // ──────────────────────────────────────────────

    [Fact]
    public void ToBytes_SimpleProducts_RoundTripPreservesAllFields()
    {
        var products = new List<SimpleProduct>
        {
            new() { Name = "Widget", Price = 9.99m, Quantity = 100 },
            new() { Name = "Gadget", Price = 24.95m, Quantity = 50 },
        };

        var bytes = HeroParser.Excel.Write<SimpleProduct>().ToBytes(products);

        using var ms = new MemoryStream(bytes);
        var readBack = HeroParser.Excel.Read<SimpleProduct>().FromStream(ms);

        Assert.Equal(2, readBack.Count);
        Assert.Equal("Widget", readBack[0].Name);
        Assert.Equal(9.99m, readBack[0].Price);
        Assert.Equal(100, readBack[0].Quantity);
        Assert.Equal("Gadget", readBack[1].Name);
        Assert.Equal(24.95m, readBack[1].Price);
        Assert.Equal(50, readBack[1].Quantity);
    }

    [Fact]
    public void ToStream_WritesValidXlsx_ReadableByExcelRead()
    {
        var products = new List<SimpleProduct>
        {
            new() { Name = "Alpha", Price = 1.00m, Quantity = 1 },
        };

        using var ms = new MemoryStream();
        HeroParser.Excel.Write<SimpleProduct>().ToStream(ms, products, leaveOpen: true);
        ms.Position = 0;

        var readBack = HeroParser.Excel.Read<SimpleProduct>().FromStream(ms);

        Assert.Single(readBack);
        Assert.Equal("Alpha", readBack[0].Name);
    }

    [Fact]
    public void ToFile_WritesFile_ReadableByExcelRead()
    {
        var products = new List<SimpleProduct>
        {
            new() { Name = "FileProduct", Price = 5.50m, Quantity = 10 },
        };

        var path = Path.GetTempFileName() + ".xlsx";
        try
        {
            HeroParser.Excel.Write<SimpleProduct>().ToFile(path, products);
            var readBack = HeroParser.Excel.Read<SimpleProduct>().FromFile(path);

            Assert.Single(readBack);
            Assert.Equal("FileProduct", readBack[0].Name);
            Assert.Equal(5.50m, readBack[0].Price);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    // ──────────────────────────────────────────────
    // Facade static methods
    // ──────────────────────────────────────────────

    [Fact]
    public void SerializeRecords_ReturnsNonEmptyBytes_ReadableByExcelRead()
    {
        var products = new List<SimpleProduct>
        {
            new() { Name = "Facade", Price = 3.14m, Quantity = 7 },
        };

        var bytes = HeroParser.Excel.SerializeRecords(products);

        Assert.NotEmpty(bytes);

        using var ms = new MemoryStream(bytes);
        var readBack = HeroParser.Excel.Read<SimpleProduct>().FromStream(ms);

        Assert.Single(readBack);
        Assert.Equal("Facade", readBack[0].Name);
    }

    [Fact]
    public void WriteToStream_WritesValidXlsx_ReadableByExcelRead()
    {
        var products = new List<SimpleProduct>
        {
            new() { Name = "StreamFacade", Price = 2.71m, Quantity = 3 },
        };

        using var ms = new MemoryStream();
        HeroParser.Excel.WriteToStream(ms, products, leaveOpen: true);
        ms.Position = 0;

        var readBack = HeroParser.Excel.Read<SimpleProduct>().FromStream(ms);

        Assert.Single(readBack);
        Assert.Equal("StreamFacade", readBack[0].Name);
    }

    [Fact]
    public void WriteToFile_WritesFile_ReadableByExcelRead()
    {
        var products = new List<SimpleProduct>
        {
            new() { Name = "FileFacade", Price = 1.23m, Quantity = 4 },
        };

        var path = Path.GetTempFileName() + ".xlsx";
        try
        {
            HeroParser.Excel.WriteToFile(path, products);
            var readBack = HeroParser.Excel.Read<SimpleProduct>().FromFile(path);

            Assert.Single(readBack);
            Assert.Equal("FileFacade", readBack[0].Name);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    // ──────────────────────────────────────────────
    // Header control
    // ──────────────────────────────────────────────

    [Fact]
    public void WithoutHeader_WritesDataRowsOnly_NoHeaderRowInOutput()
    {
        var products = new List<SimpleProduct>
        {
            new() { Name = "NoHeader", Price = 1.00m, Quantity = 1 },
        };

        var bytes = HeroParser.Excel.Write<SimpleProduct>()
            .WithoutHeader()
            .ToBytes(products);

        using var ms = new MemoryStream(bytes);
        // Read raw rows — without header row the sheet has exactly 1 row
        var rows = HeroParser.Excel.Read().WithoutHeader().FromStream(ms);
        Assert.Single(rows);
    }

    [Fact]
    public void WithHeader_Default_WritesHeaderRow()
    {
        var products = new List<SimpleProduct>
        {
            new() { Name = "HeaderTest", Price = 1.00m, Quantity = 1 },
        };

        // Default is WriteHeader = true; read back with header recognition (default)
        var bytes = HeroParser.Excel.Write<SimpleProduct>().ToBytes(products);

        using var ms = new MemoryStream(bytes);
        var readBack = HeroParser.Excel.Read<SimpleProduct>().FromStream(ms);

        // If header was not written, binder would not recognise columns and result would differ
        Assert.Single(readBack);
        Assert.Equal("HeaderTest", readBack[0].Name);
    }

    // ──────────────────────────────────────────────
    // Sheet name
    // ──────────────────────────────────────────────

    [Fact]
    public void WithSheetName_CustomName_SheetIsReadableByName()
    {
        var products = new List<SimpleProduct>
        {
            new() { Name = "SheetNameTest", Price = 1.00m, Quantity = 1 },
        };

        var bytes = HeroParser.Excel.Write<SimpleProduct>()
            .WithSheetName("MyData")
            .ToBytes(products);

        using var ms = new MemoryStream(bytes);
        var readBack = HeroParser.Excel.Read<SimpleProduct>().FromSheet("MyData").FromStream(ms);

        Assert.Single(readBack);
        Assert.Equal("SheetNameTest", readBack[0].Name);
    }

    [Fact]
    public void WithSheetName_DefaultSheet1_IsReadableByExplicitName()
    {
        var products = new List<SimpleProduct>
        {
            new() { Name = "DefaultSheet", Price = 1.00m, Quantity = 1 },
        };

        // Default sheet name is "Sheet1"
        var bytes = HeroParser.Excel.Write<SimpleProduct>().ToBytes(products);

        using var ms = new MemoryStream(bytes);
        // Read by explicit name to confirm the name was written correctly
        var readBack = HeroParser.Excel.Read<SimpleProduct>().FromSheet("Sheet1").FromStream(ms);

        Assert.Single(readBack);
        Assert.Equal("DefaultSheet", readBack[0].Name);
    }

    // ──────────────────────────────────────────────
    // Null handling
    // ──────────────────────────────────────────────

    [Fact]
    public void NullProperty_WritesEmptyCell_ReadBackAsEmpty()
    {
        var records = new List<NullableProduct>
        {
            new() { Name = null, Price = 5.00m },
        };

        var bytes = HeroParser.Excel.Write<NullableProduct>().ToBytes(records);

        using var ms = new MemoryStream(bytes);
        // Read raw rows to inspect cell values directly
        var rows = HeroParser.Excel.Read().FromStream(ms);

        Assert.Single(rows);
        Assert.Equal("", rows[0][0]); // null Name → empty cell
    }

    // ──────────────────────────────────────────────
    // Culture formatting
    // ──────────────────────────────────────────────

    [Fact]
    public void WithCulture_DeDeNumberFormat_WritesLocalizedNumber()
    {
        var records = new List<WriteFormatRecord>
        {
            new() { Label = "DE", Value = 1234.56 },
        };

        // Apply de-DE culture with NumberFormat so doubles are formatted as strings
        var bytes = HeroParser.Excel.Write<WriteFormatRecord>()
            .WithCulture("de-DE")
            .WithNumberFormat("N2")
            .ToBytes(records);

        using var ms = new MemoryStream(bytes);
        var rows = HeroParser.Excel.Read().FromStream(ms);

        Assert.Single(rows);
        // de-DE uses "," as decimal separator
        Assert.Contains(",", rows[0][1]);
    }

    [Fact]
    public void WithCulture_EnUsNumberFormat_WritesLocalizedNumber()
    {
        var records = new List<WriteFormatRecord>
        {
            new() { Label = "US", Value = 1234.56 },
        };

        var bytes = HeroParser.Excel.Write<WriteFormatRecord>()
            .WithCulture("en-US")
            .WithNumberFormat("N2")
            .ToBytes(records);

        using var ms = new MemoryStream(bytes);
        var rows = HeroParser.Excel.Read().FromStream(ms);

        Assert.Single(rows);
        // en-US uses "." as decimal separator
        Assert.Contains(".", rows[0][1]);
    }

    // ──────────────────────────────────────────────
    // MaxRowCount
    // ──────────────────────────────────────────────

    [Fact]
    public void WithMaxRowCount_ExceedsLimit_ThrowsExcelException()
    {
        var products = new List<SimpleProduct>
        {
            new() { Name = "A", Price = 1m, Quantity = 1 },
            new() { Name = "B", Price = 2m, Quantity = 2 },
            new() { Name = "C", Price = 3m, Quantity = 3 },
        };

        var ex = Assert.Throws<ExcelException>(() =>
            HeroParser.Excel.Write<SimpleProduct>()
                .WithMaxRowCount(2)
                .ToBytes(products));

        Assert.Contains("2", ex.Message);
    }

    [Fact]
    public void WithMaxRowCount_ExactlyAtLimit_WritesSuccessfully()
    {
        var products = new List<SimpleProduct>
        {
            new() { Name = "A", Price = 1m, Quantity = 1 },
            new() { Name = "B", Price = 2m, Quantity = 2 },
        };

        // Exactly 2 records, limit is 2 — should not throw
        var bytes = HeroParser.Excel.Write<SimpleProduct>()
            .WithMaxRowCount(2)
            .ToBytes(products);

        using var ms = new MemoryStream(bytes);
        var readBack = HeroParser.Excel.Read<SimpleProduct>().FromStream(ms);
        Assert.Equal(2, readBack.Count);
    }

    // ──────────────────────────────────────────────
    // WithValidationMode — Lenient skips exception
    // ──────────────────────────────────────────────

    [Fact]
    public void WithValidationMode_Lenient_DoesNotThrowOnInvalidRecord()
    {
        // ValidatedTransaction.TransactionId has NotNull = true, but we pass empty string
        var records = new List<ValidatedTransaction>
        {
            new() { TransactionId = "", Amount = 500m, Currency = "USD", Reference = "AB1234" },
        };

        // Should not throw in Lenient mode
        var bytes = HeroParser.Excel.Write<ValidatedTransaction>()
            .WithValidationMode(ValidationMode.Lenient)
            .ToBytes(records);

        Assert.NotEmpty(bytes);
    }

    // ──────────────────────────────────────────────
    // WithNullValue
    // ──────────────────────────────────────────────

    [Fact]
    public void WithNullValue_MatchingValue_WritesEmptyCell()
    {
        var records = new List<SimpleProduct>
        {
            new() { Name = "N/A", Price = 1m, Quantity = 1 },
            new() { Name = "Real", Price = 2m, Quantity = 2 },
        };

        var bytes = HeroParser.Excel.Write<SimpleProduct>()
            .WithNullValue("N/A")
            .ToBytes(records);

        using var ms = new MemoryStream(bytes);
        var rows = HeroParser.Excel.Read().FromStream(ms);
        Assert.Equal("", rows[0][0]); // "N/A" matches NullValue → empty cell
        Assert.Equal("Real", rows[1][0]);
    }

    // ──────────────────────────────────────────────
    // WithDateOnlyFormat
    // ──────────────────────────────────────────────

    [Fact]
    public void WithDateOnlyFormat_CustomFormat_PreservesInRoundTrip()
    {
        var records = new List<DateRecord>
        {
            new() { Date = new DateOnly(2025, 3, 15) },
        };

        var bytes = HeroParser.Excel.Write<DateRecord>()
            .WithDateOnlyFormat("dd/MM/yyyy")
            .ToBytes(records);

        using var ms = new MemoryStream(bytes);
        var rows = HeroParser.Excel.Read().FromStream(ms);
        Assert.Equal("15/03/2025", rows[0][0]);
    }

    // ──────────────────────────────────────────────
    // WithTimeOnlyFormat
    // ──────────────────────────────────────────────

    [Fact]
    public void WithTimeOnlyFormat_CustomFormat_PreservesInRoundTrip()
    {
        var records = new List<TimeRecord>
        {
            new() { Time = new TimeOnly(14, 30, 45) },
        };

        var bytes = HeroParser.Excel.Write<TimeRecord>()
            .WithTimeOnlyFormat("hh\\:mm\\:ss tt")
            .ToBytes(records);

        using var ms = new MemoryStream(bytes);
        var rows = HeroParser.Excel.Read().FromStream(ms);
        Assert.Contains("02:30:45", rows[0][0]);
    }

    // ──────────────────────────────────────────────
    // WithNumberFormat
    // ──────────────────────────────────────────────

    [Fact]
    public void WithNumberFormat_TwoDecimalPlaces_FormatsCorrectly()
    {
        var records = new List<WriteFormatRecord>
        {
            new() { Label = "Pi", Value = 3.14159 },
        };

        var bytes = HeroParser.Excel.Write<WriteFormatRecord>()
            .WithNumberFormat("F2")
            .ToBytes(records);

        using var ms = new MemoryStream(bytes);
        var rows = HeroParser.Excel.Read().FromStream(ms);
        Assert.Equal("3.14", rows[0][1]);
    }

    // ──────────────────────────────────────────────
    // DateTimeOffset round-trip
    // ──────────────────────────────────────────────

    [Fact]
    public void DateTimeOffset_RoundTrip_PreservesOffset()
    {
        var dto = new DateTimeOffset(2025, 6, 15, 14, 30, 0, TimeSpan.FromHours(5));
        var records = new List<DateTimeOffsetRecord>
        {
            new() { Timestamp = dto },
        };

        var bytes = HeroParser.Excel.Write<DateTimeOffsetRecord>().ToBytes(records);

        using var ms = new MemoryStream(bytes);
        var rows = HeroParser.Excel.Read().FromStream(ms);
        // ISO 8601 "O" format preserves the offset
        Assert.Contains("+05:00", rows[0][0]);
        Assert.Contains("2025-06-15", rows[0][0]);
    }

    // ──────────────────────────────────────────────
    // TimeOnly round-trip
    // ──────────────────────────────────────────────

    [Fact]
    public void TimeOnly_RoundTrip_DefaultFormat()
    {
        var records = new List<TimeRecord>
        {
            new() { Time = new TimeOnly(14, 30, 45) },
        };

        var bytes = HeroParser.Excel.Write<TimeRecord>().ToBytes(records);

        using var ms = new MemoryStream(bytes);
        var rows = HeroParser.Excel.Read().FromStream(ms);
        Assert.Equal("14:30:45", rows[0][0]);
    }

    // ──────────────────────────────────────────────
    // Argument validation error paths
    // ──────────────────────────────────────────────

    [Fact]
    public void WriteToFile_NullRecords_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            HeroParser.Excel.WriteToFile<SimpleProduct>("test.xlsx", null!));
    }

    [Fact]
    public void WriteToFile_EmptyPath_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            HeroParser.Excel.WriteToFile("", new List<SimpleProduct>()));
    }

    [Fact]
    public void WriteToStream_NullStream_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            HeroParser.Excel.WriteToStream(null!, new List<SimpleProduct>()));
    }

    [Fact]
    public void SerializeRecords_NullRecords_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            HeroParser.Excel.SerializeRecords<SimpleProduct>(null!));
    }

    [Fact]
    public void WriteMultiSheet_EmptySheetName_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            HeroParser.Excel.WriteMultiSheet()
                .WithSheet("", new List<SimpleProduct>()));
    }

    [Fact]
    public void WriteMultiSheet_NullRecords_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            HeroParser.Excel.WriteMultiSheet()
                .WithSheet<SimpleProduct>("Sheet1", null!));
    }

    // ──────────────────────────────────────────────
    // Sheet name validation
    // ──────────────────────────────────────────────

    [Fact]
    public void SheetName_ExceedsMaxLength_ThrowsExcelException()
    {
        var longName = new string('A', 32);
        Assert.Throws<ExcelException>(() =>
            HeroParser.Excel.Write<SimpleProduct>()
                .WithSheetName(longName)
                .ToBytes([]));
    }

    [Theory]
    [InlineData("Sheet/1")]
    [InlineData("Sheet\\1")]
    [InlineData("Sheet?1")]
    [InlineData("Sheet*1")]
    [InlineData("Sheet[1]")]
    [InlineData("Sheet:1")]
    public void SheetName_IllegalCharacters_ThrowsExcelException(string name)
    {
        Assert.Throws<ExcelException>(() =>
            HeroParser.Excel.Write<SimpleProduct>()
                .WithSheetName(name)
                .ToBytes([]));
    }

    [Fact]
    public void SheetName_DuplicateNames_ThrowsExcelException()
    {
        var sheet1 = new List<SimpleProduct> { new() { Name = "A", Price = 1m, Quantity = 1 } };
        var sheet2 = new List<SimpleProduct> { new() { Name = "B", Price = 2m, Quantity = 2 } };
        Assert.Throws<ExcelException>(() =>
            HeroParser.Excel.WriteMultiSheet()
                .WithSheet("Data", sheet1)
                .WithSheet("Data", sheet2)
                .ToBytes());
    }

    // ──────────────────────────────────────────────
    // Facade sheetName parameter
    // ──────────────────────────────────────────────

    [Fact]
    public void SerializeRecords_CustomSheetName_CanBeReadBack()
    {
        var records = new List<SimpleProduct>
        {
            new() { Name = "Test", Price = 1m, Quantity = 1 },
        };

        var bytes = HeroParser.Excel.SerializeRecords(records, sheetName: "MySheet");

        using var ms = new MemoryStream(bytes);
        var readBack = HeroParser.Excel.Read<SimpleProduct>().FromSheet("MySheet").FromStream(ms);
        Assert.Single(readBack);
        Assert.Equal("Test", readBack[0].Name);
    }
}

/// <summary>A record with a DateOnly property.</summary>
public class DateRecord
{
    /// <summary>Gets or sets the date.</summary>
    public DateOnly Date { get; set; }
}

/// <summary>A record with a TimeOnly property.</summary>
public class TimeRecord
{
    /// <summary>Gets or sets the time.</summary>
    public TimeOnly Time { get; set; }
}

/// <summary>A record with a DateTimeOffset property.</summary>
public class DateTimeOffsetRecord
{
    /// <summary>Gets or sets the timestamp.</summary>
    public DateTimeOffset Timestamp { get; set; }
}

/// <summary>A record with a double property to test culture-dependent number formatting.</summary>
public class WriteFormatRecord
{
    /// <summary>Gets or sets the label.</summary>
    [TabularMap(Name = "Label")]
    public string Label { get; set; } = "";

    /// <summary>Gets or sets the numeric value.</summary>
    [TabularMap(Name = "Value")]
    public double Value { get; set; }
}
