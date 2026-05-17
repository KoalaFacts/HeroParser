using System.Text.Json;
using HeroParser;
using HeroParser.Conversion;
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
