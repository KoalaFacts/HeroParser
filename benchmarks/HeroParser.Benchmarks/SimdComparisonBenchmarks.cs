using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using HeroParser.SeparatedValues;
using HeroParser.SeparatedValues.Core;
using System.Text;

namespace HeroParser.Benchmarks;

/// <summary>
/// Compares SIMD-enabled vs scalar-only parsing performance and memory allocations across UTF-8, UTF-16, unquoted, and quoted CSV inputs.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Throughput, iterationCount: 5, warmupCount: 3)]
public class SimdComparisonBenchmarks
{
    private string csv = null!;
    private byte[] csvUtf8 = null!;
    private CsvReadOptions simdOptions = null!;
    private CsvReadOptions scalarOptions = null!;

    [Params(1_000, 10_000, 100_000)]
    public int Rows { get; set; }

    [Params(10, 25)]
    public int Columns { get; set; }

    [Params(false, true)]
    public bool Quoted { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        csv = GenerateCsv(Rows, Columns, Quoted);
        csvUtf8 = Encoding.UTF8.GetBytes(csv);
        simdOptions = new CsvReadOptions
        {
            UseSimdIfAvailable = true,
            MaxColumnCount = Columns + 4,
            MaxRowCount = Rows + 100
        };
        scalarOptions = new CsvReadOptions
        {
            UseSimdIfAvailable = false,
            MaxColumnCount = Columns + 4,
            MaxRowCount = Rows + 100
        };

        int expected = checked(Rows * Columns);
        if (Utf16_WithSimd() != expected ||
            Utf16_WithoutSimd() != expected ||
            Utf8_WithSimd() != expected ||
            Utf8_WithoutSimd() != expected)
        {
            throw new InvalidOperationException("SIMD comparison benchmark returned an invalid cell count.");
        }

        Console.WriteLine($"Hardware: {Hardware.GetHardwareInfo()}");
        Console.WriteLine($"CSV Size: {csv.Length:N0} chars / {csvUtf8.Length:N0} UTF-8 bytes (Quoted={Quoted})");
    }

    private static string GenerateCsv(int rows, int columns, bool quoted)
    {
        var sb = new StringBuilder();
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                if (c > 0) sb.Append(',');
                if (quoted) sb.Append('"');
                sb.Append($"val{r}_{c}");
                if (quoted) sb.Append('"');
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    [Benchmark(Baseline = true, Description = "UTF-16 SIMD")]
    public int Utf16_WithSimd()
    {
        using var reader = Csv.ReadFromText(csv, simdOptions);
        int total = 0;
        foreach (var row in reader)
        {
            total += row.ColumnCount;
        }
        return total;
    }

    [Benchmark(Description = "UTF-16 Scalar")]
    public int Utf16_WithoutSimd()
    {
        using var reader = Csv.ReadFromText(csv, scalarOptions);
        int total = 0;
        foreach (var row in reader)
        {
            total += row.ColumnCount;
        }
        return total;
    }

    [Benchmark(Description = "UTF-8 SIMD")]
    public int Utf8_WithSimd()
    {
        using var reader = Csv.ReadFromByteSpan(csvUtf8, simdOptions);
        int total = 0;
        foreach (var row in reader)
        {
            total += row.ColumnCount;
        }
        return total;
    }

    [Benchmark(Description = "UTF-8 Scalar")]
    public int Utf8_WithoutSimd()
    {
        using var reader = Csv.ReadFromByteSpan(csvUtf8, scalarOptions);
        int total = 0;
        foreach (var row in reader)
        {
            total += row.ColumnCount;
        }
        return total;
    }
}

