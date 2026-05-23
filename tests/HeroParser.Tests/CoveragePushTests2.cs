using System.IO.Pipelines;
using System.Text;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Writing;
using HeroParser.Validation;
using Xunit;

namespace HeroParser.Tests;

/// <summary>Second wave — pipe reader edge cases, async writer paths, encoding/format variations.</summary>
public class CoveragePushTests2
{
    // ----- PipeReader: comment characters, escape characters, multi-line quoted -----

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task PipeReader_WithCommentCharacter_SkipsCommentLines()
    {
        byte[] utf8 = Encoding.UTF8.GetBytes("# header comment\nName,Age\n# inline comment\nAlice,30\n");
        using var stream = new MemoryStream(utf8);
        PipeReader pipe = PipeReader.Create(stream);

        var rows = new List<CoveragePerson>();
        await foreach (var r in Csv.DeserializeRecordsAsync<CoveragePerson>(
            pipe,
            recordOptions: null,
            parserOptions: new CsvReadOptions { CommentCharacter = '#' },
            cancellationToken: TestContext.Current.CancellationToken))
        {
            rows.Add(r);
        }
        Assert.Single(rows);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task PipeReader_CommentEndingWithCrLf()
    {
        byte[] utf8 = Encoding.UTF8.GetBytes("# c1\r\nName,Age\r\nAlice,30\r\n");
        using var stream = new MemoryStream(utf8);
        var rows = new List<CoveragePerson>();
        await foreach (var r in Csv.DeserializeRecordsAsync<CoveragePerson>(
            PipeReader.Create(stream),
            parserOptions: new CsvReadOptions { CommentCharacter = '#' },
            cancellationToken: TestContext.Current.CancellationToken))
        {
            rows.Add(r);
        }
        Assert.Single(rows);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task PipeReader_WithEscapeCharacter()
    {
        byte[] utf8 = Encoding.UTF8.GetBytes("Name,Age\n\"Al\\\"ice\",30\n");
        using var stream = new MemoryStream(utf8);
        int count = 0;
        await foreach (var _ in Csv.DeserializeRecordsAsync<CoveragePerson>(
            PipeReader.Create(stream),
            parserOptions: new CsvReadOptions { EscapeCharacter = '\\' },
            cancellationToken: TestContext.Current.CancellationToken))
        {
            count++;
        }
        Assert.Equal(1, count);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task PipeReader_NewlineInsideQuotes()
    {
        byte[] utf8 = Encoding.UTF8.GetBytes("Name,Age\n\"Line1\nLine2\",30\n");
        using var stream = new MemoryStream(utf8);
        int count = 0;
        await foreach (var _ in Csv.DeserializeRecordsAsync<CoveragePerson>(
            PipeReader.Create(stream),
            parserOptions: new CsvReadOptions { AllowNewlinesInsideQuotes = true },
            cancellationToken: TestContext.Current.CancellationToken))
        {
            count++;
        }
        Assert.Equal(1, count);
    }

    // (Removed: HasHeaderRow=false requires positional/index mapping which the source-generated
    // binder for CoveragePerson does not provide; no-header reading is exercised in existing tests.)

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task PipeReader_SkipRows()
    {
        byte[] utf8 = Encoding.UTF8.GetBytes("BANNER\nName,Age\nAlice,30\nBob,25\n");
        using var stream = new MemoryStream(utf8);
        int count = 0;
        await foreach (var _ in Csv.DeserializeRecordsAsync<CoveragePerson>(
            PipeReader.Create(stream),
            recordOptions: new SeparatedValues.Reading.Records.CsvRecordOptions { SkipRows = 1 },
            cancellationToken: TestContext.Current.CancellationToken))
        {
            count++;
        }
        Assert.Equal(2, count);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task PipeReader_EmptyInput()
    {
        using var stream = new MemoryStream();
        int count = 0;
        await foreach (var _ in Csv.DeserializeRecordsAsync<CoveragePerson>(
            PipeReader.Create(stream),
            cancellationToken: TestContext.Current.CancellationToken))
        {
            count++;
        }
        Assert.Equal(0, count);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task PipeReader_LargeRowSpanningSegments()
    {
        // 50 KiB single row across multiple pipe segments.
        var sb = new StringBuilder("Name,Age\n");
        sb.Append('"');
        sb.Append('x', 50000);
        sb.Append("\",30\n");
        byte[] utf8 = Encoding.UTF8.GetBytes(sb.ToString());
        using var stream = new MemoryStream(utf8);

        int count = 0;
        await foreach (var _ in Csv.DeserializeRecordsAsync<CoveragePerson>(
            PipeReader.Create(stream),
            cancellationToken: TestContext.Current.CancellationToken))
        {
            count++;
        }
        Assert.Equal(1, count);
    }

    // ----- Async stream writer: exercise different write paths -----

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task AsyncWriter_LargeBatch_SpansFlushes()
    {
        var rows = Enumerable.Range(0, 5_000).Select(i => new CoveragePerson { Name = $"Person{i}", Age = i });
        await ToAsync(rows, async source =>
        {
            using var stream = new MemoryStream();
            await Csv.WriteToStreamAsync(stream, source, cancellationToken: TestContext.Current.CancellationToken);
            Assert.True(stream.Length > 50_000);
        });
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task AsyncWriter_QuotedFieldsWithCommas()
    {
        var rows = new[]
        {
            new CoveragePerson { Name = "comma,inside", Age = 30 },
            new CoveragePerson { Name = "quote\"inside", Age = 31 },
            new CoveragePerson { Name = "newline\ninside", Age = 32 }
        };
        using var stream = new MemoryStream();
        await Csv.WriteToStreamAsync(stream, rows, cancellationToken: TestContext.Current.CancellationToken);
        string csv = Encoding.UTF8.GetString(stream.ToArray());
        Assert.Contains("\"comma,inside\"", csv);
        Assert.Contains("\"quote\"\"inside\"", csv);
        Assert.Contains("\"newline\ninside\"", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task AsyncWriter_NullValues_Honored()
    {
        var rows = new[] { new NullableAgePerson { Name = "Alice", Age = null } };
        using var stream = new MemoryStream();
        await Csv.WriteToStreamAsync(
            stream,
            rows,
            options: new CsvWriteOptions { NullValue = "NULL" },
            cancellationToken: TestContext.Current.CancellationToken);
        string csv = Encoding.UTF8.GetString(stream.ToArray());
        Assert.Contains("NULL", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task AsyncWriter_AlwaysQuote()
    {
        var rows = new[] { new CoveragePerson { Name = "Alice", Age = 30 } };
        using var stream = new MemoryStream();
        await Csv.WriteToStreamAsync(
            stream,
            rows,
            options: new CsvWriteOptions { QuoteStyle = QuoteStyle.Always },
            cancellationToken: TestContext.Current.CancellationToken);
        string csv = Encoding.UTF8.GetString(stream.ToArray());
        Assert.Contains("\"Alice\"", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task AsyncWriter_NeverQuote()
    {
        var rows = new[] { new CoveragePerson { Name = "Alice", Age = 30 } };
        using var stream = new MemoryStream();
        await Csv.WriteToStreamAsync(
            stream,
            rows,
            options: new CsvWriteOptions { QuoteStyle = QuoteStyle.Never },
            cancellationToken: TestContext.Current.CancellationToken);
        string csv = Encoding.UTF8.GetString(stream.ToArray());
        Assert.DoesNotContain("\"", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task AsyncWriter_LongFieldsForceFlushes()
    {
        var rows = Enumerable.Range(0, 50).Select(i => new CoveragePerson { Name = new string('x', 4000), Age = i });
        using var stream = new MemoryStream();
        await Csv.WriteToStreamAsync(stream, rows, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(stream.Length > 100_000);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task AsyncWriter_CustomEncoding_Latin1()
    {
        var rows = new[] { new CoveragePerson { Name = "Cafe", Age = 30 } };
        using var stream = new MemoryStream();
        await Csv.WriteToStreamAsync(
            stream,
            rows,
            encoding: Encoding.Latin1,
            cancellationToken: TestContext.Current.CancellationToken);
        string csv = Encoding.Latin1.GetString(stream.ToArray());
        Assert.Contains("Cafe", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task AsyncWriter_CrlfNewLine()
    {
        var rows = new[]
        {
            new CoveragePerson { Name = "Alice", Age = 30 },
            new CoveragePerson { Name = "Bob", Age = 25 }
        };
        using var stream = new MemoryStream();
        await Csv.WriteToStreamAsync(
            stream,
            rows,
            options: new CsvWriteOptions { NewLine = "\r\n" },
            cancellationToken: TestContext.Current.CancellationToken);
        string csv = Encoding.UTF8.GetString(stream.ToArray());
        Assert.Contains("\r\n", csv);
    }

    // ----- Record writer paths via Csv.WriteToText -----

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void RecordWriter_AllPrimitiveTypes()
    {
        var rows = new[]
        {
            new AllTypes
            {
                S = "text", I = 42, L = 99999999999L, D = 3.14, B = true,
                Dt = new DateTime(2024, 6, 1), G = Guid.NewGuid(), F = 2.5f, M = 1.5m
            }
        };
        string csv = Csv.WriteToText(rows);
        Assert.Contains("text", csv);
        Assert.Contains("42", csv);
        Assert.Contains("99999999999", csv);
        Assert.Contains("True", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void RecordWriter_NullableAllTypes_NullsAreEmpty()
    {
        var rows = new[] { new NullableTypes() };
        string csv = Csv.WriteToText(rows);
        Assert.Contains(",,,,", csv); // multiple consecutive commas
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void RecordWriter_RoundTripWithReader()
    {
        var rows = new[]
        {
            new AllTypes { S = "alpha", I = 1, L = 2, D = 3.5, B = true, Dt = new DateTime(2024, 1, 1), G = Guid.Empty, F = 1.5f, M = 0.5m },
            new AllTypes { S = "beta",  I = 2, L = 3, D = 4.5, B = false, Dt = new DateTime(2024, 1, 2), G = Guid.Empty, F = 2.5f, M = 0.5m }
        };
        string csv = Csv.WriteToText(rows);
        using var reader = Csv.Read<AllTypes>().FromText(csv);
        var read = SeparatedValues.Reading.Records.ExtensionsToCsvRecordReader.ToList(reader);
        Assert.Equal(2, read.Count);
        Assert.Equal("alpha", read[0].S);
        Assert.Equal(2, read[1].I);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void RecordWriter_DateTimeFormat()
    {
        var rows = new[] { new EventRow { When = new DateTime(2024, 6, 1, 12, 30, 45) } };
        string csv = Csv.WriteToText(rows, options: new CsvWriteOptions { DateTimeFormat = "yyyyMMdd" });
        Assert.Contains("20240601", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void RecordWriter_NumberFormat()
    {
        var rows = new[] { new MoneyRow { Amount = 1234.5678m } };
        string csv = Csv.WriteToText(rows, options: new CsvWriteOptions { NumberFormat = "0.00" });
        Assert.Contains("1234.57", csv);
    }

    // ----- CsvAsyncStreamReader edge cases -----

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task AsyncStreamReader_ReadAsync_ProducesRows()
    {
        byte[] utf8 = Encoding.UTF8.GetBytes("a,b\n1,2\n3,4\n");
        using var stream = new MemoryStream(utf8);
        await using var reader = Csv.CreateAsyncStreamReader(stream);
        int rowCount = 0;
        while (await reader.MoveNextAsync(TestContext.Current.CancellationToken))
        {
            rowCount++;
        }
        Assert.Equal(3, rowCount);
    }

    private static async Task ToAsync<T>(IEnumerable<T> source, Func<IAsyncEnumerable<T>, Task> consumer)
    {
        await consumer(Async(source));
        static async IAsyncEnumerable<T> Async(IEnumerable<T> s)
        {
            foreach (var item in s) { yield return item; await Task.Yield(); }
        }
    }
}

[GenerateBinder]
public class AllTypes
{
    public string? S { get; set; }
    public int I { get; set; }
    public long L { get; set; }
    public double D { get; set; }
    public bool B { get; set; }
    public DateTime Dt { get; set; }
    public Guid G { get; set; }
    public float F { get; set; }
    public decimal M { get; set; }
}

[GenerateBinder]
public class NullableTypes
{
    public string? S { get; set; }
    public int? I { get; set; }
    public long? L { get; set; }
    public double? D { get; set; }
    public bool? B { get; set; }
    public DateTime? Dt { get; set; }
}
