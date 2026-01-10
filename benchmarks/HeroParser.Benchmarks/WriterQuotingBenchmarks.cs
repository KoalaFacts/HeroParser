using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using HeroParser.SeparatedValues.Writing;

namespace HeroParser.Benchmarks;

/// <summary>
/// Benchmarks comparing writer quoting strategies.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Throughput, iterationCount: 5, warmupCount: 3)]
public class WriterQuotingBenchmarks
{
    private string[] normalValues = null!;
    private string[] quotableValues = null!;

    [Params(10_000)]
    public int Rows { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        normalValues = ["Hello", "World", "Test", "Data"];
        quotableValues = ["Hello,World", "With\"Quote", "Line\nBreak", "Normal"];
    }

    [Benchmark(Baseline = true)]
    public string WriteNormalValues_WhenNeeded()
    {
        var options = new CsvWriteOptions { QuoteStyle = QuoteStyle.WhenNeeded };
        using var sw = new StringWriter();
        using var writer = new CsvStreamWriter(sw, options, leaveOpen: true);

        for (int i = 0; i < Rows; i++)
        {
            writer.WriteRow(normalValues);
        }
        writer.Flush();
        return sw.ToString();
    }

    [Benchmark]
    public string WriteNormalValues_Always()
    {
        var options = new CsvWriteOptions { QuoteStyle = QuoteStyle.Always };
        using var sw = new StringWriter();
        using var writer = new CsvStreamWriter(sw, options, leaveOpen: true);

        for (int i = 0; i < Rows; i++)
        {
            writer.WriteRow(normalValues);
        }
        writer.Flush();
        return sw.ToString();
    }

    [Benchmark]
    public string WriteQuotableValues_WhenNeeded()
    {
        var options = new CsvWriteOptions { QuoteStyle = QuoteStyle.WhenNeeded };
        using var sw = new StringWriter();
        using var writer = new CsvStreamWriter(sw, options, leaveOpen: true);

        for (int i = 0; i < Rows; i++)
        {
            writer.WriteRow(quotableValues);
        }
        writer.Flush();
        return sw.ToString();
    }

    [Benchmark]
    public string WriteQuotableValues_Always()
    {
        var options = new CsvWriteOptions { QuoteStyle = QuoteStyle.Always };
        using var sw = new StringWriter();
        using var writer = new CsvStreamWriter(sw, options, leaveOpen: true);

        for (int i = 0; i < Rows; i++)
        {
            writer.WriteRow(quotableValues);
        }
        writer.Flush();
        return sw.ToString();
    }
}

