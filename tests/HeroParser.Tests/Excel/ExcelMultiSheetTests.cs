using HeroParser.Tests.Fixtures.Excel;
using Xunit;

namespace HeroParser.Tests.Excel;

[Trait("Category", "Unit")]
public class ExcelMultiSheetTests
{
    [Fact]
    public void MultiSheet_DifferentTypes_ReadsCorrectly()
    {
        var sheets = new Dictionary<string, string[][]>
        {
            ["Orders"] = [["Product", "Amount"], ["Widget", "9.99"]],
            ["Customers"] = [["Name", "Email"], ["Alice", "a@b.com"]],
        };
        using var xlsx = ExcelTestHelper.CreateXlsx(sheets);

        var result = HeroParser.Excel.Read()
            .WithSheet<OrderRecord>("Orders")
            .WithSheet<CustomerRecord>("Customers")
            .FromStream(xlsx);

        var orders = result.Get<OrderRecord>();
        Assert.Single(orders);
        Assert.Equal("Widget", orders[0].Product);
        Assert.Equal(9.99m, orders[0].Amount);

        var customers = result.Get<CustomerRecord>();
        Assert.Single(customers);
        Assert.Equal("Alice", customers[0].Name);
        Assert.Equal("a@b.com", customers[0].Email);
    }

    [Fact]
    public void MultiSheet_GetUnregisteredType_ThrowsInvalidOperation()
    {
        var sheets = new Dictionary<string, string[][]>
        {
            ["Orders"] = [["Product", "Amount"], ["Widget", "9.99"]],
        };
        using var xlsx = ExcelTestHelper.CreateXlsx(sheets);

        var result = HeroParser.Excel.Read()
            .WithSheet<OrderRecord>("Orders")
            .FromStream(xlsx);

        var ex = Assert.Throws<InvalidOperationException>(result.Get<CustomerRecord>);
        Assert.Contains("CustomerRecord", ex.Message);
    }

    [Fact]
    public void MultiSheet_EmptySheets_ReturnsEmptyLists()
    {
        var sheets = new Dictionary<string, string[][]>
        {
            ["Orders"] = [["Product", "Amount"]],
            ["Customers"] = [["Name", "Email"]],
        };
        using var xlsx = ExcelTestHelper.CreateXlsx(sheets);

        var result = HeroParser.Excel.Read()
            .WithSheet<OrderRecord>("Orders")
            .WithSheet<CustomerRecord>("Customers")
            .FromStream(xlsx);

        Assert.Empty(result.Get<OrderRecord>());
        Assert.Empty(result.Get<CustomerRecord>());
    }

    [Fact]
    public void MultiSheet_MultipleRowsPerSheet()
    {
        var sheets = new Dictionary<string, string[][]>
        {
            ["Orders"] = [
                ["Product", "Amount"],
                ["Widget", "9.99"],
                ["Gadget", "24.95"]
            ],
            ["Customers"] = [
                ["Name", "Email"],
                ["Alice", "a@b.com"],
                ["Bob", "b@c.com"],
                ["Charlie", "c@d.com"]
            ],
        };
        using var xlsx = ExcelTestHelper.CreateXlsx(sheets);

        var result = HeroParser.Excel.Read()
            .WithSheet<OrderRecord>("Orders")
            .WithSheet<CustomerRecord>("Customers")
            .FromStream(xlsx);

        Assert.Equal(2, result.Get<OrderRecord>().Count);
        Assert.Equal(3, result.Get<CustomerRecord>().Count);
        Assert.Equal("Gadget", result.Get<OrderRecord>()[1].Product);
        Assert.Equal("Charlie", result.Get<CustomerRecord>()[2].Name);
    }
}

[GenerateBinder]
public class OrderRecord
{
    [TabularMap(Name = "Product")]
    public string Product { get; set; } = "";

    [TabularMap(Name = "Amount")]
    public decimal Amount { get; set; }
}

[GenerateBinder]
public class CustomerRecord
{
    [TabularMap(Name = "Name")]
    public string Name { get; set; } = "";

    [TabularMap(Name = "Email")]
    public string Email { get; set; } = "";
}
