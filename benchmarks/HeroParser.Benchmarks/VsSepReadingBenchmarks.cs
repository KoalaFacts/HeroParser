using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using HeroParser.SeparatedValues;
using nietras.SeparatedValues;
using System.Text;

namespace HeroParser.Benchmarks;

/// <summary>
/// Head-to-head CSV READING comparison: HeroParser vs Sep library.
/// Sep by nietras (https://github.com/nietras/Sep) is currently one of the fastest CSV parsers for .NET
/// and served as the primary inspiration for HeroParser's SIMD architecture.
/// These benchmarks ensure HeroParser remains competitive with Sep's reading performance.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Throughput, iterationCount: 5, warmupCount: 3)]
public class VsSepReadingBenchmarks
{
    private string csv = null!;
    private byte[] utf8 = null!;

    [Params(100, 1_000, 10_000, 100_000)]
    public int Rows { get; set; }

    [Params(10, 25, 50, 100)]
    public int Columns { get; set; }

    [Params(false, true)]
    public bool WithQuotes { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        csv = GenerateCsv(Rows, Columns, WithQuotes);
        utf8 = Encoding.UTF8.GetBytes(csv);
    }

    private static string GenerateCsv(int rows, int columns, bool withQuotes)
    {
        var sb = new StringBuilder();
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                if (c > 0) sb.Append(',');

                string value = $"value_{r}_{c}";

                // Quote 50% of fields when withQuotes is true
                if (withQuotes && (r * columns + c) % 2 == 0)
                {
                    sb.Append('"').Append(value).Append('"');
                }
                else
                {
                    sb.Append(value);
                }
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    [Benchmark(Baseline = true, Description = "Sep")]
    public int Sep_Parse()
    {
        using var reader = Sep.Reader().FromText(csv);

        int total = 0;
        foreach (var row in reader)
        {
            total += row.ColCount;
        }

        return total;
    }

    [Benchmark(Description = "HeroParser (string)")]
    public int HeroParser_ParseString()
    {
        var options = new CsvParserOptions
        {
            MaxColumnCount = 1_000,
            MaxRowCount = 1_000_000,
            EnableQuotedFields = WithQuotes,              // skip quote machinery when data has no quotes
            AllowNewlinesInsideQuotes = WithQuotes        // only meaningful when quotes are present
        };

        using var reader = Csv.ReadFromText(csv, new()
        {
            MaxColumnCount = options.MaxColumnCount,
            MaxRowCount = options.MaxRowCount,
            EnableQuotedFields = options.EnableQuotedFields,
            AllowNewlinesInsideQuotes = options.AllowNewlinesInsideQuotes
        });

        int total = 0;
        foreach (var row in reader)
        {
            total += row.ColumnCount;
        }

        return total;
    }

    [Benchmark(Description = "HeroParser (UTF-8)")]
    public int HeroParser_ParseUtf8()
    {
        var options = new CsvParserOptions
        {
            MaxColumnCount = 1_000,
            MaxRowCount = 1_000_000,
            EnableQuotedFields = WithQuotes,
            AllowNewlinesInsideQuotes = WithQuotes
        };

        using var reader = Csv.ReadFromByteSpan(utf8, new()
        {
            MaxColumnCount = options.MaxColumnCount,
            MaxRowCount = options.MaxRowCount,
            EnableQuotedFields = options.EnableQuotedFields,
            AllowNewlinesInsideQuotes = options.AllowNewlinesInsideQuotes
        });

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
        Console.WriteLine();
        Console.WriteLine("=== Comparison Analysis ===");
        Console.WriteLine($"CSV size: {csv.Length:N0} chars ({csv.Length * 2:N0} bytes)");
        Console.WriteLine($"Rows: {Rows:N0}, Columns: {Columns}");
        Console.WriteLine($"HeroParser using: {Hardware.GetHardwareInfo()}");
        Console.WriteLine();
    }
}
