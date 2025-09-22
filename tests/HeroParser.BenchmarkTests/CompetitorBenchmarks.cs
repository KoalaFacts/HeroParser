using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Validators;
using CsvHelper;
using nietras.SeparatedValues;
using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace HeroParser.BenchmarkTests;

/// <summary>
/// Competitor baseline benchmarks per Constitution Principle II.
/// Compares against Sep (21 GB/s leader), Sylvan.Data.Csv, and CsvHelper.
/// Target: >25 GB/s single-threaded performance (>20% improvement over Sep).
/// </summary>
[Config(typeof(PerformanceConfig))]
[MemoryDiagnoser(false)]
public class CompetitorBenchmarks
{
    private string _csvData1KB = string.Empty;
    private string _csvData1MB = string.Empty;
    private string _csvData1GB = string.Empty;
    private long _dataSize1MB;
    private long _dataSize1GB;

    public class PerformanceConfig : ManualConfig
    {
        public PerformanceConfig()
        {
            // Constitution: Performance-First Architecture
            AddJob(Job.Default
                .WithRuntime(BenchmarkDotNet.Environments.CoreRuntime.Core80)
                .WithPlatform(BenchmarkDotNet.Environments.Platform.X64)
                .WithJit(BenchmarkDotNet.Environments.Jit.RyuJit));

            // Constitution: Benchmark-Driven Development - track allocations
            AddDiagnoser(MemoryDiagnoser.Default);
            AddValidator(JitOptimizationsValidator.DontFailOnError);
        }
    }

    [GlobalSetup]
    public void Setup()
    {
        // Constitution: "small files (1KB), medium files (1MB), large files (1GB)"
        _csvData1KB = File.ReadAllText("BenchmarkData/simple_1kb.csv");
        _csvData1MB = File.ReadAllText("BenchmarkData/simple_1mb.csv");
        _csvData1GB = File.ReadAllText("BenchmarkData/simple_1gb.csv");

        _dataSize1MB = Encoding.UTF8.GetByteCount(_csvData1MB);
        _dataSize1GB = Encoding.UTF8.GetByteCount(_csvData1GB);
    }

    [Params("1KB", "1MB", "1GB")]
    public string DataSize { get; set; } = string.Empty;

    private string GetDataForSize(string size) => size switch
    {
        "1KB" => _csvData1KB,
        "1MB" => _csvData1MB,
        "1GB" => _csvData1GB,
        _ => throw new ArgumentException($"Unknown data size: {size}")
    };

    // Constitution: CsvHelper baseline (industry standard)
    [Benchmark(Baseline = true)]
    public int CsvHelper_Parse()
    {
        var csvData = GetDataForSize(DataSize);
        using var reader = new StringReader(csvData);
        using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);

        var recordCount = 0;
        while (csv.Read())
        {
            for (int i = 0; i < csv.ColumnCount; i++)
            {
                _ = csv.GetField(i); // Prevent dead code elimination
            }
            recordCount++;
        }
        return recordCount;
    }

    // Constitution: Sep baseline (current 21 GB/s leader to beat)
    [Benchmark]
    public int Sep_Parse()
    {
        var csvData = GetDataForSize(DataSize);
        using var reader = Sep.Reader().FromText(csvData);

        var recordCount = 0;
        foreach (var row in reader)
        {
            for (int i = 0; i < row.ColCount; i++)
            {
                _ = row[i].ToString(); // Prevent dead code elimination
            }
            recordCount++;
        }
        return recordCount;
    }

    // Constitution: Sylvan baseline (zero-allocation competitor)
    [Benchmark]
    public int Sylvan_Parse()
    {
        var csvData = GetDataForSize(DataSize);
        using var reader = new StringReader(csvData);
        using var csv = Sylvan.Data.Csv.CsvDataReader.Create(reader);

        var recordCount = 0;
        while (csv.Read())
        {
            for (int i = 0; i < csv.FieldCount; i++)
            {
                _ = csv.GetString(i); // Prevent dead code elimination
            }
            recordCount++;
        }
        return recordCount;
    }

    // Constitution: Target >25 GB/s (>20% improvement over Sep)
    [Benchmark]
    public int HeroParser_Parse()
    {
        var csvData = GetDataForSize(DataSize);

        // TODO: Replace with actual HeroParser implementation
        // Placeholder: return line count to prevent benchmark errors
        var lines = csvData.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        return lines.Length - 1; // Subtract header
    }
}