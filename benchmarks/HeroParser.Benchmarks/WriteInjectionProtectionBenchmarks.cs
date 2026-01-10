using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using HeroParser.SeparatedValues.Writing;

namespace HeroParser.Benchmarks;

/// <summary>
/// Benchmarks for write-side injection protection.
/// Measures the overhead of enabling injection protection features during CSV writing.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Throughput, iterationCount: 5, warmupCount: 3)]
public class WriteInjectionProtectionBenchmarks
{
    private string[] normalValues = null!;
    private string[] numbersWithSign = null!;
    private string[] phoneNumbers = null!;

    [Params(10_000, 100_000)]
    public int Rows { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        // Normal values - no dangerous characters
        normalValues = ["Hello", "World", "Test123", "Data"];

        // Values with - and + followed by digits (safe patterns)
        numbersWithSign = ["-100", "+200", "-3.14", "+1.5"];

        // Phone numbers (safe patterns)
        phoneNumbers = ["+1-555-1234", "+44 20 7946", "-100.50", "Normal"];
    }

    [Benchmark(Baseline = true)]
    public string Write_NoProtection()
    {
        var options = new CsvWriteOptions { InjectionProtection = CsvInjectionProtection.None };
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
    public string Write_WithSanitize_NormalValues()
    {
        var options = new CsvWriteOptions { InjectionProtection = CsvInjectionProtection.Sanitize };
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
    public string Write_WithReject_NormalValues()
    {
        var options = new CsvWriteOptions { InjectionProtection = CsvInjectionProtection.Reject };
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
    public string Write_WithReject_NumbersWithSign()
    {
        // Tests smart detection: -100, +200 should pass (digit after sign)
        var options = new CsvWriteOptions { InjectionProtection = CsvInjectionProtection.Reject };
        using var sw = new StringWriter();
        using var writer = new CsvStreamWriter(sw, options, leaveOpen: true);

        for (int i = 0; i < Rows; i++)
        {
            writer.WriteRow(numbersWithSign);
        }
        writer.Flush();
        return sw.ToString();
    }

    [Benchmark]
    public string Write_WithReject_PhoneNumbers()
    {
        // Tests smart detection: +1-555-1234 should pass (digit after +)
        var options = new CsvWriteOptions { InjectionProtection = CsvInjectionProtection.Reject };
        using var sw = new StringWriter();
        using var writer = new CsvStreamWriter(sw, options, leaveOpen: true);

        for (int i = 0; i < Rows; i++)
        {
            writer.WriteRow(phoneNumbers);
        }
        writer.Flush();
        return sw.ToString();
    }

    [Benchmark]
    public string Write_WithEscapeQuote_NormalValues()
    {
        var options = new CsvWriteOptions { InjectionProtection = CsvInjectionProtection.EscapeWithQuote };
        using var sw = new StringWriter();
        using var writer = new CsvStreamWriter(sw, options, leaveOpen: true);

        for (int i = 0; i < Rows; i++)
        {
            writer.WriteRow(normalValues);
        }
        writer.Flush();
        return sw.ToString();
    }
}

