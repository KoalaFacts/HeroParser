using HeroParser.Conversion;
using HeroParser.FixedWidths;
using Xunit;

namespace HeroParser.Tests.Memory;

[Collection(MemoryRegressionCollection.NAME)]
[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
public sealed class DataReaderAndCompressionMemoryRegressionTests
{
    private const int SMALL_ROWS = 200;
    private const int LARGE_ROWS = 2_000;
    private const int DELTA_ROWS = LARGE_ROWS - SMALL_ROWS;

    private static readonly FixedWidthFieldDefinition[] fixedWidthColumns =
    [
        new("Id", 8, FieldAlignment.Right, '0'),
        new("BigId", 14, FieldAlignment.Right, '0'),
        new("SmallId", 6, FieldAlignment.Right),
        new("Tag", 5, FieldAlignment.Right),
        new("Ratio", 8, FieldAlignment.Right),
        new("Metric", 8, FieldAlignment.Right),
        new("Amount", 10, FieldAlignment.Right),
        new("Active", 6, FieldAlignment.Left),
        new("CreatedAt", 28, FieldAlignment.Left),
        new("Token", 36, FieldAlignment.Left),
        new("Code", 2, FieldAlignment.Left),
        new("Label", 10, FieldAlignment.Left)
    ];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Csv_WriteFromDataReader_Sync_ZeroBoxing_ZeroPerRowGrowth(bool includeNulls)
    {
        using var reader = new SyntheticDataReader(SMALL_ROWS, includeNulls);

        void Run(int rows)
        {
            reader.Reset(rows);
            Csv.WriteFromDataReader(reader, TextWriter.Null, leaveOpen: true);
            if (reader.BoxedValueTypeGetValueCallCount != 0 || reader.GetValuesCallCount != 0)
                throw new InvalidOperationException("DataReaderColumnPlan unexpectedly boxed via GetValue/GetValues.");
        }

        AllocationMeter.AssertZeroPerRowGrowth(
            () => Run(SMALL_ROWS),
            () => Run(LARGE_ROWS),
            DELTA_ROWS,
            scenario: $"Csv.WriteFromDataReader sync (includeNulls={includeNulls})");
    }

    [Fact]
    public async Task Csv_WriteFromDataReader_Async_ZeroBoxing_ZeroPerRowGrowth()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var reader = new SyntheticDataReader(SMALL_ROWS, includeNulls: true);

        async Task RunAsync(int rows)
        {
            reader.Reset(rows);
            await Csv.WriteFromDataReaderAsync(reader, Stream.Null, leaveOpen: true, cancellationToken: ct);
            if (reader.BoxedValueTypeGetValueCallCount != 0 || reader.GetValuesCallCount != 0)
                throw new InvalidOperationException("DataReaderColumnPlan unexpectedly boxed via GetValue/GetValues.");
        }

        await AllocationMeter.AssertZeroPerRowGrowthAsync(
            () => RunAsync(SMALL_ROWS),
            () => RunAsync(LARGE_ROWS),
            DELTA_ROWS,
            scenario: "Csv.WriteFromDataReaderAsync");
    }

    [Fact]
    public async Task FixedWidth_WriteFromDataReader_SyncAndAsync_ZeroBoxing_ZeroPerRowGrowth()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var reader = new SyntheticDataReader(SMALL_ROWS, includeNulls: true);

        void RunSync(int rows)
        {
            reader.Reset(rows);
            FixedWidth.WriteFromDataReader(reader, TextWriter.Null, fixedWidthColumns, leaveOpen: true);
            if (reader.BoxedValueTypeGetValueCallCount != 0 || reader.GetValuesCallCount != 0)
                throw new InvalidOperationException("DataReaderColumnPlan unexpectedly boxed via GetValue/GetValues.");
        }

        AllocationMeter.AssertZeroPerRowGrowth(
            () => RunSync(SMALL_ROWS),
            () => RunSync(LARGE_ROWS),
            DELTA_ROWS,
            scenario: "FixedWidth.WriteFromDataReader sync");

        async Task RunAsync(int rows)
        {
            reader.Reset(rows);
            await FixedWidth.WriteFromDataReaderAsync(reader, Stream.Null, fixedWidthColumns, leaveOpen: true, cancellationToken: ct);
            if (reader.BoxedValueTypeGetValueCallCount != 0 || reader.GetValuesCallCount != 0)
                throw new InvalidOperationException("DataReaderColumnPlan unexpectedly boxed via GetValue/GetValues.");
        }

        await AllocationMeter.AssertZeroPerRowGrowthAsync(
            () => RunAsync(SMALL_ROWS),
            () => RunAsync(LARGE_ROWS),
            DELTA_ROWS,
            scenario: "FixedWidth.WriteFromDataReaderAsync");
    }

    [Fact]
    public async Task Htb_WriteFromDataReader_SyncAndAsync_ZeroBoxing_ZeroPerRowGrowth()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var reader = new SyntheticDataReader(SMALL_ROWS, includeNulls: true);

        void RunSync(int rows)
        {
            reader.Reset(rows);
            global::HeroParser.Htb.WriteFromDataReader(reader, Stream.Null, leaveOpen: true);
            if (reader.BoxedValueTypeGetValueCallCount != 0 || reader.GetValuesCallCount != 0)
                throw new InvalidOperationException("DataReaderColumnPlan unexpectedly boxed via GetValue/GetValues.");
        }

        AllocationMeter.AssertZeroPerRowGrowth(
            () => RunSync(SMALL_ROWS),
            () => RunSync(LARGE_ROWS),
            DELTA_ROWS,
            scenario: "Htb.WriteFromDataReader sync");

        async Task RunAsync(int rows)
        {
            reader.Reset(rows);
            await global::HeroParser.Htb.WriteFromDataReaderAsync(reader, Stream.Null, leaveOpen: true, cancellationToken: ct);
            if (reader.BoxedValueTypeGetValueCallCount != 0 || reader.GetValuesCallCount != 0)
                throw new InvalidOperationException("DataReaderColumnPlan unexpectedly boxed via GetValue/GetValues.");
        }

        await AllocationMeter.AssertZeroPerRowGrowthAsync(
            () => RunAsync(SMALL_ROWS),
            () => RunAsync(LARGE_ROWS),
            DELTA_ROWS,
            scenario: "Htb.WriteFromDataReaderAsync");
    }

    [Fact]
    public async Task Jsonl_WriteFromDataReader_SyncAndAsync_ZeroBoxing_ZeroPerRowGrowth()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var reader = new SyntheticDataReader(SMALL_ROWS, includeNulls: true);

        void RunSync(int rows)
        {
            reader.Reset(rows);
            Jsonl.WriteFromDataReader(reader, Stream.Null, leaveOpen: true);
            if (reader.BoxedValueTypeGetValueCallCount != 0 || reader.GetValuesCallCount != 0)
                throw new InvalidOperationException("DataReaderColumnPlan unexpectedly boxed via GetValue/GetValues.");
        }

        AllocationMeter.AssertZeroPerRowGrowth(
            () => RunSync(SMALL_ROWS),
            () => RunSync(LARGE_ROWS),
            DELTA_ROWS,
            scenario: "Jsonl.WriteFromDataReader sync");

        async Task RunAsync(int rows)
        {
            reader.Reset(rows);
            await Jsonl.WriteFromDataReaderAsync(reader, Stream.Null, leaveOpen: true, cancellationToken: ct);
            if (reader.BoxedValueTypeGetValueCallCount != 0 || reader.GetValuesCallCount != 0)
                throw new InvalidOperationException("DataReaderColumnPlan unexpectedly boxed via GetValue/GetValues.");
        }

        await AllocationMeter.AssertZeroPerRowGrowthAsync(
            () => RunAsync(SMALL_ROWS),
            () => RunAsync(LARGE_ROWS),
            DELTA_ROWS,
            scenario: "Jsonl.WriteFromDataReaderAsync");
    }

    [Fact]
    public void Excel_WriteFromDataReader_ZeroBoxing_BoundedPerRowMemory()
    {
        using var reader = new SyntheticDataReader(SMALL_ROWS, includeNulls: true);
        using var reusableMs = new MemoryStream(capacity: 512 * 1024);
        long bytesWritten = 0;

        void Run(int rows)
        {
            reusableMs.Position = 0;
            reusableMs.SetLength(0);
            reader.Reset(rows);
            global::HeroParser.Excel.WriteFromDataReader(reader, reusableMs, leaveOpen: true);
            bytesWritten = reusableMs.Length;
            if (reader.BoxedValueTypeGetValueCallCount != 0 || reader.GetValuesCallCount != 0)
                throw new InvalidOperationException("DataReaderColumnPlan unexpectedly boxed via GetValue/GetValues.");
        }

        AllocationMeter.AssertBoundedPerRowGrowth(
            () => Run(SMALL_ROWS),
            () => Run(LARGE_ROWS),
            DELTA_ROWS,
            maxBytesPerRow: 2048.0,
            scenario: "Excel.WriteFromDataReader");

        Assert.True(bytesWritten > 0);
    }

    [Theory]
    [InlineData(".csv.gz")]
    [InlineData(".csv.br")]
