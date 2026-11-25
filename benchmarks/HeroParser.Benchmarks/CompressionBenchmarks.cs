using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;

namespace HeroParser.Benchmarks;

/// <summary>
/// Measures streaming memory footprint and throughput across transparent uncompressed, .gz, .br, and (.NET 11+) .zst CSV I/O.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Throughput, iterationCount: 5, warmupCount: 3)]
public class CompressionBenchmarks
{
    private List<CompressedRow> rows = null!;
    private string tempDir = null!;
    private string plainPath = null!;
    private string gzipPath = null!;
    private string brotliPath = null!;
    private string writePlainPath = null!;
    private string writeGzipPath = null!;
    private string writeBrotliPath = null!;
#if NET11_0_OR_GREATER
    private string zstdPath = null!;
    private string writeZstdPath = null!;
#endif

    [Params(10_000)]
    public int Rows { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        rows = new List<CompressedRow>(Rows);
        for (int i = 0; i < Rows; i++)
        {
            rows.Add(new CompressedRow
            {
                Id = i,
                Name = $"customer-{i}",
                Amount = i * 1.5m,
                Active = (i & 1) == 0
            });
        }

        tempDir = Path.Combine(Path.GetTempPath(), $"heroparser-comp-bench-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        plainPath = Path.Combine(tempDir, "data.csv");
        gzipPath = Path.Combine(tempDir, "data.csv.gz");
        brotliPath = Path.Combine(tempDir, "data.csv.br");
        writePlainPath = Path.Combine(tempDir, "out.csv");
        writeGzipPath = Path.Combine(tempDir, "out.csv.gz");
        writeBrotliPath = Path.Combine(tempDir, "out.csv.br");

        Csv.Write<CompressedRow>().ToFile(plainPath, rows);
        Csv.Write<CompressedRow>().ToFile(gzipPath, rows);
        Csv.Write<CompressedRow>().ToFile(brotliPath, rows);
#if NET11_0_OR_GREATER
        zstdPath = Path.Combine(tempDir, "data.csv.zst");
        writeZstdPath = Path.Combine(tempDir, "out.csv.zst");
        Csv.Write<CompressedRow>().ToFile(zstdPath, rows);
#endif

        ValidateAsync().GetAwaiter().GetResult();
    }

    private async Task ValidateAsync()
    {
        long expectedSum = (long)Rows * (Rows - 1) / 2;
        if (await ReadPlainAsync() != expectedSum ||
            await ReadGzipAsync() != expectedSum ||
            await ReadBrotliAsync() != expectedSum)
        {
            throw new InvalidOperationException("Compression benchmark reader returned an unexpected ID sum.");
        }
#if NET11_0_OR_GREATER
        if (await ReadZstdAsync() != expectedSum)
            throw new InvalidOperationException("Compression benchmark zstd reader returned an unexpected ID sum.");
#endif

        if (await WritePlainAsync() <= 0 ||
            await WriteGzipAsync() <= 0 ||
            await WriteBrotliAsync() <= 0)
        {
            throw new InvalidOperationException("Compression benchmark writer produced an empty file.");
        }
#if NET11_0_OR_GREATER
        if (await WriteZstdAsync() <= 0)
            throw new InvalidOperationException("Compression benchmark zstd writer produced an empty file.");
#endif
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        try
        {
            if (!string.IsNullOrEmpty(tempDir) && Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [Benchmark(Baseline = true)]
    public async Task<long> ReadPlainAsync()
    {
        long total = 0;
        await foreach (CompressedRow row in Csv.Read<CompressedRow>().FromFileAsync(plainPath))
            total += row.Id;
        return total;
    }

    [Benchmark]
    public async Task<long> ReadGzipAsync()
    {
        long total = 0;
        await foreach (CompressedRow row in Csv.Read<CompressedRow>().FromFileAsync(gzipPath))
            total += row.Id;
        return total;
    }

    [Benchmark]
    public async Task<long> ReadBrotliAsync()
    {
        long total = 0;
        await foreach (CompressedRow row in Csv.Read<CompressedRow>().FromFileAsync(brotliPath))
            total += row.Id;
        return total;
    }

#if NET11_0_OR_GREATER
    [Benchmark]
    public async Task<long> ReadZstdAsync()
    {
        long total = 0;
        await foreach (CompressedRow row in Csv.Read<CompressedRow>().FromFileAsync(zstdPath))
            total += row.Id;
        return total;
    }
#endif

    [Benchmark]
    public async Task<long> WritePlainAsync()
    {
        await Csv.Write<CompressedRow>().ToFileAsync(writePlainPath, StreamRowsAsync());
        return new FileInfo(writePlainPath).Length;
    }

    [Benchmark]
    public async Task<long> WriteGzipAsync()
    {
        await Csv.Write<CompressedRow>().ToFileAsync(writeGzipPath, StreamRowsAsync());
        return new FileInfo(writeGzipPath).Length;
    }

    [Benchmark]
    public async Task<long> WriteBrotliAsync()
    {
        await Csv.Write<CompressedRow>().ToFileAsync(writeBrotliPath, StreamRowsAsync());
        return new FileInfo(writeBrotliPath).Length;
    }

#if NET11_0_OR_GREATER
    [Benchmark]
    public async Task<long> WriteZstdAsync()
    {
        await Csv.Write<CompressedRow>().ToFileAsync(writeZstdPath, StreamRowsAsync());
        return new FileInfo(writeZstdPath).Length;
    }
#endif

    private async IAsyncEnumerable<CompressedRow> StreamRowsAsync()
    {
        await Task.CompletedTask;
        foreach (CompressedRow row in rows)
            yield return row;
    }

    [GenerateBinder]
    public sealed class CompressedRow
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public decimal Amount { get; set; }
        public bool Active { get; set; }
    }
}
