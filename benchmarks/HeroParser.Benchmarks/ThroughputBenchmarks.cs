using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
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
    private string _smallCsv = null!;
    private string _mediumCsv = null!;
    private string _largeCsv = null!;
    private string _wideCsv = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Small: 1000 rows x 10 columns
        _smallCsv = GenerateCsv(1_000, 10);

        // Medium: 10,000 rows x 20 columns
        _mediumCsv = GenerateCsv(10_000, 20);

        // Large: 100,000 rows x 10 columns
        _largeCsv = GenerateCsv(100_000, 25);

        // Wide: 1,000 rows x 100 columns
        _wideCsv = GenerateCsv(1_000, 100);
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

    [Benchmark(Description = "Small (1k rows x 10 cols)")]
    public int Small()
    {
        using var reader = Csv.ReadFromText(_smallCsv);
        int total = 0;
        foreach (var row in reader)
        {
            total += row.ColumnCount;
        }
        return total;
    }

    [Benchmark(Description = "Medium (10k rows x 20 cols)")]
    public int Medium()
    {
        using var reader = Csv.ReadFromText(_mediumCsv);
        int total = 0;
        foreach (var row in reader)
        {
            total += row.ColumnCount;
        }
        return total;
    }

    [Benchmark(Description = "Large (100k rows x 25 cols)")]
    public int Large()
    {
        using var reader = Csv.ReadFromText(_largeCsv);
        int total = 0;
        foreach (var row in reader)
        {
            total += row.ColumnCount;
        }
        return total;
    }

    [Benchmark(Description = "Wide (1k rows x 100 cols)")]
    public int Wide()
    {
        using var reader = Csv.ReadFromText(_wideCsv);
        int total = 0;
        foreach (var row in reader)
        {
            total += row.ColumnCount;
        }
        return total;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        // Calculate and display throughput
        Console.WriteLine();
        Console.WriteLine("=== Throughput Analysis ===");
        Console.WriteLine($"Small CSV size: {_smallCsv.Length:N0} chars ({_smallCsv.Length * 2:N0} bytes)");
        Console.WriteLine($"Medium CSV size: {_mediumCsv.Length:N0} chars ({_mediumCsv.Length * 2:N0} bytes)");
        Console.WriteLine($"Large CSV size: {_largeCsv.Length:N0} chars ({_largeCsv.Length * 2:N0} bytes)");
        Console.WriteLine($"Wide CSV size: {_wideCsv.Length:N0} chars ({_wideCsv.Length * 2:N0} bytes)");
        Console.WriteLine();
        Console.WriteLine($"Hardware: {Hardware.GetHardwareInfo()}");
    }
}
