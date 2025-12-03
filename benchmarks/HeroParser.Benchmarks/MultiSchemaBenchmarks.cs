using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using HeroParser.SeparatedValues;
using HeroParser.SeparatedValues.Records;
using HeroParser.SeparatedValues.Records.Binding;
using HeroParser.SeparatedValues.Records.MultiSchema;
using System.Text;

namespace HeroParser.Benchmarks;

/// <summary>
/// Benchmarks to measure multi-schema reading overhead compared to single-schema.
/// Validates that discriminator-based type routing has acceptable performance.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Throughput, iterationCount: 5, warmupCount: 3)]
public class MultiSchemaBenchmarks
{
    private string multiSchemaCsv = null!;
    private string singleSchemaCsv = null!;
    private CsvParserOptions options = null!;

    [Params(1_000, 10_000, 100_000)]
    public int Rows { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        multiSchemaCsv = GenerateMultiSchemaCsv(Rows);
        singleSchemaCsv = GenerateSingleSchemaCsv(Rows);
        options = new CsvParserOptions
        {
            MaxColumnCount = 10,
            MaxRowCount = Rows + 100
        };
        Console.WriteLine($"Hardware: {Hardware.GetHardwareInfo()}");
        Console.WriteLine($"Multi-Schema CSV Size: {multiSchemaCsv.Length:N0} chars");
        Console.WriteLine($"Single-Schema CSV Size: {singleSchemaCsv.Length:N0} chars");
    }

    private static string GenerateMultiSchemaCsv(int rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Type,Id,Amount,Date,Count");

        // Pattern: 1 Header, N-2 Details, 1 Trailer
        sb.AppendLine("H,0,0.00,2024-01-01,0");

        for (int i = 1; i < rows - 1; i++)
        {
            sb.AppendLine($"D,{i},{i * 10.50m:F2},,0");
        }

        sb.AppendLine($"T,0,{(rows - 2) * 10.50m:F2},,{rows - 2}");

        return sb.ToString();
    }

    private static string GenerateSingleSchemaCsv(int rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Type,Id,Amount,Date,Count");

        for (int i = 0; i < rows; i++)
        {
            sb.AppendLine($"D,{i},{i * 10.50m:F2},,0");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Baseline: Raw parsing without record binding.
    /// </summary>
    [Benchmark(Baseline = true)]
    public int RawParsing()
    {
        using var reader = Csv.ReadFromText(multiSchemaCsv, options);
        int total = 0;
        foreach (var row in reader)
        {
            total += row.ColumnCount;
        }
        return total;
    }

    /// <summary>
    /// Single-schema parsing with typed binding.
    /// </summary>
    [Benchmark]
    public int SingleSchemaTyped()
    {
        int total = 0;
        foreach (var record in Csv.Read<DetailRecord>().FromText(singleSchemaCsv))
        {
            total += record.Id;
        }
        return total;
    }

    /// <summary>
    /// Multi-schema parsing with 3 record types.
    /// </summary>
    [Benchmark]
    public int MultiSchemaThreeTypes()
    {
        int total = 0;
        foreach (var record in Csv.Read()
            .WithMultiSchema()
            .WithDiscriminator("Type")
            .MapRecord<HeaderRecord>("H")
            .MapRecord<DetailRecord>("D")
            .MapRecord<TrailerRecord>("T")
            .AllowMissingColumns()
            .FromText(multiSchemaCsv))
        {
            switch (record)
            {
                case HeaderRecord:
                    total += 1;
                    break;
                case DetailRecord d:
                    total += d.Id;
                    break;
                case TrailerRecord t:
                    total += t.Count;
                    break;
                default:
                    break;
            }
        }
        return total;
    }

    /// <summary>
    /// Multi-schema with single type (tests discriminator overhead only).
    /// </summary>
    [Benchmark]
    public int MultiSchemaSingleType()
    {
        int total = 0;
        foreach (var record in Csv.Read()
            .WithMultiSchema()
            .WithDiscriminator("Type")
            .MapRecord<DetailRecord>("D")
            .OnUnmatchedRow(UnmatchedRowBehavior.Skip)
            .AllowMissingColumns()
            .FromText(multiSchemaCsv))
        {
            if (record is DetailRecord d)
            {
                total += d.Id;
            }
        }
        return total;
    }

    /// <summary>
    /// Multi-schema with fallback factory (tests factory overhead).
    /// </summary>
    [Benchmark]
    public int MultiSchemaWithFallback()
    {
        int total = 0;
        foreach (var record in Csv.Read()
            .WithMultiSchema()
            .WithDiscriminator("Type")
            .MapRecord<DetailRecord>("D")
            .MapRecord((discriminator, columns, rowNum) => new UnknownRecord { Type = discriminator })
            .AllowMissingColumns()
            .FromText(multiSchemaCsv))
        {
            switch (record)
            {
                case DetailRecord d:
                    total += d.Id;
                    break;
                case UnknownRecord:
                    total += 1;
                    break;
                default:
                    break;
            }
        }
        return total;
    }

    #region Record Types

    [CsvGenerateBinder]
    public class HeaderRecord
    {
        [CsvColumn(Name = "Type")]
        public string Type { get; set; } = "";

        [CsvColumn(Name = "Date")]
        public string Date { get; set; } = "";
    }

    [CsvGenerateBinder]
    public class DetailRecord
    {
        [CsvColumn(Name = "Type")]
        public string Type { get; set; } = "";

        [CsvColumn(Name = "Id")]
        public int Id { get; set; }

        [CsvColumn(Name = "Amount")]
        public decimal Amount { get; set; }
    }

    [CsvGenerateBinder]
    public class TrailerRecord
    {
        [CsvColumn(Name = "Type")]
        public string Type { get; set; } = "";

        [CsvColumn(Name = "Count")]
        public int Count { get; set; }
    }

    public class UnknownRecord
    {
        public string Type { get; set; } = "";
    }

    #endregion
}
