using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using HeroParser.Conversion;
using HeroParser.JsonLines.Reading;
using HeroParser.SeparatedValues.Core;

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
    private CsvToJsonlOptions csvConversionOptions = null!;

    [Params(10_000, 100_000)]
    public int Rows { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        serializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        // CsvReadOptions.MaxRowCount counts the CSV header as a row too.
        csvConversionOptions = new CsvToJsonlOptions { MaxRowCount = checked(Rows + 1) };

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
        ValidateConversions();

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

        // The timed writers do real serialization, so verify their complete
        // output—not merely equal byte counts—before any benchmark is timed.
        string reflectionText = WriteToText();
        if (!string.Equals(reflectionText, WriteToText_SourceGenerated(), StringComparison.Ordinal))
            throw new InvalidOperationException("JSONL reflection and source-generated writers produced different output.");
        ValidateRows(Jsonl.Read<FlatRow>().WithJsonOptions(serializerOptions).FromText(reflectionText), "reflection writer text");
        byte[] expectedUtf8 = Encoding.UTF8.GetBytes(reflectionText);

        using var reflectionOutput = new MemoryStream(capacity: expectedUtf8.Length);
        Jsonl.Write<FlatRow>().WithJsonOptions(serializerOptions).ToStream(reflectionOutput, rows, leaveOpen: true);
        ValidateStreamOutput(reflectionOutput, expectedUtf8, "reflection");

        using var generatedOutput = new MemoryStream(capacity: expectedUtf8.Length);
        Jsonl.Write<FlatRow>().WithTypeInfo(FlatRowJsonContext.Default.FlatRow).ToStream(generatedOutput, rows, leaveOpen: true);
        ValidateStreamOutput(generatedOutput, expectedUtf8, "source-generated");
    }

    private static void ValidateStreamOutput(MemoryStream output, ReadOnlySpan<byte> expectedUtf8, string variant)
    {
        if (!output.GetBuffer().AsSpan(0, checked((int)output.Length)).SequenceEqual(expectedUtf8))
            throw new InvalidOperationException($"JSONL {variant} stream writer output differs from the verified UTF-8 text output.");
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

    private void ValidateConversions()
    {
        // Conversion benchmarks are timed separately from reader/writer benchmarks.
        // Verify their complete logical output during setup rather than assuming
        // that successful execution or the correct row count implies correct data.
        using (var convertedJsonl = new StringReader(ConvertCsvToJsonlFlat()))
        {
            for (int index = 0; index < rows.Count; index++)
            {
                string line = convertedJsonl.ReadLine()
                    ?? throw new InvalidOperationException($"CSV-to-JSONL ended before row {index}.");
                using JsonDocument document = JsonDocument.Parse(line);
                JsonElement actual = document.RootElement;
                FlatRow expected = rows[index];
                if (actual.ValueKind != JsonValueKind.Object ||
                    actual.EnumerateObject().Count() != 4 ||
                    actual.GetProperty("Id").GetString() != expected.Id.ToString(CultureInfo.InvariantCulture) ||
                    actual.GetProperty("Name").GetString() != expected.Name ||
                    actual.GetProperty("Amount").GetString() != expected.Amount.ToString(CultureInfo.InvariantCulture) ||
                    actual.GetProperty("Active").GetString() != expected.Active.ToString())
                {
                    throw new InvalidOperationException($"CSV-to-JSONL differs from input at record {index}.");
                }
            }
            if (convertedJsonl.ReadLine() is not null)
                throw new InvalidOperationException("CSV-to-JSONL emitted extra records.");
        }

        // The reverse converter emits a header as well as all JSONL data rows.
        // Give the validation reader a limit that includes that header.
        using var convertedCsv = Csv.ReadFromText(ConvertJsonlToCsv(), new CsvReadOptions
        {
            MaxColumnCount = 4,
            MaxRowCount = checked(Rows + 1)
        });
        if (!convertedCsv.MoveNext() ||
            convertedCsv.Current.ColumnCount != 4 ||
            convertedCsv.Current[0].UnquoteToString() != "id" ||
            convertedCsv.Current[1].UnquoteToString() != "name" ||
            convertedCsv.Current[2].UnquoteToString() != "amount" ||
            convertedCsv.Current[3].UnquoteToString() != "active")
        {
            throw new InvalidOperationException("JSONL-to-CSV did not preserve the expected header.");
        }

        int csvRow = 0;
        while (convertedCsv.MoveNext())
        {
            if (csvRow >= rows.Count)
                throw new InvalidOperationException("JSONL-to-CSV emitted extra records.");
            FlatRow expected = rows[csvRow];
            var actual = convertedCsv.Current;
            if (actual.ColumnCount != 4 ||
                actual[0].UnquoteToString() != expected.Id.ToString(CultureInfo.InvariantCulture) ||
                actual[1].UnquoteToString() != expected.Name ||
                actual[2].UnquoteToString() != expected.Amount.ToString(CultureInfo.InvariantCulture) ||
                actual[3].UnquoteToString() != (expected.Active ? "true" : "false"))
            {
                throw new InvalidOperationException($"JSONL-to-CSV differs from input at record {csvRow}.");
            }
            csvRow++;
        }
        if (csvRow != rows.Count)
            throw new InvalidOperationException($"JSONL-to-CSV emitted {csvRow} of {rows.Count} records.");
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
    public long ReadFromText()
    {
        long total = 0;
        foreach (FlatRow row in Jsonl.Read<FlatRow>().WithJsonOptions(serializerOptions).FromText(jsonl))
        {
            total += row.Id;
        }
        return total;
    }

    [Benchmark]
    public long ReadFromStream()
    {
        using var ms = new MemoryStream(jsonlUtf8, writable: false);
        long total = 0;
        foreach (FlatRow row in Jsonl.Read<FlatRow>().WithJsonOptions(serializerOptions).FromStream(ms))
        {
            total += row.Id;
        }
        return total;
    }

    [Benchmark]
    public async Task<long> ReadFromFileAsync()
    {
        long total = 0;
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
    public long ReadFromText_SourceGenerated()
    {
        long total = 0;
        foreach (FlatRow row in Jsonl.Read<FlatRow>().WithTypeInfo(FlatRowJsonContext.Default.FlatRow).FromText(jsonl))
        {
            total += row.Id;
        }
        return total;
    }

    [Benchmark]
    public long ReadFromStream_SourceGenerated()
    {
        using var ms = new MemoryStream(jsonlUtf8, writable: false);
        long total = 0;
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
        => CsvToJsonlConverter.Convert(csv, CsvToJsonlShape.FlatObject(), csvConversionOptions);

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
