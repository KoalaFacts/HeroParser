using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using HeroParser.SeparatedValues.Validation;

namespace HeroParser.Benchmarks;

/// <summary>
/// Microbenchmarks for smart detection logic.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Throughput, iterationCount: 5, warmupCount: 3)]
public class SmartDetectionMicroBenchmarks
{
    private readonly IFieldValidator noInjectionValidator = CsvValidators.NoInjection();

    // Test values
    private string safeNormal = null!;
    private string safeNegativeNumber = null!;
    private string safePhoneNumber = null!;
    private string dangerousEquals = null!;
    private string dangerousMinusFormula = null!;
    private string dangerousPlusFormula = null!;

    [GlobalSetup]
    public void Setup()
    {
        safeNormal = "Hello World";
        safeNegativeNumber = "-123.45";
        safePhoneNumber = "+1-555-1234";
        dangerousEquals = "=SUM(A1)";
        dangerousMinusFormula = "-HYPERLINK()";
        dangerousPlusFormula = "+SUM(A1:B10)";
    }

    [Benchmark(Baseline = true)]
    public bool Validate_SafeNormal()
    {
        return noInjectionValidator.Validate(safeNormal, safeNormal).IsValid;
    }

    [Benchmark]
    public bool Validate_SafeNegativeNumber()
    {
        return noInjectionValidator.Validate(safeNegativeNumber, safeNegativeNumber).IsValid;
    }

    [Benchmark]
    public bool Validate_SafePhoneNumber()
    {
        return noInjectionValidator.Validate(safePhoneNumber, safePhoneNumber).IsValid;
    }

    [Benchmark]
    public bool Validate_DangerousEquals()
    {
        return noInjectionValidator.Validate(dangerousEquals, dangerousEquals).IsValid;
    }

    [Benchmark]
    public bool Validate_DangerousMinusFormula()
    {
        return noInjectionValidator.Validate(dangerousMinusFormula, dangerousMinusFormula).IsValid;
    }

    [Benchmark]
    public bool Validate_DangerousPlusFormula()
    {
        return noInjectionValidator.Validate(dangerousPlusFormula, dangerousPlusFormula).IsValid;
    }
}
