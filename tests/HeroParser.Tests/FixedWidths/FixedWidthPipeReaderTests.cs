using System.IO.Pipelines;
using System.Globalization;
using System.Text;
using HeroParser.FixedWidths;
using HeroParser.FixedWidths.Mapping;
using HeroParser.FixedWidths.Records.Binding;
using Xunit;

namespace HeroParser.Tests.FixedWidths;

public class FixedWidthPipeReaderTests
{
    [FixedWidthGenerateBinder]
    public sealed class PipeBoundRecord
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

    [FixedWidthGenerateBinder]
    public sealed class PipeTypedRecord
    {
        [FixedWidthColumn(Start = 0, Length = 5, Alignment = FieldAlignment.Right, PadChar = '0')]
        public int Id { get; set; }

        [FixedWidthColumn(Start = 5, Length = 8, Format = "yyyyMMdd")]
        public DateTime DateValue { get; set; }

        [FixedWidthColumn(Start = 13, Length = 1)]
        public bool Flag { get; set; }
    }

    [FixedWidthGenerateBinder]
    public sealed class PipeCultureRecord
    {
        [FixedWidthColumn(Start = 0, Length = 5)]
        public decimal Amount { get; set; }

        [FixedWidthColumn(Start = 5, Length = 5)]
        public string? Code { get; set; }
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
    public async Task DeserializeRecordsAsync_FromPipeReader_BindsGeneratedRecords_FromChunkedSegments()
    {
        var pipe = new Pipe();
        var bytes = Encoding.UTF8.GetBytes("0001Alice \r\n0002Bob   \r\n");
        var ct = TestContext.Current.CancellationToken;

        _ = Task.Run(async () =>
        {
            for (int i = 0; i < bytes.Length; i += 2)
            {
                var chunk = bytes.AsMemory(i, Math.Min(2, bytes.Length - i));
                await pipe.Writer.WriteAsync(chunk, ct);
                await Task.Delay(1, ct);
            }

            await pipe.Writer.CompleteAsync();
        }, ct);

        var records = new List<PipeBoundRecord>();
        await foreach (var record in FixedWidth.DeserializeRecordsAsync<PipeBoundRecord>(
            pipe.Reader,
            cancellationToken: ct))
        {
            records.Add(record);
        }

        Assert.Collection(
            records,
            record =>
            {
                Assert.Equal(1, record.Id);
                Assert.Equal("Alice", record.Name);
            },
            record =>
            {
                Assert.Equal(2, record.Id);
                Assert.Equal("Bob", record.Name);
            });
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void GeneratedByteBinder_BindsUtf8RowDirectly()
    {
        Assert.True(FixedWidthRecordBinderFactory.TryCreateGeneratedByteBinder<PipeTypedRecord>(
            CultureInfo.InvariantCulture,
            null,
            out var binder));

        var row = new FixedWidthByteSpanRow(
            Encoding.UTF8.GetBytes("0012320231225Y"),
            recordNumber: 1,
            sourceLineNumber: 1,
            new FixedWidthReadOptions());

        Assert.NotNull(binder);
        Assert.True(binder.TryBind(row, out var record));
        Assert.Equal(123, record.Id);
        Assert.Equal(new DateTime(2023, 12, 25), record.DateValue);
        Assert.True(record.Flag);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void GeneratedByteBinder_UsesCultureAndNullValues()
    {
        Assert.True(FixedWidthRecordBinderFactory.TryCreateGeneratedByteBinder<PipeCultureRecord>(
            CultureInfo.GetCultureInfo("fr-FR"),
            ["NULL"],
            out var binder));

        var row = new FixedWidthByteSpanRow(
            Encoding.UTF8.GetBytes("12,34NULL "),
            recordNumber: 1,
            sourceLineNumber: 1,
            new FixedWidthReadOptions());

        Assert.NotNull(binder);
        Assert.True(binder.TryBind(row, out var record));
        Assert.Equal(12.34m, record.Amount);
        Assert.Null(record.Code);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task DeserializeRecordsAsync_FromPipeReader_RespectsProvidedEncoding()
    {
        var encoding = Encoding.Latin1;
        var pipe = CreatePipeFromBytes(encoding.GetBytes("0001\u00C5sa   \r\n0002Bj\u00F6rn \r\n"));

        var records = new List<PipeBoundRecord>();
        await foreach (var record in FixedWidth.DeserializeRecordsAsync<PipeBoundRecord>(
            pipe.Reader,
            encoding: encoding,
            cancellationToken: TestContext.Current.CancellationToken))
        {
            records.Add(record);
        }

        Assert.Equal(new[] { "\u00C5sa", "Bj\u00F6rn" }, records.Select(r => r.Name).ToArray());
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
    public async Task Builder_FromPipeReaderAsync_WithMap_BindsChunkedRecords()
    {
        var pipe = new Pipe();
        var bytes = Encoding.UTF8.GetBytes("0001Alice \r\n0002Bob   \r\n");
        var ct = TestContext.Current.CancellationToken;
        var map = new PipeMappedRecordMap();

        _ = Task.Run(async () =>
        {
            for (int i = 0; i < bytes.Length; i += 2)
            {
                var chunk = bytes.AsMemory(i, Math.Min(2, bytes.Length - i));
                await pipe.Writer.WriteAsync(chunk, ct);
                await Task.Delay(1, ct);
            }

            await pipe.Writer.CompleteAsync();
        }, ct);

        var records = new List<PipeMappedRecord>();
        await foreach (var record in FixedWidth.Read<PipeMappedRecord>()
            .WithMap(map)
            .FromPipeReaderAsync(pipe.Reader, ct))
        {
            records.Add(record);
        }

        Assert.Collection(
            records,
            record =>
            {
                Assert.Equal(1, record.Id);
                Assert.Equal("Alice", record.Name);
            },
            record =>
            {
                Assert.Equal(2, record.Id);
                Assert.Equal("Bob", record.Name);
            });
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void DescriptorBinder_BindsUtf8RowDirectly()
    {
        var descriptor = new PipeMappedRecordMap().BuildReadDescriptor();
        var binder = new FixedWidthDescriptorBinder<PipeMappedRecord>(descriptor);
        var byteBinder = Assert.IsAssignableFrom<IFixedWidthByteBinder<PipeMappedRecord>>(binder);
        var row = new FixedWidthByteSpanRow(
            Encoding.UTF8.GetBytes("0001Alice "),
            recordNumber: 1,
            sourceLineNumber: 1,
            new FixedWidthReadOptions());

        Assert.True(byteBinder.TryBind(row, out var record));
        Assert.Equal(1, record.Id);
        Assert.Equal("Alice", record.Name);
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
