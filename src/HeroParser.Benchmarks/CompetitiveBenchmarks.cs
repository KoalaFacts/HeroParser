using BenchmarkDotNet.Attributes;
using nietras.SeparatedValues;
using System.Globalization;
using System.Text;

namespace HeroParser.Benchmarks;

/// <summary>
/// Competitive benchmark comparing HeroParser against other popular CSV libraries.
/// Tests parsing performance across different data sizes to identify strengths and weaknesses.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
[MinColumn, MaxColumn, MeanColumn, MedianColumn]
public class CompetitiveBenchmarks
{
    private string _smallCsv = string.Empty;   // 100 rows × 5 columns
    private string _mediumCsv = string.Empty;  // 10,000 rows × 25 columns
    private string _largeCsv = string.Empty;   // 100,000 rows × 100 columns

    [GlobalSetup]
    public void Setup()
    {
        Console.WriteLine("=== Competitive CSV Parsing Benchmark ===");
        Console.WriteLine("Comparing: HeroParser vs Sep vs Sylvan vs CsvHelper");
        Console.WriteLine();

        // Small CSV - Quick operations, startup overhead matters
        _smallCsv = GenerateCsv(100, 5);

        // Medium CSV - Typical workload, balanced test
        _mediumCsv = GenerateCsv(10_000, 25);

        // Large CSV - Stress test, throughput matters most
        _largeCsv = GenerateCsv(100_000, 100);

        Console.WriteLine($"Small:  {_smallCsv.Length / 1024.0:F1} KB (100 rows × 5 columns)");
        Console.WriteLine($"Medium: {_mediumCsv.Length / 1024.0 / 1024.0:F1} MB (10K rows × 25 columns)");
        Console.WriteLine($"Large:  {_largeCsv.Length / 1024.0 / 1024.0:F1} MB (100K rows × 100 columns)");
        Console.WriteLine();
    }

    private static string GenerateCsv(int rows, int columns)
    {
        var builder = new StringBuilder();

        // Header
        var headers = Enumerable.Range(0, columns).Select(i => $"Column{i}");
        builder.AppendLine(string.Join(",", headers));

        // Data rows
        for (int row = 0; row < rows; row++)
        {
            var values = Enumerable.Range(0, columns).Select(col => $"Value{row}_{col}");
            builder.AppendLine(string.Join(",", values));
        }

        return builder.ToString();
    }

    // ===============================
    // SMALL CSV - Startup overhead test
    // ===============================

    [BenchmarkCategory("Small"), Benchmark(Baseline = true)]
    public List<string[]> Small_HeroParser()
    {
        var records = new List<string[]>();
        using var reader = HeroParser.Csv.OpenContent(_smallCsv);
        while (reader.Read())
        {
            var row = new string[reader.CurrentRow.ColumnCount];
            for (int i = 0; i < reader.CurrentRow.ColumnCount; i++)
            {
                row[i] = reader.CurrentRow[i].ToString();
            }
            records.Add(row);
        }
        return records;
    }

    [BenchmarkCategory("Small"), Benchmark]
    public List<string[]> Small_Sep()
    {
        var records = new List<string[]>();
        using var reader = Sep.Reader().FromText(_smallCsv);
        foreach (var readRow in reader)
        {
            var row = new string[readRow.ColCount];
            for (int i = 0; i < readRow.ColCount; i++)
            {
                row[i] = readRow[i].ToString();
            }
            records.Add(row);
        }
        return records;
    }

    [BenchmarkCategory("Small"), Benchmark]
    public List<string[]> Small_Sylvan()
    {
        var records = new List<string[]>();
        using var reader = new StringReader(_smallCsv);
        using var csvReader = Sylvan.Data.Csv.CsvDataReader.Create(reader);

        while (csvReader.Read())
        {
            var row = new string[csvReader.FieldCount];
            for (int i = 0; i < csvReader.FieldCount; i++)
            {
                row[i] = csvReader.GetString(i);
            }
            records.Add(row);
        }
        return records;
    }

    [BenchmarkCategory("Small"), Benchmark]
    public List<string[]> Small_CsvHelper()
    {
        var records = new List<string[]>();
        using var reader = new StringReader(_smallCsv);
        using var csv = new CsvHelper.CsvReader(reader, CultureInfo.InvariantCulture);

        csv.Read();
        csv.ReadHeader();
        while (csv.Read())
        {
            var row = new string[csv.HeaderRecord?.Length ?? 0];
            for (int i = 0; i < (csv.HeaderRecord?.Length ?? 0); i++)
            {
                row[i] = csv.GetField(i) ?? string.Empty;
            }
            records.Add(row);
        }
        return records;
    }

