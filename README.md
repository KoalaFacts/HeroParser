# HeroParser - High-Performance, AI-Native CSV, Fixed-Width, Excel (.xlsx), JSONL & HTB Parser

[![Build and Test](https://github.com/KoalaFacts/HeroParser/actions/workflows/ci.yml/badge.svg)](https://github.com/KoalaFacts/HeroParser/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/HeroParser.svg)](https://www.nuget.org/packages/HeroParser)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

**HeroParser** is a zero-allocation, SIMD-accelerated tabular data parser and writer for .NET 8, 9, and 10. Designed for extreme speed, memory efficiency, and Native AOT compatibility, it also offers first-class integrations for AI agents, vector embeddings, and LLM pipelines.

### Why Choose HeroParser?
* **Extreme Performance**: Engineered with AVX-512, AVX2, and ARM NEON SIMD optimizations to deliver ultra-high-throughput reading and writing.
* **AI-Native integrations**: Built-in support for token-budgeted chunking, LLM output structured repair, vector embedding pipelines, and agent tool mapping.
* **Zero Dependencies & Low Footprint**: Operates with zero external packages. Employs a fixed **112-byte heap memory footprint** on the reading hot-path regardless of file size.
* **Unified Attributes**: Annotate your C# classes once, and use them across CSV, Excel, Fixed-Width, and HTB APIs.

---

## Performance & Memory

Tested under **.NET 10.0** on an **AMD Ryzen AI 9 HX PRO 370 CPU**:
* **Read Throughput**: SIMD-accelerated UTF-8 (`byte[]`) read paths on both quoted and unquoted data.
* **Write Throughput**: Highly optimized CSV/JSONL serialization achieving massive throughput.
* **GC Allocations**: Fixed 112-byte allocation throughout parsing, representing a **97% memory reduction** compared to traditional reflection-based parsers.
* **String Generation**: **Up to 64% speedup** on synchronous text generation via pre-allocated capacities.

View live performance graphs and history on the [HeroParser Performance Portal](https://KoalaFacts.github.io/HeroParser/).

---

## Install

### Core Library

```bash
dotnet add package HeroParser
```

### High-Performance Console Engine (Alternative to Spectre.Console)

```bash
dotnet add package HeroParser.Console
```

### Command-Line Utility (CLI)

#### Option 1: Dotnet Global Tool (Cross-Platform)
```bash
dotnet tool install --global HeroParser.Cli --version 2.5.2
```

#### Option 2: Homebrew Tap (macOS & Linux)
Install the native binary using Homebrew:
```bash
brew tap KoalaFacts/heroparser
brew install heroparser
```

#### Option 3: Shell Script (macOS & Linux fallback)
Install the native binary without Homebrew:
```bash
curl -fsSL https://raw.githubusercontent.com/KoalaFacts/HeroParser/main/install.sh | sh
```

On Windows, you can install via WinGet (pending registry merge):
```bash
winget install KoalaFacts.HeroParser
```


---

## Simple Quick Starts

Define your record type. Decorate it with `[GenerateBinder]` to enable source-generated, reflection-free, and Native AOT-safe binding:

```csharp
using HeroParser;

[GenerateBinder]
public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    
    [Validate(RangeMin = 0)]
    public decimal Price { get; set; }
    
    [Parse(Format = "yyyy-MM-dd")]
    public DateTime ReleaseDate { get; set; }
}
```

### CSV

```csharp
// Read a file in one line (zero-allocation binding)
List<Product> products = Csv.Read<Product>().FromFile("products.csv").ToList();

// Async stream millions of rows without buffering
await foreach (Product p in Csv.Read<Product>().FromFileAsync("products.csv"))
{
    Console.WriteLine($"{p.Name}: {p.Price:C}");
}

// Write collection to a file
Csv.Write<Product>().ToFile("out.csv", products);
```

### Excel (.xlsx)
 
Reads and writes Excel workbooks with zero external dependencies (utilizes only standard `.NET` compression and XML packages).
 
```csharp
using HeroParser.Excels.Core;

// Read Excel files
List<Product> products = Excel.Read<Product>().FromFile("products.xlsx");

// Write Excel workbook with custom header styles, column styles, and auto-merged duplicate values
var headerStyle = ExcelStyle.Create()
    .WithFont(f => f.WithName("Arial").WithSize(12).WithBold().WithColor("FFFFFF"))
    .WithFill(fill => fill.WithSolidColor("007ACC")); // Blue background

Excel.Write<Product>()
    .WithHeaderStyle(headerStyle)
    .WithMergeDuplicates(p => p.Category) // Vertically merge contiguous duplicate Categories
    .ToFile("out.xlsx", products);
```

### Fixed-Width

Map properties to specific character boundaries using the `[PositionalMap]` attribute:

```csharp
[GenerateBinder]
public class Employee
{
    [PositionalMap(Start = 0, Length = 10)]
    public string Id { get; set; } = "";

    [PositionalMap(Start = 10, Length = 30)]
    public string Name { get; set; } = "";

    [PositionalMap(Start = 40, Length = 10, Alignment = FieldAlignment.Right)]
    public decimal Salary { get; set; }
}
```
```csharp
// Read positional records
var employees = FixedWidth.Read<Employee>().FromFile("employees.dat").Records;

// Write positional records
FixedWidth.Write<Employee>().ToFile("out.dat", employees);
```

### JSON Lines (JSONL)

Perfect for AI fine-tuning datasets, streamed LLM responses, and database bulk loading.

```csharp
// Read JSONL files (AOT-safe)
var records = Jsonl.Read<Product>().FromFile("products.jsonl").ToList();

// Write JSONL files
Jsonl.Write<Product>().ToFile("out.jsonl", records);
```

### High-Throughput Tabular Binary (HTB)

A custom, high-speed binary serialization format optimized for zero allocations, platform independence, and vector embedding storage (supporting `float[]` arrays).

#### Why HTB?
* **Zero Heap Allocations**: Utilizes Roslyn source generators (`[GenerateBinder]`) to map properties directly with zero-boxing and zero-reflection overhead.
* **Vector Embedding Native**: Natively supports floating-point arrays (`float[]`), enabling ultra-fast vector embedding serialization without string parsing overhead.
* **Platform-Independent Endianness**: Automatically handles big-endian byte-order reversal for floats, doubles, and ints for cross-architecture safety.
* **Allocation-Free CSV ↔ HTB Conversion**: Stream-convert CSV directly to HTB (and vice-versa) with zero heap allocation overhead.
* **AOT & Trim Ready**: Fully compatible with Native AOT compilation out-of-the-box.

```csharp
// Read HTB binary files (AOT-safe)
List<Product> products = Htb.Read<Product>().FromFile("products.htb").ToList();

// Async stream HTB records
await foreach (Product p in Htb.Read<Product>().FromFileAsync("products.htb"))
{
    Console.WriteLine($"{p.Name}: {p.Price:C}");
}

// Write records to an HTB file
Htb.Write<Product>().ToFile("out.htb", products);

// Direct, allocation-free CSV ↔ HTB conversions
Htb.ConvertFromCsv("products.csv", "products.htb", HtbSchema.FromType<Product>());
```

### Console (High-Performance Terminal Widget Engine)

A zero-allocation, reflection-free, and 100% Native AOT-compatible library designed to replace or drop-in substitute `Spectre.Console` in high-performance terminal applications.

```csharp
using HeroParser.Console;

// Render styled ANSI markup
AnsiConsole.MarkupLine("[bold green]Success:[/] Row validation completed in [yellow]4.2ms[/].");

// Render highly styled tables and panels with zero allocations
var table = new Table().Border(TableBorder.Rounded);
table.AddColumn("[blue]Filename[/]");
table.AddColumn("[blue]Records[/]");

table.AddRow("data.csv", "10,240");
table.AddRow("data.jsonl", "102,400");

AnsiConsole.Write(table);
```

---

## Unified Attribute System

Annotate a single record class once, and read or write it across multiple formats:

| Attribute | Purpose | CSV | Excel | Fixed-Width | HTB |
|-----------|---------|:---:|:---:|:---:|:---:|
| `[GenerateBinder]` | Emits Roslyn source-generated, reflection-free mapping binder | Yes | Yes | Yes | Yes |
| `[TabularMap(Name, Index)]` | Maps property to column header or index | Yes | Yes | No | Yes |
| `[PositionalMap(Start, Length...)]` | Declares character position, alignment, and pad characters | No | No | Yes | No |
| `[Parse(Format)]` | Converts raw values to custom types (e.g. DateTime format) | Yes | Yes | Yes | No |
| `[Format(WriteFormat...)]` | Customizes output formatting during serialization | Yes | Yes | Yes | No |
| `[Validate(Range, Pattern...)]` | Validates properties bidirectionally (Strict/Lenient modes) | Yes | Yes | Yes | Yes |

---

## AI-Native Tabular Capabilities

HeroParser includes first-class support for LLM, vector search, and RAG pipelines:

### 1. LLM Structured Output Repair (`LlmRepair`)
Repairs truncated final rows (unclosed quotes/escapes) and strips markdown code blocks from raw LLM text streams.
```csharp
using HeroParser.AI;

// Repaired on-the-fly and parsed directly into strongly-typed records
await foreach (var dev in LlmRepair.ReadFromTextAsync<Developer>(rawLlmResponse))
{
    Console.WriteLine($"{dev.Name} is a {dev.Role}");
}
```

### 2. Tabular Embedding Pipeline (`ToLlmEmbeddingsAsync`)
Batches streamed records and pairs them with vector embeddings with a zero-allocation, token-budgeted streaming wrapper.
```csharp
using HeroParser.AI;

await foreach (var chunk in developers.ToLlmEmbeddingsAsync(
    async (texts, ct) => await GetEmbeddingsFromApiAsync(texts, ct),
    options: new LlmChunkOptions { MaxTokensPerChunk = 250 },
    batchSize: 16))
{
    Console.WriteLine($"Chunk of {chunk.Chunk.EndRow - chunk.Chunk.StartRow + 1} rows embedded.");
}
```

### 3. Agent Tool Argument Mapper (`SchemaMetadata.MapFromToolCall`)
Maps flat dictionaries of case-insensitive arguments returned by tool calling models into typed record models, executing validation constraints and raising rich validation feedback.
```csharp
using HeroParser.AI;

Developer dev = SchemaMetadata.MapFromToolCall<Developer>(arguments);
```

### 4. Tabular Context Profiler (`TabularContextProfiler`)
Generates structured statistical profile cards in markdown directly from datasets to inject into LLM system prompts.
```csharp
string contextCard = developers.GenerateContextCard(datasetName: "Engineering Team");
```

### 5. Token-Bounded JSON Chunker (`JsonLlmChunker`)
Chunks datasets into valid, token-bounded JSON array blocks, optimized for ingestion into RAG context windows.
```csharp
await foreach (var jsonChunk in developers.ToJsonLlmChunksAsync(options)) { ... }
```

---

## Key Features

* **SIMD-accelerated CSV parsing** — AVX-512, AVX2, and ARM NEON instruction sets; PCLMULQDQ-based branchless quote tracking
* **Zero allocations** — fixed 4 KB stack footprint regardless of column count or file size; `ArrayPool` for buffers
* **AOT/trimming ready** — source generators emit reflection-free binders; annotated with `[RequiresUnreferencedCode]` where reflection is unavoidable
* **Async streaming** — `IAsyncEnumerable<T>` for CSV, Fixed-Width, Excel, JSONL, and HTB; true non-blocking I/O with sync fast paths
* **Excel without extra dependencies** — reads and writes `.xlsx` using only `System.IO.Compression` and `System.Xml`
* **JSONL for AI/ML pipelines** — `Jsonl.Read<T>()` / `Jsonl.Write<T>()` mirror the CSV builder pattern; AOT-safe via `JsonTypeInfo<T>`; `CsvToJsonlConverter` projects tabular data into OpenAI/Anthropic fine-tuning shapes
* **HTB binary format** — custom, high-speed, zero-allocation binary format featuring float-array embedding support, big-endian byte-order reversal, and direct CSV ↔ HTB conversion
* **Embedding-API batching** — `IAsyncEnumerable<T>.BatchAsync(size)` groups streamed records into fixed-size batches for OpenAI/Voyage/Cohere/Anthropic embedding calls
* **Inline vector parser** — `VectorParser.ParseFloats(span)` handles pre-computed embeddings (`"[0.1,0.2,…]"`, comma/semicolon/whitespace separators, culture-aware)
* **DataReader support** — `Csv.CreateDataReader()`, `FixedWidth.CreateDataReader()`, `Excel.CreateDataReader()`, `Jsonl.CreateDataReader()` for database bulk loading via `SqlBulkCopy`
* **PipeReader integration** — `Csv.ReadFromPipeReaderAsync(pipe)` for network streaming without buffering the entire payload
* **Multi-schema CSV** — discriminator-based row routing to different record types; source-generated dispatch for ~2.85x faster throughput
* **Delimiter detection** — auto-detect comma, semicolon, pipe, or tab from sample rows with a confidence score
* **CSV validation** — pre-flight structural checks with detailed per-row error reporting
* **Field validation** — `[Validate]` constraints (NotNull, NotEmpty, Range, Pattern) collected lazily; `result.ThrowIfAnyError()` for fail-fast
* **CSV injection protection** — configurable sanitization modes for user-data exports
* **Progress reporting** — row/byte callbacks for large-file UX
* **Custom type converters** — register converters for domain types on any reader or writer
* **Write capacity pre-allocation** — backing buffer capacity is pre-allocated via estimated record counts when writing collections to strings, yielding up to 64% speedup and completely eliminating buffer resize copy overhead
* **Multi-framework** — .NET 8, 9, 10; CI validates all three on Windows, Linux, and macOS

---

## Detailed Documentation

For advanced features and full API guides, see the files under the `docs` folder:
* [CSV Guide](docs/csv.md) — Fluent readers/writers, validation, PipeReader, and multi-schema dispatching.
* [Excel Guide](docs/excel.md) — Multi-sheet workbooks, custom formatting, and progress tracking.
* [Fixed-Width Guide](docs/fixed-width.md) — Positional mapping, alignment, padding, and custom type converters.
* [JSONL Guide](docs/jsonl.md) — Fine-tuning templates, vector parsing, and Native AOT setups.
* [HTB Guide](docs/htb.md) — High-Throughput Tabular Binary format, fluent APIs, CSV ↔ HTB conversion, and Native AOT support.
* [Console Guide](docs/console.md) — Standard rendering, markup, widgets (Table, Panel, Rule), interactive prompts, and Spectre.Console compatibility.
* [CLI Guide](docs/cli.md) — Local-First AI-native architecture, auto-detecting terminal CLIs (`agy`/`openai`/`claude`/`copilot`/`ollama`), stdin streaming, process management, and interactive query/translate wizard.
* [Benchmarks Guide](docs/benchmarks.md) — Execution environments, detailed CPU metrics, and comparisons.

---

## Building & Testing

```bash
# Build all projects
dotnet build

# Run unit tests
dotnet test --filter Category=Unit

# Run integration tests
dotnet test --filter Category=Integration

# Run all tests
dotnet test

# Check code formatting
dotnet format --verify-no-changes

# Run benchmarks
dotnet run -c Release --project benchmarks/HeroParser.Benchmarks
```

---

## License

MIT
