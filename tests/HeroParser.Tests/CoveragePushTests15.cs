using System.IO.Pipelines;
using System.Text;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Writing;
using HeroParser.Validation;
using Xunit;

namespace HeroParser.Tests;

/// <summary>Wave 15: actually-reachable SIMD bounds-check, buffer overflow, async-path empty fields.</summary>
public class CoveragePushTests15
{
    // ---------- CsvRowParser: bounds-checked AppendColumnUnchecked path ----------
    //
    // Triggered when columnCount + delimCount in a SIMD chunk exceeds columnCapacity
    // (= MaxColumnCount). The chunk then uses the slow bounds-checked path, after
    // which ThrowTooManyColumns fires. Both branches end in the throw, but the else
    // branch IS exercised.

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRowParser_TooManyColumns_BytePath_ChunkOverflow()
    {
        // Default MaxColumnCount = 100. Build a row with 200 columns separated by commas.
        var sb = new StringBuilder();
        for (int i = 0; i < 200; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append('a');
        }
        sb.Append('\n');

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString()));
        Assert.Throws<CsvException>(() =>
        {
            using var reader = Csv.Read().FromStream(stream, out _);
            while (reader.MoveNext()) { }
        });
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRowParser_TooManyColumns_CharPath_ChunkOverflow()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < 200; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append('a');
        }
        sb.Append('\n');

        Assert.Throws<CsvException>(() =>
        {
            using var reader = Csv.Read().FromText(sb.ToString());
            while (reader.MoveNext()) { }
        });
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRowParser_BytePath_RowWithMaxColumns_BoundaryCase()
    {
        // Exactly at MaxColumnCount = 100 should succeed.
        var sb = new StringBuilder();
        for (int i = 0; i < 100; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append('a');
        }
        sb.Append('\n');

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString()));
        using var reader = Csv.Read().FromStream(stream, out _);
        Assert.True(reader.MoveNext());
        Assert.Equal(100, reader.Current.ColumnCount);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRowParser_BytePath_RowWithCustomLargeMaxColumns()
    {
        // Higher MaxColumnCount + row near the limit: exercises non-throw paths
        // with a wide row that still spans multiple SIMD chunks.
        var sb = new StringBuilder();
        for (int i = 0; i < 500; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append('a');
        }
        sb.Append('\n');

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString()));
        using var reader = Csv.Read()
            .WithMaxColumns(1000)
            .FromStream(stream, out _);
        Assert.True(reader.MoveNext());
        Assert.Equal(500, reader.Current.ColumnCount);
    }

    // ---------- CsvRowParser: maxFieldLength enforcement (per-column post-chunk check) ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRowParser_BytePath_MaxFieldLength_Enforced()
    {
        string csv = "longvalue,short\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        Assert.Throws<CsvException>(() =>
        {
            using var reader = Csv.Read().WithMaxFieldSize(3).FromStream(stream, out _);
            while (reader.MoveNext()) { }
        });
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRowParser_CharPath_MaxFieldLength_Enforced()
    {
        string csv = "longvalue,short\n";
        Assert.Throws<CsvException>(() =>
        {
            using var reader = Csv.Read().WithMaxFieldSize(3).FromText(csv);
            while (reader.MoveNext()) { }
        });
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRowParser_MaxRowCount_Throws()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < 50; i++) sb.Append("a,b\n");
        Assert.Throws<CsvException>(() =>
        {
            using var reader = Csv.Read().WithMaxRows(10).FromText(sb.ToString());
            while (reader.MoveNext()) { }
        });
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRowParser_MaxRowSize_Throws()
    {
        // Single long row.
        var sb = new StringBuilder();
        for (int i = 0; i < 200; i++) sb.Append("data,");
        sb.Append("end\n");
        Assert.Throws<CsvException>(() =>
        {
            using var reader = Csv.Read().WithMaxRowSize(50).FromText(sb.ToString());
            while (reader.MoveNext()) { }
        });
    }

    // ---------- CsvAsyncStreamWriter: actually force the async-path empty + Always quote ----------
    //
    // The async path is taken when sync fast-path returns false. Trigger via small buffer
    // size (forces sync to overflow quickly), OR with injection-protection fallback.

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task AsyncWriter_AsyncPath_EmptyField_AlwaysQuote()
    {
        // Mix of large + empty fields under Always quote.
        // First write a row big enough to fill the sync buffer, then write a row with
        // an empty field. The second row's empty field hits the async path.
        var rows = new List<NullableAgePerson>();
        for (int i = 0; i < 5; i++)
        {
            rows.Add(new NullableAgePerson
            {
                Name = new string('x', 8000),  // ~16KB total per row drives sync overflow
                Age = i,
            });
            rows.Add(new NullableAgePerson { Name = "", Age = null }); // empty-field row
        }

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
    public async Task AsyncWriter_AsyncPath_DangerousField_ForcesAsync()
    {
        // InjectionProtection on a non-trivial size forces fallback to async path.
        var rows = new List<CoveragePerson>();
        for (int i = 0; i < 100; i++)
        {
            rows.Add(new CoveragePerson { Name = "=DANGEROUS_" + i + "_" + new string('x', 200), Age = i });
        }
        using var ms = new MemoryStream();
        await Csv.WriteToStreamAsync(
            ms,
            rows,
            options: new CsvWriteOptions
            {
                InjectionProtection = CsvInjectionProtection.EscapeWithQuote,
                QuoteStyle = QuoteStyle.Always
            },
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(ms.Length > 1000);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task AsyncWriter_SingleFieldLargerThanBuffer_GrowsBuffer()
    {
        // A single field > default 16K buffer forces GrowCharBuffer.
        var rows = new[] { new CoveragePerson { Name = new string('y', 60_000), Age = 1 } };
        using var ms = new MemoryStream();
        await Csv.WriteToStreamAsync(ms, rows, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(ms.Length > 50_000);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task AsyncWriter_AlwaysQuote_HugeFieldRequiresFlushThenGrow()
    {
        // Two rows: first fills the buffer (forces flush), second has a huge field (forces grow).
        var rows = new[]
        {
            new CoveragePerson { Name = new string('a', 12_000), Age = 1 },
            new CoveragePerson { Name = new string('b', 60_000), Age = 2 },
        };
        using var ms = new MemoryStream();
        await Csv.WriteToStreamAsync(
            ms,
            rows,
            options: new CsvWriteOptions { QuoteStyle = QuoteStyle.Always },
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(ms.Length > 60_000);
    }

    // ---------- AsyncWriter: WriteFieldValueFromBufferAsync path (primitives + Always quote) ----------
    //
    // Line 1346-1353 path: primitive value already formatted in buffer + QuoteStyle.Always
    // requires copying out and re-writing as quoted.

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task AsyncWriter_AlwaysQuote_PrimitiveValues_ForcesAsyncReformat()
    {
        // Many records with primitive values + Always quote + enough volume to overflow sync.
        var rows = new List<AllTypes>();
        for (int i = 0; i < 200; i++)
        {
            rows.Add(new AllTypes
            {
                S = "value" + i,
                I = i,
                L = i * 100L,
                D = i + 0.5,
                B = i % 2 == 0,
                Dt = DateTime.UtcNow,
                G = Guid.NewGuid(),
                F = i + 0.25f,
                M = i + 0.1m,
            });
        }
        using var ms = new MemoryStream();
        await Csv.WriteToStreamAsync(
            ms,
            rows,
            options: new CsvWriteOptions { QuoteStyle = QuoteStyle.Always },
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(ms.Length > 10_000);
    }

    // ---------- CsvAsyncStreamWriter: explicit buffer overflow with non-empty value ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task AsyncWriter_QuoteStyleNever_LargeVolumeBoundary()
    {
        // QuoteStyle.Never + many rows → exercises 936-937 unquoted async branch.
        var rows = new List<CoveragePerson>();
        for (int i = 0; i < 500; i++)
        {
            rows.Add(new CoveragePerson { Name = "value_" + i + "_" + new string('z', 50), Age = i });
        }
        using var ms = new MemoryStream();
        await Csv.WriteToStreamAsync(
            ms,
            rows,
            options: new CsvWriteOptions { QuoteStyle = QuoteStyle.Never },
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(ms.Length > 10_000);
    }

    // ---------- CsvRowParser: long quoted multi-line fields spanning chunks ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRowParser_BytePath_QuotedMultiLineSpanningChunks()
    {
        // Large multi-line quoted field spanning multiple SIMD chunks with embedded
        // quote-escapes — exercises the in-quotes SIMD path.
        var sb = new StringBuilder("a,b\n\"");
        for (int i = 0; i < 1000; i++)
        {
            sb.Append("line ").Append(i).Append(" with \"\"escaped\"\" quote\n");
        }
        sb.Append("\",end\n");
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString()));
        using var reader = Csv.Read().AllowNewlinesInQuotes().FromStream(stream, out _);
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.Equal(2, n);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRowParser_CharPath_QuotedMultiLineSpanningChunks()
    {
        var sb = new StringBuilder("a,b\n\"");
        for (int i = 0; i < 1000; i++)
        {
            sb.Append("line ").Append(i).Append(" with \"\"escaped\"\" quote\n");
        }
        sb.Append("\",end\n");
        using var reader = Csv.Read().AllowNewlinesInQuotes().FromText(sb.ToString());
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.Equal(2, n);
    }

    // ---------- CsvRowParser: leading/trailing whitespace + trim ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRowParser_BytePath_TrimFields_LargeData()
    {
        var sb = new StringBuilder("a,b\n");
        for (int i = 0; i < 1000; i++)
        {
            sb.Append("  field").Append(i).Append("  ,  more  \n");
        }
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString()));
        using var reader = Csv.Read().TrimFields().FromStream(stream, out _);
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.Equal(1001, n);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRowParser_CharPath_QuotedWithLeadingWhitespace()
    {
        string csv = "a,b\n  \"value\"  ,  \"other\"  \n";
        using var reader = Csv.Read().FromText(csv);
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.Equal(2, n);
    }

    // ---------- CsvRowParser: comment with quoted content following ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRowParser_BytePath_CommentThenQuoted()
    {
        string csv = "# comment line\n\"quoted value\",2\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        using var reader = Csv.Read().WithCommentCharacter('#').FromStream(stream, out _);
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.Equal(1, n);
    }

    // ---------- Pipe Reader: tiny pipe segments forcing buffer splits ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task PipeSequenceReader_TinyPipeSegments()
    {
        // Custom Pipe with small segment size — forces multi-segment row reads.
        var pipe = new Pipe(new PipeOptions(
            minimumSegmentSize: 16,
            pauseWriterThreshold: 64,
            resumeWriterThreshold: 32));

        var ct = TestContext.Current.CancellationToken;
        var writeTask = Task.Run(async () =>
        {
            string data = "a,b,c\n1,2,3\n4,5,6\n7,8,9\n10,11,12\n";
            byte[] bytes = Encoding.UTF8.GetBytes(data);
            for (int i = 0; i < bytes.Length; i += 4)
            {
                int len = Math.Min(4, bytes.Length - i);
                await pipe.Writer.WriteAsync(bytes.AsMemory(i, len), ct);
                await Task.Yield();
            }
            await pipe.Writer.CompleteAsync();
        }, ct);

        int n = 0;
        await foreach (var _ in Csv.ReadFromPipeReaderAsync(
            pipe.Reader,
            cancellationToken: ct))
        {
            n++;
        }
        await writeTask;
        Assert.Equal(5, n);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task PipeSequenceReader_TinyPipeSegments_QuotedMultiline()
    {
        var pipe = new Pipe(new PipeOptions(
            minimumSegmentSize: 16,
            pauseWriterThreshold: 64,
            resumeWriterThreshold: 32));

        var ct = TestContext.Current.CancellationToken;
        var writeTask = Task.Run(async () =>
        {
            string data = "a,b\n\"hello\nworld\",1\n\"another\nrow\",2\n";
            byte[] bytes = Encoding.UTF8.GetBytes(data);
            for (int i = 0; i < bytes.Length; i += 3)
            {
                int len = Math.Min(3, bytes.Length - i);
                await pipe.Writer.WriteAsync(bytes.AsMemory(i, len), ct);
                await Task.Yield();
            }
            await pipe.Writer.CompleteAsync();
        }, ct);

        int n = 0;
        await foreach (var _ in Csv.ReadFromPipeReaderAsync(
            pipe.Reader,
            new CsvReadOptions { AllowNewlinesInsideQuotes = true },
            cancellationToken: ct))
        {
            n++;
        }
        await writeTask;
        Assert.Equal(3, n);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task PipeReader_BuilderPath_TinyPipeSegments()
    {
        var pipe = new Pipe(new PipeOptions(
            minimumSegmentSize: 16,
            pauseWriterThreshold: 64,
            resumeWriterThreshold: 32));

        var ct = TestContext.Current.CancellationToken;
        var writeTask = Task.Run(async () =>
        {
            string data = "a,b,c\n1,2,3\n4,5,6\n";
            byte[] bytes = Encoding.UTF8.GetBytes(data);
            for (int i = 0; i < bytes.Length; i += 4)
            {
                int len = Math.Min(4, bytes.Length - i);
                await pipe.Writer.WriteAsync(bytes.AsMemory(i, len), ct);
                await Task.Yield();
            }
            await pipe.Writer.CompleteAsync();
        }, ct);

        int n = 0;
        await using (var reader = Csv.Read().FromPipeReaderAsync(pipe.Reader))
        {
            while (await reader.MoveNextAsync(ct)) n++;
        }
        await writeTask;
        Assert.Equal(3, n);
    }

    // ---------- Pipe Reader: very large row that needs buffer growth ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task PipeSequenceReader_BufferGrowthForLongRow()
    {
        var pipe = new Pipe(new PipeOptions(minimumSegmentSize: 64));
        var ct = TestContext.Current.CancellationToken;
        var writeTask = Task.Run(async () =>
        {
            var sb = new StringBuilder("a,b\n");
            sb.Append('"').Append('z', 100_000).Append("\",end\n");
            byte[] bytes = Encoding.UTF8.GetBytes(sb.ToString());
            await pipe.Writer.WriteAsync(bytes, ct);
            await pipe.Writer.CompleteAsync();
        }, ct);

        int n = 0;
        await foreach (var _ in Csv.ReadFromPipeReaderAsync(
            pipe.Reader,
            cancellationToken: ct))
        {
            n++;
        }
        await writeTask;
        Assert.Equal(2, n);
    }
}
