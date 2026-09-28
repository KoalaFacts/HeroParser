using System.Text;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Detection;
using Xunit;

namespace HeroParser.Tests;

[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
public sealed class SchemaInferenceParityTests
{
    [Theory]
    [InlineData("A,B\n1,2\n", 100)]
    [InlineData("A,B", 100)]
    [InlineData("A,B\n", 100)]
    [InlineData("A,B\n\n", 100)]
    [InlineData("A,B\r\n1,2\r\n\r\n", 100)]
    [InlineData("A,B\r1,2\r3,4\r", 100)]
    [InlineData("Name,Note\n\"Ada\",\"line\nbreak\"\n\"Ben\",ok\n", 100)]
    [InlineData("A,B\n1\n2,3", 100)]
    [InlineData("\nA,B\n1,2\n", 100)]
    [InlineData("A,B\n1,2\n3,4\n5,6", 1)]
    [InlineData("Only\n42\n", 100)]
    [InlineData("   ", 100)]
    [InlineData("Name,Value\n猫,1.25\n犬,2\n", 100)]
    [InlineData("A,B\n1,2\n\n", 100)]
    [InlineData("A,B\n1,2\n\n\n", 100)]
    [InlineData("Id,Active\n1,true\n2,false\n\"unterminated", 2)]
    public async Task Utf8Input_HasSameSchemaAcrossSources(string csv, int sampleRows)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(csv);
        var options = new CsvSchemaInferenceOptions { Delimiter = ',', SampleRows = sampleRows };

        await AssertSourcesAgreeAsync(csv, bytes, options);
    }

    [Fact]
    public async Task AutoDetectedDelimiter_AgreesAcrossSeekableSources()
    {
        const string csv = "Name;Value\n猫;1\n犬;2\n";
        byte[] bytes = Encoding.UTF8.GetBytes(csv);
        var options = new CsvSchemaInferenceOptions();

        await AssertSourcesAgreeAsync(csv, bytes, options, includeNonSeekable: false);
    }

    [Fact]
    public async Task AutoDetection_DoesNotReadBeyondBoundedSample()
    {
        string csv = "Name;Value\n\"" + new string('x', 70_000) + "\";1\n";
        byte[] bytes = Encoding.UTF8.GetBytes(csv);
        string path = Path.Join(Path.GetTempPath(), Path.GetRandomFileName() + ".csv");
        try
        {
            await File.WriteAllBytesAsync(path, bytes, TestContext.Current.CancellationToken);
            Assert.Throws<InvalidOperationException>(() => Csv.InferSchema(csv));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                Csv.InferSchemaFileAsync(path, cancellationToken: TestContext.Current.CancellationToken));
            using var stream = new MemoryStream(bytes, writable: false);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                Csv.InferSchemaAsync(stream, cancellationToken: TestContext.Current.CancellationToken));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Utf8Bom_AgreesAcrossSources()
    {
        const string csv = "\uFEFFName,Age\nAda,30\n";
        byte[] bytes = Encoding.UTF8.GetBytes(csv);
        var options = new CsvSchemaInferenceOptions { Delimiter = ',' };

        await AssertSourcesAgreeAsync(csv, bytes, options);
    }

    [Fact]
    public async Task LargeSampleRow_AgreesAcrossSeekableSources()
    {
        string csv = "Value\n" + new string('x', 600_000);
        byte[] bytes = Encoding.UTF8.GetBytes(csv);
        var options = new CsvSchemaInferenceOptions { Delimiter = ',' };

        await AssertSourcesAgreeAsync(csv, bytes, options, includeNonSeekable: false);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BomMarkedUtf16_AgreesWithTextAcrossSources(bool bigEndian)
    {
        const string csv = "Name;Note\n猫;\"line\nbreak\"\n犬;ok\n";
        Encoding encoding = bigEndian ? Encoding.BigEndianUnicode : Encoding.Unicode;
        byte[] bytes = [.. encoding.GetPreamble(), .. encoding.GetBytes(csv)];
        var options = new CsvSchemaInferenceOptions { Delimiter = ';' };

        await AssertSourcesAgreeAsync(csv, bytes, options);
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(-1, 100)]
    [InlineData(100, 0)]
    [InlineData(100, -1)]
    public async Task InvalidSampleAndColumnLimits_AreRejectedByEverySource(int sampleRows, int maxColumns)
    {
        var options = new CsvSchemaInferenceOptions
        {
            Delimiter = ',',
            SampleRows = sampleRows,
            MaxColumnCount = maxColumns
        };
        byte[] bytes = "A,B\n1,2"u8.ToArray();
        string path = Path.Join(Path.GetTempPath(), Path.GetRandomFileName() + ".csv");
        try
        {
            await File.WriteAllBytesAsync(path, bytes, TestContext.Current.CancellationToken);
            Assert.Throws<ArgumentOutOfRangeException>(() => Csv.InferSchema("A,B\n1,2", options));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                Csv.InferSchemaFileAsync(path, options, TestContext.Current.CancellationToken));
            using var stream = new MemoryStream(bytes, writable: false);
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                Csv.InferSchemaAsync(stream, options, TestContext.Current.CancellationToken));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ColumnLimit_IsEnforcedByEverySource()
    {
        const string csv = "A,B\n1,2";
        byte[] bytes = Encoding.UTF8.GetBytes(csv);
        var options = new CsvSchemaInferenceOptions { Delimiter = ',', MaxColumnCount = 1 };
        string path = Path.Join(Path.GetTempPath(), Path.GetRandomFileName() + ".csv");
        try
        {
            await File.WriteAllBytesAsync(path, bytes, TestContext.Current.CancellationToken);
            Assert.Throws<CsvException>(() => Csv.InferSchema(csv, options));
            await Assert.ThrowsAsync<CsvException>(() =>
                Csv.InferSchemaFileAsync(path, options, TestContext.Current.CancellationToken));
            using var stream = new MemoryStream(bytes, writable: false);
            await Assert.ThrowsAsync<CsvException>(() =>
                Csv.InferSchemaAsync(stream, options, TestContext.Current.CancellationToken));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static async Task AssertSourcesAgreeAsync(
        string csv, byte[] bytes, CsvSchemaInferenceOptions options, bool includeNonSeekable = true)
    {
        var token = TestContext.Current.CancellationToken;
        var expected = Csv.InferSchema(csv, options);
        string path = Path.Join(Path.GetTempPath(), Path.GetRandomFileName() + ".csv");
        try
        {
            await File.WriteAllBytesAsync(path, bytes, token);
            var file = await Csv.InferSchemaFileAsync(path, options, token);
            AssertSame(expected, file);

            using var seekable = new MemoryStream(bytes, writable: false);
            var seekableResult = await Csv.InferSchemaAsync(seekable, options, token);
            AssertSame(expected, seekableResult);
            Assert.Equal(0, seekable.Position);

            if (!includeNonSeekable)
                return;

            int[] chunkSizes = [1, 2, 3, 7];
            foreach (int chunkSize in chunkSizes)
            {
                using var chunked = new ChunkedReadStream(bytes, chunkSize);
                var streamed = await Csv.InferSchemaAsync(chunked, options, token);
                AssertSame(expected, streamed);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void AssertSame(CsvSchemaInferenceResult expected, CsvSchemaInferenceResult actual)
    {
        Assert.Equal(expected.SampledRowCount, actual.SampledRowCount);
        Assert.Equal(expected.Columns, actual.Columns);
    }

    private sealed class ChunkedReadStream(byte[] data, int chunkSize) : Stream
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

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            int count = Math.Min(buffer.Length, Math.Min(chunkSize, data.Length - position));
            data.AsSpan(position, count).CopyTo(buffer);
            position += count;
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
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
