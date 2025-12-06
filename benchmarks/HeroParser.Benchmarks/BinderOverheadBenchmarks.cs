using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using HeroParser.SeparatedValues;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Reading.Records;
using HeroParser.SeparatedValues.Reading.Shared;
using System.Globalization;
using System.Text;

namespace HeroParser.Benchmarks;

/// <summary>
/// Benchmarks to profile where typed binder overhead comes from.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Throughput, iterationCount: 5, warmupCount: 3)]
public class BinderOverheadBenchmarks
{
    private string csv = null!;
    private CsvParserOptions options = null!;

    [Params(1000)]
    public int Rows { get; set; }

    // Pre-created options for typed binder comparison
    private CsvParserOptions typedParserOptions = null!;
    private CsvRecordOptions typedRecordOptions = null!;

    [GlobalSetup]
    public void Setup()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Name,Value,Description,Category");

        for (int i = 0; i < Rows; i++)
        {
            sb.AppendLine($"Item{i},{i},This is a description for item {i},Category{i % 10}");
        }

        csv = sb.ToString();
        options = new CsvParserOptions { MaxColumnCount = 10, MaxRowCount = Rows + 10 };

        // Pre-create the options so we can measure binding overhead without builder allocation
        typedParserOptions = new CsvParserOptions { MaxColumnCount = 10, MaxRowCount = Rows + 10 };
        typedRecordOptions = new CsvRecordOptions { HasHeaderRow = true };
    }

    // ============================================================
    // Baseline: Raw Parsing
    // ============================================================

    /// <summary>
    /// Raw parsing - just iterate rows, count columns.
    /// </summary>
    [Benchmark(Baseline = true)]
    public int RawParse_CountColumns()
    {
        using var reader = Csv.ReadFromText(csv, options);
        int total = 0;
        foreach (var row in reader)
        {
            total += row.ColumnCount;
        }
        return total;
    }

    // ============================================================
    // Component 1: Column Access Overhead
    // ============================================================

    /// <summary>
    /// Raw parsing + accessing 4 column spans (no string allocation).
    /// </summary>
    [Benchmark]
    public int RawParse_AccessSpans()
    {
        using var reader = Csv.ReadFromText(csv, options);
        int total = 0;
        foreach (var row in reader)
        {
            var s0 = row[0].Span;
            var s1 = row[1].Span;
            var s2 = row[2].Span;
            var s3 = row[3].Span;
            total += s0.Length + s1.Length + s2.Length + s3.Length;
        }
        return total;
    }

    // ============================================================
    // Component 2: String Allocation Overhead
    // ============================================================

    /// <summary>
    /// Raw parsing + creating 4 strings per row.
    /// </summary>
    [Benchmark]
    public int RawParse_CreateStrings()
    {
        using var reader = Csv.ReadFromText(csv, options);
        int total = 0;
        foreach (var row in reader)
        {
            var s0 = new string(row[0].Span);
            var s1 = new string(row[1].Span);
            var s2 = new string(row[2].Span);
            var s3 = new string(row[3].Span);
            total += s0.Length + s1.Length + s2.Length + s3.Length;
        }
        return total;
    }

    // ============================================================
    // Component 3: Object Allocation Overhead
    // ============================================================

    /// <summary>
    /// Raw parsing + creating new object per row (no property assignment).
    /// </summary>
    [Benchmark]
    public int RawParse_CreateObjects()
    {
        using var reader = Csv.ReadFromText(csv, options);
        int total = 0;
        foreach (var row in reader)
        {
            var obj = new SimpleRecord();
            total += row.ColumnCount;
        }
        return total;
    }

    // ============================================================
    // Component 4: Object + String Assignment
    // ============================================================

    /// <summary>
    /// Raw parsing + creating object + assigning 4 string properties.
    /// </summary>
    [Benchmark]
    public int RawParse_CreateObjectsWithStrings()
    {
        using var reader = Csv.ReadFromText(csv, options);
        int total = 0;
        foreach (var row in reader)
        {
            var obj = new SimpleRecord
            {
                Name = new string(row[0].Span),
                Value = new string(row[1].Span),
                Description = new string(row[2].Span),
                Category = new string(row[3].Span)
            };
            total += obj.Name.Length;
        }
        return total;
    }

    // ============================================================
    // Component 5: Object + String + Int Parsing
    // ============================================================

    /// <summary>
    /// Raw parsing + creating object + 3 strings + 1 int parse.
    /// </summary>
    [Benchmark]
    public int RawParse_WithIntParse()
    {
        using var reader = Csv.ReadFromText(csv, options);
        int total = 0;
        foreach (var row in reader)
        {
            int.TryParse(row[1].Span, NumberStyles.Integer, CultureInfo.InvariantCulture, out var val);
            var obj = new TypedRecord
            {
                Name = new string(row[0].Span),
                Value = val,
                Description = new string(row[2].Span),
                Category = new string(row[3].Span)
            };
            total += obj.Value;
        }
        return total;
    }

    // ============================================================
    // Component 6: Skip Header Manually
    // ============================================================

    /// <summary>
    /// Manual binding with header skip (to match typed binder behavior).
    /// </summary>
    [Benchmark]
    public int Manual_SkipHeader()
    {
        using var reader = Csv.ReadFromText(csv, options);
        int total = 0;
        bool isHeader = true;
        foreach (var row in reader)
        {
            if (isHeader)
            {
                isHeader = false;
                continue;
            }

            int.TryParse(row[1].Span, NumberStyles.Integer, CultureInfo.InvariantCulture, out var val);
            var obj = new TypedRecord
            {
                Name = new string(row[0].Span),
                Value = val,
                Description = new string(row[2].Span),
                Category = new string(row[3].Span)
            };
            total += obj.Value;
        }
        return total;
    }

    public class SimpleRecord
    {
        public string Name { get; set; } = "";
        public string Value { get; set; } = "";
        public string Description { get; set; } = "";
        public string Category { get; set; } = "";
    }

    // ============================================================
    // Component 7: TypedBinder with Pre-created Options (isolates binding)
    // ============================================================

    /// <summary>
    /// Using pre-created options to isolate binder performance from builder allocation.
    /// </summary>
    [Benchmark]
    public int TypedBinder_PreCreatedOptions()
    {
        int total = 0;
        foreach (var record in Csv.DeserializeRecords<TypedRecord>(csv, typedRecordOptions, typedParserOptions))
        {
            total += record.Value;
        }
        return total;
    }

    [CsvGenerateBinder]
    public class TypedRecord
    {
        public string Name { get; set; } = "";
        public int Value { get; set; }
        public string Description { get; set; } = "";
        public string Category { get; set; } = "";
    }
}
