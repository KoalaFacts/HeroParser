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
        // Mixed normal CSV with one intentionally dangerous value per eight data rows.
        var sb = new StringBuilder();
        sb.Append("Name");
        for (int c = 1; c < Columns; c++) sb.Append($",Col{c}");
        sb.AppendLine();
        for (int i = 0; i < Rows; i++)
        {
            sb.Append($"Item{i}");
            for (int c = 1; c < Columns; c++)
            {
                // Keep exactly one suspicious field in every eighth data row.
                // Detection must affect the returned checksum; discarded bools
                // allowed these benchmark calls to be eliminated by the JIT.
                sb.Append(c == 1 && i % 8 == 0 ? ",=cmd" : $",Value{i}_{c}");
            }
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
        // Fixture and every actual timed path must agree before BDN starts.
        ValidateGeneratedRows(csvNormalData, "Col", "Item");
        ValidateGeneratedRows(csvWithNumbers, "Amount", "Item");
        ValidateGeneratedRows(csvWithPhones, "Phone", "Person");
        ValidateTimedResults();
    }

    private void ValidateGeneratedRows(string csv, string headerPrefix, string rowPrefix)
    {
        using var reader = Csv.ReadFromText(csv);
        int rowIndex = -1; // Header is deliberately included in raw CSV parsing.
        foreach (var row in reader)
        {
            if (row.ColumnCount != Columns)
                throw new InvalidOperationException($"Injection fixture row {rowIndex} has {row.ColumnCount} columns; expected {Columns}.");

            for (int c = 0; c < Columns; c++)
            {
                string expected = rowIndex < 0
                    ? (c == 0 ? "Name" : $"{headerPrefix}{c}")
                    : c == 0
                        ? $"{rowPrefix}{rowIndex}"
                        : headerPrefix switch
                        {
                            "Col" => c == 1 && rowIndex % 8 == 0 ? "=cmd" : $"Value{rowIndex}_{c}",
                            "Amount" => (rowIndex + c) % 2 == 0 ? $"-{rowIndex * 10 + c}" : $"+{rowIndex * 10 + c}",
                            "Phone" => $"+1-555-{1000 + rowIndex + c}",
                            _ => throw new InvalidOperationException("Unknown benchmark fixture.")
                        };

                if (!row[c].Span.SequenceEqual(expected.AsSpan()))
                    throw new InvalidOperationException($"Injection fixture {headerPrefix}: field {c} in row {rowIndex} is incorrect.");
            }
            rowIndex++;
        }
        if (rowIndex != Rows)
            throw new InvalidOperationException($"Injection fixture {headerPrefix}: {rowIndex} data rows instead of {Rows}.");
    }

    private void ValidateTimedResults()
    {
        int cells = checked((Rows + 1) * Columns);
        int detected = (Rows + 7) / 8;
        RequireCount(nameof(Read_NoValidation), Read_NoValidation(), cells);

        RequireCount(nameof(Read_OldApproach_NormalData), Read_OldApproach_NormalData(), cells + detected);
        RequireCount(nameof(Read_Optimized_NormalData), Read_Optimized_NormalData(), cells + detected);
        RequireCount(nameof(Read_OptimizedPerCol_NormalData), Read_OptimizedPerCol_NormalData(), cells + detected);

        RequireCount(nameof(Read_OldApproach_NumbersData), Read_OldApproach_NumbersData(), cells);
        RequireCount(nameof(Read_Optimized_NumbersData), Read_Optimized_NumbersData(), cells);
        RequireCount(nameof(Read_OptimizedPerCol_NumbersData), Read_OptimizedPerCol_NumbersData(), cells);

        RequireCount(nameof(Read_OldApproach_PhoneData), Read_OldApproach_PhoneData(), cells);
        RequireCount(nameof(Read_Optimized_PhoneData), Read_Optimized_PhoneData(), cells);
        RequireCount(nameof(Read_OptimizedPerCol_PhoneData), Read_OptimizedPerCol_PhoneData(), cells);
    }

    private static void RequireCount(string method, int actual, int expected)
    {
        if (actual != expected)
            throw new InvalidOperationException($"Injection benchmark {method}: checksum {actual}, expected {expected}.");
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
                var span = row[c].Span;
                if (IsDangerousField(span)) count++;
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
                var span = row[c].Span;
                if (IsDangerousField(span)) count++;
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
                var span = row[c].Span;
                if (IsDangerousField(span)) count++;
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
            if (row.HasDangerousFields()) count++;
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
            if (row.HasDangerousFields()) count++;
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
            if (row.HasDangerousFields()) count++;
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
                if (row.IsDangerousColumn(c)) count++;
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
                if (row.IsDangerousColumn(c)) count++;
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
                if (row.IsDangerousColumn(c)) count++;
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
