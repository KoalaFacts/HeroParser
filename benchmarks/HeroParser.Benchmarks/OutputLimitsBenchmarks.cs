using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using HeroParser.SeparatedValues.Writing;

namespace HeroParser.Benchmarks;

/// <summary>
/// Benchmarks for output size limits feature.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Throughput, iterationCount: 5, warmupCount: 3)]
public class OutputLimitsBenchmarks
{
    private string[] values = null!;

    [Params(10_000, 100_000)]
    public int Rows { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        values = ["Field1", "Field2", "Field3", "Field4"];
    }

    [Benchmark(Baseline = true)]
    public string Write_NoLimits()
    {
        var options = new CsvWriteOptions();
        using var sw = new StringWriter();
        using var writer = new CsvStreamWriter(sw, options, leaveOpen: true);

        for (int i = 0; i < Rows; i++)
        {
            writer.WriteRow(values);
        }
        writer.Flush();
        return sw.ToString();
    }

    [Benchmark]
    public string Write_WithMaxOutputSize()
    {
        // Set a high limit that won't be hit
        var options = new CsvWriteOptions { MaxOutputSize = 100_000_000 };
        using var sw = new StringWriter();
        using var writer = new CsvStreamWriter(sw, options, leaveOpen: true);

        for (int i = 0; i < Rows; i++)
        {
            writer.WriteRow(values);
        }
        writer.Flush();
        return sw.ToString();
    }

    [Benchmark]
    public string Write_WithMaxFieldSize()
    {
        var options = new CsvWriteOptions { MaxFieldSize = 1000 };
        using var sw = new StringWriter();
        using var writer = new CsvStreamWriter(sw, options, leaveOpen: true);

        for (int i = 0; i < Rows; i++)
        {
            writer.WriteRow(values);
        }
        writer.Flush();
        return sw.ToString();
    }

    [Benchmark]
    public string Write_WithMaxColumnCount()
    {
        var options = new CsvWriteOptions { MaxColumnCount = 100 };
        using var sw = new StringWriter();
        using var writer = new CsvStreamWriter(sw, options, leaveOpen: true);

        for (int i = 0; i < Rows; i++)
        {
            writer.WriteRow(values);
        }
        writer.Flush();
        return sw.ToString();
    }

    [Benchmark]
    public string Write_WithAllLimits()
    {
        var options = new CsvWriteOptions
        {
            MaxOutputSize = 100_000_000,
            MaxFieldSize = 1000,
            MaxColumnCount = 100
        };
        using var sw = new StringWriter();
        using var writer = new CsvStreamWriter(sw, options, leaveOpen: true);

        for (int i = 0; i < Rows; i++)
        {
            writer.WriteRow(values);
        }
        writer.Flush();
        return sw.ToString();
    }
}

