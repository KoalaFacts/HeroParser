using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using HeroParser.SeparatedValues;
using System.Text;

namespace HeroParser.Benchmarks;

/// <summary>
/// Throughput benchmarks to validate 30+ GB/s claim.
/// Measures raw parsing speed in GB/s.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Throughput, iterationCount: 10, warmupCount: 3)]
public class ThroughputBenchmarks
{
    private string csv = null!;
    private CsvParserOptions options = null!;

    [Params(1_000, 10_000, 100_000)]
    public int Rows { get; set; }

    [Params(10, 25, 100)]
    public int Columns { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        csv = GenerateCsv(Rows, Columns);
        options = new CsvParserOptions
        {
            MaxColumns = Columns + 4, // small headroom beyond generated data
            MaxRows = Rows + 100      // allow end-of-file without tripping the limit
        };
        Console.WriteLine($"Hardware: {Hardware.GetHardwareInfo()}");
        Console.WriteLine($"CSV Size: {csv.Length:N0} chars ({csv.Length * 2:N0} bytes)");
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

    [Benchmark]
    public int ParseCsv()
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
