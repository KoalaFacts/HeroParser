using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;

namespace HeroParser.Benchmarks;

/// <summary>
/// Benchmarks for writer with different output destinations.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Throughput, iterationCount: 5, warmupCount: 3)]
public class WriterDestinationBenchmarks
{
    private TestRecord[] records = null!;

    [Params(10_000)]
    public int Rows { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        records = GenerateRecords(Rows);
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

    [Benchmark(Baseline = true)]
    public string ToText()
    {
        return Csv.WriteToText(records);
    }

    [Benchmark]
    public void ToMemoryStream()
    {
        using var ms = new MemoryStream();
        Csv.WriteToStream(ms, records);
    }

    [Benchmark]
    public void ToNullStream()
    {
        Csv.WriteToStream(Stream.Null, records);
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
