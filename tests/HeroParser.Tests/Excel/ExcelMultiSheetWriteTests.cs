using Xunit;

namespace HeroParser.Tests.Excel;

/// <summary>
/// Integration tests for multi-sheet Excel writing via <see cref="HeroParser.Excel.WriteMultiSheet()"/>.
/// Verifies that multiple typed sheets are written correctly and can be read back individually.
/// </summary>
[Trait("Category", "Integration")]
public class ExcelMultiSheetWriteTests
{
    // ──────────────────────────────────────────────
    // Basic two-sheet write and read-back
    // ──────────────────────────────────────────────

    [Fact]
    public void WriteMultiSheet_TwoSheetsDifferentTypes_RoundTripPreservesAllData()
    {
        var orders = new List<OrderRecord>
        {
            new() { Product = "Widget", Amount = 9.99m },
            new() { Product = "Gadget", Amount = 24.95m },
        };
        var customers = new List<CustomerRecord>
        {
            new() { Name = "Alice", Email = "alice@example.com" },
            new() { Name = "Bob", Email = "bob@example.com" },
        };

        var bytes = HeroParser.Excel.WriteMultiSheet()
            .WithSheet("Orders", orders)
            .WithSheet("Customers", customers)
            .ToBytes();

        using var ms = new MemoryStream(bytes);
        var result = HeroParser.Excel.Read()
            .WithSheet<OrderRecord>("Orders")
            .WithSheet<CustomerRecord>("Customers")
            .FromStream(ms);

        var readOrders = result.Get<OrderRecord>();
        Assert.Equal(2, readOrders.Count);
        Assert.Equal("Widget", readOrders[0].Product);
        Assert.Equal(9.99m, readOrders[0].Amount);
        Assert.Equal("Gadget", readOrders[1].Product);
        Assert.Equal(24.95m, readOrders[1].Amount);

        var readCustomers = result.Get<CustomerRecord>();
        Assert.Equal(2, readCustomers.Count);
        Assert.Equal("Alice", readCustomers[0].Name);
        Assert.Equal("alice@example.com", readCustomers[0].Email);
        Assert.Equal("Bob", readCustomers[1].Name);
        Assert.Equal("bob@example.com", readCustomers[1].Email);
    }

    [Fact]
    public void WriteMultiSheet_ToStream_CanBeReadBackBothSheets()
    {
        var orders = new List<OrderRecord>
        {
            new() { Product = "StreamWidget", Amount = 1.00m },
        };
        var customers = new List<CustomerRecord>
        {
            new() { Name = "StreamAlice", Email = "s@example.com" },
        };

        using var ms = new MemoryStream();
        HeroParser.Excel.WriteMultiSheet()
            .WithSheet("Orders", orders)
            .WithSheet("Customers", customers)
            .ToStream(ms, leaveOpen: true);

        ms.Position = 0;

        var result = HeroParser.Excel.Read()
            .WithSheet<OrderRecord>("Orders")
            .WithSheet<CustomerRecord>("Customers")
            .FromStream(ms);

        Assert.Single(result.Get<OrderRecord>());
        Assert.Single(result.Get<CustomerRecord>());
        Assert.Equal("StreamWidget", result.Get<OrderRecord>()[0].Product);
        Assert.Equal("StreamAlice", result.Get<CustomerRecord>()[0].Name);
    }

    [Fact]
    public void WriteMultiSheet_ToFile_CanBeReadBackBothSheets()
    {
        var orders = new List<OrderRecord>
        {
            new() { Product = "FileWidget", Amount = 2.50m },
        };
        var customers = new List<CustomerRecord>
        {
            new() { Name = "FileAlice", Email = "f@example.com" },
        };

        var path = Path.GetTempFileName() + ".xlsx";
        try
        {
            HeroParser.Excel.WriteMultiSheet()
                .WithSheet("Orders", orders)
                .WithSheet("Customers", customers)
                .ToFile(path);

            var result = HeroParser.Excel.Read()
                .WithSheet<OrderRecord>("Orders")
                .WithSheet<CustomerRecord>("Customers")
                .FromFile(path);

            Assert.Single(result.Get<OrderRecord>());
            Assert.Equal("FileWidget", result.Get<OrderRecord>()[0].Product);
            Assert.Single(result.Get<CustomerRecord>());
            Assert.Equal("FileAlice", result.Get<CustomerRecord>()[0].Name);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    // ──────────────────────────────────────────────
    // Sheet ordering
    // ──────────────────────────────────────────────

    [Fact]
    public void WriteMultiSheet_SheetsWrittenInRegistrationOrder()
    {
        var orders = new List<OrderRecord>
        {
            new() { Product = "First", Amount = 1.00m },
        };
        var customers = new List<CustomerRecord>
        {
            new() { Name = "Second", Email = "s@x.com" },
        };

        var bytes = HeroParser.Excel.WriteMultiSheet()
            .WithSheet("Orders", orders)
            .WithSheet("Customers", customers)
            .ToBytes();

        using var ms = new MemoryStream(bytes);
        // First sheet (index 0) should be Orders
        var firstSheetOrders = HeroParser.Excel.Read<OrderRecord>().FromSheet(0).FromStream(ms);
        Assert.Single(firstSheetOrders);
        Assert.Equal("First", firstSheetOrders[0].Product);
    }

    // ──────────────────────────────────────────────
    // Empty sheets
    // ──────────────────────────────────────────────

    [Fact]
    public void WriteMultiSheet_EmptySheets_ReadBackAsEmptyLists()
    {
        var bytes = HeroParser.Excel.WriteMultiSheet()
            .WithSheet<OrderRecord>("Orders", [])
            .WithSheet<CustomerRecord>("Customers", [])
            .ToBytes();

        using var ms = new MemoryStream(bytes);
        var result = HeroParser.Excel.Read()
            .WithSheet<OrderRecord>("Orders")
            .WithSheet<CustomerRecord>("Customers")
            .FromStream(ms);

        Assert.Empty(result.Get<OrderRecord>());
        Assert.Empty(result.Get<CustomerRecord>());
    }

    // ──────────────────────────────────────────────
    // Same type on different sheets
    // ──────────────────────────────────────────────

    [Fact]
    public void WriteMultiSheet_SameTypeOnDifferentSheets_EachReadableBySheetName()
    {
        var ordersA = new List<OrderRecord>
        {
            new() { Product = "SheetA-Product", Amount = 11.00m },
        };
        var ordersB = new List<OrderRecord>
        {
            new() { Product = "SheetB-Product", Amount = 22.00m },
        };

        var bytes = HeroParser.Excel.WriteMultiSheet()
            .WithSheet("SheetA", ordersA)
            .WithSheet("SheetB", ordersB)
            .ToBytes();

        using var msA = new MemoryStream(bytes);
        var fromA = HeroParser.Excel.Read<OrderRecord>().FromSheet("SheetA").FromStream(msA);
        Assert.Single(fromA);
        Assert.Equal("SheetA-Product", fromA[0].Product);

        using var msB = new MemoryStream(bytes);
        var fromB = HeroParser.Excel.Read<OrderRecord>().FromSheet("SheetB").FromStream(msB);
        Assert.Single(fromB);
        Assert.Equal("SheetB-Product", fromB[0].Product);
    }
}
