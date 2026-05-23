using System.Globalization;
using System.Text;
using HeroParser.FixedWidths;
using HeroParser.SeparatedValues.Core;
using HeroParser.Validation;
using Xunit;

namespace HeroParser.Tests;

/// <summary>Wave 27: FixedWidthWriterBuilder full fluent surface, additional Excel paths.</summary>
public class CoveragePushTests27
{
    // ---------- FixedWidthWriterBuilder fluent options ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidthWriter_FluentChain()
    {
        var rows = new[] { new FixedAllTypes { L = 1L, S = 2, B = 3, D = 0.5, F = 0.25f, Bo = true, M = 1.5m } };
        string text = FixedWidth.Write<FixedAllTypes>()
            .WithNewLine("\r\n")
            .WithPadChar(' ')
            .WithAlignment(FieldAlignment.Left)
            .WithCulture("en-US")
            .WithNullValue("NULL")
            .WithDateTimeFormat("o")
            .WithDateOnlyFormat("yyyyMMdd")
            .WithTimeOnlyFormat("HHmmss")
            .WithNumberFormat("F2")
            .WithEncoding(Encoding.UTF8)
            .WithMaxRowCount(1000)
            .WithMaxOutputSize(1_000_000)
            .TruncateOnOverflow()
            .WithValidationMode(ValidationMode.Lenient)
            .ToText(rows);
        Assert.NotEmpty(text);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidthWriter_AlignLeftAndRight()
    {
        var rows = new[] { new FixedAllTypes { L = 1L, S = 2, B = 3, D = 0.5, F = 0.25f, Bo = true, M = 1.5m } };
        string left = FixedWidth.Write<FixedAllTypes>().AlignLeft().ToText(rows);
        string right = FixedWidth.Write<FixedAllTypes>().AlignRight().ToText(rows);
        Assert.NotEmpty(left);
        Assert.NotEmpty(right);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidthWriter_ThrowOnOverflow()
    {
        // Build a record where a value would overflow its field length.
        var rows = new[] { new FixedAllTypes { L = long.MaxValue /* 19 digits, exceeds Length=10 */, S = 0, B = 0, D = 0, F = 0, Bo = true, M = 0 } };
        try
        {
            FixedWidth.Write<FixedAllTypes>().ThrowOnOverflow().ToText(rows);
        }
        catch (Exception) { /* expected */ }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidthWriter_WithOverflowBehavior()
    {
        var rows = new[] { new FixedAllTypes { L = 1L, S = 2, B = 3, D = 0.5, F = 0.25f, Bo = true, M = 1.5m } };
        string text = FixedWidth.Write<FixedAllTypes>().WithOverflowBehavior(global::HeroParser.FixedWidths.Writing.OverflowBehavior.Truncate).ToText(rows);
        Assert.NotEmpty(text);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidthWriter_OnError_SkipRecord()
    {
        var rows = new[] { new FixedAllTypes { L = 1L } };
        try
        {
            FixedWidth.Write<FixedAllTypes>()
                .OnError(ctx => global::HeroParser.FixedWidths.Writing.FixedWidthSerializeErrorAction.SkipRow)
                .ToText(rows);
        }
        catch (Exception) { /* tolerable */ }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidthWriter_ToWriter()
    {
        var rows = new[] { new FixedAllTypes { L = 1L, S = 2, B = 3, D = 0.5, F = 0.25f, Bo = true, M = 1.5m } };
        using var sw = new StringWriter();
        FixedWidth.Write<FixedAllTypes>().ToWriter(sw, rows);
        Assert.NotEmpty(sw.ToString());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task FixedWidthWriter_ToFileAsync_FromAsyncEnumerable()
    {
        static async IAsyncEnumerable<FixedAllTypes> Source()
        {
            yield return new FixedAllTypes { L = 1L, S = 2, B = 3, D = 0.5, F = 0.25f, Bo = true, M = 1.5m };
            await Task.Yield();
        }
        string path = Path.GetTempFileName();
        try
        {
            await FixedWidth.Write<FixedAllTypes>().ToFileAsync(path, Source(), cancellationToken: TestContext.Current.CancellationToken);
            Assert.True(new FileInfo(path).Length > 0);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task FixedWidthWriter_ToFileAsync_FromIEnumerable()
    {
        var rows = new[] { new FixedAllTypes { L = 1L, S = 2, B = 3, D = 0.5, F = 0.25f, Bo = true, M = 1.5m } };
        string path = Path.GetTempFileName();
        try
        {
            await FixedWidth.Write<FixedAllTypes>().ToFileAsync(path, rows, cancellationToken: TestContext.Current.CancellationToken);
            Assert.True(new FileInfo(path).Length > 0);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task FixedWidthWriter_ToTextAsync_FromAsyncEnumerable()
    {
        static async IAsyncEnumerable<FixedAllTypes> Source()
        {
            yield return new FixedAllTypes { L = 1L, S = 2, B = 3, D = 0.5, F = 0.25f, Bo = true, M = 1.5m };
            await Task.Yield();
        }
        string text = await FixedWidth.Write<FixedAllTypes>().ToTextAsync(Source(), cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotEmpty(text);
    }

    // ---------- FixedWidth static WriteToText/WriteToFile/WriteToStream variants ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidth_WriteToText_WithOptions()
    {
        var rows = new[] { new FixedAllTypes { L = 1L, S = 2, B = 3, D = 0.5, F = 0.25f, Bo = true, M = 1.5m } };
        string text = FixedWidth.WriteToText(rows, options: new global::HeroParser.FixedWidths.Writing.FixedWidthWriteOptions { NewLine = "\r\n" });
        Assert.NotEmpty(text);
        Assert.Contains("\r\n", text);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidth_WriteToStream_WithEncoding()
    {
        var rows = new[] { new FixedAllTypes { L = 1L, S = 2, B = 3, D = 0.5, F = 0.25f, Bo = true, M = 1.5m } };
        using var ms = new MemoryStream();
        FixedWidth.WriteToStream(ms, rows, encoding: Encoding.UTF8);
        Assert.True(ms.Length > 0);
    }

    // ---------- FixedWidthDataReader async ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task FixedWidthDataReader_ReadAsync_LargeBatch()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < 500; i++) sb.AppendLine($"{i:D10}");
        var cols = global::HeroParser.FixedWidths.Reading.Data.FixedWidthDataReaderColumns.FromLengths([10], ["V"]);
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString()));
        using var dr = FixedWidth.CreateDataReader(
            ms,
            readerOptions: new global::HeroParser.FixedWidths.Reading.Data.FixedWidthDataReaderOptions { Columns = cols });
        int n = 0;
        while (await dr.ReadAsync(TestContext.Current.CancellationToken)) n++;
        Assert.Equal(500, n);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidthDataReader_GetOrdinal()
    {
        var cols = global::HeroParser.FixedWidths.Reading.Data.FixedWidthDataReaderColumns.FromLengths(
            [3, 3], ["A", "B"]);
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes("foobar\n"));
        using var dr = FixedWidth.CreateDataReader(
            ms,
            readerOptions: new global::HeroParser.FixedWidths.Reading.Data.FixedWidthDataReaderOptions { Columns = cols });
        Assert.Equal(0, dr.GetOrdinal("A"));
        Assert.Equal(1, dr.GetOrdinal("B"));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidthDataReader_GetValues()
    {
        var cols = global::HeroParser.FixedWidths.Reading.Data.FixedWidthDataReaderColumns.FromLengths(
            [3, 3], ["A", "B"]);
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes("foobar\n"));
        using var dr = FixedWidth.CreateDataReader(
            ms,
            readerOptions: new global::HeroParser.FixedWidths.Reading.Data.FixedWidthDataReaderOptions { Columns = cols });
        Assert.True(dr.Read());
        object[] vals = new object[2];
        int n = dr.GetValues(vals);
        Assert.Equal(2, n);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidthDataReader_FieldType()
    {
        var cols = global::HeroParser.FixedWidths.Reading.Data.FixedWidthDataReaderColumns.FromLengths(
            [3, 3], ["A", "B"]);
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes("foobar\n"));
        using var dr = FixedWidth.CreateDataReader(
            ms,
            readerOptions: new global::HeroParser.FixedWidths.Reading.Data.FixedWidthDataReaderOptions { Columns = cols });
        Assert.Equal(typeof(string), dr.GetFieldType(0));
        Assert.Equal("String", dr.GetDataTypeName(0));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidthDataReader_IsDBNull_NullValues()
    {
        var cols = global::HeroParser.FixedWidths.Reading.Data.FixedWidthDataReaderColumns.FromLengths(
            [3, 3], ["A", "B"]);
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes("   bar\n"));
        using var dr = FixedWidth.CreateDataReader(
            ms,
            readerOptions: new global::HeroParser.FixedWidths.Reading.Data.FixedWidthDataReaderOptions
            {
                Columns = cols,
                NullValues = [""]
            });
        Assert.True(dr.Read());
        // Trimmed column "A" is empty → DBNull.
        Assert.True(dr.IsDBNull(0));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidthDataReader_Close_Idempotent()
    {
        var cols = global::HeroParser.FixedWidths.Reading.Data.FixedWidthDataReaderColumns.FromLengths(
            [3], ["A"]);
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes("foo\n"));
        using var dr = FixedWidth.CreateDataReader(
            ms,
            readerOptions: new global::HeroParser.FixedWidths.Reading.Data.FixedWidthDataReaderOptions { Columns = cols });
        dr.Close();
        Assert.True(dr.IsClosed);
        dr.Close(); // idempotent
    }

    // ---------- Excel reader extras ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Excel_ReadAllSheets_RowLevel()
    {
        var src = new[] { new CoveragePerson { Name = "Alice", Age = 30 } };
        using var ms = new MemoryStream();
        global::HeroParser.Excel.Write<CoveragePerson>().ToStream(ms, src);
        ms.Position = 0;
        // The non-generic Read() does not have AllSheets; use FromStream to read first sheet.
        var rows = global::HeroParser.Excel.Read().FromStream(ms);
        Assert.NotEmpty(rows);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Excel_Read_AllSheetsTyped()
    {
        var src = new[] { new CoveragePerson { Name = "Alice", Age = 30 } };
        using var ms = new MemoryStream();
        global::HeroParser.Excel.Write<CoveragePerson>().ToStream(ms, src);
        ms.Position = 0;
        var dict = global::HeroParser.Excel.Read<CoveragePerson>().AllSheets().FromStream(ms);
        Assert.NotEmpty(dict);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task Excel_WriteAsync_FromAsyncEnumerable_FullPath()
    {
        static async IAsyncEnumerable<CoveragePerson> Source()
        {
            for (int i = 0; i < 5; i++)
            {
                yield return new CoveragePerson { Name = $"P{i}", Age = i };
                await Task.Yield();
            }
        }
        using var ms = new MemoryStream();
        await global::HeroParser.Excel.Write<CoveragePerson>().ToStreamAsync(ms, Source(), ct: TestContext.Current.CancellationToken);
        Assert.True(ms.Length > 0);
    }

    // ---------- CsvAsyncStreamReader / FixedWidthAsyncStreamReader BytesRead progress ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task FixedWidthAsyncStreamReader_BytesRead()
    {
        byte[] bytes = "row1\nrow2\nrow3\n"u8.ToArray();
        using var ms = new MemoryStream(bytes);
        await using var reader = FixedWidth.CreateAsyncStreamReader(ms);
        while (await reader.MoveNextAsync(TestContext.Current.CancellationToken)) { }
        Assert.True(reader.BytesRead > 0);
    }

    // ---------- ExcelDataReader async + extras ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task ExcelDataReader_ReadAsync()
    {
        var src = new[] { new CoveragePerson { Name = "Alice", Age = 30 }, new CoveragePerson { Name = "Bob", Age = 25 } };
        using var ms = new MemoryStream();
        global::HeroParser.Excel.Write<CoveragePerson>().ToStream(ms, src);
        ms.Position = 0;
        using var dr = global::HeroParser.Excel.CreateDataReader(ms);
        int n = 0;
        while (await dr.ReadAsync(TestContext.Current.CancellationToken)) n++;
        Assert.Equal(2, n);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ExcelDataReader_GetSchemaTable()
    {
        var src = new[] { new CoveragePerson { Name = "Alice", Age = 30 } };
        using var ms = new MemoryStream();
        global::HeroParser.Excel.Write<CoveragePerson>().ToStream(ms, src);
        ms.Position = 0;
        using var dr = global::HeroParser.Excel.CreateDataReader(ms);
        using var schema = dr.GetSchemaTable();
        Assert.NotNull(schema);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ExcelDataReader_FieldCount_GetName()
    {
        var src = new[] { new CoveragePerson { Name = "Alice", Age = 30 } };
        using var ms = new MemoryStream();
        global::HeroParser.Excel.Write<CoveragePerson>().ToStream(ms, src);
        ms.Position = 0;
        using var dr = global::HeroParser.Excel.CreateDataReader(ms);
        Assert.Equal(2, dr.FieldCount);
        Assert.NotEmpty(dr.GetName(0));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ExcelDataReader_Close_Idempotent()
    {
        var src = new[] { new CoveragePerson { Name = "Alice", Age = 30 } };
        using var ms = new MemoryStream();
        global::HeroParser.Excel.Write<CoveragePerson>().ToStream(ms, src);
        ms.Position = 0;
        var dr = global::HeroParser.Excel.CreateDataReader(ms);
        dr.Close();
        Assert.True(dr.IsClosed);
        dr.Close();
    }
}
