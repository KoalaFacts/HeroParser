using HeroParser.Excels.Reading;
using HeroParser.Tests.Fixtures.Excel;
using HeroParser.Tests.Validation;
using HeroParser.Validation;
using Xunit;

namespace HeroParser.Tests.Excel;

[Trait("Category", "Unit")]
public class ExcelReadAsyncTests
{
    [Fact]
    public async Task FromStreamAsync_StreamsRecordsInOrder()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["Name", "Price", "Quantity"],
            ["Widget", "9.99", "100"],
            ["Gadget", "24.95", "50"]
        ]);

        var records = new List<SimpleProduct>();
        await foreach (var record in HeroParser.Excel.Read<SimpleProduct>()
            .FromStreamAsync(xlsx, TestContext.Current.CancellationToken))
        {
            records.Add(record);
        }

        Assert.Equal(2, records.Count);
        Assert.Equal("Widget", records[0].Name);
        Assert.Equal(9.99m, records[0].Price);
        Assert.Equal(100, records[0].Quantity);
        Assert.Equal("Gadget", records[1].Name);
    }

    [Fact]
    public async Task DeserializeRecordsAsync_FromStream_MatchesSyncResults()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["Name", "Price", "Quantity"],
            ["Widget", "9.99", "100"],
            ["Gadget", "24.95", "50"]
        ]);

        var records = new List<SimpleProduct>();
        await foreach (var record in HeroParser.Excel.DeserializeRecordsAsync<SimpleProduct>(
            xlsx, TestContext.Current.CancellationToken))
        {
            records.Add(record);
        }

        Assert.Equal(2, records.Count);
        Assert.Equal("Widget", records[0].Name);
        Assert.Equal("Gadget", records[1].Name);
    }

    [Fact]
    public async Task FromFileAsync_StreamsFromDisk()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["Name", "Price", "Quantity"],
            ["Widget", "9.99", "100"],
            ["Gadget", "24.95", "50"]
        ]);

        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(tempFile, xlsx.ToArray(), TestContext.Current.CancellationToken);

            var records = new List<SimpleProduct>();
            await foreach (var record in HeroParser.Excel.Read<SimpleProduct>()
                .FromFileAsync(tempFile, TestContext.Current.CancellationToken))
            {
                records.Add(record);
            }

            Assert.Equal(2, records.Count);
            Assert.Equal("Widget", records[0].Name);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task FromStreamAsync_EmptyWorksheet_YieldsNothing()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", []);

        var records = new List<SimpleProduct>();
        await foreach (var record in HeroParser.Excel.Read<SimpleProduct>()
            .FromStreamAsync(xlsx, TestContext.Current.CancellationToken))
        {
            records.Add(record);
        }

        Assert.Empty(records);
    }

    [Fact]
    public async Task FromStreamAsync_HeaderOnly_YieldsNothing()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["Name", "Price", "Quantity"]
        ]);

        var records = new List<SimpleProduct>();
        await foreach (var record in HeroParser.Excel.Read<SimpleProduct>()
            .FromStreamAsync(xlsx, TestContext.Current.CancellationToken))
        {
            records.Add(record);
        }

        Assert.Empty(records);
    }

    [Fact]
    public async Task FromStreamAsync_RespectsMaxRows()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["Name", "Price", "Quantity"],
            ["A", "1", "1"],
            ["B", "2", "2"],
            ["C", "3", "3"]
        ]);

        var records = new List<SimpleProduct>();
        await foreach (var record in HeroParser.Excel.Read<SimpleProduct>()
            .WithMaxRows(2)
            .FromStreamAsync(xlsx, TestContext.Current.CancellationToken))
        {
            records.Add(record);
        }

        Assert.Equal(2, records.Count);
        Assert.Equal("A", records[0].Name);
        Assert.Equal("B", records[1].Name);
    }

    [Fact]
    public async Task FromStreamAsync_RespectsSkipRows()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["Comment row to skip"],
            ["Name", "Price", "Quantity"],
            ["Widget", "9.99", "100"]
        ]);

        var records = new List<SimpleProduct>();
        await foreach (var record in HeroParser.Excel.Read<SimpleProduct>()
            .SkipRows(1)
            .FromStreamAsync(xlsx, TestContext.Current.CancellationToken))
        {
            records.Add(record);
        }

        Assert.Single(records);
        Assert.Equal("Widget", records[0].Name);
    }

    [Fact]
    public async Task FromStreamAsync_OnErrorSkipRecord_SkipsBadRow()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["Name", "Price", "Quantity"],
            ["Widget", "9.99", "100"],
            ["Broken", "1.00", "not-a-number"],
            ["Gadget", "2.50", "50"]
        ]);

        var skippedRows = new List<int>();

        var records = new List<SimpleProduct>();
        await foreach (var record in HeroParser.Excel.Read<SimpleProduct>()
            .OnError((ctx, _) =>
            {
                skippedRows.Add(ctx.Row);
                return ExcelDeserializeErrorAction.SkipRecord;
            })
            .FromStreamAsync(xlsx, TestContext.Current.CancellationToken))
        {
            records.Add(record);
        }

        Assert.Equal(2, records.Count);
        Assert.Equal("Widget", records[0].Name);
        Assert.Equal("Gadget", records[1].Name);
        Assert.Single(skippedRows);
    }

    [Fact]
    public async Task FromStreamAsync_StrictValidation_ThrowsAtEnd()
    {
        // Row 2 violates NotNull on Amount. Validation errors are collected during binding
        // and only thrown after the stream is exhausted, so the prior valid row is still yielded.
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["Id", "Amount", "Currency", "Reference"],
            ["TXN001", "500.00", "USD", "AB1234"],
            ["TXN002", "", "USD", "AB1234"]
        ]);

        var iterated = new List<ValidatedTransaction>();

        await Assert.ThrowsAsync<ValidationException>(async () =>
        {
            await foreach (var record in HeroParser.Excel.Read<ValidatedTransaction>()
                .WithValidationMode(ValidationMode.Strict)
                .FromStreamAsync(xlsx, TestContext.Current.CancellationToken))
            {
                iterated.Add(record);
            }
        });

        Assert.Single(iterated);
        Assert.Equal("TXN001", iterated[0].TransactionId);
    }

    [Fact]
    public async Task FromStreamAsync_CancellationToken_StopsIteration()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["Name", "Price", "Quantity"],
            ["A", "1", "1"],
            ["B", "2", "2"],
            ["C", "3", "3"]
        ]);

        using var cts = new CancellationTokenSource();
        var records = new List<SimpleProduct>();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await foreach (var record in HeroParser.Excel.Read<SimpleProduct>()
                .FromStreamAsync(xlsx, cts.Token))
            {
                records.Add(record);
                if (records.Count == 1)
                    cts.Cancel();
            }
        });

        Assert.Single(records);
    }
}
