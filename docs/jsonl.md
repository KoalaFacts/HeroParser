# JSONL API Reference

[Back to README](../README.md)

JSONL (JSON Lines, also called NDJSON) — one JSON object per line — is the dominant interchange format for LLM fine-tuning datasets (OpenAI, Anthropic, HuggingFace), model evaluations, synthetic data, and streamed AI responses. HeroParser ships a first-class JSONL reader/writer that mirrors the `Csv` and `Excel` builder pattern, plus a `DbDataReader` adapter, CSV↔JSONL converters, a `VectorParser` for inline embedding columns, and an async `BatchAsync` extension for embedding-API pipelines.

The primary entry point is the `Jsonl` static class.

---

## Table of Contents

1. [JSONL Reading](#1-jsonl-reading)
   - [Quick start](#11-quick-start)
   - [Fluent builder options](#12-fluent-builder-options)
   - [Async streaming](#13-async-streaming)
   - [Raw line iteration](#14-raw-line-iteration)
   - [PipeReader integration](#15-pipereader-integration)
   - [DataReader for database bulk loading](#16-datareader-for-database-bulk-loading)
   - [OnError callback](#17-onerror-callback)
   - [Progress reporting](#18-progress-reporting)
   - [AOT / trimming support](#19-aot--trimming-support)
2. [JSONL Writing](#2-jsonl-writing)
   - [Quick start](#21-quick-start)
   - [Fluent builder options](#22-fluent-builder-options)
   - [Async writing](#23-async-writing)
   - [OnError callback](#24-onerror-callback)
3. [Options Reference](#3-options-reference)
   - [JsonlReadOptions](#31-jsonlreadoptions)
   - [JsonlWriteOptions](#32-jsonlwriteoptions)
   - [JsonlDataReaderOptions](#33-jsonldatareaderoptions)
4. [CSV ↔ JSONL Conversion](#4-csv--jsonl-conversion)
   - [CSV → JSONL (flat)](#41-csv--jsonl-flat)
   - [CSV → JSONL (OpenAI chat shape)](#42-csv--jsonl-openai-chat-shape)
   - [CSV → JSONL (Anthropic messages shape)](#43-csv--jsonl-anthropic-messages-shape)
   - [JSONL → CSV](#44-jsonl--csv)
5. [BatchAsync — Embedding API Pipelines](#5-batchasync--embedding-api-pipelines)
6. [VectorParser — Inline Embedding Columns](#6-vectorparser--inline-embedding-columns)
7. [Errors, Security, and DoS Limits](#7-errors-security-and-dos-limits)

---

## 1. JSONL Reading

### 1.1 Quick start

```csharp
public sealed class ChatExample
{
    public List<ChatMessage>? Messages { get; set; }
}

public sealed class ChatMessage
{
    public string? Role { get; set; }
    public string? Content { get; set; }
}

// Sync — load to list
List<ChatExample> all = Jsonl.Read<ChatExample>()
    .FromFile("training.jsonl")
    .ToList();

// Async streaming — process millions of lines without buffering
await foreach (ChatExample example in Jsonl.Read<ChatExample>().FromFileAsync("training.jsonl"))
{
    Console.WriteLine(example.Messages?[^1].Content);
}
```

Deserialization is performed by `System.Text.Json`. JSONL is line-delimited: HeroParser splits on `\n`, transparently strips `\r` from `\r\n`, removes any UTF-8 BOM, and enforces a per-line size cap (default 1 MiB).

### 1.2 Fluent builder options

```csharp
var records = Jsonl.Read<ChatExample>()
    .WithJsonOptions(new JsonSerializerOptions(JsonSerializerDefaults.Web))
    .SkipEmptyLines(true)            // default true
    .WithMaxLineSize(4 * 1024 * 1024) // 4 MiB cap (default 1 MiB)
    .WithMaxRowCount(5_000_000)
    .SkipRows(1)                      // skip a custom header line
    .WithValidationMode(ValidationMode.Lenient)
    .WithProgress(new Progress<JsonlProgress>(p =>
        Console.WriteLine($"line {p.LineNumber:N0} - {p.BytesRead:N0} bytes - {p.RecordsRead:N0} records")))
    .OnError((ctx, ex) =>
    {
        Console.WriteLine($"line {ctx.LineNumber}: {ex.Message}");
        return JsonlDeserializeErrorAction.SkipRecord;
    })
    .FromFile("training.jsonl")
    .ToList();
```

### 1.3 Async streaming

```csharp
// From a file
await foreach (ChatExample r in Jsonl.Read<ChatExample>().FromFileAsync("training.jsonl"))
{
    // ...
}

// From any Stream (e.g. an HttpResponse content stream)
await using Stream http = await httpClient.GetStreamAsync(url);
await foreach (ChatExample r in Jsonl.Read<ChatExample>().FromStreamAsync(http))
{
    // ...
}
```

`FromFileAsync` opens the file with `FileOptions.Asynchronous | FileOptions.SequentialScan` and routes through a `PipeReader` — line splitting uses `ReadOnlySequence<byte>.PositionOf((byte)'\n')` so partial-line buffering happens inside `System.IO.Pipelines` without per-line allocations.

### 1.4 Raw line iteration

If you need the raw UTF-8 bytes of each line (e.g. to forward them unparsed to another service):

```csharp
PipeReader pipe = PipeReader.Create(stream);
await foreach (JsonlLine line in Jsonl.ReadLinesAsync(pipe))
{
    // line.Utf8 is ReadOnlyMemory<byte> — valid only for this iteration
    // line.LineNumber starts at 1
    await target.WriteAsync(line.Utf8);
}
```

### 1.5 PipeReader integration

```csharp
PipeReader pipe = PipeReader.Create(socketStream);
await foreach (ChatExample r in Jsonl.DeserializeRecordsAsync<ChatExample>(pipe))
{
    // streaming straight off the wire
}
```

### 1.6 DataReader for database bulk loading

`Jsonl.CreateDataReader` returns a `DbDataReader` adapter — drop-in for `SqlBulkCopy.WriteToServer` or any tool that consumes `IDataReader`.

```csharp
// Schema inferred from the first line
using var reader = Jsonl.CreateDataReader("inputs.jsonl");

using var bulk = new SqlBulkCopy(conn);
bulk.DestinationTableName = "Inputs";
bulk.WriteToServer(reader);
```

For explicit projection (recommended when the JSON is nested or has optional fields):

```csharp
var options = new JsonlDataReaderOptions
{
    Columns =
    [
        new JsonlColumnDefinition("id", "id", typeof(long)),
        new JsonlColumnDefinition("question", "messages[0].content", typeof(string)),
        new JsonlColumnDefinition("answer",   "messages[1].content", typeof(string)),
    ]
};

using var reader = Jsonl.CreateDataReader("training.jsonl", readerOptions: options);
```

The JSONPath syntax supports dotted keys and bracket-indexed arrays (`key.key[idx].key`). Missing keys produce `DBNull`.

### 1.7 OnError callback

```csharp
Jsonl.Read<ChatExample>()
    .OnError((ctx, ex) =>
    {
        // ctx.LineNumber, ctx.RecordIndex, ctx.RawLine, ctx.TargetType, ex
        return ex is JsonException
            ? JsonlDeserializeErrorAction.SkipRecord
            : JsonlDeserializeErrorAction.Throw;
    })
    .FromFile("training.jsonl");
```

Returning `SkipRecord` continues enumeration; `Throw` wraps the exception in a `JsonlException` with `JsonlErrorCode.DeserializeError` and the offending line number.

### 1.8 Progress reporting

```csharp
Jsonl.Read<ChatExample>()
    .WithProgress(
        new Progress<JsonlProgress>(p => Console.WriteLine($"{p.RecordsRead:N0} records")),
        intervalRows: 10_000)
    .FromFile("training.jsonl");
```

### 1.9 AOT / trimming support

Pass a `JsonTypeInfo<T>` produced by a source-generated `JsonSerializerContext` to bypass reflection entirely:

```csharp
[JsonSerializable(typeof(ChatExample))]
[JsonSerializable(typeof(ChatMessage))]
public partial class TrainingContext : JsonSerializerContext;

var records = Jsonl.Read<ChatExample>()
    .WithTypeInfo(TrainingContext.Default.ChatExample)
    .FromFile("training.jsonl")
    .ToList();
```

Reflection-based overloads carry `[RequiresUnreferencedCode]` and `[RequiresDynamicCode]` so AOT/trim users get a clear build warning telling them to switch to `WithTypeInfo`. The `JsonTypeInfo<T>` path is fully trim/AOT-safe.

---

## 2. JSONL Writing

### 2.1 Quick start

```csharp
IEnumerable<ChatExample> records = BuildTrainingData();

// To a file
Jsonl.Write<ChatExample>().ToFile("out.jsonl", records);

// To a string
string text = Jsonl.Write<ChatExample>().ToText(records);

// Async, from an IAsyncEnumerable source
await Jsonl.Write<ChatExample>().ToFileAsync("out.jsonl", asyncRecords);
```

### 2.2 Fluent builder options

```csharp
Jsonl.Write<ChatExample>()
    .WithJsonOptions(new JsonSerializerOptions(JsonSerializerDefaults.Web))
    .WithNewLine("\n")               // "\n" (default) or "\r\n"
    .WithEncoding(Encoding.UTF8)
    .WithFinalNewline()              // emit trailing "\n" (default false)
    .WithMaxRowCount(100_000)
    .WithMaxOutputSize(50L * 1024 * 1024) // 50 MiB cap
    .OnError(ctx =>
    {
        Console.WriteLine($"row {ctx.RowIndex}: {ctx.Exception.Message}");
        return JsonlSerializeErrorAction.SkipRecord;
    })
    .ToFile("out.jsonl", records);
```

Output is UTF-8 without BOM by default. Each record is serialized with a fresh `Utf8JsonWriter` reset between rows so cross-record state never leaks.

### 2.3 Async writing

```csharp
await using FileStream output = File.Create("out.jsonl");

await Jsonl.Write<ChatExample>()
    .WithFinalNewline()
    .ToStreamAsync(output, asyncRecords);
```

For AOT, pass `JsonTypeInfo<T>` via `.WithTypeInfo(...)` exactly as on the read side.

### 2.4 OnError callback

```csharp
Jsonl.Write<ChatExample>()
    .OnError(ctx => JsonlSerializeErrorAction.SkipRecord)
    .ToFile("out.jsonl", records);
```

Skipping does **not** emit an empty line — the offending record disappears.

---

## 3. Options Reference

### 3.1 `JsonlReadOptions`

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `SerializerOptions` | `JsonSerializerOptions?` | `null` | `System.Text.Json` options for the reflection deserialization path. |
| `MaxLineSizeBytes` | `int` | `1 MiB` | Hard cap per line. Exceeding throws `JsonlException(LineTooLong)` — **not** skippable. |
| `MaxRowCount` | `int` | `1_000_000` | Hard cap on total records. Exceeding throws `JsonlException(TooManyRows)`. |
| `SkipEmptyLines` | `bool` | `true` | Silently skip blank lines. |
| `SkipRows` | `int` | `0` | Skip the first N records before yielding. |
| `ValidationMode` | `ValidationMode` | `Strict` | `Strict` throws at end-of-stream when skipped errors were collected; `Lenient` does not. |
| `OnError` | `JsonlDeserializeErrorHandler?` | `null` | Per-line callback. |
| `Progress` | `IProgress<JsonlProgress>?` | `null` | Progress reporter. |
| `ProgressIntervalRows` | `int` | `1000` | Rows between progress callbacks. |

### 3.2 `JsonlWriteOptions`

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `SerializerOptions` | `JsonSerializerOptions?` | `null` | Serializer options for the reflection path. |
| `NewLine` | `string` | `"\n"` | Line separator. |
| `Encoding` | `Encoding?` | UTF-8 (no BOM) | Output encoding. |
| `MaxRowCount` | `int?` | `null` | Optional record cap. |
| `MaxOutputSize` | `long?` | `null` | Optional byte cap (DoS protection). |
| `WriteFinalNewline` | `bool` | `false` | Emit a trailing newline after the last record. |
| `OnError` | `JsonlSerializeErrorHandler?` | `null` | Per-record callback. |

### 3.3 `JsonlDataReaderOptions`

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Columns` | `IReadOnlyList<JsonlColumnDefinition>?` | `null` | Explicit projection. When `null`, schema is inferred from the first non-empty line. |
| `InferSchemaFromFirstLine` | `bool` | `true` | Inferred-schema toggle. |
| `SkipRows` | `int` | `0` | Skip leading records. |

`JsonlColumnDefinition(string Name, string JsonPath, Type DataType)` — `JsonPath` supports `key.key[idx].key`.

---

## 4. CSV ↔ JSONL Conversion

`CsvToJsonlConverter` projects each CSV row into a JSONL record using a `CsvToJsonlShape` descriptor. Three shapes ship:

- `CsvToJsonlShape.FlatObject()` — one JSON property per CSV column.
- `CsvToJsonlShape.OpenAiChat(systemColumn, userColumn, assistantColumn)` — OpenAI fine-tuning shape.
- `CsvToJsonlShape.AnthropicMessages(userColumn, assistantColumn)` — Anthropic messages shape (no system role).

Shape descriptors carry no SDK dependencies and make no network calls — they only describe the JSON layout.

### 4.1 CSV → JSONL (flat)

```csharp
string jsonl = CsvToJsonlConverter.Convert(csv, CsvToJsonlShape.FlatObject());
// {"Id":"1","Customer":"Acme","Amount":"99.50"}
// {"Id":"2","Customer":"Foo","Amount":"42.00"}
```

### 4.2 CSV → JSONL (OpenAI chat shape)

```csharp
string csv = """
    System,Question,Answer
    "You are a math tutor.","What is 2+2?","4"
    """;

string jsonl = CsvToJsonlConverter.Convert(
    csv,
    CsvToJsonlShape.OpenAiChat(systemColumn: "System", userColumn: "Question", assistantColumn: "Answer"));

// {"messages":[
//   {"role":"system","content":"You are a math tutor."},
//   {"role":"user","content":"What is 2+2?"},
//   {"role":"assistant","content":"4"}
// ]}
```

Use `systemColumn: null` to omit the system role entirely.

### 4.3 CSV → JSONL (Anthropic messages shape)

```csharp
string jsonl = CsvToJsonlConverter.Convert(
    csv,
    CsvToJsonlShape.AnthropicMessages(userColumn: "Question", assistantColumn: "Answer"));
```

File-path and async stream overloads:

```csharp
CsvToJsonlConverter.Convert("train.csv", "train.jsonl", CsvToJsonlShape.OpenAiChat(null, "q", "a"));
await CsvToJsonlConverter.ConvertAsync(csvStream, jsonlStream, CsvToJsonlShape.FlatObject());
```

All scalar CSV values are emitted as JSON strings. Numeric type inference is intentionally out of scope; if you need typed numerics, project through a CLR record first and use `Jsonl.Write<T>()`.

### 4.4 JSONL → CSV

```csharp
string csv = JsonlToCsvConverter.Convert(jsonl);
JsonlToCsvConverter.Convert("training.jsonl", "out.csv");
```

The CSV column set is inferred from the union of top-level keys observed in the first `SchemaInferencePeekRows` lines (default 100). Nested objects/arrays are emitted as JSON-encoded strings in their cell.

---

## 5. BatchAsync — Embedding API Pipelines

`HeroParser.Streaming.AsyncEnumerableExtensions.BatchAsync(int size)` groups an `IAsyncEnumerable<T>` into fixed-size batches. The classic use case is feeding records into an embedding API — OpenAI, Voyage, Cohere, and Anthropic all accept inputs in batches.

```csharp
using HeroParser.Streaming;

const int BatchSize = 100;

await foreach (IReadOnlyList<ChatExample> batch in
    Jsonl.Read<ChatExample>()
        .FromFileAsync("inputs.jsonl")
        .BatchAsync(BatchSize))
{
    // Ship batch to /v1/embeddings, then write vectors to disk or a vector DB.
    var vectors = await embeddingClient.EmbedAsync(batch.Select(x => x.Messages![^1].Content!));
    await vectorDb.UpsertAsync(batch.Zip(vectors));
}
```

Semantics:

- The final partial batch is yielded if the source ends mid-batch.
- `size <= 0` throws `ArgumentOutOfRangeException`.
- Each yielded list is a freshly allocated `List<T>` — callers may retain it past the next iteration.
- Cancellation is honored between items.

`BatchAsync` is not JSONL-specific — it works on any `IAsyncEnumerable<T>`. Pair it with `Csv.Read<T>().FromFileAsync(...)` or `Excel.Read<T>().FromFileAsync(...)` just as easily.

---

## 6. VectorParser — Inline Embedding Columns

`HeroParser.Vectors.VectorParser` parses inline vector columns — the format AI/ML datasets use for pre-computed embeddings.

Accepted shapes (square brackets optional, separators may be comma, semicolon, or whitespace):

```text
[0.1, 0.2, 0.3]
0.1,0.2,0.3
0.1 0.2 0.3
[]                  (empty vector)
```

```csharp
using HeroParser.Vectors;

float[]  fv = VectorParser.ParseFloats(row[3].CharSpan);
double[] dv = VectorParser.ParseDoubles(row[3].CharSpan);

if (VectorParser.TryParseFloats(row[3].CharSpan, out float[]? result))
{
    // ...
}
```

A `CultureInfo` overload lets you parse vectors written with comma-as-decimal-separator (`"1,5;2,5;3,5"` with `de-DE`):

```csharp
float[] vec = VectorParser.ParseFloats("1,5;2,5;3,5", CultureInfo.GetCultureInfo("de-DE"));
```

When comma is the decimal separator, it is **not** treated as a value separator — use `;` or whitespace between elements.

---

## 7. Errors, Security, and DoS Limits

`JsonlException` is thrown for HeroParser-specific failures and carries a `JsonlErrorCode`:

| Code | When |
|------|------|
| `LineTooLong` | A line exceeds `MaxLineSizeBytes`. Always thrown; not skippable via `OnError`. |
| `TooManyRows` | Reader produced more than `MaxRowCount` records. |
| `InvalidOptions` | `JsonlReadOptions.Validate()` or `JsonlWriteOptions.Validate()` rejected a value. |
| `DeserializeError` | `System.Text.Json` threw — wraps the original exception. |
| `SerializeError` | Writer-side serialize failure. |

`LineTooLong` is a security cap (defaults to 1 MiB) — it is unconditional and not skippable, which prevents an adversary from forcing unbounded buffering. Use `WithMaxLineSize(int.MaxValue)` only when input is fully trusted.

JSONL forbids raw newlines inside values, so splitting on `\n` is always safe regardless of escaped sequences inside strings.

---

[Back to README](../README.md)
