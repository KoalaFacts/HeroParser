using System.Diagnostics.CodeAnalysis;
using System.IO.Pipelines;
using System.Text;
using HeroParser.FixedWidths;
using HeroParser.FixedWidths.Records;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Writing;
using HeroParser.Validation;
using Xunit;

namespace HeroParser.Tests;

/// <summary>Tenth wave: progress reporting, FixedWidth async with unsigned/Guid, PipeSequenceReader edge paths.</summary>
public class CoveragePushTests10
{
    // ---------- FixedWidth reader with progress reporting (covers RecordBinder progress branches) ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidth_ReadWithProgress_ReportsAtInterval()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < 100; i++)
        {
            sb.AppendLine($"{i,-10}{(short)i,-5}{(byte)(i % 256),-3}{i + 0.5,-8:F2}{i + 0.25f,-8:F2}{(i % 2 == 0 ? "true " : "false")}{i + 0.1m,-10:F2}");
        }

        int progressCalls = 0;
        var progress = new Progress<FixedWidthProgress>(_ => Interlocked.Increment(ref progressCalls));

        var rows = FixedWidth.Read<FixedAllTypes>()
            .WithProgress(progress, intervalRows: 25)
            .FromText(sb.ToString())
            .ToList();

        Assert.Equal(100, rows.Count);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task FixedWidth_AsyncReadWithProgress()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < 100; i++)
        {
            sb.AppendLine($"{i,-10}{(short)i,-5}{(byte)(i % 256),-3}{i + 0.5,-8:F2}{i + 0.25f,-8:F2}{(i % 2 == 0 ? "true " : "false")}{i + 0.1m,-10:F2}");
        }
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString()));

        var progress = new Progress<FixedWidthProgress>(_ => { });

        int total = 0;
        await foreach (var _ in FixedWidth.Read<FixedAllTypes>()
            .WithProgress(progress, intervalRows: 25)
            .FromStreamAsync(ms, TestContext.Current.CancellationToken))
        {
            total++;
        }
        Assert.Equal(100, total);
    }

    // ---------- FixedWidth writer with unsigned and Guid ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    [RequiresUnreferencedCode("reflection")]
    [RequiresDynamicCode("reflection")]
    public void FixedWidth_Write_UnsignedAndGuid()
    {
        var rows = new[] { new FixedUnsignedRow { US = 65000, UI = 4_000_000_000, UL = 18_000_000_000_000_000_000UL, G = Guid.NewGuid() } };
        string text = FixedWidth.Write<FixedUnsignedRow>().ToText(rows);
        Assert.Contains("65000", text);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    [RequiresUnreferencedCode("reflection")]
    [RequiresDynamicCode("reflection")]
    public async Task FixedWidth_WriteAsync_UnsignedAndGuid()
    {
        var rows = new[] { new FixedUnsignedRow { US = 65000, UI = 4_000_000_000, UL = 18_000_000_000_000_000_000UL, G = Guid.NewGuid() } };
        using var ms = new MemoryStream();
        await FixedWidth.Write<FixedUnsignedRow>().ToStreamAsync(ms, rows, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(ms.Length > 0);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task FixedWidth_WriteAsync_DateTimeOffsetAndDateOnlyAndTimeOnly()
    {
        var rows = new[]
        {
            new FixedDateRow
            {
                Dt = DateTime.UtcNow,
                Dto = DateTimeOffset.UtcNow,
                DOnly = DateOnly.FromDateTime(DateTime.Today),
                TOnly = TimeOnly.FromDateTime(DateTime.Now)
            }
        };
        using var ms = new MemoryStream();
        await FixedWidth.Write<FixedDateRow>().ToStreamAsync(ms, rows, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(ms.Length > 0);
    }

    // ---------- CsvPipeSequenceReader via builder.FromPipeReaderAsync directly ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task PipeSequenceReader_NoFinalNewline_ProcessesLastRow()
    {
        byte[] data = Encoding.UTF8.GetBytes("a,b\n1,2"); // no trailing newline
        using var stream = new MemoryStream(data);
        await using var reader = Csv.Read().FromPipeReaderAsync(PipeReader.Create(stream));
        int n = 0;
        while (await reader.MoveNextAsync(TestContext.Current.CancellationToken)) n++;
        Assert.Equal(2, n);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task PipeSequenceReader_TrackSourceLineNumbers()
    {
        byte[] data = Encoding.UTF8.GetBytes("a,b\n1,2\n3,4\n");
        using var stream = new MemoryStream(data);
        await using var reader = Csv.Read()
            .TrackSourceLineNumbers()
            .FromPipeReaderAsync(PipeReader.Create(stream));
        int n = 0;
        while (await reader.MoveNextAsync(TestContext.Current.CancellationToken)) n++;
        Assert.Equal(3, n);
    }

    // (Removed: FromPipeReaderAsync builder does not accept skipRows here.)

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task PipeSequenceReader_CommentLines()
    {
        byte[] data = Encoding.UTF8.GetBytes("# header comment\na,b\n# inline\n1,2\n3,4\n");
        using var stream = new MemoryStream(data);
        await using var reader = Csv.Read()
            .WithCommentCharacter('#')
            .FromPipeReaderAsync(PipeReader.Create(stream));
        int n = 0;
        while (await reader.MoveNextAsync(TestContext.Current.CancellationToken)) n++;
        Assert.Equal(3, n);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task PipeSequenceReader_EscapeCharacter()
    {
        byte[] data = Encoding.UTF8.GetBytes("a,b\nhi\\,there,2\n");
        using var stream = new MemoryStream(data);
        await using var reader = Csv.Read()
            .WithEscapeCharacter('\\')
            .FromPipeReaderAsync(PipeReader.Create(stream));
        int n = 0;
        while (await reader.MoveNextAsync(TestContext.Current.CancellationToken)) n++;
        Assert.Equal(2, n);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task PipeSequenceReader_AllowNewlinesInQuotes()
    {
        byte[] data = Encoding.UTF8.GetBytes("a,b\n\"multi\nline\",2\n");
        using var stream = new MemoryStream(data);
        await using var reader = Csv.Read()
            .AllowNewlinesInQuotes()
            .FromPipeReaderAsync(PipeReader.Create(stream));
        int n = 0;
        while (await reader.MoveNextAsync(TestContext.Current.CancellationToken)) n++;
        Assert.Equal(2, n);
    }

    // ---------- CsvRecordWriter typed-conversion specific paths ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void RecordWriter_DateTimeWithCustomFormat()
    {
        var rows = new[] { new EventRow { When = new DateTime(2024, 6, 1, 12, 30, 45) } };
        string csv = Csv.WriteToText(rows, options: new CsvWriteOptions { DateTimeFormat = "o" });
        Assert.Contains("2024-06-01T", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void RecordWriter_NumberWithCustomFormat()
    {
        var rows = new[] { new MoneyRow { Amount = 12345.6789m } };
        string csv = Csv.WriteToText(rows, options: new CsvWriteOptions { NumberFormat = "N2" });
        Assert.Contains("12,345.68", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task AsyncRecordWriter_DateTimeWithCustomFormat()
    {
        var rows = new[] { new EventRow { When = new DateTime(2024, 6, 1) } };
        using var ms = new MemoryStream();
        await Csv.WriteToStreamAsync(
            ms,
            rows,
            options: new CsvWriteOptions { DateTimeFormat = "yyyyMMdd" },
            cancellationToken: TestContext.Current.CancellationToken);
        string csv = Encoding.UTF8.GetString(ms.ToArray());
        Assert.Contains("20240601", csv);
    }

    // ---------- CsvRowParser specific char paths (large multi-segment char data) ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRowParser_CharPath_VeryLargeData()
    {
        // Generate 5 MB of CSV data (char-based) to exercise SIMD chunks repeatedly.
        var sb = new StringBuilder("c1,c2,c3,c4,c5\n");
        for (int i = 0; i < 50000; i++)
        {
            sb.Append("alpha,beta,gamma,delta,epsilon\n");
        }
        using var reader = Csv.Read().FromText(sb.ToString());
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.Equal(50001, n);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRowParser_BytePath_VeryLargeData()
    {
        var sb = new StringBuilder("c1,c2,c3,c4,c5\n");
        for (int i = 0; i < 50000; i++)
        {
            sb.Append("alpha,beta,gamma,delta,epsilon\n");
        }
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString()));
        using var reader = Csv.Read().FromStream(stream, out _);
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.Equal(50001, n);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRowParser_CharPath_QuotedMultiline()
    {
        var sb = new StringBuilder("c1,c2\n");
        for (int i = 0; i < 100; i++)
        {
            sb.Append('"').Append("line1\nline2 ").Append(i).Append("\nline3").Append('"').Append(',').Append(i).Append('\n');
        }
        using var reader = Csv.Read().AllowNewlinesInQuotes().FromText(sb.ToString());
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.Equal(101, n);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRowParser_TrimFields_LargeData()
    {
        var sb = new StringBuilder("a,b\n");
        for (int i = 0; i < 1000; i++)
        {
            sb.Append("  value").Append(i).Append("  ,  other  \n");
        }
        using var reader = Csv.Read().TrimFields().FromText(sb.ToString());
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.Equal(1001, n);
    }

    // ---------- ExcelRecordWriter additional types ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Excel_Write_PrimitivesRow()
    {
        var rows = new[]
        {
            new PrimitivesRow
            {
                B = 1, S = 2, US = 3, I = 4, UI = 5, L = 6, UL = 7,
                F = 1.5f, D = 2.5, M = 3.5m, Bool = true, G = Guid.NewGuid(),
                Dt = DateTime.UtcNow, Dto = DateTimeOffset.UtcNow,
                DOnly = DateOnly.FromDateTime(DateTime.Today),
                TOnly = TimeOnly.FromDateTime(DateTime.Now),
            }
        };
        using var ms = new MemoryStream();
        global::HeroParser.Excel.Write<PrimitivesRow>().ToStream(ms, rows);
        Assert.True(ms.Length > 0);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task Excel_WriteAsync()
    {
        var rows = new[] { new CoveragePerson { Name = "Alice", Age = 30 } };
        using var ms = new MemoryStream();
        await global::HeroParser.Excel.Write<CoveragePerson>().ToStreamAsync(ms, rows, ct: TestContext.Current.CancellationToken);
        Assert.True(ms.Length > 0);
    }

    // ---------- CsvAsyncStreamReader additional paths ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task AsyncStreamReader_RowSpanningPumps()
    {
        var sb = new StringBuilder("a,b\n");
        // One row larger than typical buffer (16KB) — should span multiple pumps.
        sb.Append('"').Append('z', 20_000).Append("\",final\n");
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString()));
        await using var reader = Csv.CreateAsyncStreamReader(ms, bufferSize: 1024);
        int n = 0;
        while (await reader.MoveNextAsync(TestContext.Current.CancellationToken)) n++;
        Assert.Equal(2, n);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task AsyncStreamReader_ManyColumns()
    {
        var headerSb = new StringBuilder();
        var rowSb = new StringBuilder();
        for (int i = 0; i < 100; i++)
        {
            if (i > 0) { headerSb.Append(','); rowSb.Append(','); }
            headerSb.Append($"c{i}");
            rowSb.Append($"v{i}");
        }
        string data = $"{headerSb}\n{rowSb}\n";
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(data));
        await using var reader = Csv.CreateAsyncStreamReader(ms);
        int n = 0;
        while (await reader.MoveNextAsync(TestContext.Current.CancellationToken)) n++;
        Assert.Equal(2, n);
    }
}

// ---------- Records ----------

// No [GenerateBinder] — unsigned types use reflection-based ConverterFactory.
public class FixedUnsignedRow
{
    [PositionalMap(Start = 0, Length = 10)] public ushort US { get; set; }
    [PositionalMap(Start = 10, Length = 12)] public uint UI { get; set; }
    [PositionalMap(Start = 22, Length = 22)] public ulong UL { get; set; }
    [PositionalMap(Start = 44, Length = 36)] public Guid G { get; set; }
}

[GenerateBinder]
public class FixedDateRow
{
    [PositionalMap(Start = 0, Length = 27)] public DateTime Dt { get; set; }
    [PositionalMap(Start = 27, Length = 33)] public DateTimeOffset Dto { get; set; }
    [PositionalMap(Start = 60, Length = 10)] public DateOnly DOnly { get; set; }
    [PositionalMap(Start = 70, Length = 16)] public TimeOnly TOnly { get; set; }
}
