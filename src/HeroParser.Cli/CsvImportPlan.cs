using System.Text.Json;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Validation;

namespace HeroParser.Cli;

internal sealed record CsvImportPlan
{
    private const int MAX_PLAN_BYTES = 64 * 1024;

    public char Delimiter { get; init; }
    public bool HasHeaderRow { get; init; } = true;
    public int ExpectedColumnCount { get; init; }
    public int SampledDataRows { get; init; }
    public int SampledWidthMismatches { get; init; }
    public int? DelimiterConfidence { get; init; }
    public bool Utf8BomObserved { get; init; }

    public CsvValidationOptions ToValidationOptions() => new()
    {
        Delimiter = Delimiter,
        HasHeaderRow = HasHeaderRow,
        ExpectedColumnCount = ExpectedColumnCount,
        MaxRows = 0,
        ParseOptions = new CsvReadOptions
        {
            Delimiter = Delimiter,
            MaxColumnCount = 1000,
            MaxRowCount = int.MaxValue,
            AllowNewlinesInsideQuotes = true
        }
    };

    public void Save(string path)
    {
        Validate();
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        writer.WriteNumber("version", 1);
        writer.WriteString("delimiter", Delimiter.ToString());
        writer.WriteBoolean("hasHeaderRow", HasHeaderRow);
        writer.WriteNumber("expectedColumnCount", ExpectedColumnCount);
        writer.WriteNumber("sampledDataRows", SampledDataRows);
        writer.WriteNumber("sampledWidthMismatches", SampledWidthMismatches);
        if (DelimiterConfidence.HasValue)
            writer.WriteNumber("delimiterConfidence", DelimiterConfidence.Value);
        else
            writer.WriteNull("delimiterConfidence");
        writer.WriteBoolean("utf8BomObserved", Utf8BomObserved);
        writer.WriteEndObject();
    }

    public static CsvImportPlan Load(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MAX_PLAN_BYTES)
            throw new InvalidDataException("CSV import plan exceeds 64 KiB.");

        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || root.GetProperty("version").GetInt32() != 1)
            throw new InvalidDataException("Unsupported CSV import plan version.");

        string? delimiter = root.GetProperty("delimiter").GetString();
        if (delimiter?.Length != 1)
            throw new InvalidDataException("Plan delimiter must be one character.");

        var confidenceElement = root.GetProperty("delimiterConfidence");
        var plan = new CsvImportPlan
        {
            Delimiter = delimiter[0],
            HasHeaderRow = root.GetProperty("hasHeaderRow").GetBoolean(),
            ExpectedColumnCount = root.GetProperty("expectedColumnCount").GetInt32(),
            SampledDataRows = root.GetProperty("sampledDataRows").GetInt32(),
            SampledWidthMismatches = root.GetProperty("sampledWidthMismatches").GetInt32(),
            DelimiterConfidence = confidenceElement.ValueKind == JsonValueKind.Null ? null : confidenceElement.GetInt32(),
            Utf8BomObserved = root.GetProperty("utf8BomObserved").GetBoolean()
        };
        plan.Validate();
        return plan;
    }

    private void Validate()
    {
        if (Delimiter > 127 || Delimiter == '"' || (char.IsControl(Delimiter) && Delimiter != '\t') ||
            !HasHeaderRow || ExpectedColumnCount is < 1 or > 1000 ||
            SampledDataRows is < 0 or > 10000 ||
            SampledWidthMismatches < 0 || SampledWidthMismatches > SampledDataRows ||
            DelimiterConfidence is < 0 or > 100)
            throw new InvalidDataException("CSV import plan contains unsupported or inconsistent settings.");
    }
}
