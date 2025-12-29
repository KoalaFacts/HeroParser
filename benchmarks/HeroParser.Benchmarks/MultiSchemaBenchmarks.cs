using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using HeroParser.SeparatedValues;
using HeroParser.SeparatedValues.Reading.Records.MultiSchema;
using HeroParser.SeparatedValues.Reading.Rows;
using HeroParser.SeparatedValues.Reading.Shared;
using System.Text;

namespace HeroParser.Benchmarks;

/// <summary>
/// Benchmarks to measure multi-schema parsing overhead vs single-schema.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Throughput, iterationCount: 5, warmupCount: 3)]
public class MultiSchemaBenchmarks
{
    private string singleTypeCsv = null!;
    private string multiTypeCsv = null!;
    private string multiTypeCsvThreeChar = null!;

    [Params(1000, 10000)]
    public int Rows { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        // Single-type CSV (all same record type)
        var sb = new StringBuilder();
        sb.AppendLine("Type,Name,Value,Description");
        for (int i = 0; i < Rows; i++)
        {
            sb.AppendLine($"D,Item{i},{i},Description{i}");
        }
        singleTypeCsv = sb.ToString();

        // Multi-type CSV (H/D/T pattern - common in banking)
        sb = new StringBuilder();
        sb.AppendLine("Type,Data1,Data2,Data3");
        // 1 header
        sb.AppendLine("H,FileId,2024-01-01,");
        // N-2 details
        for (int i = 0; i < Rows - 2; i++)
        {
            sb.AppendLine($"D,Item{i},{i * 10.5m},Desc{i}");
        }
        // 1 trailer
        sb.AppendLine($"T,{Rows - 2},{(Rows - 2) * 10.5m},");
        multiTypeCsv = sb.ToString();

        // Multi-type CSV with 3-char discriminators (HDR/DTL/TRL)
        sb = new StringBuilder();
        sb.AppendLine("Type,Data1,Data2,Data3");
        sb.AppendLine("HDR,FileId,2024-01-01,");
        for (int i = 0; i < Rows - 2; i++)
        {
            sb.AppendLine($"DTL,Item{i},{i * 10.5m},Desc{i}");
        }
        sb.AppendLine($"TRL,{Rows - 2},{(Rows - 2) * 10.5m},");
        multiTypeCsvThreeChar = sb.ToString();
    }

    // ============================================================
    // Baseline: Single-Schema Typed Binder
    // ============================================================

    [Benchmark(Baseline = true)]
    public int SingleSchema_TypedBinder()
    {
        int total = 0;
        foreach (var record in Csv.DeserializeRecords<SingleTypeRecord>(singleTypeCsv))
        {
            total += record.Value;
        }
        return total;
    }

    // ============================================================
    // Multi-Schema: Single Char Discriminator (Fast Path)
    // ============================================================

    [Benchmark]
    public int MultiSchema_SingleCharDiscriminator()
    {
        int total = 0;
        foreach (var record in Csv.Read()
            .WithMultiSchema()
            .WithDiscriminator("Type")
            .MapRecord<HeaderRecord>("H")
            .MapRecord<DetailRecord>("D")
            .MapRecord<TrailerRecord>("T")
            .AllowMissingColumns()
            .FromText(multiTypeCsv))
        {
            if (record is DetailRecord d)
                total += (int)d.Amount;
        }
        return total;
    }

    // ============================================================
    // Multi-Schema: Multi Char Discriminator (Still Fast Path)
    // ============================================================

    [Benchmark]
    public int MultiSchema_ThreeCharDiscriminator()
    {
        // Use 3-char discriminators (HDR, DTL, TRL) - tests packed key with multiple chars
        int total = 0;
        foreach (var record in Csv.Read()
            .WithMultiSchema()
            .WithDiscriminator("Type")
            .MapRecord<HeaderRecord>("HDR")
            .MapRecord<DetailRecord>("DTL")
            .MapRecord<TrailerRecord>("TRL")
            .AllowMissingColumns()
            .FromText(multiTypeCsvThreeChar))
        {
            if (record is DetailRecord d)
                total += (int)d.Amount;
        }
        return total;
    }

    // ============================================================
    // Multi-Schema: Index-based Discriminator (No Header Lookup)
    // ============================================================

    [Benchmark]
    public int MultiSchema_IndexBasedDiscriminator()
    {
        int total = 0;
        foreach (var record in Csv.Read()
            .WithMultiSchema()
            .WithDiscriminator(0) // Column index instead of name
            .MapRecord<HeaderRecord>("H")
            .MapRecord<DetailRecord>("D")
            .MapRecord<TrailerRecord>("T")
            .AllowMissingColumns()
            .FromText(multiTypeCsv))
        {
            if (record is DetailRecord d)
                total += (int)d.Amount;
        }
        return total;
    }

    // ============================================================
    // Source-Generated Dispatcher (Optimal)
    // ============================================================

    [Benchmark]
    public int MultiSchema_SourceGenerated()
    {
        int total = 0;
        int rowNumber = 0;

        // Skip header row
        var reader = Csv.Read().FromText(multiTypeCsv);
        if (reader.MoveNext())
            rowNumber++; // header

        while (reader.MoveNext())
        {
            rowNumber++;
            var record = BankingDispatcher.Dispatch(reader.Current, rowNumber);
            if (record is DetailRecord d)
                total += (int)d.Amount;
        }
        return total;
    }

    // ============================================================
    // Record Types
    // ============================================================

    [CsvGenerateBinder]
    public class SingleTypeRecord
    {
        public string Type { get; set; } = "";
        public string Name { get; set; } = "";
        public int Value { get; set; }
        public string Description { get; set; } = "";
    }

    [CsvGenerateBinder]
    public class HeaderRecord
    {
        [CsvColumn(Name = "Data1")]
        public string FileId { get; set; } = "";

        [CsvColumn(Name = "Data2")]
        public string Date { get; set; } = "";
    }

    [CsvGenerateBinder]
    public class DetailRecord
    {
        [CsvColumn(Name = "Data1")]
        public string ItemId { get; set; } = "";

        [CsvColumn(Name = "Data2")]
        public decimal Amount { get; set; }

        [CsvColumn(Name = "Data3")]
        public string Description { get; set; } = "";
    }

    [CsvGenerateBinder]
    public class TrailerRecord
    {
        [CsvColumn(Name = "Data1")]
        public int RecordCount { get; set; }

        [CsvColumn(Name = "Data2")]
        public decimal TotalAmount { get; set; }
    }

}

// ============================================================
// Source-Generated Dispatcher
// ============================================================

/// <summary>
/// Source-generated multi-schema dispatcher for optimal performance.
/// Uses switch expressions compiled to jump tables - no dictionary lookups.
/// All methods are auto-generated from [CsvSchemaMapping] attributes.
/// </summary>
[CsvGenerateDispatcher(DiscriminatorIndex = 0)]
[CsvSchemaMapping("H", typeof(MultiSchemaBenchmarks.HeaderRecord))]
[CsvSchemaMapping("D", typeof(MultiSchemaBenchmarks.DetailRecord))]
[CsvSchemaMapping("T", typeof(MultiSchemaBenchmarks.TrailerRecord))]
public partial class BankingDispatcher { }
