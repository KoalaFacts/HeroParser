using System.Data;
using System.Text;
using System.Text.Json.Serialization;
using HeroParser;
using HeroParser.AI;
using HeroParser.Conversion;
using HeroParser.Excels.Core;
using HeroParser.SeparatedValues.Detection;
using HeroParser.Streaming;

// HeroParser AI-pipeline & low-memory showcase.
//
// Demonstrates end-to-end workflows designed around low/zero memory footprint and NativeAOT safety:
//   1. Zero-allocation UTF-8 span row scanning + temporal-aware schema inference.
//   2. Malformed LLM CSV repair + transparent .csv.gz compression via [GenerateBinder].
//   3. OpenAI fine-tuning JSONL export + BatchAsync streaming + flat [GenerateBinder] JSONL (.jsonl.gz).
//   4. Non-boxing DbDataReader CSV/HTB (.htb.gz) export & Htb.CreateDataReader round-trip.
//   5. Styled & merged Excel (.xlsx) report with DateOnly + [GenerateBinder].
//
// To run: dotnet run --project examples/HeroParser.Examples.AiPipeline

Console.WriteLine("HeroParser AI Pipeline & Low-Memory Example");
Console.WriteLine("============================================");
Console.WriteLine();

string tempDir = Path.Combine(Path.GetTempPath(), $"heroparser-ai-{Guid.NewGuid():N}");
Directory.CreateDirectory(tempDir);

