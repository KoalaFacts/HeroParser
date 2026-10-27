using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using CsvHelper;
using CsvHelper.Configuration;
using HeroParser.SeparatedValues.Core;
using nietras.SeparatedValues;
using Sylvan.Data.Csv;
using System.Globalization;
using System.Text;

namespace HeroParser.Benchmarks;

// These benchmarks measure row scanning, not value materialization or typed binding.
// Full decoded-value equivalence is checked once in GlobalSetup, outside timing.
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Throughput, iterationCount: 5, warmupCount: 3)]
public class VsSepReadingBenchmarks
{
    private string csv = null!;
    private byte[] utf8 = null!;

    [Params(10_000)]
    public int Rows { get; set; }

    [Params(25)]
    public int Columns { get; set; }

    [Params(false, true)]
    public bool WithQuotes { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        csv = GenerateCsv(Rows, Columns, WithQuotes);
        utf8 = Encoding.UTF8.GetBytes(csv);
        ValidateReaders();
        ValidateCellCount("Sep", Sep_Parse());
        ValidateCellCount("Sylvan", Sylvan_Parse());
        ValidateCellCount("CsvHelper", CsvHelper_Parse());
        ValidateCellCount("HeroParser UTF-16", HeroParser_ParseString());
        ValidateCellCount("HeroParser UTF-8", HeroParser_ParseBytes());
    }

