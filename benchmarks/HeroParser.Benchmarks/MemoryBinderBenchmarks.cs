using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using HeroParser.SeparatedValues;
using HeroParser.SeparatedValues.Records.Binding;
using System.Text;

namespace HeroParser.Benchmarks;

/// <summary>
/// Benchmarks comparing string vs ReadOnlyMemory&lt;char&gt; binding performance.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Throughput, iterationCount: 5, warmupCount: 3)]
public class MemoryBinderBenchmarks
{
    private string csv = null!;
    private CsvParserOptions options = null!;

    [Params(1000)]
    public int Rows { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Name,Value,Description,Category");

        for (int i = 0; i < Rows; i++)
        {
            sb.AppendLine($"Item{i},Value{i},This is a description for item {i},Category{i % 10}");
        }

        csv = sb.ToString();
        options = new CsvParserOptions { MaxColumnCount = 10, MaxRowCount = Rows + 10 };
    }

    // ============================================================
    // Raw Parsing Benchmarks (no column access)
    // ============================================================

    /// <summary>
    /// Raw parsing with CsvCharSpanReader - just iterate rows.
    /// </summary>
    [Benchmark(Baseline = true)]
    public int RawParse_CharSpanReader()
    {
        using var reader = Csv.ReadFromText(csv, options);
        int total = 0;
        foreach (var row in reader)
        {
            total += row.ColumnCount;
        }
        return total;
    }

    /// <summary>
    /// Raw parsing with CsvMemoryReader - just iterate rows.
    /// </summary>
    [Benchmark]
    public int RawParse_MemoryReader()
    {
        var memory = csv.AsMemory();
        var reader = new CsvMemoryReader(memory, options);
        int total = 0;
        foreach (var row in reader)
        {
            total += row.ColumnCount;
        }
        return total;
    }

    // ============================================================
    // Column Access Benchmarks (no string allocation)
    // ============================================================

    /// <summary>
    /// Access column spans with CsvCharSpanReader.
    /// </summary>
    [Benchmark]
    public int AccessSpans_CharSpanReader()
    {
        using var reader = Csv.ReadFromText(csv, options);
        int total = 0;
        foreach (var row in reader)
        {
            var name = row[0].CharSpan;
            var value = row[1].CharSpan;
            var description = row[2].CharSpan;
            var category = row[3].CharSpan;
            total += name.Length + value.Length + description.Length + category.Length;
        }
        return total;
    }

    /// <summary>
    /// Access column memory with CsvMemoryReader.
    /// </summary>
    [Benchmark]
    public int AccessMemory_MemoryReader()
    {
        var memory = csv.AsMemory();
        var reader = new CsvMemoryReader(memory, options);
        int total = 0;
        foreach (var row in reader)
        {
            var name = row.GetColumnMemory(0);
            var value = row.GetColumnMemory(1);
            var description = row.GetColumnMemory(2);
            var category = row.GetColumnMemory(3);
            total += name.Length + value.Length + description.Length + category.Length;
        }
        return total;
    }

    // ============================================================
    // String Materialization Benchmarks
    // ============================================================

    /// <summary>
    /// Materialize strings from CsvCharSpanReader.
    /// </summary>
    [Benchmark]
    public int MaterializeStrings_CharSpanReader()
    {
        using var reader = Csv.ReadFromText(csv, options);
        int total = 0;
        foreach (var row in reader)
        {
            var name = new string(row[0].CharSpan);
            var value = new string(row[1].CharSpan);
            var description = new string(row[2].CharSpan);
            var category = new string(row[3].CharSpan);
            total += name.Length + value.Length + description.Length + category.Length;
        }
        return total;
    }

    /// <summary>
    /// Materialize strings from CsvMemoryReader.
    /// </summary>
    [Benchmark]
    public int MaterializeStrings_MemoryReader()
    {
        var memory = csv.AsMemory();
        var reader = new CsvMemoryReader(memory, options);
        int total = 0;
        foreach (var row in reader)
        {
            var name = new string(row.GetColumnSpan(0));
            var value = new string(row.GetColumnSpan(1));
            var description = new string(row.GetColumnSpan(2));
            var category = new string(row.GetColumnSpan(3));
            total += name.Length + value.Length + description.Length + category.Length;
        }
        return total;
    }
}
