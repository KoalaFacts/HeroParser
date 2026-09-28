using System.Text;
using HeroParser.SeparatedValues.Detection;
using Xunit;

namespace HeroParser.Tests;

[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
public sealed class SchemaInferenceStreamTests
{
    private sealed class ChunkedNonSeekableStream(byte[] data, int chunkSize) : Stream
    {
        private int Offset { get; set; }

        public int BytesRead => Offset;
        public bool IsDisposed { get; private set; }
        public override bool CanRead => !IsDisposed;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int bufferOffset, int count) =>
            Read(buffer.AsSpan(bufferOffset, count));

        public override int Read(Span<byte> buffer)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            int count = Math.Min(Math.Min(buffer.Length, chunkSize), data.Length - Offset);
            data.AsSpan(Offset, count).CopyTo(buffer);
            Offset += count;
            return count;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Read(buffer.Span));
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int bufferOffset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }

    [Fact]
    public async Task SeekableStream_AutoDetectsAndRestoresOriginalPosition()
    {
        const string csv = "Name;Age\nAda;30\nBen;40";
        byte[] data = Encoding.UTF8.GetBytes("xx" + csv);
        using var stream = new MemoryStream(data, writable: false);
        stream.Position = 2;

        var result = await Csv.InferSchemaAsync(stream, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(Csv.InferSchema(csv).Columns, result.Columns);
        Assert.Equal(2, result.SampledRowCount);
        Assert.Equal(2, stream.Position);
        Assert.True(stream.CanRead);
    }

    [Fact]
    public async Task NonSeekableStream_SamplesOnlyRequestedRowsAndStaysOpen()
    {
        string csv = "Id,Active\n1,true\n2,false\n3,unexpected\n" + new string('x', 600_000);
        using var stream = new ChunkedNonSeekableStream(Encoding.UTF8.GetBytes(csv), chunkSize: 7);

        var result = await Csv.InferSchemaAsync(stream, new CsvSchemaInferenceOptions
        {
            Delimiter = ',',
            SampleRows = 2
        }, TestContext.Current.CancellationToken);

        Assert.Equal(2, result.SampledRowCount);
        Assert.Equal(CsvInferredType.Integer, result.Columns[0].InferredType);
        Assert.Equal(CsvInferredType.Boolean, result.Columns[1].InferredType);
        Assert.True(stream.BytesRead < 600_000);
        Assert.False(stream.IsDisposed);
    }

    [Fact]
    public async Task NonSeekableStream_RequiresExplicitDelimiterWithoutConsumingInput()
    {
        using var stream = new ChunkedNonSeekableStream(Encoding.UTF8.GetBytes("A;B\n1;2\n"), chunkSize: 3);

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await Csv.InferSchemaAsync(stream, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(0, stream.BytesRead);
        Assert.False(stream.IsDisposed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SeekableUtf16Bom_AutoDetectsAndRestoresPosition(bool bigEndian)
    {
        Encoding encoding = bigEndian ? Encoding.BigEndianUnicode : Encoding.Unicode;
        byte[] data = [.. encoding.GetPreamble(), .. encoding.GetBytes("Name;Count\nCat;2\nDog;3")];
        using var stream = new MemoryStream(data, writable: false);

        var result = await Csv.InferSchemaAsync(stream, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, result.SampledRowCount);
        Assert.Equal("Name", result.Columns[0].Name);
        Assert.Equal(CsvInferredType.Integer, result.Columns[1].InferredType);
        Assert.Equal(0, stream.Position);
        Assert.True(stream.CanRead);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NonSeekableUtf16Bom_IsTranscodedBeforeInference(bool bigEndian)
    {
        Encoding encoding = bigEndian ? Encoding.BigEndianUnicode : Encoding.Unicode;
        byte[] data = [.. encoding.GetPreamble(), .. encoding.GetBytes("Name;Count\nCat;2\nDog;3\n")];
        using var stream = new ChunkedNonSeekableStream(data, chunkSize: 3);

        var result = await Csv.InferSchemaAsync(stream, new CsvSchemaInferenceOptions { Delimiter = ';' },
            TestContext.Current.CancellationToken);

        Assert.Equal(2, result.SampledRowCount);
        Assert.Equal("Name", result.Columns[0].Name);
        Assert.Equal(CsvInferredType.Integer, result.Columns[1].InferredType);
        Assert.False(stream.IsDisposed);
    }

    [Fact]
    public async Task NonSeekableUtf8Bom_IsHandledAcrossPrefixBoundary()
    {
        byte[] data = [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes("Name,Count\nCat,2")];
        using var stream = new ChunkedNonSeekableStream(data, chunkSize: 1);

        var result = await Csv.InferSchemaAsync(stream, new CsvSchemaInferenceOptions { Delimiter = ',' },
            TestContext.Current.CancellationToken);

        Assert.Equal("Name", result.Columns[0].Name);
        Assert.Equal(CsvInferredType.Integer, result.Columns[1].InferredType);
        Assert.False(stream.IsDisposed);
    }

    [Fact]
    public async Task CanceledInference_RestoresSeekablePositionAndLeavesStreamOpen()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("xA,B\n1,2\n"), writable: false);
        stream.Position = 1;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await Csv.InferSchemaAsync(stream, cancellationToken: cancellation.Token));

        Assert.Equal(1, stream.Position);
        Assert.True(stream.CanRead);
    }

    [Fact]
    public async Task FailedDelimiterDetection_RestoresSeekablePosition()
    {
        byte[] data = Encoding.UTF8.GetBytes("xx\"" + new string('x', 70_000) + "\";B\n1;2");
        using var stream = new MemoryStream(data, writable: false);
        stream.Position = 2;

        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await Csv.InferSchemaAsync(stream, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("specify a delimiter", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, stream.Position);
        Assert.True(stream.CanRead);
    }
}
