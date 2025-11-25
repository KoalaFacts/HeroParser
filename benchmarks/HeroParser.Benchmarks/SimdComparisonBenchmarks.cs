using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using HeroParser.SeparatedValues;
using System.Text;

namespace HeroParser.Benchmarks;

/// <summary>
/// Compares SIMD-enabled vs scalar-only parsing performance.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Throughput, iterationCount: 5, warmupCount: 3)]
public class SimdComparisonBenchmarks
{
    private string csv = null!;
    private CsvParserOptions simdOptions = null!;
    private CsvParserOptions scalarOptions = null!;

    [Params(1_000, 10_000, 100_000)]
    public int Rows { get; set; }

    [Params(10, 25)]
    public int Columns { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        csv = GenerateCsv(Rows, Columns);
        simdOptions = new CsvParserOptions { UseSimdIfAvailable = true };
        scalarOptions = new CsvParserOptions { UseSimdIfAvailable = false };

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

    [Benchmark(Baseline = true, Description = "SIMD Enabled")]
    public int WithSimd()
    {
        using var reader = Csv.ReadFromText(csv, simdOptions);
        int total = 0;
        foreach (var row in reader)
        {
            total += row.ColumnCount;
        }
        return total;
    }

    [Benchmark(Description = "Scalar Only")]
    public int WithoutSimd()
    {
        using var reader = Csv.ReadFromText(csv, scalarOptions);
        int total = 0;
        foreach (var row in reader)
        {
            total += row.ColumnCount;
        }
        return total;
    }
}
