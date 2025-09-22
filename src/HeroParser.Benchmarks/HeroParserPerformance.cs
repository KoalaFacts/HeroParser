using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using System.Text;
using HeroParser.Configuration;

namespace HeroParser.Benchmarks;

/// <summary>
/// Performance benchmark focused on HeroParser's internal optimizations.
/// Tests different parsing paths, configurations, and edge cases.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class HeroParserPerformance
{
    private string _simpleCsv = string.Empty;      // No quotes, basic delimiters
    private string _quotedCsv = string.Empty;      // Contains quoted fields
    private string _escapedCsv = string.Empty;     // Contains escaped quotes
    private string _multilineCsv = string.Empty;   // Contains newlines in fields

    [GlobalSetup]
    public void Setup()
    {
        // Simple CSV - triggers fast path
        _simpleCsv = GenerateSimpleCsv(10_000, 10);

        // Quoted CSV - triggers complex path
        _quotedCsv = GenerateQuotedCsv(10_000, 10);

        // Escaped CSV - stress tests quote handling
        _escapedCsv = GenerateEscapedCsv(10_000, 10);

        // Multiline CSV - tests field parsing with embedded newlines
        _multilineCsv = GenerateMultilineCsv(10_000, 10);
    }

    private static string GenerateSimpleCsv(int rows, int columns)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(",", Enumerable.Range(0, columns).Select(i => $"Col{i}")));

        for (int row = 0; row < rows; row++)
        {
            builder.AppendLine(string.Join(",", Enumerable.Range(0, columns).Select(c => $"R{row}C{c}")));
        }
        return builder.ToString();
    }

    private static string GenerateQuotedCsv(int rows, int columns)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(",", Enumerable.Range(0, columns).Select(i => $"\"Column {i}\"")));

        for (int row = 0; row < rows; row++)
        {
            var values = Enumerable.Range(0, columns).Select(c =>
                c % 2 == 0 ? $"\"Value {row},{c}\"" : $"Simple{row}_{c}");
            builder.AppendLine(string.Join(",", values));
        }
        return builder.ToString();
    }

    private static string GenerateEscapedCsv(int rows, int columns)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(",", Enumerable.Range(0, columns).Select(i => $"Col{i}")));

        for (int row = 0; row < rows; row++)
        {
            var values = Enumerable.Range(0, columns).Select(c =>
                c % 3 == 0 ? $"\"Value with \"\"quotes\"\" {row}\"" : $"Simple{row}_{c}");
            builder.AppendLine(string.Join(",", values));
        }
        return builder.ToString();
    }

    private static string GenerateMultilineCsv(int rows, int columns)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(",", Enumerable.Range(0, columns).Select(i => $"Col{i}")));

        for (int row = 0; row < rows; row++)
        {
            var values = Enumerable.Range(0, columns).Select(c =>
                c % 4 == 0 ? $"\"Line1\nLine2\nLine3\"" : $"Simple{row}_{c}");
            builder.AppendLine(string.Join(",", values));
        }
        return builder.ToString();
    }

    // ===============================
    // Test different parsing paths
    // ===============================

    [Benchmark(Baseline = true)]
    public string[][] SimpleCsv_FastPath()
    {
        // Should trigger SIMD fast path (no quotes)
        return Csv.ParseString(_simpleCsv);
    }

    [Benchmark]
    public string[][] QuotedCsv_ComplexPath()
    {
        // Should trigger complex parsing path
        return Csv.ParseString(_quotedCsv);
    }

    [Benchmark]
    public string[][] EscapedCsv_StressTest()
    {
        // Tests escaped quote handling
        return Csv.ParseString(_escapedCsv);
    }

    [Benchmark]
    public string[][] MultilineCsv_EdgeCase()
    {
        // Tests multiline field handling
        return Csv.ParseString(_multilineCsv);
    }

    // ===============================
    // Test configuration impact
    // ===============================

    [Benchmark]
    public string[][] WithTrimming_Enabled()
    {
        var config = CsvReadConfiguration.Default with { TrimValues = true };
        return Csv.ParseString(_simpleCsv, config);
    }

    [Benchmark]
    public string[][] WithStrictMode_Enabled()
    {
        var config = CsvReadConfiguration.Default with { StrictMode = true };
        return Csv.ParseString(_simpleCsv, config);
    }

    [Benchmark]
    public string[][] WithCustomDelimiter()
    {
        var csvWithTabs = _simpleCsv.Replace(',', '\t');
        var config = CsvReadConfiguration.Default with { Delimiter = '\t' };
        return Csv.ParseString(csvWithTabs, config);
    }

    // ===============================
    // Memory allocation patterns
    // ===============================

    [Benchmark]
    public void StreamingEnumeration()
    {
        // Tests lazy enumeration (no ToArray/ToList)
        foreach (var row in Csv.FromString(_simpleCsv))
        {
            // Just enumerate, don't store
            _ = row.Length;
        }
    }

    [Benchmark]
    public List<string[]> ToList_Allocation()
    {
        return Csv.FromString(_simpleCsv).ToList();
    }

    [Benchmark]
    public string[][] ToArray_Allocation()
    {
        return Csv.FromString(_simpleCsv).ToArray();
    }
}