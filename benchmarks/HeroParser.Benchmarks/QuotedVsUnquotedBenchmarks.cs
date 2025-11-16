using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using System.Text;

namespace HeroParser.Benchmarks;

/// <summary>
/// Benchmark to verify SIMD quote handling performance.
/// Compares unquoted vs quoted CSV to measure the overhead of quote processing.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Throughput, iterationCount: 15, warmupCount: 3)]
public class QuotedVsUnquotedBenchmarks
{
    private string _unquotedCsv = null!;
    private string _quotedCsv = null!;
    private string _mixedCsv = null!;

    [Params(10_000, 100_000)]
    public int Rows { get; set; }

    [Params(10, 50)]
    public int Columns { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _unquotedCsv = GenerateUnquotedCsv(Rows, Columns);
        _quotedCsv = GenerateQuotedCsv(Rows, Columns);
        _mixedCsv = GenerateMixedCsv(Rows, Columns);
    }

    /// <summary>
    /// Baseline: unquoted CSV (pure SIMD fast path)
    /// </summary>
    [Benchmark(Baseline = true, Description = "Unquoted CSV")]
    public int Parse_Unquoted()
    {
        var reader = Csv.Parse(_unquotedCsv);
        int total = 0;
        foreach (var row in reader)
        {
            total += row.Count;
        }
        return total;
    }

    /// <summary>
    /// Quoted CSV with delimiters inside quotes (tests quote-aware SIMD)
    /// </summary>
    [Benchmark(Description = "Quoted CSV (delimiters in quotes)")]
    public int Parse_Quoted()
    {
        var reader = Csv.Parse(_quotedCsv);
        int total = 0;
        foreach (var row in reader)
        {
            total += row.Count;
        }
        return total;
    }

    /// <summary>
    /// Mixed: some quoted, some unquoted (realistic scenario)
    /// </summary>
    [Benchmark(Description = "Mixed CSV (50% quoted)")]
    public int Parse_Mixed()
    {
        var reader = Csv.Parse(_mixedCsv);
        int total = 0;
        foreach (var row in reader)
        {
            total += row.Count;
        }
        return total;
    }

    /// <summary>
    /// Generate unquoted CSV (no quotes at all)
    /// Example: value_0_0,value_0_1,value_0_2
    /// </summary>
    private static string GenerateUnquotedCsv(int rows, int columns)
    {
        var sb = new StringBuilder();
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                if (c > 0) sb.Append(',');
                sb.Append($"value_{r}_{c}");
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    /// <summary>
    /// Generate quoted CSV with delimiters inside quotes
    /// Example: "value,0,0","value,0,1","value,0,2"
    /// This tests the quote-aware SIMD parser
    /// </summary>
    private static string GenerateQuotedCsv(int rows, int columns)
    {
        var sb = new StringBuilder();
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                if (c > 0) sb.Append(',');
                // Add delimiters inside quotes to force quote parsing
                sb.Append($"\"value,{r},{c}\"");
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    /// <summary>
    /// Generate mixed CSV (50% quoted, 50% unquoted)
    /// Example: value_0_0,"value,0,1",value_0_2,"value,0,3"
    /// Realistic scenario
    /// </summary>
    private static string GenerateMixedCsv(int rows, int columns)
    {
        var sb = new StringBuilder();
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                if (c > 0) sb.Append(',');
                // Alternate quoted and unquoted
                if (c % 2 == 0)
                {
                    sb.Append($"value_{r}_{c}");
                }
                else
                {
                    sb.Append($"\"value,{r},{c}\"");
                }
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        Console.WriteLine();
        Console.WriteLine("=== Quote Performance Analysis ===");
        Console.WriteLine($"Hardware: {HeroParser.Simd.SimdParserFactory.GetHardwareInfo()}");
        Console.WriteLine($"Test size: {Rows:N0} rows × {Columns} columns");
        Console.WriteLine($"Unquoted CSV: {_unquotedCsv.Length:N0} chars ({_unquotedCsv.Length * 2:N0} bytes)");
        Console.WriteLine($"Quoted CSV:   {_quotedCsv.Length:N0} chars ({_quotedCsv.Length * 2:N0} bytes)");
        Console.WriteLine($"Mixed CSV:    {_mixedCsv.Length:N0} chars ({_mixedCsv.Length * 2:N0} bytes)");
        Console.WriteLine();
        Console.WriteLine("Expected results if quote-aware SIMD works:");
        Console.WriteLine("  - Unquoted should be fastest (baseline)");
        Console.WriteLine("  - Quoted should be only slightly slower (<20% overhead)");
        Console.WriteLine("  - Mixed should be between the two");
        Console.WriteLine();
        Console.WriteLine("If quoted is much slower (>50% overhead), SIMD quote handling has issues.");
        Console.WriteLine();
    }
}