#if NET11_0_OR_GREATER
    [InlineData(".csv.zst")]
#endif
    public void Compression_StreamingCsv_GzipBrotliZstd_ConstantMemoryPerRow(string extension)
    {
        string dir = Path.Combine(Path.GetTempPath(), $"hero-mem-comp-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            string smallPath = Path.Combine(dir, $"small{extension}");
            string largePath = Path.Combine(dir, $"large{extension}");

            using (var smallWriter = new SyntheticDataReader(SMALL_ROWS, includeNulls: false))
                Csv.WriteFromDataReaderToFile(smallPath, smallWriter);

            using (var largeWriter = new SyntheticDataReader(LARGE_ROWS, includeNulls: false))
                Csv.WriteFromDataReaderToFile(largePath, largeWriter);

            long checksum = 0;

            static long StreamRead(string path)
            {
                using var dr = Csv.CreateDataReader(path);
                long sum = 0;
                while (dr.Read())
                {
                    sum += dr.GetInt32(0) + (dr.GetInt64(1) & 0xFF) + (long)dr.GetDecimal(6);
                }
                return sum;
            }

            // Streaming decompression reuses FileStream + codec + CsvAsyncStreamReader buffers;
            // per-row growth should stay tiny / constant regardless of input file size.
            AllocationMeter.AssertBoundedPerRowGrowth(
                () => checksum = StreamRead(smallPath),
                () => checksum = StreamRead(largePath),
                DELTA_ROWS,
                maxBytesPerRow: 256.0,
                scenario: $"Csv.CreateDataReader streaming decompression ({extension})");

            Assert.NotEqual(0L, checksum);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
