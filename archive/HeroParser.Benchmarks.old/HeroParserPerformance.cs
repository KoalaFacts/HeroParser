using BenchmarkDotNet.Attributes;
using HeroParser.Configuration;
using System.Text;

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
        return Csv.ParseContent(_simpleCsv);
    }

    [Benchmark]
    public string[][] QuotedCsv_ComplexPath()
    {
        // Should trigger complex parsing path
        return Csv.ParseContent(_quotedCsv);
    }

    [Benchmark]
    public string[][] EscapedCsv_StressTest()
    {
        // Tests escaped quote handling
        return Csv.ParseContent(_escapedCsv);
    }

    [Benchmark]
    public string[][] MultilineCsv_EdgeCase()
    {
        // Tests multiline field handling
        return Csv.ParseContent(_multilineCsv);
    }

    // ===============================
    // Test configuration impact
    // ===============================

    [Benchmark]
    public string[][] WithTrimming_Enabled()
    {
        return [.. Csv.Configure()
            .WithContent(_simpleCsv)
            .TrimValues(true)
            .Build()
            .ReadAll()];
    }

    [Benchmark]
    public string[][] WithStrictMode_Enabled()
    {
        return [.. Csv.Configure()
            .WithContent(_simpleCsv)
            .StrictMode(true)
            .Build()
            .ReadAll()];
    }

    [Benchmark]
    public string[][] WithCustomDelimiter()
    {
        var csvWithTabs = _simpleCsv.Replace(',', '\t');
        return [.. Csv.Configure()
            .WithContent(csvWithTabs)
            .WithDelimiter('\t')
            .Build()
            .ReadAll()];
    }

    // ===============================
    // Memory allocation patterns
    // ===============================

    [Benchmark]
    public async Task<int> StreamingEnumeration()
    {
        // Tests lazy enumeration (no ToArray/ToList)
        var rows = await Csv.FromContent(_simpleCsv);
        int totalFields = 0;
        foreach (var row in rows)
        {
            // Count fields to prevent optimization
            totalFields += row.Length;
        }
        return totalFields;
    }

    [Benchmark]
    public async Task<List<string[]>> ToList_Allocation()
    {
        var rows = await Csv.FromContent(_simpleCsv);
        return [.. rows];
    }

    [Benchmark]
    public async Task<string[][]> ToArray_Allocation()
    {
        return await Csv.FromContent(_simpleCsv);
    }
}