using System.Text.Json;
using HeroParser;
using HeroParser.Conversion;
using HeroParser.Excels.Core;
using HeroParser.Streaming;

// HeroParser AI-pipeline example.
//
// Demonstrates the AI-trends features end-to-end:
//   1. Read a CSV of question/answer pairs.
//   2. Convert to OpenAI chat-completion JSONL via CsvToJsonlConverter (fine-tuning shape).
//   3. Stream the JSONL back in and batch records via BatchAsync — ready to ship to an embedding API.
//
// To run:  dotnet run --project examples/HeroParser.Examples.AiPipeline

Console.WriteLine("HeroParser AI Pipeline Example");
Console.WriteLine("================================");
Console.WriteLine();

// Step 1: build a small in-memory CSV of Q/A pairs.
string csv = """
    System,Question,Answer
    "You are a math tutor.","What is 2+2?","4"
    "You are a math tutor.","What is the square root of 16?","4"
    "You are a math tutor.","Is zero an even number?","Yes."
    "You are a code reviewer.","What is a hoisted lambda?","An anonymous function lifted out of a loop."
    "You are a code reviewer.","What is RAII?","Resource Acquisition Is Initialization — a C++ idiom for tying resource lifetime to object lifetime."
    """;

Console.WriteLine($"Input CSV: {csv.Split('\n').Length} rows");

// Step 2: convert to OpenAI fine-tuning JSONL.
string jsonl = CsvToJsonlConverter.Convert(
    csv,
    CsvToJsonlShape.OpenAiChat(systemColumn: "System", userColumn: "Question", assistantColumn: "Answer"));

string outputPath = Path.Combine(Path.GetTempPath(), "training.jsonl");
File.WriteAllText(outputPath, jsonl);

Console.WriteLine($"Wrote OpenAI fine-tuning JSONL to: {outputPath}");
Console.WriteLine($"   Bytes: {new FileInfo(outputPath).Length:N0}");
Console.WriteLine();
Console.WriteLine("First record:");
Console.WriteLine($"   {jsonl.Split('\n')[0]}");
Console.WriteLine();

// Step 3: stream JSONL back in and batch — the shape an embedding-API client would consume.
const int BatchSize = 2;
int batchCount = 0;
int totalRecords = 0;

JsonSerializerOptions readerJsonOptions = new(JsonSerializerDefaults.Web);

await foreach (var batch in
    Jsonl.Read<ChatExample>()
        .WithJsonOptions(readerJsonOptions)
        .FromFileAsync(outputPath)
        .BatchAsync(BatchSize))
{
    batchCount++;
    totalRecords += batch.Count;
    Console.WriteLine($"Batch {batchCount}: {batch.Count} record(s). User Qs: {string.Join(" | ", batch.Select(b => b.Messages?[1].Content))}");
}

Console.WriteLine();
Console.WriteLine($"Streamed {totalRecords} record(s) across {batchCount} batch(es) of {BatchSize}.");
Console.WriteLine();
Console.WriteLine("In a real pipeline this is where you'd POST each batch to OpenAI/Voyage/Cohere embeddings,");
Console.WriteLine("or push each Jsonl.CreateDataReader row into SqlBulkCopy.");

// ============================================================================
// Excel Styling and Merging Example
// ============================================================================
Console.WriteLine();
Console.WriteLine("Excel Styling and Merging Example");
Console.WriteLine("=================================");
Console.WriteLine();

var salesData = new List<SalesRecord>
{
    new() { Region = "North", Category = "Electronics", Amount = 12000.50m },
    new() { Region = "North", Category = "Books", Amount = 450.00m },
    new() { Region = "South", Category = "Electronics", Amount = 8500.00m },
    new() { Region = "South", Category = "Electronics", Amount = 9200.75m },
    new() { Region = "South", Category = "Books", Amount = 1500.25m }
};

var blueHeaderStyle = ExcelStyle.Create()
    .WithFont(f => f.WithName("Segoe UI").WithSize(12).WithBold().WithColor("FFFFFF"))
    .WithFill(fill => fill.WithSolidColor("1B365D")) // Deep navy background
    .WithAlignment(a => a.WithHorizontal(ExcelHorizontalAlignment.Center));

var currencyStyle = ExcelStyle.Create()
    .WithFont(f => f.WithName("Segoe UI").WithSize(11))
    .WithNumberFormat("$#,##0.00")
    .WithAlignment(a => a.WithHorizontal(ExcelHorizontalAlignment.Right));

var textStyle = ExcelStyle.Create()
    .WithFont(f => f.WithName("Segoe UI").WithSize(11))
    .WithAlignment(a => a.WithHorizontal(ExcelHorizontalAlignment.Center));

string excelPath = Path.Combine(Path.GetTempPath(), "sales_report.xlsx");

// Write with header style, custom column styles, and automatic merging of duplicate contiguous Regions
Excel.Write<SalesRecord>()
    .WithHeaderStyle(blueHeaderStyle)
    .WithColumnStyle(x => x.Region, textStyle)
    .WithColumnStyle(x => x.Category, textStyle)
    .WithColumnStyle(x => x.Amount, currencyStyle)
    .WithMergeDuplicates(x => x.Region) // Vertically merge contiguous identical Regions
    .ToFile(excelPath, salesData);

Console.WriteLine($"Wrote styled Excel report to: {excelPath}");
Console.WriteLine($"   Bytes: {new FileInfo(excelPath).Length:N0}");
Console.WriteLine("   Merged regions on the 'Region' column dynamically.");
Console.WriteLine();

// Read it back to verify the values are present (top-left cell has value, duplicate cells are empty)
var readBack = Excel.Read<SalesRecord>().FromFile(excelPath);
Console.WriteLine("Read back values (demonstrating that auto-merged duplicates are stored in the top-left cell):");
foreach (var record in readBack)
{
    string regionVal = string.IsNullOrEmpty(record.Region) ? "(merged/empty)" : record.Region;
    Console.WriteLine($"   Region: {regionVal,-15} | Category: {record.Category,-12} | Amount: {record.Amount:C}");
}

return 0;

internal sealed class ChatExample
{
    public List<ChatMessage>? Messages { get; set; }
}

internal sealed class ChatMessage
{
    public string? Role { get; set; }
    public string? Content { get; set; }
}

internal sealed class SalesRecord
{
    public string Region { get; set; } = "";
    public string Category { get; set; } = "";
    public decimal Amount { get; set; }
}
