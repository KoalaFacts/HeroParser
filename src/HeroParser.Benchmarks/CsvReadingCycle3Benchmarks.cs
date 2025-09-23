using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using HeroParser.Configuration;
using System.Text;

namespace HeroParser.Benchmarks;

/// <summary>
/// F1 Cycle 3 SIMD validation benchmark for CSV reading performance.
/// Tests SIMD optimizations against constitutional targets and competitive baselines.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 5, iterationCount: 15)]
[MinColumn, MaxColumn, MeanColumn, MedianColumn, StdDevColumn]
public class CsvReadingCycle3Benchmarks
{
    private string _simpleCsv = string.Empty;       // No quotes, SIMD fast path
    private string _complexCsv = string.Empty;      // Quotes and escapes, complex path
    private string _largeCsv = string.Empty;        // Large dataset for throughput testing
    private string _multilineCsv = string.Empty;    // Multiline fields testing

    [GlobalSetup]
    public void Setup()
    {
        Console.WriteLine("=== F1 Cycle 3 SIMD Validation Benchmark ===");
        Console.WriteLine("Testing HeroParser SIMD optimizations against constitutional targets");
        Console.WriteLine();

        // Simple CSV - should trigger SIMD fast path
        _simpleCsv = GenerateSimpleCsv(10_000, 10);

        // Complex CSV - mixed quotes and escapes
        _complexCsv = GenerateComplexCsv(10_000, 10);

        // Large CSV - constitutional throughput testing
        _largeCsv = GenerateLargeCsv(100_000, 20);

        // Multiline CSV - edge case handling
        _multilineCsv = GenerateMultilineCsv(5_000, 8);

        Console.WriteLine($"Test Data Generated:");
        Console.WriteLine($"  Simple CSV: {_simpleCsv.Length / 1024.0:F1} KB (SIMD fast path)");
        Console.WriteLine($"  Complex CSV: {_complexCsv.Length / 1024.0:F1} KB (quotes/escapes)");
        Console.WriteLine($"  Large CSV: {_largeCsv.Length / 1024.0 / 1024.0:F1} MB (throughput test)");
        Console.WriteLine($"  Multiline CSV: {_multilineCsv.Length / 1024.0:F1} KB (edge cases)");
        Console.WriteLine();

        // Hardware capability reporting
        Console.WriteLine("Hardware Capabilities:");
#if NET6_0_OR_GREATER
        Console.WriteLine($"  SIMD Support: {(Core.SpanOperations.HardwareCapabilities.SupportsAvx2 ? "AVX2" : Core.SpanOperations.HardwareCapabilities.SupportsSse2 ? "SSE2" : "None")}");
        Console.WriteLine($"  Vector Size: {Core.SpanOperations.HardwareCapabilities.OptimalVectorSize} bytes");
#else
        Console.WriteLine($"  SIMD Support: Framework fallback (no SIMD)");
#endif
        Console.WriteLine();
    }

    // ====================================
    // CONSTITUTIONAL TARGET BENCHMARKS
    // ====================================

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Constitutional")]
    public string[][] Constitutional_Target_SimpleCsv()
    {
        // Target: >25 GB/s single-threaded (constitutional requirement)
        // This benchmark measures our progress toward the constitutional target
        return Csv.ParseContent(_simpleCsv);
    }

    [Benchmark]
    [BenchmarkCategory("Constitutional")]
    public string[][] Constitutional_Target_LargeCsv()
    {
        // Large dataset throughput test for constitutional compliance
        return Csv.ParseContent(_largeCsv);
    }

    // ====================================
    // SIMD OPTIMIZATION VALIDATION
    // ====================================

    [Benchmark]
    [BenchmarkCategory("SIMD")]
    public string[][] SIMD_FastPath_SimpleData()
    {
        // Should utilize SIMD for delimiter scanning
        // No quotes or escapes - pure fast path
        return Csv.ParseContent(_simpleCsv);
    }

    [Benchmark]
    [BenchmarkCategory("SIMD")]
    public string[][] SIMD_ComplexPath_QuotedData()
    {
        // Tests SIMD with quoted fields and escapes
        // Should still benefit from SIMD where possible
        return Csv.ParseContent(_complexCsv);
    }

    [Benchmark]
    [BenchmarkCategory("SIMD")]
    public string[][] SIMD_EdgeCase_MultilineFields()
    {
        // Tests SIMD with multiline fields
        // Edge case for newline detection
        return Csv.ParseContent(_multilineCsv);
    }

    // ====================================
    // CONFIGURATION IMPACT TESTING
    // ====================================

    [Benchmark]
    [BenchmarkCategory("Configuration")]
    public string[][] Config_TrimEnabled()
    {
        return Csv.Configure()
            .WithContent(_simpleCsv)
            .TrimValues(true)
            .Build()
            .ReadAll().ToArray();
    }

