using System.IO.Pipelines;
using System.Text;
using HeroParser.FixedWidths;
using HeroParser.FixedWidths.Mapping;
using HeroParser.FixedWidths.Records.Binding;
using Xunit;

namespace HeroParser.Tests.FixedWidths;

public class FixedWidthPipeReaderTests
{
    [FixedWidthGenerateBinder]
    private sealed class PipeBoundRecord
    {
        [FixedWidthColumn(Start = 0, Length = 4, Alignment = FieldAlignment.Right, PadChar = '0')]
        public int Id { get; set; }

        [FixedWidthColumn(Start = 4, Length = 6)]
        public string Name { get; set; } = string.Empty;
    }

    private sealed class PipeMappedRecord
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    private sealed class PipeMappedRecordMap : FixedWidthMap<PipeMappedRecord>
    {
        public PipeMappedRecordMap()
        {
            Map(x => x.Id, c => c.Start(0).Length(4).PadChar('0').Alignment(FieldAlignment.Right))
                .Map(x => x.Name, c => c.Start(4).Length(6));
        }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task ReadFromPipeReader_LineBased_ParsesCorrectly()
    {
        var pipe = CreatePipeFromString("0001Alice \r\n0002Bob   \r\n");

        var rows = new List<(string Id, string Name)>();
        await foreach (var row in FixedWidth.ReadFromPipeReaderAsync(pipe.Reader, cancellationToken: TestContext.Current.CancellationToken))
        {
            rows.Add((
                row.GetField(0, 4).ToString(),
                row.GetField(4, 6).ToString()));
        }

        Assert.Equal(2, rows.Count);
        Assert.Equal(("0001", "Alice"), rows[0]);
        Assert.Equal(("0002", "Bob"), rows[1]);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task ReadFromPipeReader_RecordLengthMode_ParsesCorrectly()
    {
        var pipe = CreatePipeFromString("0001Alice 0002Bob   ");
        var options = new FixedWidthReadOptions { RecordLength = 10 };

        var rows = new List<string>();
        await foreach (var row in FixedWidth.ReadFromPipeReaderAsync(pipe.Reader, options, TestContext.Current.CancellationToken))
        {
            rows.Add(row.GetField(4, 6).ToString());
        }

        Assert.Equal(["Alice", "Bob"], rows);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task ReadFromPipeReader_SkipRowsHeaderCommentsAndEmptyLines_UsesOptions()
    {
        var data = "META000001\r\nHEADER0001\r\n\r\n#comment\r\n0001Alice \r\n0002Bob   \r\n";
        var pipe = CreatePipeFromString(data);
        var options = new FixedWidthReadOptions
        {
            SkipRows = 1,
            HasHeaderRow = true,
            SkipEmptyLines = true,
            CommentCharacter = '#'
        };

        var rows = new List<string>();
        await foreach (var row in FixedWidth.ReadFromPipeReaderAsync(pipe.Reader, options, TestContext.Current.CancellationToken))
        {
            rows.Add(row.GetField(4, 6).ToString());
        }

        Assert.Equal(["Alice", "Bob"], rows);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task ReadFromPipeReader_TrackSourceLineNumbers_TracksCorrectly()
    {
        var data = "SKIPROW000\r\n0001Alice \r\n#comment\r\n0002Bob   \r\n";
        var pipe = CreatePipeFromString(data);
        var options = new FixedWidthReadOptions
        {
            SkipRows = 1,
            CommentCharacter = '#',
            TrackSourceLineNumbers = true
        };

        var lines = new List<int>();
        await foreach (var row in FixedWidth.ReadFromPipeReaderAsync(pipe.Reader, options, TestContext.Current.CancellationToken))
        {
            lines.Add(row.SourceLineNumber);
        }

        Assert.Equal([2, 4], lines);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task ReadFromPipeReader_StripsUtf8Bom()
    {
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("0001Alice \r\n")).ToArray();
        var pipe = CreatePipeFromBytes(bytes);

        var rows = new List<string>();
        await foreach (var row in FixedWidth.ReadFromPipeReaderAsync(pipe.Reader, cancellationToken: TestContext.Current.CancellationToken))
        {
            rows.Add(row.GetField(0, 4).ToString());
        }

        Assert.Equal(["0001"], rows);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task ReadFromPipeReader_MaxRecordCount_ThrowsWhenExceeded()
    {
        var pipe = CreatePipeFromString("0001Alice \r\n0002Bob   \r\n");
        var options = new FixedWidthReadOptions { MaxRecordCount = 1 };

        var count = 0;
        var ex = await Assert.ThrowsAsync<FixedWidthException>(async () =>
        {
            await foreach (var _ in FixedWidth.ReadFromPipeReaderAsync(pipe.Reader, options, TestContext.Current.CancellationToken))
            {
                count++;
            }
        });

        Assert.Equal(1, count);
        Assert.Equal(FixedWidthErrorCode.TooManyRecords, ex.ErrorCode);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task ReadFromPipeReader_MaxInputSize_ThrowsWhenExceeded()
    {
        var pipe = CreatePipeFromString("TOO-LARGE");
        var options = new FixedWidthReadOptions { MaxInputSize = 4 };

        var ex = await Assert.ThrowsAsync<FixedWidthException>(async () =>
        {
            await foreach (var _ in FixedWidth.ReadFromPipeReaderAsync(pipe.Reader, options, TestContext.Current.CancellationToken))
            {
            }
        });

        Assert.Equal(FixedWidthErrorCode.InvalidOptions, ex.ErrorCode);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task ReadFromPipeReader_PartialFixedLengthRecord_Throws()
    {
        var pipe = CreatePipeFromString("0001Alice");
        var options = new FixedWidthReadOptions { RecordLength = 10 };

        var ex = await Assert.ThrowsAsync<FixedWidthException>(async () =>
        {
            await foreach (var _ in FixedWidth.ReadFromPipeReaderAsync(pipe.Reader, options, TestContext.Current.CancellationToken))
            {
            }
        });

        Assert.Equal(FixedWidthErrorCode.InvalidRecordLength, ex.ErrorCode);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task ReadFromPipeReader_AllowShortRows_ReturnsEmptyFieldBeyondEnd()
    {
        var pipe = CreatePipeFromString("ABCD\r\n");
        var options = new FixedWidthReadOptions { AllowShortRows = true };

        await foreach (var row in FixedWidth.ReadFromPipeReaderAsync(pipe.Reader, options, TestContext.Current.CancellationToken))
        {
            Assert.True(row.GetField(10, 5).IsEmpty);
        }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task ReadFromPipeReader_SlowProducer_HandlesChunkedInput()
    {
        var pipe = new Pipe();
        var bytes = Encoding.UTF8.GetBytes("0001Alice \r\n0002Bob   \r\n");
        var ct = TestContext.Current.CancellationToken;

        _ = Task.Run(async () =>
        {
            for (int i = 0; i < bytes.Length; i += 3)
            {
                var chunk = bytes.AsMemory(i, Math.Min(3, bytes.Length - i));
                await pipe.Writer.WriteAsync(chunk, ct);
                await Task.Delay(1, ct);
            }

            await pipe.Writer.CompleteAsync();
        }, ct);

        var ids = new List<string>();
        await foreach (var row in FixedWidth.ReadFromPipeReaderAsync(pipe.Reader, cancellationToken: TestContext.Current.CancellationToken))
        {
            ids.Add(row.GetField(0, 4).ToString());
        }

        Assert.Equal(["0001", "0002"], ids);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task DeserializeRecordsAsync_FromPipeReader_BindsGeneratedRecords()
    {
        var pipe = CreatePipeFromString("0001Alice \r\n0002Bob   \r\n");

        var records = new List<PipeBoundRecord>();
        await foreach (var record in FixedWidth.DeserializeRecordsAsync<PipeBoundRecord>(
            pipe.Reader,
            cancellationToken: TestContext.Current.CancellationToken))
        {
            records.Add(record);
        }

        Assert.Equal(2, records.Count);
        Assert.Equal(1, records[0].Id);
        Assert.Equal("Alice", records[0].Name);
        Assert.Equal(2, records[1].Id);
        Assert.Equal("Bob", records[1].Name);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task Builder_FromPipeReaderAsync_BindsGeneratedRecords()
    {
        var pipe = CreatePipeFromString("0001Alice \r\n0002Bob   \r\n");

        var records = new List<PipeBoundRecord>();
        await foreach (var record in FixedWidth.Read<PipeBoundRecord>()
            .FromPipeReaderAsync(pipe.Reader, TestContext.Current.CancellationToken))
        {
            records.Add(record);
        }

        Assert.Equal(2, records.Count);
        Assert.Equal("Alice", records[0].Name);
        Assert.Equal("Bob", records[1].Name);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task Builder_FromPipeReaderAsync_WithMap_BindsRecords()
    {
        var pipe = CreatePipeFromString("0001Alice \r\n0002Bob   \r\n");
        var map = new PipeMappedRecordMap();

        var records = new List<PipeMappedRecord>();
        await foreach (var record in FixedWidth.Read<PipeMappedRecord>()
            .WithMap(map)
            .FromPipeReaderAsync(pipe.Reader, TestContext.Current.CancellationToken))
        {
            records.Add(record);
        }

        Assert.Equal(2, records.Count);
        Assert.Equal(1, records[0].Id);
        Assert.Equal("Alice", records[0].Name);
        Assert.Equal(2, records[1].Id);
        Assert.Equal("Bob", records[1].Name);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task ReadFromPipeReader_NullReader_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            FixedWidth.ReadFromPipeReaderAsync(null!, cancellationToken: TestContext.Current.CancellationToken)
                .GetAsyncEnumerator(TestContext.Current.CancellationToken)
                .MoveNextAsync()
                .AsTask());
    }

    private static Pipe CreatePipeFromString(string data)
        => CreatePipeFromBytes(Encoding.UTF8.GetBytes(data));

    private static Pipe CreatePipeFromBytes(byte[] data)
    {
        var pipe = new Pipe();
        pipe.Writer.WriteAsync(data).GetAwaiter().GetResult();
        pipe.Writer.Complete();
        return pipe;
    }
}
