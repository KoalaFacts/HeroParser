using System.IO.Compression;
using System.Text;
using HeroParser.FixedWidths;
using HeroParser.FixedWidths.Records;
using Xunit;

namespace HeroParser.Tests.FixedWidths;

[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
public sealed class FixedWidthAsyncStreamingRecordTests
{
    [GenerateBinder]
    public sealed class StreamedRecord
    {
        [PositionalMap(Start = 0, Length = 5, Alignment = FieldAlignment.Right, PadChar = '0')]
        public int Id { get; set; }

        [PositionalMap(Start = 5, Length = 10)]
        public string Name { get; set; } = string.Empty;
    }

    public sealed class MappedRecord
    {
        public string Name { get; set; } = string.Empty;
        public int Age { get; set; }
    }

    [Fact]
    public async Task Builder_FromStreamAsync_EnforcesMaxInputSize_OnNonSeekableStream()
    {
        var ct = TestContext.Current.CancellationToken;
        string payload = "00001Alice     \n00002Bob       \n00003Charlie   \n";
        using var stream = new NonSeekableStream(Encoding.UTF8.GetBytes(payload));

        var ex = await Assert.ThrowsAsync<FixedWidthException>(async () =>
        {
            await foreach (var _ in FixedWidth.Read<StreamedRecord>()
                .WithMaxInputSize(16)
                .FromStreamAsync(stream, ct))
            {
            }
        });

        Assert.Equal(FixedWidthErrorCode.InvalidOptions, ex.ErrorCode);
    }

    [Fact]
    public async Task Builder_FromStreamAsync_And_FromFileAsync_ValidateHeaderNames_WithSkipRowsAndBlankLines()
    {
        var ct = TestContext.Current.CancellationToken;
        const string data =
            "PREAMBLE\n" +
            "\n" +
            "Name      Age\n" +
            "Alice     030\n" +
            "Bob       025\n";

        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(data), writable: false);
        var rows = new List<MappedRecord>();
        await foreach (var item in FixedWidth.Read<MappedRecord>()
            .SkipRows(1)
            .WithHeader()
            .CaseSensitiveHeaders()
            .Map(x => x.Name, f => f.Start(0).Length(10).WithHeaderName("Name"))
            .Map(x => x.Age, f => f.Start(10).Length(3).WithHeaderName("Age"))
            .FromStreamAsync(ms, ct))
        {
            rows.Add(item);
        }

        Assert.Equal(2, rows.Count);
        Assert.Equal("Alice", rows[0].Name);
        Assert.Equal(30, rows[0].Age);
        Assert.Equal("Bob", rows[1].Name);
        Assert.Equal(25, rows[1].Age);
        Assert.True(ms.CanRead);

        string tempFile = Path.Combine(Path.GetTempPath(), $"fw-header-{Guid.NewGuid():N}.dat");
        try
        {
            await File.WriteAllTextAsync(tempFile, data, Encoding.UTF8, ct);
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await foreach (var _ in FixedWidth.Read<MappedRecord>()
                    .SkipRows(1)
                    .WithHeader()
                    .CaseSensitiveHeaders()
                    .Map(x => x.Name, f => f.Start(0).Length(10).WithHeaderName("name"))
                    .Map(x => x.Age, f => f.Start(10).Length(3).WithHeaderName("Age"))
                    .FromFileAsync(tempFile, ct))
                {
                }
            });

            Assert.Contains("mismatch", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task Static_DeserializeRecordsAsync_SupportsGzipFile_OnError_And_Utf16Bom()
    {
        var ct = TestContext.Current.CancellationToken;
        string tempGz = Path.Combine(Path.GetTempPath(), $"fw-stream-{Guid.NewGuid():N}.dat.gz");
        try
        {
            const string gzContent = "00001Alice     \nBADIDBob       \n00003Charlie   \n";
            await using (var fs = File.Create(tempGz))
            await using (var gz = new GZipStream(fs, CompressionLevel.Fastest))
            {
                byte[] bytes = Encoding.UTF8.GetBytes(gzContent);
                await gz.WriteAsync(bytes, ct);
            }

            int skippedErrors = 0;
            var results = new List<StreamedRecord>();
            await foreach (var r in FixedWidth.DeserializeRecordsAsync<StreamedRecord>(
                tempGz,
                onError: (_, _) =>
                {
                    skippedErrors++;
                    return FixedWidthDeserializeErrorAction.SkipRecord;
                },
                cancellationToken: ct))
            {
                results.Add(r);
            }

            Assert.Equal(1, skippedErrors);
            Assert.Equal(2, results.Count);
            Assert.Equal(1, results[0].Id);
            Assert.Equal("Alice", results[0].Name);
            Assert.Equal(3, results[1].Id);
            Assert.Equal("Charlie", results[1].Name);
        }
        finally
        {
            if (File.Exists(tempGz))
                File.Delete(tempGz);
        }

        byte[] utf16Bytes = [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes("00007Zoe       \n")];
        using var utf16Stream = new MemoryStream(utf16Bytes, writable: false);
        var utf16Rows = new List<StreamedRecord>();
        await foreach (var r in FixedWidth.DeserializeRecordsAsync<StreamedRecord>(
            utf16Stream,
            encoding: Encoding.Unicode,
            leaveOpen: true,
            cancellationToken: ct))
        {
            utf16Rows.Add(r);
        }

        var single = Assert.Single(utf16Rows);
        Assert.Equal(7, single.Id);
        Assert.Equal("Zoe", single.Name);
        Assert.True(utf16Stream.CanRead);
    }

    private sealed class NonSeekableStream(byte[] data) : Stream
    {
        private int position;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count)
        {
            int toCopy = Math.Min(count, data.Length - position);
            if (toCopy <= 0)
                return 0;

            Buffer.BlockCopy(data, position, buffer, offset, toCopy);
            position += toCopy;
            return toCopy;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int toCopy = Math.Min(buffer.Length, data.Length - position);
            if (toCopy <= 0)
                return ValueTask.FromResult(0);

            data.AsMemory(position, toCopy).CopyTo(buffer);
            position += toCopy;
            return ValueTask.FromResult(toCopy);
        }
    }
}
