using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using HeroParser.SeparatedValues;
using HeroParser.SeparatedValues.Core;
using System.Text;

namespace HeroParser.Benchmarks;

/// <summary>
/// Throughput benchmarks to validate 30+ GB/s claim.
/// Measures raw parsing speed in GB/s.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Throughput, iterationCount: 5, warmupCount: 3)]
public class ThroughputBenchmarks
{
    private string csv = null!;
    private byte[] csvUtf8 = null!;
    private CsvReadOptions options = null!;

    [Params(1_000, 10_000, 100_000)]
    public int Rows { get; set; }

    [Params(10, 25, 100)]
    public int Columns { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        csv = GenerateCsv(Rows, Columns);
        csvUtf8 = Encoding.UTF8.GetBytes(csv);
        options = new CsvReadOptions
        {
            MaxColumnCount = Columns + 4, // small headroom beyond generated data
            MaxRowCount = Rows + 100      // allow end-of-file without tripping the limit
        };
        Console.WriteLine($"Hardware: {Hardware.GetHardwareInfo()}");
        Console.WriteLine($"CSV Size: {csv.Length:N0} chars ({csvUtf8.Length:N0} UTF-8 bytes)");
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
    public int ParseCsvUtf8()
    {
        using var reader = Csv.ReadFromByteSpan(csvUtf8, options);
        int total = 0;
        foreach (var row in reader)
        {
            total += row.ColumnCount;
        }
        return total;
    }

    [Benchmark]
    public int ParseCsvUtf16Scalar()
    {
        using var reader = Csv.ReadFromText(csv, options);
        int total = 0;
        foreach (var row in reader)
        {
            total += row.ColumnCount;
        }
        return total;
    }
}

