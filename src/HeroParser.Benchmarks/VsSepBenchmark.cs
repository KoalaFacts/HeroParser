using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using nietras.SeparatedValues;
using System.Text;

namespace HeroParser.Benchmarks;

/// <summary>
/// Head-to-head benchmark: HeroParser vs Sep.
/// Measures throughput in GB/s for various CSV sizes.
/// Target: Beat Sep's 21 GB/s on AVX-512, 9.5 GB/s on ARM.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 5, iterationCount: 15)]
[MinColumn, MaxColumn, MeanColumn, MedianColumn]
public class VsSepBenchmark
{
    private string _smallCsv = string.Empty;   // 1 KB - startup overhead test
    private string _mediumCsv = string.Empty;  // 1 MB - typical workload
    private string _largeCsv = string.Empty;   // 10 MB - throughput test
    private string _hugeCsv = string.Empty;    // 100 MB - extreme throughput

    [GlobalSetup]
    public void Setup()
    {
        Console.WriteLine("=== HeroParser vs Sep Benchmark ===");
        Console.WriteLine($"Hardware: {Simd.SimdParserFactory.GetHardwareInfo()}");
        Console.WriteLine();

        // Generate test data
        _smallCsv = GenerateCsv(20, 10);          // ~1 KB
        _mediumCsv = GenerateCsv(10_000, 25);     // ~1 MB
        _largeCsv = GenerateCsv(100_000, 25);     // ~10 MB
        _hugeCsv = GenerateCsv(1_000_000, 25);    // ~100 MB

        Console.WriteLine($"Small:  {_smallCsv.Length / 1024.0:F1} KB");
        Console.WriteLine($"Medium: {_mediumCsv.Length / 1024.0 / 1024.0:F2} MB");
        Console.WriteLine($"Large:  {_largeCsv.Length / 1024.0 / 1024.0:F2} MB");
        Console.WriteLine($"Huge:   {_hugeCsv.Length / 1024.0 / 1024.0:F2} MB");
        Console.WriteLine();
    }

    private static string GenerateCsv(int rows, int columns)
    {
        var sb = new StringBuilder();

        // Header
        for (int c = 0; c < columns; c++)
        {
            if (c > 0) sb.Append(',');
            sb.Append($"Column{c}");
        }
        sb.AppendLine();

        // Data rows
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                if (c > 0) sb.Append(',');
                sb.Append($"Value{r}_{c}");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    // =================================
    // SMALL CSV (1 KB) - Startup Test
    // =================================

    [BenchmarkCategory("Small"), Benchmark(Baseline = true)]
    public int Small_HeroParser()
    {
        int count = 0;
        foreach (var row in Csv.Parse(_smallCsv.AsSpan()))
        {
            count += row.Count;
        }
        return count;
    }

    [BenchmarkCategory("Small"), Benchmark]
    public int Small_Sep()
    {
        int count = 0;
        using var reader = Sep.Reader().FromText(_smallCsv);
        foreach (var row in reader)
        {
            count += row.ColCount;
        }
        return count;
    }

    // =================================
    // MEDIUM CSV (1 MB) - Typical Workload
    // =================================

    [BenchmarkCategory("Medium"), Benchmark(Baseline = true)]
    public int Medium_HeroParser()
    {
        int count = 0;
        foreach (var row in Csv.Parse(_mediumCsv.AsSpan()))
        {
            count += row.Count;
        }
        return count;
    }

    [BenchmarkCategory("Medium"), Benchmark]
    public int Medium_Sep()
    {
        int count = 0;
        using var reader = Sep.Reader().FromText(_mediumCsv);
        foreach (var row in reader)
        {
            count += row.ColCount;
        }
        return count;
    }

    // =================================
    // LARGE CSV (10 MB) - Throughput Test
    // =================================

    [BenchmarkCategory("Large"), Benchmark(Baseline = true)]
    public int Large_HeroParser()
    {
        int count = 0;
        foreach (var row in Csv.Parse(_largeCsv.AsSpan()))
        {
            count += row.Count;
        }
        return count;
    }

    [BenchmarkCategory("Large"), Benchmark]
    public int Large_Sep()
    {
        int count = 0;
        using var reader = Sep.Reader().FromText(_largeCsv);
        foreach (var row in reader)
        {
            count += row.ColCount;
        }
        return count;
    }

    // =================================
    // HUGE CSV (100 MB) - Maximum Throughput
    // =================================

    [BenchmarkCategory("Huge"), Benchmark(Baseline = true)]
    public int Huge_HeroParser()
    {
        int count = 0;
        foreach (var row in Csv.Parse(_hugeCsv.AsSpan()))
        {
            count += row.Count;
        }
        return count;
    }

    [BenchmarkCategory("Huge"), Benchmark]
    public int Huge_Sep()
    {
        int count = 0;
        using var reader = Sep.Reader().FromText(_hugeCsv);
        foreach (var row in reader)
        {
            count += row.ColCount;
        }
        return count;
    }

    // =================================
    // PARALLEL (100 MB) - Multi-Core Test
    // =================================

    [BenchmarkCategory("Parallel"), Benchmark]
    public int Parallel_HeroParser()
    {
        var reader = Csv.ParseParallel(_hugeCsv.AsSpan(), threadCount: 8);
        var rows = reader.ParseAll();
        return rows.Sum(r => r.Length);
    }
}