    // ===============================
    // MEDIUM CSV - Typical workload
    // ===============================

    [BenchmarkCategory("Medium"), Benchmark]
    public List<string[]> Medium_HeroParser()
    {
        var records = new List<string[]>();
        using var reader = HeroParser.Csv.OpenContent(_mediumCsv);
        while (reader.Read())
        {
            var row = new string[reader.CurrentRow.ColumnCount];
            for (int i = 0; i < reader.CurrentRow.ColumnCount; i++)
            {
                row[i] = reader.CurrentRow[i].ToString();
            }
            records.Add(row);
        }
        return records;
    }

    [BenchmarkCategory("Medium"), Benchmark]
    public List<string[]> Medium_Sep()
    {
        var records = new List<string[]>();
        using var reader = Sep.Reader().FromText(_mediumCsv);
        foreach (var readRow in reader)
        {
            var row = new string[readRow.ColCount];
            for (int i = 0; i < readRow.ColCount; i++)
            {
                row[i] = readRow[i].ToString();
            }
            records.Add(row);
        }
        return records;
    }

    [BenchmarkCategory("Medium"), Benchmark]
    public List<string[]> Medium_Sylvan()
    {
        var records = new List<string[]>();
        using var reader = new StringReader(_mediumCsv);
        using var csvReader = Sylvan.Data.Csv.CsvDataReader.Create(reader);

        while (csvReader.Read())
        {
            var row = new string[csvReader.FieldCount];
            for (int i = 0; i < csvReader.FieldCount; i++)
            {
                row[i] = csvReader.GetString(i);
            }
            records.Add(row);
        }
        return records;
    }

    [BenchmarkCategory("Medium"), Benchmark]
    public List<string[]> Medium_CsvHelper()
    {
        var records = new List<string[]>();
        using var reader = new StringReader(_mediumCsv);
        using var csv = new CsvHelper.CsvReader(reader, CultureInfo.InvariantCulture);

        csv.Read();
        csv.ReadHeader();
        while (csv.Read())
        {
            var row = new string[csv.HeaderRecord?.Length ?? 0];
            for (int i = 0; i < (csv.HeaderRecord?.Length ?? 0); i++)
            {
                row[i] = csv.GetField(i) ?? string.Empty;
            }
            records.Add(row);
        }
        return records;
    }

    // ===============================
    // LARGE CSV - Throughput test
    // ===============================

    [BenchmarkCategory("Large"), Benchmark]
    public List<string[]> Large_HeroParser()
    {
        var records = new List<string[]>();
        using var reader = HeroParser.Csv.OpenContent(_largeCsv);
        while (reader.Read())
        {
            var row = new string[reader.CurrentRow.ColumnCount];
            for (int i = 0; i < reader.CurrentRow.ColumnCount; i++)
            {
                row[i] = reader.CurrentRow[i].ToString();
            }
            records.Add(row);
        }
        return records;
    }

    [BenchmarkCategory("Large"), Benchmark]
    public List<string[]> Large_Sep()
    {
        var records = new List<string[]>();
        using var reader = Sep.Reader().FromText(_largeCsv);
        foreach (var readRow in reader)
        {
            var row = new string[readRow.ColCount];
            for (int i = 0; i < readRow.ColCount; i++)
            {
                row[i] = readRow[i].ToString();
            }
            records.Add(row);
        }
        return records;
    }

    [BenchmarkCategory("Large"), Benchmark]
    public List<string[]> Large_Sylvan()
    {
        var records = new List<string[]>();
        using var reader = new StringReader(_largeCsv);
        using var csvReader = Sylvan.Data.Csv.CsvDataReader.Create(reader);

        while (csvReader.Read())
        {
            var row = new string[csvReader.FieldCount];
            for (int i = 0; i < csvReader.FieldCount; i++)
            {
                row[i] = csvReader.GetString(i);
            }
            records.Add(row);
        }
        return records;
    }

    [BenchmarkCategory("Large"), Benchmark]
    public List<string[]> Large_CsvHelper()
    {
        var records = new List<string[]>();
        using var reader = new StringReader(_largeCsv);
        using var csv = new CsvHelper.CsvReader(reader, CultureInfo.InvariantCulture);

        csv.Read();
        csv.ReadHeader();
        while (csv.Read())
        {
            var row = new string[csv.HeaderRecord?.Length ?? 0];
            for (int i = 0; i < (csv.HeaderRecord?.Length ?? 0); i++)
            {
                row[i] = csv.GetField(i) ?? string.Empty;
            }
            records.Add(row);
        }
        return records;
    }
}