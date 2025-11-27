using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using HeroParser.SeparatedValues.Writing;

namespace HeroParser.Benchmarks;

/// <summary>
/// Benchmarks for CSV writing performance.
/// Measures throughput and allocations of the CsvStreamWriter.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Throughput, iterationCount: 5, warmupCount: 3)]
public class WriterBenchmarks
{
    private TestRecord[] records = null!;
    private string[] stringValues = null!;

    [Params(1_000, 10_000, 100_000)]
    public int Rows { get; set; }

    [Params(10, 25)]
    public int Columns { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        // Generate test data
        records = GenerateRecords(Rows);

        // Generate string values for raw writing tests
        stringValues = GenerateStrings(Columns);

        Console.WriteLine($"Records: {Rows:N0}, Columns: {Columns}");
    }

    private static TestRecord[] GenerateRecords(int count)
    {
        var result = new TestRecord[count];
        for (int i = 0; i < count; i++)
        {
            result[i] = new TestRecord
            {
                Id = i,
                Name = $"Name{i}",
                Value = i * 1.5,
                IsActive = i % 2 == 0,
                Created = DateTime.Now.AddDays(-i)
            };
        }
        return result;
    }

    private static string[] GenerateStrings(int count)
    {
        var result = new string[count];
        for (int i = 0; i < count; i++)
        {
            result[i] = $"value{i}";
        }
        return result;
    }

    [Benchmark(Baseline = true)]
    public string WriteRawRows()
    {
        using var sw = new StringWriter();
        using var writer = new CsvStreamWriter(sw, CsvWriterOptions.Default, leaveOpen: true);

        for (int r = 0; r < Rows; r++)
        {
            writer.WriteRow(stringValues);
        }
        writer.Flush();

        return sw.ToString();
    }

    [Benchmark]
    public string WriteRecords()
    {
        return Csv.WriteToText(records);
    }

    [Benchmark]
    public string WriteRecordsNoHeader()
    {
        return Csv.WriteToText(records, new CsvWriterOptions { WriteHeader = false });
    }

    [Benchmark]
    public string WriteAlwaysQuoted()
    {
        return Csv.WriteToText(records, new CsvWriterOptions { QuoteStyle = QuoteStyle.Always });
    }

    [Benchmark]
    public void WriteToStream()
    {
        using var ms = new MemoryStream();
        Csv.WriteToStream(ms, records);
    }

    public class TestRecord
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public double Value { get; set; }
        public bool IsActive { get; set; }
        public DateTime Created { get; set; }
    }
}
