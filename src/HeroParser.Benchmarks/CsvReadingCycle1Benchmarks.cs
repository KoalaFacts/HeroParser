using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using HeroParser.Core;
using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;
using System.Text;
using Sylvan.Data.Csv;

namespace HeroParser.Benchmarks;

/// <summary>
/// Baseline CSV reading performance benchmarks for F1 Cycle 1.
/// Compares HeroParser against Sep, Sylvan.Data.Csv, and CsvHelper.
/// </summary>
[MemoryDiagnoser]
[SimpleJob]
public class CsvReadingCycle1Benchmarks
{
    private string _smallCsv = string.Empty;
    private string _mediumCsv = string.Empty;
    private string _largeCsv = string.Empty;

    [GlobalSetup]
    public void Setup()
    {
        // Small CSV (100 rows, 5 columns)
        var smallBuilder = new StringBuilder();
        smallBuilder.AppendLine("Name,Age,City,Country,Salary");
        for (int i = 0; i < 100; i++)
        {
            smallBuilder.AppendLine($"Person{i},{20 + (i % 50)},City{i % 10},Country{i % 5},{30000 + (i * 100)}");
        }
        _smallCsv = smallBuilder.ToString();

        // Medium CSV (10,000 rows, 5 columns)
        var mediumBuilder = new StringBuilder();
        mediumBuilder.AppendLine("Name,Age,City,Country,Salary");
        for (int i = 0; i < 10000; i++)
        {
            mediumBuilder.AppendLine($"Person{i},{20 + (i % 50)},City{i % 100},Country{i % 20},{30000 + (i * 10)}");
        }
        _mediumCsv = mediumBuilder.ToString();

        // Large CSV (100,000 rows, 5 columns)
        var largeBuilder = new StringBuilder();
        largeBuilder.AppendLine("Name,Age,City,Country,Salary");
        for (int i = 0; i < 100000; i++)
        {
            largeBuilder.AppendLine($"Person{i},{20 + (i % 50)},City{i % 1000},Country{i % 50},{30000 + i}");
        }
        _largeCsv = largeBuilder.ToString();
    }

    // Small CSV Benchmarks (100 rows)
    [Benchmark]
    public string[][] HeroParser_Small()
    {
        return HeroParser.Core.CsvParser.Parse(_smallCsv);
    }

    // Sep benchmark temporarily disabled due to namespace issues
    // [Benchmark]
    // public List<string[]> Sep_Small() { ... }

    [Benchmark]
    public List<string[]> Sylvan_Small()
    {
        using var reader = new StringReader(_smallCsv);
        using var csv = Sylvan.Data.Csv.CsvDataReader.Create(reader);
        var results = new List<string[]>();

        while (csv.Read())
        {
            var row = new string[csv.FieldCount];
            for (int i = 0; i < csv.FieldCount; i++)
            {
                row[i] = csv.GetString(i);
            }
            results.Add(row);
        }

        return results;
    }

    [Benchmark]
    public List<string[]> CsvHelper_Small()
    {
        using var reader = new StringReader(_smallCsv);
        using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
        var results = new List<string[]>();

        while (csv.Read())
        {
            var row = new string[csv.Parser.Count];
            for (int i = 0; i < csv.Parser.Count; i++)
            {
                row[i] = csv.GetField(i) ?? string.Empty;
            }
            results.Add(row);
        }

        return results;
    }

    // Medium CSV Benchmarks (10,000 rows)
    [Benchmark]
    public string[][] HeroParser_Medium()
    {
        return HeroParser.Core.CsvParser.Parse(_mediumCsv);
    }

    // Sep benchmark temporarily disabled due to namespace issues
    // [Benchmark]
    // public List<string[]> Sep_Medium() { ... }

    [Benchmark]
    public List<string[]> Sylvan_Medium()
    {
        using var reader = new StringReader(_mediumCsv);
        using var csv = Sylvan.Data.Csv.CsvDataReader.Create(reader);
        var results = new List<string[]>();

        while (csv.Read())
        {
            var row = new string[csv.FieldCount];
            for (int i = 0; i < csv.FieldCount; i++)
            {
                row[i] = csv.GetString(i);
            }
            results.Add(row);
        }

        return results;
    }

    [Benchmark]
    public List<string[]> CsvHelper_Medium()
    {
        using var reader = new StringReader(_mediumCsv);
        using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
        var results = new List<string[]>();

        while (csv.Read())
        {
            var row = new string[csv.Parser.Count];
            for (int i = 0; i < csv.Parser.Count; i++)
            {
                row[i] = csv.GetField(i) ?? string.Empty;
            }
            results.Add(row);
        }

        return results;
    }

    // Large CSV Benchmarks (100,000 rows)
    [Benchmark]
    public string[][] HeroParser_Large()
    {
        return HeroParser.Core.CsvParser.Parse(_largeCsv);
    }

    // Sep benchmark temporarily disabled due to namespace issues
    // [Benchmark]
    // public List<string[]> Sep_Large() { ... }

    [Benchmark]
    public List<string[]> Sylvan_Large()
    {
        using var reader = new StringReader(_largeCsv);
        using var csv = Sylvan.Data.Csv.CsvDataReader.Create(reader);
        var results = new List<string[]>();

        while (csv.Read())
        {
            var row = new string[csv.FieldCount];
            for (int i = 0; i < csv.FieldCount; i++)
            {
                row[i] = csv.GetString(i);
            }
            results.Add(row);
        }

        return results;
    }

    [Benchmark]
    public List<string[]> CsvHelper_Large()
    {
        using var reader = new StringReader(_largeCsv);
        using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
        var results = new List<string[]>();

        while (csv.Read())
        {
            var row = new string[csv.Parser.Count];
            for (int i = 0; i < csv.Parser.Count; i++)
            {
                row[i] = csv.GetField(i) ?? string.Empty;
            }
            results.Add(row);
        }

        return results;
    }
}

/// <summary>
/// Program entry point for running the benchmarks.
/// </summary>
public class Program
{
    public static void Main(string[] args)
    {
        var summary = BenchmarkRunner.Run<CsvReadingCycle1Benchmarks>();

        Console.WriteLine("\n=== F1 Cycle 1 Benchmark Summary ===");
        Console.WriteLine("Baseline CSV reading performance comparison");
        Console.WriteLine("Libraries tested: HeroParser, Sylvan.Data.Csv, CsvHelper (Sep disabled)");
        Console.WriteLine($"Benchmark completed at: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine("\nNext steps: Analyze results and identify optimization opportunities for F1 Cycle 2");
    }
}