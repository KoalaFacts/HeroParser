using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using HeroParser.SeparatedValues.Reading.Records;
using HeroParser.SeparatedValues.Validation;
using System.Text;

namespace HeroParser.Benchmarks;

/// <summary>
/// Benchmarks for field validation during record reading.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Throughput, iterationCount: 5, warmupCount: 3)]
public class ValidationBenchmarks
{
    private string csvData = null!;

    [Params(1_000, 10_000)]
    public int Rows { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Name,Age,Email");
        for (int i = 0; i < Rows; i++)
        {
            sb.AppendLine($"Person{i},{20 + (i % 50)},person{i}@example.com");
        }
        csvData = sb.ToString();
    }

    [Benchmark(Baseline = true)]
    public int Read_NoValidation()
    {
        using var reader = Csv.Read<Person>()
            .FromText(csvData);

        int count = 0;
        foreach (var person in reader)
        {
            count++;
        }
        return count;
    }

    [Benchmark]
    public int Read_ValidationEnabled_NoValidators()
    {
        using var reader = Csv.Read<Person>()
            .EnableValidation()
            .FromText(csvData);

        int count = 0;
        foreach (var person in reader)
        {
            count++;
        }
        return count;
    }

    [Benchmark]
    public int Read_WithRequiredValidator()
    {
        using var reader = Csv.Read<Person>()
            .Validate(p => p.Name, CsvValidators.Required())
            .FromText(csvData);

        int count = 0;
        foreach (var person in reader)
        {
            count++;
        }
        return count;
    }

    [Benchmark]
    public int Read_WithRangeValidator()
    {
        using var reader = Csv.Read<Person>()
            .Validate(p => p.Age, CsvValidators.Range(0, 150))
            .FromText(csvData);

        int count = 0;
        foreach (var person in reader)
        {
            count++;
        }
        return count;
    }

    [Benchmark]
    public int Read_WithMultipleValidators()
    {
        using var reader = Csv.Read<Person>()
            .Validate(p => p.Name, CsvValidators.Required(), CsvValidators.MaxLength(100))
            .Validate(p => p.Age, CsvValidators.Range(0, 150))
            .Validate(p => p.Email, CsvValidators.Required())
            .FromText(csvData);

        int count = 0;
        foreach (var person in reader)
        {
            count++;
        }
        return count;
    }

    [Benchmark]
    public int Read_WithRegexValidator()
    {
        using var reader = Csv.Read<Person>()
            .Validate(p => p.Email, CsvValidators.Regex(@"^[\w.-]+@[\w.-]+\.\w+$"))
            .FromText(csvData);

        int count = 0;
        foreach (var person in reader)
        {
            count++;
        }
        return count;
    }

    [Benchmark]
    public int Read_WithNoInjectionValidator()
    {
        using var reader = Csv.Read<Person>()
            .Validate(p => p.Name, CsvValidators.NoInjection())
            .FromText(csvData);

        int count = 0;
        foreach (var person in reader)
        {
            count++;
        }
        return count;
    }

    public class Person
    {
        public string? Name { get; set; }
        public int Age { get; set; }
        public string? Email { get; set; }
    }
}
