using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using HeroParser.Conversion;
using HeroParser.JsonLines.Reading;

namespace HeroParser.Benchmarks;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(JsonlBenchmark.FlatRow))]
[JsonSerializable(typeof(List<JsonlBenchmark.FlatRow>))]
internal partial class FlatRowJsonContext : JsonSerializerContext
{
}

[MemoryDiagnoser]
[SimpleJob(RunStrategy.Throughput, iterationCount: 5, warmupCount: 3)]
public class JsonlBenchmark
{
    private string jsonl = null!;
    private byte[] jsonlUtf8 = null!;
    private string filePath = null!;
    private string csv = null!;
    private List<FlatRow> rows = null!;
    private JsonSerializerOptions serializerOptions = null!;

    [Params(10_000, 100_000)]
    public int Rows { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        serializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        rows = new List<FlatRow>(Rows);
        var sb = new StringBuilder(Rows * 80);
        var csvSb = new StringBuilder(Rows * 60);
        csvSb.AppendLine("Id,Name,Amount,Active");
        for (int i = 0; i < Rows; i++)
        {
            string name = $"customer-{i}";
            decimal amount = (decimal)(i * 1.5);
            bool active = (i & 1) == 0;
            rows.Add(new FlatRow { Id = i, Name = name, Amount = amount, Active = active });
            sb.Append("{\"id\":").Append(i)
              .Append(",\"name\":\"").Append(name)
              .Append("\",\"amount\":").Append(amount.ToString(System.Globalization.CultureInfo.InvariantCulture))
              .Append(",\"active\":").Append(active ? "true" : "false")
              .Append('}').Append('\n');
            csvSb.Append(i).Append(',').Append(name).Append(',')
                 .Append(amount.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                 .Append(active).Append('\n');
        }

        jsonl = sb.ToString();
        jsonlUtf8 = Encoding.UTF8.GetBytes(jsonl);
        csv = csvSb.ToString();

        ValidateReadersAndWriters();

        filePath = Path.Combine(Path.GetTempPath(), $"heroparser-jsonl-{Rows}-{Guid.NewGuid():N}.jsonl");
        File.WriteAllBytes(filePath, jsonlUtf8);
    }

    private void ValidateReadersAndWriters()
    {
        // Validate complete records outside timing; an Id checksum alone misses
        // silently defaulted names, amounts, booleans, and JSON naming differences.
        ValidateRows(Jsonl.Read<FlatRow>().WithJsonOptions(serializerOptions).FromText(jsonl), "reflection text");
        ValidateRows(Jsonl.Read<FlatRow>().WithTypeInfo(FlatRowJsonContext.Default.FlatRow).FromText(jsonl), "source-generated text");

        using var reflectionStream = new MemoryStream(jsonlUtf8, writable: false);
        ValidateRows(Jsonl.Read<FlatRow>().WithJsonOptions(serializerOptions).FromStream(reflectionStream), "reflection stream");
        using var generatedStream = new MemoryStream(jsonlUtf8, writable: false);
        ValidateRows(Jsonl.Read<FlatRow>().WithTypeInfo(FlatRowJsonContext.Default.FlatRow).FromStream(generatedStream), "source-generated stream");

        if (!string.Equals(WriteToText(), WriteToText_SourceGenerated(), StringComparison.Ordinal))
            throw new InvalidOperationException("JSONL reflection and source-generated writers produced different output.");
    }

    private void ValidateRows(JsonlRecordReader<FlatRow> reader, string variant)
    {
        using (reader)
        {
            int index = 0;
            foreach (FlatRow actual in reader)
            {
                if (index >= rows.Count)
                    throw new InvalidOperationException($"JSONL {variant}: unexpected extra record at {index}.");
                FlatRow expected = rows[index];
                if (actual.Id != expected.Id ||
                    !string.Equals(actual.Name, expected.Name, StringComparison.Ordinal) ||
                    actual.Amount != expected.Amount || actual.Active != expected.Active)
                    throw new InvalidOperationException($"JSONL {variant}: record {index} differs from the generated input.");
                index++;
            }
            if (index != rows.Count)
                throw new InvalidOperationException($"JSONL {variant}: expected {rows.Count} records, read {index}.");
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        try
        {
            if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                File.Delete(filePath);
        }
        catch (IOException)
        {
            // best-effort cleanup
        }
        catch (UnauthorizedAccessException)
        {
            // best-effort cleanup
        }
    }

    [Benchmark(Baseline = true)]
    public int ReadFromText()
    {
        int total = 0;
        foreach (FlatRow row in Jsonl.Read<FlatRow>().WithJsonOptions(serializerOptions).FromText(jsonl))
        {
            total += row.Id;
        }
        return total;
    }

    [Benchmark]
    public int ReadFromStream()
    {
        using var ms = new MemoryStream(jsonlUtf8, writable: false);
        int total = 0;
        foreach (FlatRow row in Jsonl.Read<FlatRow>().WithJsonOptions(serializerOptions).FromStream(ms))
        {
            total += row.Id;
        }
        return total;
    }

    [Benchmark]
    public async Task<int> ReadFromFileAsync()
    {
        int total = 0;
        await foreach (FlatRow row in Jsonl.Read<FlatRow>().WithJsonOptions(serializerOptions).FromFileAsync(filePath))
        {
            total += row.Id;
        }
        return total;
    }

    [Benchmark]
    public string WriteToText()
        => Jsonl.Write<FlatRow>().WithJsonOptions(serializerOptions).ToText(rows);

    [Benchmark]
    public long WriteToStream()
    {
        using var ms = new MemoryStream(capacity: jsonlUtf8.Length);
        Jsonl.Write<FlatRow>().WithJsonOptions(serializerOptions).ToStream(ms, rows, leaveOpen: true);
        return ms.Length;
    }

    [Benchmark]
    public int ReadFromText_SourceGenerated()
    {
        int total = 0;
        foreach (FlatRow row in Jsonl.Read<FlatRow>().WithTypeInfo(FlatRowJsonContext.Default.FlatRow).FromText(jsonl))
        {
            total += row.Id;
        }
        return total;
    }

    [Benchmark]
    public int ReadFromStream_SourceGenerated()
    {
        using var ms = new MemoryStream(jsonlUtf8, writable: false);
        int total = 0;
        foreach (FlatRow row in Jsonl.Read<FlatRow>().WithTypeInfo(FlatRowJsonContext.Default.FlatRow).FromStream(ms))
        {
            total += row.Id;
        }
        return total;
    }

    [Benchmark]
    public string WriteToText_SourceGenerated()
        => Jsonl.Write<FlatRow>().WithTypeInfo(FlatRowJsonContext.Default.FlatRow).ToText(rows);

    [Benchmark]
    public long WriteToStream_SourceGenerated()
    {
        using var ms = new MemoryStream(capacity: jsonlUtf8.Length);
        Jsonl.Write<FlatRow>().WithTypeInfo(FlatRowJsonContext.Default.FlatRow).ToStream(ms, rows, leaveOpen: true);
        return ms.Length;
    }

    [Benchmark]
    public string ConvertCsvToJsonlFlat()
        => CsvToJsonlConverter.Convert(csv, CsvToJsonlShape.FlatObject());

    [Benchmark]
    public string ConvertJsonlToCsv()
        => JsonlToCsvConverter.Convert(jsonl);

    public sealed class FlatRow
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public decimal Amount { get; set; }
        public bool Active { get; set; }
    }
}
