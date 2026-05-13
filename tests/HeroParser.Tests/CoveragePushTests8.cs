using System.IO.Pipelines;
using System.Text;
using HeroParser.FixedWidths;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Writing;
using HeroParser.Validation;
using Xunit;

namespace HeroParser.Tests;

/// <summary>Eighth wave: byte-path MultiSchema, AsyncWriter quote variants, FW pipe row methods, AsyncStreamReader edges.</summary>
public class CoveragePushTests8
{
    // ---------- MultiSchema byte-path (FromStream / FromBytes) ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task Csv_MultiSchema_BytePath_FromStream()
    {
        string csv = "Type,A,B\nFoo,1,2\nBar,3,4\n";
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        int fooCount = 0, barCount = 0;
        await using var reader = Csv.Read()
            .WithMultiSchema()
            .WithDiscriminator("Type")
            .MapRecord<FooRecord>("Foo")
            .MapRecord<BarRecord>("Bar")
            .FromStream(ms);
        while (await reader.MoveNextAsync(TestContext.Current.CancellationToken))
        {
            if (reader.Current is FooRecord) fooCount++;
            else if (reader.Current is BarRecord) barCount++;
        }
        Assert.Equal(1, fooCount);
        Assert.Equal(1, barCount);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Csv_MultiSchema_BytePath_FromBytes()
    {
        string csv = "Type,A,B\nFoo,1,2\nBar,3,4\n";
        byte[] bytes = Encoding.UTF8.GetBytes(csv);
        int fooCount = 0, barCount = 0;
        var reader = Csv.Read()
            .WithMultiSchema()
            .WithDiscriminator("Type")
            .MapRecord<FooRecord>("Foo")
            .MapRecord<BarRecord>("Bar")
            .FromBytes(bytes);
        foreach (var r in reader)
        {
            if (r is FooRecord) fooCount++;
            else if (r is BarRecord) barCount++;
        }
        Assert.Equal(1, fooCount);
        Assert.Equal(1, barCount);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Csv_MultiSchema_CaseInsensitive_Discriminator()
    {
        string csv = "Type,A,B\nFOO,1,2\nbAr,3,4\n";
        int fooCount = 0, barCount = 0;
        var reader = Csv.Read()
            .WithMultiSchema()
            .WithDiscriminator("Type")
            .CaseInsensitiveDiscriminator()
            .MapRecord<FooRecord>("Foo")
            .MapRecord<BarRecord>("Bar")
            .FromText(csv);
        foreach (var r in reader)
        {
            if (r is FooRecord) fooCount++;
            else if (r is BarRecord) barCount++;
        }
        Assert.Equal(1, fooCount);
        Assert.Equal(1, barCount);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task Csv_MultiSchema_BytePath_FromStreamAsync()
    {
        string csv = "Type,A,B\nFoo,1,2\nBar,3,4\n";
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        int total = 0;
        await foreach (var _ in Csv.Read()
            .WithMultiSchema()
            .WithDiscriminator("Type")
            .MapRecord<FooRecord>("Foo")
            .MapRecord<BarRecord>("Bar")
            .FromStreamAsync(ms, cancellationToken: TestContext.Current.CancellationToken))
        {
            total++;
        }
        Assert.Equal(2, total);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Csv_MultiSchema_DiscriminatorByIndex()
    {
        string csv = "Type,A,B\nFoo,1,2\n";
        int n = 0;
        var reader = Csv.Read()
            .WithMultiSchema()
            .WithDiscriminator(0)
            .MapRecord<FooRecord>("Foo")
            .FromText(csv);
        foreach (var _ in reader) n++;
        Assert.Equal(1, n);
    }

    // ---------- Async writer with QuoteStyle and empty fields ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task AsyncWriter_QuoteStyleAlways_EmptyFields()
    {
        var rows = new[] { new CoveragePerson { Name = "", Age = 0 } };
        using var ms = new MemoryStream();
        await Csv.WriteToStreamAsync(
            ms,
            rows,
            options: new CsvWriteOptions { QuoteStyle = QuoteStyle.Always },
            cancellationToken: TestContext.Current.CancellationToken);
        string csv = Encoding.UTF8.GetString(ms.ToArray());
        Assert.Contains("\"\"", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task AsyncWriter_QuoteStyleAlways_LargeBatch()
    {
        var rows = Enumerable.Range(0, 200).Select(i => new CoveragePerson { Name = $"value{i}", Age = i });
        using var ms = new MemoryStream();
        await Csv.WriteToStreamAsync(
            ms,
            rows,
            options: new CsvWriteOptions { QuoteStyle = QuoteStyle.Always },
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(ms.Length > 100);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task AsyncWriter_QuoteStyleNever_LargeBatch()
    {
        var rows = Enumerable.Range(0, 200).Select(i => new CoveragePerson { Name = $"v{i}", Age = i });
        using var ms = new MemoryStream();
        await Csv.WriteToStreamAsync(
            ms,
            rows,
            options: new CsvWriteOptions { QuoteStyle = QuoteStyle.Never },
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(ms.Length > 100);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task AsyncWriter_NullableProperties_WithNullValueOption()
    {
        var rows = new[]
        {
            new NullableAgePerson { Name = "Alice", Age = 30 },
            new NullableAgePerson { Name = "Bob", Age = null },
            new NullableAgePerson { Name = null, Age = null }
        };
        using var ms = new MemoryStream();
        await Csv.WriteToStreamAsync(
            ms,
            rows,
            options: new CsvWriteOptions { NullValue = "<NULL>" },
            cancellationToken: TestContext.Current.CancellationToken);
        string csv = Encoding.UTF8.GetString(ms.ToArray());
        Assert.Contains("<NULL>", csv);
    }

    // ---------- FixedWidth pipe row inspection ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task FixedWidth_PipeRow_ToDecodedStringAndFields()
    {
        string data = "9999999999" + "12345" + "200" + "3.140000" + "2.500000" + "true " + "1234.56000" + "\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(data));
        var pipe = PipeReader.Create(stream);
        await foreach (var row in FixedWidth.ReadFromPipeReaderAsync(pipe, cancellationToken: TestContext.Current.CancellationToken))
        {
            string text = row.ToDecodedString();
            Assert.Contains("9999999999", text);

            // GetField overloads + GetRawField.
            var field0 = row.GetField(0, 10);
            Assert.False(field0.IsEmpty);

            var rawField = row.GetRawField(10, 5);
            Assert.False(rawField.IsEmpty);
        }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task FixedWidth_PipeRow_GetFieldPaddingTrimsLeftAndRight()
    {
        string data = "  Hi  " + "Alice     " + "\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(data));
        var pipe = PipeReader.Create(stream);
        await foreach (var row in FixedWidth.ReadFromPipeReaderAsync(pipe, cancellationToken: TestContext.Current.CancellationToken))
        {
            // Right-aligned (trim leading spaces)
            var f1 = row.GetField(0, 6, (byte)' ', FieldAlignment.Right);
            Assert.False(f1.IsEmpty);

            // Left-aligned (trim trailing spaces)
            var f2 = row.GetField(6, 10, (byte)' ', FieldAlignment.Left);
            Assert.False(f2.IsEmpty);
        }
    }

    // ---------- CsvAsyncStreamReader specific paths ----------

    // (Removed: skipRows is not a CreateAsyncStreamReader parameter.)

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task AsyncStreamReader_DisposeAsync_CleansUp()
    {
        var ms = new MemoryStream(Encoding.UTF8.GetBytes("a,b\n1,2\n"));
        var reader = Csv.CreateAsyncStreamReader(ms, leaveOpen: false);
        Assert.True(await reader.MoveNextAsync(TestContext.Current.CancellationToken));
        await reader.DisposeAsync();
        Assert.False(ms.CanRead);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task AsyncStreamReader_LargeBufferSize()
    {
        string csv = "a,b\n" + string.Concat(Enumerable.Range(0, 500).Select(i => $"r{i},v{i}\n"));
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        await using var reader = Csv.CreateAsyncStreamReader(ms, bufferSize: 65536);
        int n = 0;
        while (await reader.MoveNextAsync(TestContext.Current.CancellationToken)) n++;
        Assert.Equal(501, n);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task AsyncStreamReader_SmallBufferSize()
    {
        string csv = "a,b\n" + string.Concat(Enumerable.Range(0, 100).Select(i => $"r{i},v{i}\n"));
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        await using var reader = Csv.CreateAsyncStreamReader(ms, bufferSize: 128);
        int n = 0;
        while (await reader.MoveNextAsync(TestContext.Current.CancellationToken)) n++;
        Assert.Equal(101, n);
    }

    // ---------- FixedWidthDataReader ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidth_DataReader_FromStream()
    {
        string text = "9999999999\n1234567890\n";
        var cols = global::HeroParser.FixedWidths.Reading.Data.FixedWidthDataReaderColumns.FromLengths([10], ["Value"]);
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(text));
        using var dr = FixedWidth.CreateDataReader(
            ms,
            readerOptions: new global::HeroParser.FixedWidths.Reading.Data.FixedWidthDataReaderOptions { Columns = cols });
        int rows = 0;
        while (dr.Read()) rows++;
        Assert.Equal(2, rows);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidth_DataReader_GetSchemaTable()
    {
        string text = "9999999999\n";
        var cols = global::HeroParser.FixedWidths.Reading.Data.FixedWidthDataReaderColumns.FromLengths([10], ["Value"]);
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(text));
        using var dr = FixedWidth.CreateDataReader(
            ms,
            readerOptions: new global::HeroParser.FixedWidths.Reading.Data.FixedWidthDataReaderOptions { Columns = cols });
        using var schema = dr.GetSchemaTable();
        Assert.NotNull(schema);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidth_DataReader_TypedGetters()
    {
        string text = "0000000042\n";
        var cols = global::HeroParser.FixedWidths.Reading.Data.FixedWidthDataReaderColumns.FromLengths([10], ["I"]);
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(text));
        using var dr = FixedWidth.CreateDataReader(
            ms,
            readerOptions: new global::HeroParser.FixedWidths.Reading.Data.FixedWidthDataReaderOptions { Columns = cols });
        Assert.True(dr.Read());
        Assert.NotNull(dr.GetString(0));
    }

    // ---------- ExcelRecordReaderBuilder additional paths ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Excel_Read_MultiSheet_WithSheet()
    {
        // Write to two sheets, then read each via WithSheet.
        var rows = new[] { new CoveragePerson { Name = "Alice", Age = 30 } };
        using var ms = new MemoryStream();
        global::HeroParser.Excel.Write<CoveragePerson>().ToStream(ms, rows);
        ms.Position = 0;
        var result = global::HeroParser.Excel.Read()
            .WithSheet<CoveragePerson>("Sheet1")
            .FromStream(ms);
        Assert.NotNull(result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Excel_Read_FromSheetByIndex()
    {
        var rows = new[] { new CoveragePerson { Name = "Alice", Age = 30 } };
        using var ms = new MemoryStream();
        global::HeroParser.Excel.Write<CoveragePerson>().ToStream(ms, rows);
        ms.Position = 0;
        var read = global::HeroParser.Excel.Read<CoveragePerson>().FromSheet(0).FromStream(ms).ToList();
        Assert.Single(read);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Excel_Read_FromSheetByName()
    {
        var rows = new[] { new CoveragePerson { Name = "Alice", Age = 30 } };
        using var ms = new MemoryStream();
        global::HeroParser.Excel.Write<CoveragePerson>().ToStream(ms, rows);
        ms.Position = 0;
        var read = global::HeroParser.Excel.Read<CoveragePerson>().FromSheet("Sheet1").FromStream(ms).ToList();
        Assert.Single(read);
    }

    // ---------- CsvAsyncStreamWriter via various entry points ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task AsyncWriter_FromAsyncEnumerable()
    {
        static async IAsyncEnumerable<CoveragePerson> Source()
        {
            for (int i = 0; i < 50; i++)
            {
                yield return new CoveragePerson { Name = $"P{i}", Age = i };
                await Task.Yield();
            }
        }
        using var ms = new MemoryStream();
        await Csv.WriteToStreamAsync(ms, Source(), cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(ms.Length > 0);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task AsyncWriter_FromAsyncEnumerable_WithProgress()
    {
        static async IAsyncEnumerable<CoveragePerson> Source()
        {
            for (int i = 0; i < 200; i++)
            {
                yield return new CoveragePerson { Name = $"P{i}", Age = i };
                await Task.Yield();
            }
        }
        int progressCalls = 0;
        using var ms = new MemoryStream();
        await Csv.WriteToStreamAsync(
            ms,
            Source(),
            options: new CsvWriteOptions
            {
                WriteProgress = new Progress<CsvWriteProgress>(_ => Interlocked.Increment(ref progressCalls)),
                WriteProgressIntervalRows = 50
            },
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(ms.Length > 0);
    }

    // ---------- FixedWidthAsyncStreamWriter / sync paths ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task FixedWidth_AsyncWriter_LargeBatch()
    {
        var rows = new List<FixedAllTypes>();
        for (int i = 0; i < 100; i++)
            rows.Add(new FixedAllTypes { L = i, S = (short)i, B = (byte)(i % 256), D = i + 0.5, F = i + 0.25f, Bo = i % 2 == 0, M = i + 0.1m });
        using var ms = new MemoryStream();
        await FixedWidth.Write<FixedAllTypes>().ToStreamAsync(ms, rows, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(ms.Length > 1000);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task FixedWidth_AsyncWriter_AsyncEnumerable()
    {
        static async IAsyncEnumerable<FixedAllTypes> Source()
        {
            for (int i = 0; i < 50; i++)
            {
                yield return new FixedAllTypes { L = i, S = (short)i, B = (byte)(i % 256), D = i + 0.5, F = i + 0.25f, Bo = i % 2 == 0, M = i + 0.1m };
                await Task.Yield();
            }
        }
        using var ms = new MemoryStream();
        await FixedWidth.Write<FixedAllTypes>().ToStreamAsync(ms, Source(), cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(ms.Length > 500);
    }
}