try
{
    // 1. Zero-allocation UTF-8 span scanning + temporal-aware schema inference
    string qaCsv = """
        System,Question,Answer,CreatedOn
        "You are a math tutor.","What is 2+2?","4",2026-01-10
        "You are a math tutor.","What is the square root of 16?","4",2026-01-11
        "You are a math tutor.","Is zero an even number?","Yes.",2026-01-12
        "You are a code reviewer.","What is a hoisted lambda?","An anonymous function lifted out of a loop.",2026-01-13
        "You are a code reviewer.","What is RAII?","Resource Acquisition Is Initialization — tying resource lifetime to scope.",2026-01-14
        """;

    byte[] qaUtf8 = Encoding.UTF8.GetBytes(qaCsv);
    int rawRowCount = 0;
    using (var rawReader = Csv.ReadFromByteSpan(qaUtf8))
    {
        while (rawReader.MoveNext())
        {
            rawRowCount++;
        }
    }

    var inferred = Csv.InferSchema(
        qaCsv,
        new CsvSchemaInferenceOptions { DistinguishTemporalTypes = true });

    Console.WriteLine($"1. Zero-alloc UTF-8 scan counted {rawRowCount - 1} data rows + header.");
    Console.WriteLine($"   Inferred columns: {string.Join(", ", inferred.Columns.Select(c => $"{c.Name}:{c.InferredType}"))}");
    Console.WriteLine();

    // 2. LLM CSV repair + transparent .csv.gz persistence via [GenerateBinder]
    string noisyLlmCsv = """
        Here is your cleaned dataset:
        ```csv
        Id,PromptDate,Topic
        1,2026-02-01,"What is C# 14 field keyword?"
        2,2026-02-02,"Explain "zero-alloc" spans
        ```
        Hope this helps!
        """;

    List<RepairedPrompt> repairedPrompts = [];
    await foreach (RepairedPrompt row in LlmRepair.ReadFromTextAsync<RepairedPrompt>(noisyLlmCsv))
    {
        repairedPrompts.Add(row);
    }

    string compressedCsvPath = Path.Combine(tempDir, "repaired.csv.gz");
    Csv.WriteToFile(compressedCsvPath, repairedPrompts);
    List<RepairedPrompt> roundTrippedPrompts = [];
    using (var csvReader = Csv.Read<RepairedPrompt>().FromFile(compressedCsvPath, out _))
    {
        foreach (RepairedPrompt prompt in csvReader)
        {
            roundTrippedPrompts.Add(prompt);
        }
    }

    Console.WriteLine($"2. Repaired {repairedPrompts.Count} LLM CSV rows and round-tripped via .csv.gz ({new FileInfo(compressedCsvPath).Length:N0} bytes).");
    Console.WriteLine(roundTrippedPrompts.GenerateContextCard(datasetName: "Repaired LLM Prompts"));
    Console.WriteLine();

    // 3. OpenAI fine-tuning JSONL + AOT JsonTypeInfo<T> BatchAsync + flat [GenerateBinder] JSONL (.jsonl.gz)
    string chatJsonl = CsvToJsonlConverter.Convert(
        qaCsv,
        CsvToJsonlShape.OpenAiChat(systemColumn: "System", userColumn: "Question", assistantColumn: "Answer"));

    string chatJsonlPath = Path.Combine(tempDir, "training.jsonl");
    await File.WriteAllTextAsync(chatJsonlPath, chatJsonl);

    const int BatchSize = 2;
    int batchCount = 0;
    int totalRecords = 0;

    await foreach (var batch in
        Jsonl.Read<ChatExample>()
            .WithTypeInfo(ExampleJsonContext.Default.ChatExample)
            .FromFileAsync(chatJsonlPath)
            .BatchAsync(BatchSize))
    {
        batchCount++;
        totalRecords += batch.Count;
        Console.WriteLine($"   Batch {batchCount}: {batch.Count} record(s). User Qs: {string.Join(" | ", batch.Select(b => b.Messages?[1].Content))}");
    }

    FlatMetricRow[] metricRows =
    [
        new() { Id = 1, RecordedOn = new DateOnly(2026, 2, 1), Similarity = (Half)0.94f, Latency = TimeSpan.FromMilliseconds(12), Label = "math" },
        new() { Id = 2, RecordedOn = new DateOnly(2026, 2, 2), Similarity = (Half)0.88f, Latency = TimeSpan.FromMilliseconds(18), Label = "code" },
    ];
    string metricsJsonlGzPath = Path.Combine(tempDir, "metrics.jsonl.gz");
    Jsonl.Write<FlatMetricRow>().ToFile(metricsJsonlGzPath, metricRows);
    List<FlatMetricRow> loadedMetrics = [];
    using (var jsonlReader = Jsonl.Read<FlatMetricRow>().FromFile(metricsJsonlGzPath))
    {
        foreach (FlatMetricRow row in jsonlReader)
        {
            loadedMetrics.Add(row);
        }
    }

    Console.WriteLine($"3. Streamed {totalRecords} OpenAI chat records in {batchCount} batches; flat [GenerateBinder] JSONL.gz read back {loadedMetrics.Count} metric rows.");
    Console.WriteLine();

    // 4. Non-boxing DbDataReader export (CSV + compressed binary HTB) & Htb.CreateDataReader
    using var table = new DataTable();
    table.Columns.Add("Id", typeof(int));
    table.Columns.Add("RecordedOn", typeof(DateOnly));
    table.Columns.Add("Score", typeof(decimal));
    table.Columns.Add("Label", typeof(string));
    table.Rows.Add(1, new DateOnly(2026, 2, 1), 0.94m, "math");
    table.Rows.Add(2, new DateOnly(2026, 2, 2), 0.88m, "code");

    using var csvExportReader = table.CreateDataReader();
    string exportedCsv = Csv.WriteFromDataReaderToText(csvExportReader);

    string htbGzPath = Path.Combine(tempDir, "telemetry.htb.gz");
    using var htbSourceReader = table.CreateDataReader();
    Htb.WriteFromDataReaderToFile(htbGzPath, htbSourceReader);

    using var htbDataReader = Htb.CreateDataReader(htbGzPath);
    int htbRows = 0;
    while (htbDataReader.Read())
    {
        htbRows++;
    }

    Console.WriteLine($"4. Non-boxing DataReader export wrote {exportedCsv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length} CSV lines and {htbRows} HTB.gz rows.");
    Console.WriteLine();

    // 5. Styled & merged Excel (.xlsx) report with DateOnly + [GenerateBinder]
    var salesData = new List<SalesRecord>
    {
        new() { Region = "North", Category = "Electronics", ReportDate = new DateOnly(2026, 1, 15), Amount = 12000.50m },
        new() { Region = "North", Category = "Books", ReportDate = new DateOnly(2026, 1, 16), Amount = 450.00m },
        new() { Region = "South", Category = "Electronics", ReportDate = new DateOnly(2026, 1, 17), Amount = 8500.00m },
        new() { Region = "South", Category = "Electronics", ReportDate = new DateOnly(2026, 1, 18), Amount = 9200.75m },
        new() { Region = "South", Category = "Books", ReportDate = new DateOnly(2026, 1, 19), Amount = 1500.25m }
    };

    var blueHeaderStyle = ExcelStyle.Create()
        .WithFont(f => f.WithName("Segoe UI").WithSize(12).WithBold().WithColor("FFFFFF"))
        .WithFill(fill => fill.WithSolidColor("1B365D"))
        .WithAlignment(a => a.WithHorizontal(ExcelHorizontalAlignment.Center));

    var currencyStyle = ExcelStyle.Create()
        .WithFont(f => f.WithName("Segoe UI").WithSize(11))
        .WithNumberFormat("$#,##0.00")
        .WithAlignment(a => a.WithHorizontal(ExcelHorizontalAlignment.Right));

    var textStyle = ExcelStyle.Create()
        .WithFont(f => f.WithName("Segoe UI").WithSize(11))
        .WithAlignment(a => a.WithHorizontal(ExcelHorizontalAlignment.Center));

    string excelPath = Path.Combine(tempDir, "sales_report.xlsx");
    Excel.Write<SalesRecord>()
        .WithHeaderStyle(blueHeaderStyle)
        .WithColumnStyle(x => x.Region, textStyle)
        .WithColumnStyle(x => x.Category, textStyle)
        .WithColumnStyle(x => x.Amount, currencyStyle)
        .WithMergeDuplicates(x => x.Region)
        .ToFile(excelPath, salesData);

    Console.WriteLine($"5. Wrote styled Excel report ({new FileInfo(excelPath).Length:N0} bytes) with merged 'Region' cells:");
    foreach (var record in Excel.Read<SalesRecord>().FromFile(excelPath))
    {
        string regionVal = string.IsNullOrEmpty(record.Region) ? "(merged/empty)" : record.Region;
        Console.WriteLine($"   Region: {regionVal,-15} | Category: {record.Category,-12} | Date: {record.ReportDate:yyyy-MM-dd} | Amount: {record.Amount:C}");
    }
}
finally
{
    if (Directory.Exists(tempDir))
    {
        Directory.Delete(tempDir, recursive: true);
    }
}

return 0;

[GenerateBinder]
internal sealed class RepairedPrompt
{
    public int Id { get; set; }
    public DateOnly PromptDate { get; set; }
    public string Topic { get; set; } = "";
}

[GenerateBinder]
internal sealed class FlatMetricRow
{
    public int Id { get; set; }
    public DateOnly RecordedOn { get; set; }
    public Half Similarity { get; set; }
    public TimeSpan Latency { get; set; }
    public string Label { get; set; } = "";
}

internal sealed class ChatExample
{
    public List<ChatMessage>? Messages { get; set; }
}

internal sealed class ChatMessage
{
    public string? Role { get; set; }
    public string? Content { get; set; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ChatExample))]
[JsonSerializable(typeof(ChatMessage))]
internal partial class ExampleJsonContext : JsonSerializerContext;

[GenerateBinder]
internal sealed class SalesRecord
{
    public string Region { get; set; } = "";
    public string Category { get; set; } = "";
    public DateOnly ReportDate { get; set; }
    public decimal Amount { get; set; }
}
