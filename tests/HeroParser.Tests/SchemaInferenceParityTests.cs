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

    [Fact]
    public async Task ColumnLimit_IsEnforcedForSampledDataRows()
    {
        const string csv = "A,B\n1,2,3\n";
        byte[] bytes = Encoding.UTF8.GetBytes(csv);
        var options = new CsvSchemaInferenceOptions { Delimiter = ',', MaxColumnCount = 2 };
        Assert.Equal(CsvErrorCode.TooManyColumns,
            Assert.Throws<CsvException>(() => Csv.InferSchema(csv, options)).ErrorCode);
        using var stream = new ChunkedReadStream(bytes, 1);
        var error = await Assert.ThrowsAsync<CsvException>(() =>
            Csv.InferSchemaAsync(stream, options, TestContext.Current.CancellationToken));
        Assert.Equal(CsvErrorCode.TooManyColumns, error.ErrorCode);
    }

    [Theory]
    [InlineData("LongHeader,Value\n1,2\n", 5)]
    [InlineData("A,B\n123456,2\n", 5)]
    [InlineData("A,B\n\"123\n456\",2\n", 8)]
    public async Task RowSizeLimit_RejectsOversizedHeaderOrDataAcrossSources(string csv, int maxRowSize)
    {
        await AssertRowSizeLimitAcrossSourcesAsync(csv,
            new CsvSchemaInferenceOptions { Delimiter = ',', MaxRowSize = maxRowSize });
    }

    [Fact]
    public async Task OversizedUnterminatedQuotedRow_IsRejectedAcrossSources()
    {
        string csv = "A\n\"" + new string('x', 10);
        await AssertRowSizeLimitAcrossSourcesAsync(csv,
            new CsvSchemaInferenceOptions { Delimiter = ',', MaxRowSize = 8 });
    }

    [Fact]
    public async Task OversizedUnterminatedQuotedHeader_IsRejectedAcrossSources()
    {
        string csv = "\"" + new string('x', 10);
        await AssertRowSizeLimitAcrossSourcesAsync(csv,
            new CsvSchemaInferenceOptions { Delimiter = ',', MaxRowSize = 8 });
    }

    [Fact]
    public async Task UnterminatedQuotedRowWithinLimit_RemainsRecoverable()
    {
        const string csv = "A\n\"short";
        var options = new CsvSchemaInferenceOptions { Delimiter = ',', MaxRowSize = 8 };
        await AssertSourcesAgreeAsync(csv, Encoding.UTF8.GetBytes(csv), options);
    }

    [Fact]
    public async Task BlankRowsBeyondScanBudget_AreRejectedAcrossSources()
    {
        string csv = "A\n1\n" + new string('\n', 100);
        var options = new CsvSchemaInferenceOptions
        {
            Delimiter = ',',
            SampleRows = 2,
            MaxRowSize = 8,
            MaxScannedInputSize = 32
        };

        byte[] bytes = Encoding.UTF8.GetBytes(csv);
        string path = Path.Join(Path.GetTempPath(), Path.GetRandomFileName() + ".csv");
        try
        {
            await File.WriteAllBytesAsync(path, bytes, TestContext.Current.CancellationToken);
            Assert.Equal(CsvErrorCode.InputSizeExceeded,
                Assert.Throws<CsvException>(() => Csv.InferSchema(csv, options)).ErrorCode);
            Assert.Equal(CsvErrorCode.InputSizeExceeded,
                (await Assert.ThrowsAsync<CsvException>(() => Csv.InferSchemaFileAsync(path, options,
                    TestContext.Current.CancellationToken))).ErrorCode);

            using var seekable = new MemoryStream(bytes, writable: false);
            Assert.Equal(CsvErrorCode.InputSizeExceeded,
                (await Assert.ThrowsAsync<CsvException>(() => Csv.InferSchemaAsync(seekable, options,
                    TestContext.Current.CancellationToken))).ErrorCode);
            Assert.Equal(0, seekable.Position);

            using var chunked = new ChunkedReadStream(bytes, 1);
            Assert.Equal(CsvErrorCode.InputSizeExceeded,
                (await Assert.ThrowsAsync<CsvException>(() => Csv.InferSchemaAsync(chunked, options,
                    TestContext.Current.CancellationToken))).ErrorCode);
            Assert.InRange(chunked.BytesRead, 1, 33);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task CompletedSample_DoesNotReadTrailingInputBeyondBudget()
    {
        string csv = "A\n1\n" + new string('\n', 100);
        var options = new CsvSchemaInferenceOptions
        {
            Delimiter = ',',
            SampleRows = 1,
            MaxScannedInputSize = 8
        };

        await AssertSourcesAgreeAsync(csv, Encoding.UTF8.GetBytes(csv), options);
    }

    [Fact]
    public async Task InputExactlyAtScanBudget_IsAccepted()
    {
        const string csv = "A\n1";
        var options = new CsvSchemaInferenceOptions { Delimiter = ',', MaxScannedInputSize = 3 };

        await AssertSourcesAgreeAsync(csv, Encoding.UTF8.GetBytes(csv), options);
    }

    [Fact]
    public async Task CompletedSampleAtScanBudgetBoundary_IsAccepted()
    {
        string csv = "A,B\n1,2\n" + new string('\n', 100);
        var options = new CsvSchemaInferenceOptions
        {
            Delimiter = ',',
            SampleRows = 1,
            MaxScannedInputSize = 8
        };

        await AssertSourcesAgreeAsync(csv, Encoding.UTF8.GetBytes(csv), options);
    }

    [Fact]
    public async Task OverBudgetDelimiter_DoesNotCauseColumnError()
    {
        const string csv = "A,B,";
        var options = new CsvSchemaInferenceOptions
        {
            Delimiter = ',',
            MaxColumnCount = 2,
            MaxScannedInputSize = 3
        };

        await AssertScanLimitAcrossSourcesAsync(csv, Encoding.UTF8.GetBytes(csv), options);
    }

    [Fact]
    public async Task LeadingBom_IsIncludedInScanBudget()
    {
        const string csv = "\uFEFFA\n1";
        var options = new CsvSchemaInferenceOptions { Delimiter = ',', MaxScannedInputSize = 3 };

        await AssertScanLimitAcrossSourcesAsync(csv, Encoding.UTF8.GetBytes(csv), options);
    }

    [Fact]
    public async Task QuotedRowCutOffByScanBudget_IsRejectedAcrossSources()
    {
        string csv = "A\n\"" + new string('x', 100) + "\"";
        var options = new CsvSchemaInferenceOptions { Delimiter = ',', MaxScannedInputSize = 16 };

        await AssertScanLimitAcrossSourcesAsync(csv, Encoding.UTF8.GetBytes(csv), options);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Utf16BlankRowsBeyondScanBudget_AreRejected(bool bigEndian)
    {
        string csv = "A\n1\n" + new string('\n', 100);
        Encoding encoding = bigEndian ? Encoding.BigEndianUnicode : Encoding.Unicode;
        byte[] bytes = [.. encoding.GetPreamble(), .. encoding.GetBytes(csv)];
        var options = new CsvSchemaInferenceOptions
        {
            Delimiter = ',',
            SampleRows = 2,
            MaxRowSize = 8,
            MaxScannedInputSize = 32
        };

        await AssertScanLimitAcrossSourcesAsync(csv, bytes, options);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Utf16CompletedSample_DoesNotReadTrailingInputBeyondBudget(bool bigEndian)
    {
        string csv = "A\n1\n" + new string('\n', 100);
        Encoding encoding = bigEndian ? Encoding.BigEndianUnicode : Encoding.Unicode;
        byte[] bytes = [.. encoding.GetPreamble(), .. encoding.GetBytes(csv)];
        var options = new CsvSchemaInferenceOptions
        {
            Delimiter = ',',
            SampleRows = 1,
            MaxScannedInputSize = 10
        };

        await AssertSourcesAgreeAsync(csv, bytes, options);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task InvalidScanBudget_IsRejectedAcrossSources(long maxScannedInputSize)
    {
        var options = new CsvSchemaInferenceOptions
        {
            Delimiter = ',',
            MaxScannedInputSize = maxScannedInputSize
        };
        const string csv = "A\n1";

        Assert.Throws<ArgumentOutOfRangeException>(() => Csv.InferSchema(csv, options));
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            Csv.InferSchemaAsync(stream, options, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AutoDetection_RespectsScanBudget()
    {
        string csv = "A,B\n1,2\n" + new string('\n', 100);
        var options = new CsvSchemaInferenceOptions
        {
            SampleRows = 2,
            MaxScannedInputSize = 2
        };

        await AssertScanLimitAcrossSourcesAsync(csv, Encoding.UTF8.GetBytes(csv), options,
            includeNonSeekable: false);
    }

    [Fact]
    public async Task DefaultRowSizeLimit_RejectsOversizedDataRow()
    {
        string csv = "Value\n" + new string('x', 1024 * 1024 + 1);
        await AssertRowSizeLimitAcrossSourcesAsync(csv,
            new CsvSchemaInferenceOptions { Delimiter = ',' }, includeNonSeekable: false);
    }

    [Fact]
    public async Task RowSizeLimit_CanBeRaisedForTrustedLargeRows()
    {
        string csv = "Value\n" + new string('x', 1024 * 1024 + 1);
        var options = new CsvSchemaInferenceOptions { Delimiter = ',', MaxRowSize = 2 * 1024 * 1024 };
        await AssertSourcesAgreeAsync(csv, Encoding.UTF8.GetBytes(csv), options, includeNonSeekable: false);
    }

    [Fact]
    public async Task RowSizeLimit_AcceptsBoundaryAndDoesNotInspectUnsampledTail()
    {
        const string csv = "A,B\n12,3\n123456,7\n";
        var options = new CsvSchemaInferenceOptions { Delimiter = ',', SampleRows = 1, MaxRowSize = 4 };
        await AssertSourcesAgreeAsync(csv, Encoding.UTF8.GetBytes(csv), options);
    }

    [Fact]
    public async Task RowSizeLimit_StopsReadingAtConfiguredWindow()
    {
        string csv = "A\n" + new string('x', 100_000);
        byte[] bytes = Encoding.UTF8.GetBytes(csv);
        const int maxRowSize = 8;
        using var chunked = new ChunkedReadStream(bytes, bytes.Length);

        var error = await Assert.ThrowsAsync<CsvException>(() => Csv.InferSchemaAsync(chunked,
            new CsvSchemaInferenceOptions { Delimiter = ',', MaxRowSize = maxRowSize },
            TestContext.Current.CancellationToken));

        Assert.Equal(CsvErrorCode.ParseError, error.ErrorCode);
        Assert.True(chunked.BytesRead <= "A\n"u8.Length + maxRowSize + 2);
    }

    [Fact]
    public async Task Utf16Input_RowSizeLimitCountsTranscodedUtf8Bytes()
    {
        const string csv = "A\n猫\n";
        byte[] bytes = [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(csv)];
        var options = new CsvSchemaInferenceOptions { Delimiter = ',', MaxRowSize = 2 };

        Assert.Equal(1, Csv.InferSchema(csv, options).SampledRowCount);
        using var stream = new ChunkedReadStream(bytes, 1);
        var error = await Assert.ThrowsAsync<CsvException>(() =>
            Csv.InferSchemaAsync(stream, options, TestContext.Current.CancellationToken));
        Assert.Equal(CsvErrorCode.ParseError, error.ErrorCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task InvalidRowSizeLimit_IsRejectedByEverySource(int maxRowSize)
    {
        var options = new CsvSchemaInferenceOptions { Delimiter = ',', MaxRowSize = maxRowSize };
        const string csv = "A,B\n1,2\n";
        Assert.Throws<ArgumentOutOfRangeException>(() => Csv.InferSchema(csv, options));
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv), writable: false);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            Csv.InferSchemaAsync(stream, options, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RowSizeLimitAboveHardCap_IsRejectedByEverySource()
    {
        var options = new CsvSchemaInferenceOptions
        {
            Delimiter = ',',
            MaxRowSize = 128 * 1024 * 1024 + 1
        };
        const string csv = "A,B\n1,2\n";
        Assert.Throws<ArgumentOutOfRangeException>(() => Csv.InferSchema(csv, options));
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv), writable: false);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            Csv.InferSchemaAsync(stream, options, TestContext.Current.CancellationToken));
    }

    private static async Task AssertRowSizeLimitAcrossSourcesAsync(
        string csv, CsvSchemaInferenceOptions options, bool includeNonSeekable = true)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(csv);
        string path = Path.Join(Path.GetTempPath(), Path.GetRandomFileName() + ".csv");
        try
        {
            await File.WriteAllBytesAsync(path, bytes, TestContext.Current.CancellationToken);
            var textError = Assert.Throws<CsvException>(() => Csv.InferSchema(csv, options));
            var fileError = await Assert.ThrowsAsync<CsvException>(() =>
                Csv.InferSchemaFileAsync(path, options, TestContext.Current.CancellationToken));
            using var seekable = new MemoryStream(bytes, writable: false);
            var streamError = await Assert.ThrowsAsync<CsvException>(() =>
                Csv.InferSchemaAsync(seekable, options, TestContext.Current.CancellationToken));

            Assert.Equal(CsvErrorCode.ParseError, textError.ErrorCode);
            Assert.Equal(CsvErrorCode.ParseError, fileError.ErrorCode);
            Assert.Equal(CsvErrorCode.ParseError, streamError.ErrorCode);
            Assert.Contains("maximum size", textError.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("maximum size", fileError.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("maximum size", streamError.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, seekable.Position);

            if (includeNonSeekable)
            {
                using var chunked = new ChunkedReadStream(bytes, 1);
                var chunkedError = await Assert.ThrowsAsync<CsvException>(() =>
                    Csv.InferSchemaAsync(chunked, options, TestContext.Current.CancellationToken));
                Assert.Equal(CsvErrorCode.ParseError, chunkedError.ErrorCode);
                Assert.Contains("maximum size", chunkedError.Message, StringComparison.OrdinalIgnoreCase);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static async Task AssertScanLimitAcrossSourcesAsync(
        string csv, byte[] bytes, CsvSchemaInferenceOptions options, bool includeNonSeekable = true)
    {
        string path = Path.Join(Path.GetTempPath(), Path.GetRandomFileName() + ".csv");
        try
        {
            await File.WriteAllBytesAsync(path, bytes, TestContext.Current.CancellationToken);
            Assert.Equal(CsvErrorCode.InputSizeExceeded,
                Assert.Throws<CsvException>(() => Csv.InferSchema(csv, options)).ErrorCode);
            Assert.Equal(CsvErrorCode.InputSizeExceeded,
                (await Assert.ThrowsAsync<CsvException>(() => Csv.InferSchemaFileAsync(path, options,
                    TestContext.Current.CancellationToken))).ErrorCode);
            using var seekable = new MemoryStream(bytes, writable: false);
            Assert.Equal(CsvErrorCode.InputSizeExceeded,
                (await Assert.ThrowsAsync<CsvException>(() => Csv.InferSchemaAsync(seekable, options,
                    TestContext.Current.CancellationToken))).ErrorCode);
            Assert.Equal(0, seekable.Position);

            if (includeNonSeekable)
            {
                using var chunked = new ChunkedReadStream(bytes, 1);
                Assert.Equal(CsvErrorCode.InputSizeExceeded,
                    (await Assert.ThrowsAsync<CsvException>(() => Csv.InferSchemaAsync(chunked, options,
                        TestContext.Current.CancellationToken))).ErrorCode);
                Assert.InRange(chunked.BytesRead, 1, (int)options.MaxScannedInputSize + 1);
            }
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
        public int BytesRead { get; private set; }

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
            int count = Math.Min(buffer.Length, Math.Min(chunkSize, data.Length - BytesRead));
            data.AsSpan(BytesRead, count).CopyTo(buffer);
            BytesRead += count;
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