    internal static string GenerateCsv(int rows, int columns, bool withQuotes)
    {
        var sb = new StringBuilder();
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                if (c > 0) sb.Append(',');
                string value = GetExpectedValue(r, c, columns, withQuotes);
                if (withQuotes && (r * columns + c) % 2 == 0)
                    sb.Append('"').Append(value.Replace("\"", "\"\"", StringComparison.Ordinal)).Append('"');
                else
                    sb.Append(value);
            }
            sb.Append('\n');
        }
        return sb.ToString();
    }

    private static string GetExpectedValue(int row, int column, int columns, bool withQuotes)
    {
        string value = $"value_{row}_{column}";
        int index = row * columns + column;
        if (!withQuotes || index % 2 != 0)
            return value;

        // Half the fields are quoted, including actual CSV escaping and UTF-8 work.
        return (index / 2 % 5) switch
        {
            0 => value + ",comma",
            1 => value + "\"quote\"",
            2 => value + "\nnext line",
            3 => string.Empty,
            _ => value + "_中文_😀"
        };
    }

    private static SepReader CreateSepReader(string text, bool unescape = false)
        => Sep.New(',').Reader(options => options with { HasHeader = false, Unescape = unescape }).FromText(text);

    private static CsvDataReaderOptions CreateSylvanOptions()
        => new() { HasHeaders = false, Delimiter = ',' };

    private static CsvConfiguration CreateCsvHelperOptions()
        => new(CultureInfo.InvariantCulture) { HasHeaderRecord = false, Delimiter = "," };

    private static CsvReadOptions CreateHeroOptions()
        => new()
        {
            Delimiter = ',',
            MaxColumnCount = 1_000,
            MaxRowCount = 1_000_000,
            // Every library remains quote-aware, including for unquoted input.
            EnableQuotedFields = true,
            AllowNewlinesInsideQuotes = true
        };

    [Benchmark(Baseline = true, Description = "Sep")]
    public int Sep_Parse()
    {
        using var reader = CreateSepReader(csv);
        int total = 0;
        foreach (var row in reader)
            total += row.ColCount;
        return total;
    }

    [Benchmark(Description = "Sylvan")]
    public int Sylvan_Parse()
    {
        using var reader = new StringReader(csv);
        using var sylvan = Sylvan.Data.Csv.CsvDataReader.Create(reader, CreateSylvanOptions());
        int total = 0;
        while (sylvan.Read())
            total += sylvan.FieldCount;
        return total;
    }

    [Benchmark(Description = "CsvHelper")]
    public int CsvHelper_Parse()
    {
        using var reader = new StringReader(csv);
        using var csvHelper = new CsvReader(reader, CreateCsvHelperOptions());
        int total = 0;
        while (csvHelper.Read())
            total += csvHelper.Parser.Count;
        return total;
    }

    [Benchmark(Description = "HeroParser UTF-16 (string)")]
    public int HeroParser_ParseString()
    {
        using var reader = Csv.ReadFromText(csv, CreateHeroOptions());
        int total = 0;
        foreach (var row in reader)
            total += row.ColumnCount;
        return total;
    }

    [Benchmark(Description = "HeroParser UTF-8 (byte[])")]
    public int HeroParser_ParseBytes()
    {
        using var reader = Csv.ReadFromByteSpan(utf8, CreateHeroOptions());
        int total = 0;
        foreach (var row in reader)
            total += row.ColumnCount;
        return total;
    }

    private void ValidateReaders()
    {
        int index = 0;
        using (var reader = CreateSepReader(csv, unescape: true))
        {
            foreach (var row in reader)
            {
                ValidateColumnCount("Sep", index, row.ColCount);
                for (int c = 0; c < Columns; c++)
                    ValidateValue("Sep", index, c, row[c].ToString());
                index++;
            }
        }
        ValidateRowCount("Sep", index);

        index = 0;
        using (var textReader = new StringReader(csv))
        using (var reader = Sylvan.Data.Csv.CsvDataReader.Create(textReader, CreateSylvanOptions()))
        {
            while (reader.Read())
            {
                ValidateColumnCount("Sylvan", index, reader.FieldCount);
                for (int c = 0; c < Columns; c++)
                    ValidateValue("Sylvan", index, c, reader.GetString(c));
                index++;
            }
        }
        ValidateRowCount("Sylvan", index);

        index = 0;
        using (var textReader = new StringReader(csv))
        using (var reader = new CsvReader(textReader, CreateCsvHelperOptions()))
        {
            while (reader.Read())
            {
                ValidateColumnCount("CsvHelper", index, reader.Parser.Count);
                for (int c = 0; c < Columns; c++)
                    ValidateValue("CsvHelper", index, c, reader.GetField(c));
                index++;
            }
        }
        ValidateRowCount("CsvHelper", index);

        index = 0;
        using (var reader = Csv.ReadFromText(csv, CreateHeroOptions()))
        {
            foreach (var row in reader)
            {
                ValidateColumnCount("HeroParser UTF-16", index, row.ColumnCount);
                for (int c = 0; c < Columns; c++)
                    ValidateValue("HeroParser UTF-16", index, c, row[c].UnquoteToString());
                index++;
            }
        }
        ValidateRowCount("HeroParser UTF-16", index);

        index = 0;
        using (var reader = Csv.ReadFromByteSpan(utf8, CreateHeroOptions()))
        {
            foreach (var row in reader)
            {
                ValidateColumnCount("HeroParser UTF-8", index, row.ColumnCount);
                for (int c = 0; c < Columns; c++)
                    ValidateValue("HeroParser UTF-8", index, c, row[c].UnquoteToString());
                index++;
            }
        }
        ValidateRowCount("HeroParser UTF-8", index);
    }

    private void ValidateColumnCount(string library, int row, int count)
    {
        if (row >= Rows || count != Columns)
            throw new InvalidOperationException($"{library}: invalid shape at row {row}: {count} columns.");
    }

    private void ValidateCellCount(string library, int count)
    {
        int expected = checked(Rows * Columns);
        if (count != expected)
            throw new InvalidOperationException($"{library}: expected {expected} scanned cells, read {count}.");
    }

    private void ValidateRowCount(string library, int count)
    {
        if (count != Rows)
            throw new InvalidOperationException($"{library}: expected {Rows} data rows, read {count}.");
    }

    private void ValidateValue(string library, int row, int column, string? actual)
    {
        string expected = GetExpectedValue(row, column, Columns, WithQuotes);
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
            throw new InvalidOperationException($"{library}: decoded value mismatch at row {row}, column {column}.");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        Console.WriteLine();
        Console.WriteLine("=== Comparison Analysis ===");
        Console.WriteLine($"CSV size: {csv.Length:N0} chars ({csv.Length * 2:N0} bytes)");
        Console.WriteLine($"Rows: {Rows:N0}, Columns: {Columns}");
        Console.WriteLine($"HeroParser using: {Hardware.GetHardwareInfo()}");
        Console.WriteLine();
    }
}
