using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using HeroParser.SeparatedValues;
using HeroParser.SeparatedValues.Core;
using System.Text;

namespace HeroParser.Benchmarks;

/// <summary>
/// Compares throughput across API shapes: string, memory stream, file streaming (sync), and file streaming (async).
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Throughput, iterationCount: 5, warmupCount: 3)]
public class StreamingThroughputBenchmarks
{
    private string csv = null!;
    private byte[] csvUtf8 = null!;
    private string filePath = null!;
    private CsvParserOptions options = null!;

    [Params(10_000, 100_000)]
    public int Rows { get; set; }

    [Params(10, 25)]
    public int Columns { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        csv = GenerateCsv(Rows, Columns);
        csvUtf8 = Encoding.UTF8.GetBytes(csv);
        filePath = Path.Combine(Path.GetTempPath(), $"heroparser-stream-{Rows}-{Columns}-{Guid.NewGuid():N}.csv");
        File.WriteAllBytes(filePath, csvUtf8);

        // Tighten limits to focus measurement on parser throughput rather than unbounded configs.
        options = new CsvParserOptions
        {
            MaxColumnCount = Columns + 2, // small headroom over generated dataset
            MaxRowCount = Rows + 10       // small headroom over generated dataset
        };
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        try
        {
            if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch
        {
            // best-effort cleanup
        }
    }

    private static string GenerateCsv(int rows, int columns)
    {
        var sb = new StringBuilder();
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                if (c > 0) sb.Append(',');
                sb.Append($"val{r}_{c}");
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    [Benchmark(Baseline = true)]
    public int ParseFromText()
    {
        using var reader = Csv.ReadFromText(csv, options);
        int total = 0;
        foreach (var row in reader)
        {
            total += row.ColumnCount;
        }
        return total;
    }

    [Benchmark]
    public int ParseFromStreamMemory()
    {
        using var stream = new MemoryStream(csvUtf8, writable: false);
        using var reader = Csv.ReadFromStream(stream, options);
        int total = 0;
        while (reader.MoveNext())
        {
            total += reader.Current.ColumnCount;
        }
        return total;
    }

    [Benchmark]
    public int ParseFromFileStreaming()
    {
        using var fs = File.OpenRead(filePath);
        using var reader = Csv.ReadFromStream(fs, options, leaveOpen: false);
        int total = 0;
        while (reader.MoveNext())
        {
            total += reader.Current.ColumnCount;
        }
        return total;
    }

    [Benchmark]
    public async Task<int> ParseFromFileAsyncStreaming()
    {
        await using var reader = Csv.CreateAsyncStreamReader(File.OpenRead(filePath), options, leaveOpen: false);
        int total = 0;
        while (await reader.MoveNextAsync().ConfigureAwait(false))
        {
            total += reader.Current.ColumnCount;
        }
        return total;
    }
}
