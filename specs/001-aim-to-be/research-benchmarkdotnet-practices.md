# BenchmarkDotNet Best Practices Research - 2025

**Research Phase**: T010.2 - BenchmarkDotNet Best Practices
**Date**: September 22, 2025
**Objective**: Establish accurate measurement methodology for CSV parsing performance and zero-allocation validation

## BenchmarkDotNet Overview

BenchmarkDotNet is a powerful .NET library that transforms methods into benchmarks with reliable and precise results thanks to the perfolizer statistical engine. It provides measurement precision down to individual CPU cycles (about a tenth of a nanosecond) by running benchmarks billions of times when necessary.

## Critical Configuration for CSV Parsing Benchmarks

### 1. Environment Setup Requirements

**Release Mode Mandatory**:
```csharp
// BenchmarkDotNet prevents benchmarking DEBUG assemblies
// Always use Release configuration for reliable results
[Config(typeof(ReleaseConfig))]
public class CsvParsingBenchmarks
```

**Environment Control**:
- Exit all running applications for maximum accuracy
- Run from terminal, not IDE debugger
- Avoid hypervisors (HyperV, VMware, VirtualBox)
- BenchmarkDotNet warns about environment issues

**Optimal Configuration**:
```csharp
[Config(typeof(OptimalConfig))]
public class OptimalConfig : ManualConfig
{
    public OptimalConfig()
    {
        AddJob(Job.Default
            .WithRuntime(CoreRuntime.Core80)
            .WithPlatform(Platform.X64)
            .WithJit(Jit.RyuJit));

        AddDiagnoser(MemoryDiagnoser.Default);
        AddValidator(JitOptimizationsValidator.DontFailOnError);
    }
}
```

### 2. Memory Allocation Tracking (Critical for Zero-Allocation Goal)

**MemoryDiagnoser Setup**:
```csharp
[MemoryDiagnoser(false)] // false = don't display RatioColumn
public class CsvParsingBenchmarks
{
    [Benchmark]
    public void ParseCsv()
    {
        // Implementation
    }
}
```

**Zero-Allocation Validation**:
- **Allocated Column**: Shows managed memory allocation per invocation
- **"-" or "0 B"**: Indicates zero allocations (our target)
- **Gen X Columns**: Shows garbage collection frequency per 1,000 operations
- **99.5% Accuracy**: MemoryDiagnoser provides highly accurate allocation measurement

**Example Zero-Allocation Result**:
```
Method    | Mean     | Allocated |
--------- |--------- |---------- |
ParseCsv  | 125.3 ms | -         |  <- Zero allocations achieved!
```

### 3. CSV Parsing Benchmark Design

**Recommended Structure**:
```csharp
[MemoryDiagnoser]
[Config(typeof(OptimalConfig))]
public class CsvParsingBenchmarks
{
    private MemoryStream csvStream;
    private string csvData;

    [GlobalSetup]
    public void Setup()
    {
        csvData = GenerateTestCsv(1_000_000); // 1M rows
        csvStream = new MemoryStream(Encoding.UTF8.GetBytes(csvData));
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        csvStream?.Dispose();
    }

    [Benchmark(Baseline = true)]
    public int CsvHelper_Parse()
    {
        csvStream.Position = 0;
        // Implementation
        return recordCount;
    }

    [Benchmark]
    public int Sep_Parse()
    {
        csvStream.Position = 0;
        // Implementation
        return recordCount;
    }

    [Benchmark]
    public int HeroParser_Parse()
    {
        csvStream.Position = 0;
        // Implementation
        return recordCount;
    }
}
```

### 4. Preventing Measurement Pitfalls

**Dead Code Elimination Prevention**:
```csharp
[Benchmark]
public int ParseCsv()
{
    var result = CsvParser.Parse(csvData);

    // Always consume results to prevent optimization
    var count = 0;
    foreach (var row in result)
    {
        count++; // Prevent dead code elimination
    }
    return count; // Return meaningful value
}
```

**JIT Warming**:
- BenchmarkDotNet automatically performs warm-up iterations
- Eliminates JIT compilation costs from measurements
- Multiple iterations ensure statistical significance

### 5. Statistical Interpretation

**Key Metrics Understanding**:
- **Mean**: Average execution time
- **Error**: Half of 99.9% confidence interval
- **StdDev**: Standard deviation of measurements
- **Median**: Middle value (less affected by outliers)

**Reliability Indicators**:
```
Method   | Mean      | Error    | StdDev   | Allocated |
-------- |---------- |--------- |--------- |---------- |
ParseCsv | 125.34 ms | 2.45 ms  | 3.12 ms  | -         |
```
- Low Error/StdDev relative to Mean = reliable results
- "-" in Allocated = zero allocation success

### 6. Competitor Comparison Setup

**Baseline Methodology**:
```csharp
[Benchmark(Baseline = true)]
public int CsvHelper_Baseline() => ParseWithCsvHelper();

[Benchmark]
public int Sylvan_Comparison() => ParseWithSylvan();

[Benchmark]
public int Sep_Comparison() => ParseWithSep();

[Benchmark]
public int HeroParser_Target() => ParseWithHeroParser();
```

**Performance Target Validation**:
- Target >25 GB/s (benchmark must validate this claim)
- Compare against Sep's 21 GB/s baseline
- Measure throughput: `(file_size_bytes / execution_time_seconds) / 1_000_000_000`

### 7. Multiple Dataset Sizes

**Comprehensive Performance Profile**:
```csharp
[Params(1_000, 100_000, 1_000_000, 10_000_000)]
public int RowCount { get; set; }

[GlobalSetup]
public void Setup()
{
    csvData = GenerateTestCsv(RowCount);
    // Calculate expected throughput based on data size
}
```

**Dataset Strategy**:
- **1KB**: Startup performance measurement
- **1MB**: Throughput measurement
- **1GB**: Sustained performance and memory efficiency
- **100GB**: Streaming capability validation

### 8. Framework-Specific Testing

**Multi-Framework Validation**:
```csharp
[SimpleJob(RuntimeMoniker.Net80)]
[SimpleJob(RuntimeMoniker.Net90)]
[SimpleJob(RuntimeMoniker.Net100)]
public class FrameworkComparisonBenchmarks
```

**Performance Tracking by Framework**:
- .NET 8: Baseline LTS performance
- .NET 9: Latest features impact
- .NET 10: Peak optimization target

## Implementation Strategy for HeroParser

### 1. Benchmark Structure

**Phase 1**: Competitor baselines (T010.4)
**Phase 2**: Zero-allocation validation (T011)
**Phase 3**: Performance optimization measurement
**Phase 4**: Regression detection setup

### 2. Success Criteria Validation

**Performance Targets**:
- Throughput: >25 GB/s single-threaded measurement
- Memory: "-" in Allocated column (zero allocations)
- Reliability: Error <5% of Mean
- Competitive: >20% faster than Sep baseline

### 3. Continuous Integration

**Automated Performance Tracking**:
- Run benchmarks on each commit
- Performance regression detection
- Historical performance trending
- Multiple platform validation

## Key Implementation Requirements

✅ **Always use [MemoryDiagnoser]** for allocation tracking
✅ **Release mode only** for accurate results
✅ **Baseline comparisons** against Sep/Sylvan/CsvHelper
✅ **Multiple dataset sizes** for comprehensive profiling
✅ **Statistical significance** validation (low Error/StdDev)
✅ **Zero allocation verification** (target: "-" in Allocated)
✅ **Throughput calculation** for GB/s performance claims

This methodology ensures accurate, reliable performance measurement and validates our >25 GB/s zero-allocation goals against industry-leading competitors.