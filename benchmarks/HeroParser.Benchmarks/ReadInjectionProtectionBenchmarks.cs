using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using HeroParser.SeparatedValues;
using HeroParser.SeparatedValues.Core;
using System.Runtime.CompilerServices;
using System.Text;

namespace HeroParser.Benchmarks;

/// <summary>
/// Benchmarks for reading with injection validation.
/// Compares different approaches for validating CSV fields for injection attacks.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Throughput, iterationCount: 5, warmupCount: 3)]
public class ReadInjectionProtectionBenchmarks
{
    private string csvNormalData = null!;
    private string csvWithNumbers = null!;
    private string csvWithPhones = null!;

    [Params(1_000, 10_000)]
    public int Rows { get; set; }

    [Params(10, 25)]
    public int Columns { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        // Generate CSV with normal values
        var sb = new StringBuilder();
        sb.Append("Name");
        for (int c = 1; c < Columns; c++) sb.Append($",Col{c}");
        sb.AppendLine();
        for (int i = 0; i < Rows; i++)
        {
            sb.Append($"Item{i}");
            for (int c = 1; c < Columns; c++) sb.Append($",Value{i}_{c}");
            sb.AppendLine();
        }
        csvNormalData = sb.ToString();

        // Generate CSV with signed numbers (tests smart detection)
        sb.Clear();
        sb.Append("Name");
        for (int c = 1; c < Columns; c++) sb.Append($",Amount{c}");
        sb.AppendLine();
        for (int i = 0; i < Rows; i++)
        {
            sb.Append($"Item{i}");
            for (int c = 1; c < Columns; c++)
            {
                var amount = (i + c) % 2 == 0 ? $"-{i * 10 + c}" : $"+{i * 10 + c}";
                sb.Append($",{amount}");
            }
            sb.AppendLine();
        }
        csvWithNumbers = sb.ToString();

        // Generate CSV with phone numbers (tests smart detection)
        sb.Clear();
        sb.Append("Name");
        for (int c = 1; c < Columns; c++) sb.Append($",Phone{c}");
        sb.AppendLine();
        for (int i = 0; i < Rows; i++)
        {
            sb.Append($"Person{i}");
            for (int c = 1; c < Columns; c++) sb.Append($",+1-555-{1000 + i + c}");
            sb.AppendLine();
        }
        csvWithPhones = sb.ToString();
    }

    // =====================================================
    // BASELINE - No validation
    // =====================================================

    [Benchmark(Baseline = true)]
    public int Read_NoValidation()
    {
        using var reader = Csv.ReadFromText(csvNormalData);

        int count = 0;
        foreach (var row in reader)
        {
            count += row.ColumnCount;
        }
        return count;
    }

    // =====================================================
    // OLD APPROACH - Access CharSpan per column
    // =====================================================

    [Benchmark]
    public int Read_OldApproach_NormalData()
    {
        using var reader = Csv.ReadFromText(csvNormalData);

        int count = 0;
        foreach (var row in reader)
        {
            for (int c = 0; c < row.ColumnCount; c++)
            {
                var span = row[c].CharSpan;
                IsDangerousField(span);
            }
            count += row.ColumnCount;
        }
        return count;
    }

    [Benchmark]
    public int Read_OldApproach_NumbersData()
    {
        using var reader = Csv.ReadFromText(csvWithNumbers);

        int count = 0;
        foreach (var row in reader)
        {
            for (int c = 0; c < row.ColumnCount; c++)
            {
                var span = row[c].CharSpan;
                IsDangerousField(span);
            }
            count += row.ColumnCount;
        }
        return count;
    }

    [Benchmark]
    public int Read_OldApproach_PhoneData()
    {
        using var reader = Csv.ReadFromText(csvWithPhones);

        int count = 0;
        foreach (var row in reader)
        {
            for (int c = 0; c < row.ColumnCount; c++)
            {
                var span = row[c].CharSpan;
                IsDangerousField(span);
            }
            count += row.ColumnCount;
        }
        return count;
    }

    // =====================================================
    // NEW OPTIMIZED - Direct buffer access via row API
    // =====================================================

    [Benchmark]
    public int Read_Optimized_NormalData()
    {
        using var reader = Csv.ReadFromText(csvNormalData);

        int count = 0;
        foreach (var row in reader)
        {
            // Uses optimized HasDangerousFields() that directly accesses buffer
            _ = row.HasDangerousFields();
            count += row.ColumnCount;
        }
        return count;
    }

    [Benchmark]
    public int Read_Optimized_NumbersData()
    {
        using var reader = Csv.ReadFromText(csvWithNumbers);

        int count = 0;
        foreach (var row in reader)
        {
            _ = row.HasDangerousFields();
            count += row.ColumnCount;
        }
        return count;
    }

    [Benchmark]
    public int Read_Optimized_PhoneData()
    {
        using var reader = Csv.ReadFromText(csvWithPhones);

        int count = 0;
        foreach (var row in reader)
        {
            _ = row.HasDangerousFields();
            count += row.ColumnCount;
        }
        return count;
    }

    // =====================================================
    // NEW OPTIMIZED - Per-column check
    // =====================================================

    [Benchmark]
    public int Read_OptimizedPerCol_NormalData()
    {
        using var reader = Csv.ReadFromText(csvNormalData);

        int count = 0;
        foreach (var row in reader)
        {
            // Uses optimized IsDangerousColumn() for per-column check
            for (int c = 0; c < row.ColumnCount; c++)
            {
                _ = row.IsDangerousColumn(c);
            }
            count += row.ColumnCount;
        }
        return count;
    }

    [Benchmark]
    public int Read_OptimizedPerCol_NumbersData()
    {
        using var reader = Csv.ReadFromText(csvWithNumbers);

        int count = 0;
        foreach (var row in reader)
        {
            for (int c = 0; c < row.ColumnCount; c++)
            {
                _ = row.IsDangerousColumn(c);
            }
            count += row.ColumnCount;
        }
        return count;
    }

    [Benchmark]
    public int Read_OptimizedPerCol_PhoneData()
    {
        using var reader = Csv.ReadFromText(csvWithPhones);

        int count = 0;
        foreach (var row in reader)
        {
            for (int c = 0; c < row.ColumnCount; c++)
            {
                _ = row.IsDangerousColumn(c);
            }
            count += row.ColumnCount;
        }
        return count;
    }

    /// <summary>
    /// Switch-based validation (old approach using CharSpan).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsDangerousField(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty) return false;

        char first = value[0];

        switch (first)
        {
            case '=':
            case '@':
            case '\t':
            case '\r':
                return true;

            case '-':
            case '+':
                if (value.Length == 1) return false;
                char second = value[1];
                return !((uint)(second - '0') <= 9 || second == '.');

            default:
                return false;
        }
    }
}