    [Benchmark]
    [BenchmarkCategory("Configuration")]
    public string[][] Config_StrictMode()
    {
        return Csv.Configure()
            .WithContent(_simpleCsv)
            .StrictMode(true)
            .Build()
            .ReadAll().ToArray();
    }

    [Benchmark]
    [BenchmarkCategory("Configuration")]
    public string[][] Config_CustomDelimiter()
    {
        var tsvData = _simpleCsv.Replace(',', '\t');
        return Csv.Configure()
            .WithContent(tsvData)
            .WithDelimiter('\t')
            .Build()
            .ReadAll().ToArray();
    }

    // ====================================
    // MEMORY ALLOCATION TESTING
    // ====================================

    [Benchmark]
    [BenchmarkCategory("Memory")]
    public async Task Memory_StreamingEnumeration()
    {
        // Zero-allocation streaming test
        var rows = await Csv.FromContent(_simpleCsv);
        foreach (var row in rows)
        {
            // Enumerate without storing - tests allocation behavior
            _ = row.Length;
        }
    }

    [Benchmark]
    [BenchmarkCategory("Memory")]
    public async Task<List<string[]>> Memory_MaterializedList()
    {
        // Test memory allocation when materializing results
        var rows = await Csv.FromContent(_simpleCsv);
        return rows.ToList();
    }

    // ====================================
    // DATA GENERATION METHODS
    // ====================================

    private static string GenerateSimpleCsv(int rows, int columns)
    {
        var builder = new StringBuilder();

        // Header
        builder.AppendLine(string.Join(",", Enumerable.Range(0, columns).Select(i => $"Col{i}")));

        // Data rows - no quotes, optimal for SIMD
        for (int row = 0; row < rows; row++)
        {
            var values = Enumerable.Range(0, columns).Select(col => $"Val{row}C{col}");
            builder.AppendLine(string.Join(",", values));
        }

        return builder.ToString();
    }

    private static string GenerateComplexCsv(int rows, int columns)
    {
        var builder = new StringBuilder();

        // Header with some quoted fields
        var headers = Enumerable.Range(0, columns).Select(i =>
            i % 3 == 0 ? $"\"Header {i}\"" : $"Header{i}");
        builder.AppendLine(string.Join(",", headers));

        // Data rows with mixed complexity
        for (int row = 0; row < rows; row++)
        {
            var values = new List<string>();
            for (int col = 0; col < columns; col++)
            {
                if (col % 4 == 0)
                {
                    // Quoted field with comma
                    values.Add($"\"Value {row}, Column {col}\"");
                }
                else if (col % 4 == 1)
                {
                    // Quoted field with escaped quotes
                    values.Add($"\"Value with \"\"quotes\"\" {row}\"");
                }
                else if (col % 4 == 2)
                {
                    // Simple field
                    values.Add($"Simple{row}_{col}");
                }
                else
                {
                    // Numeric field
                    values.Add($"{row * 100 + col}");
                }
            }
            builder.AppendLine(string.Join(",", values));
        }

        return builder.ToString();
    }

    private static string GenerateLargeCsv(int rows, int columns)
    {
        var builder = new StringBuilder();

        // Header
        builder.AppendLine(string.Join(",", Enumerable.Range(0, columns).Select(i => $"Column{i}")));

        // Large dataset for throughput testing
        for (int row = 0; row < rows; row++)
        {
            var values = new List<string>();
            for (int col = 0; col < columns; col++)
            {
                // Mix of field types for realistic testing
                switch (col % 6)
                {
                    case 0: values.Add($"Employee{row:D6}"); break;
                    case 1: values.Add($"{25 + (row % 40)}"); break;
                    case 2: values.Add($"Department{row % 20}"); break;
                    case 3: values.Add($"{35000 + (row * 100)}"); break;
                    case 4: values.Add($"2020-{1 + (row % 12):D2}-{1 + (row % 28):D2}"); break;
                    case 5: values.Add($"{(row % 2 == 0 ? "true" : "false")}"); break;
                }
            }
            builder.AppendLine(string.Join(",", values));
        }

        return builder.ToString();
    }

    private static string GenerateMultilineCsv(int rows, int columns)
    {
        var builder = new StringBuilder();

        // Header
        builder.AppendLine(string.Join(",", Enumerable.Range(0, columns).Select(i => $"Col{i}")));

        // Data with multiline fields
        for (int row = 0; row < rows; row++)
        {
            var values = new List<string>();
            for (int col = 0; col < columns; col++)
            {
                if (col % 5 == 0)
                {
                    // Multiline field
                    values.Add($"\"Line1 for {row}\\nLine2 for {row}\\nLine3 for {row}\"");
                }
                else
                {
                    // Regular field
                    values.Add($"Field{row}_{col}");
                }
            }
            builder.AppendLine(string.Join(",", values));
        }

        return builder.ToString();
    }
}