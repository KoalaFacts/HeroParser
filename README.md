# HeroParser - High-Performance, AI-Native CSV, Fixed-Width, Excel (.xlsx), JSONL & HTB Parser

[![Build and Test](https://github.com/KoalaFacts/HeroParser/actions/workflows/ci.yml/badge.svg)](https://github.com/KoalaFacts/HeroParser/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/HeroParser.svg)](https://www.nuget.org/packages/HeroParser)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

**HeroParser** is a low-allocation, SIMD-accelerated tabular data parser and writer for .NET 8, 9, 10, and 11 (the net11.0 build ships from 2.9.0). Designed for extreme speed, memory efficiency, and Native AOT compatibility, it also offers first-class integrations for AI agents, vector embeddings, and LLM pipelines.

### Why Choose HeroParser?
* **Cross-Platform SIMD**: the CSV row scanner uses AVX-512 or AVX2 on x86-64 (with PCLMULQDQ quote tracking), plus ARM64 `AdvSimd` (NEON) and portable 128-bit `Vector128` SIMD (with branchless software prefix-XOR quote tracking), falling back to scalar paths when hardware SIMD is unavailable; the CSV writer's quote analysis uses AVX-512, AVX2, or SSE2.
* **AI-Native integrations**: Built-in support for token-budgeted chunking, LLM output structured repair, vector embedding pipelines, and agent tool mapping.
* **Low / Zero Memory Footprint**: Span row readers (`ReadOnlySpan<byte>` / `ReadOnlySpan<char>`) scan and parse primitive fields without allocating per-field strings, backed by `ArrayPool` row buffers, direct-to-buffer `ISpanFormattable` formatting in sync/async writers, non-boxing `DbDataReader` exports, streaming `.gz`/`.br`/`.zst` compression, and `[GenerateBinder]` AOT binders. Typed records, decoded strings, and materialized collections still allocate; see the [benchmark scope and evidence](docs/benchmarks.md).
* **Unified Attributes & Ecosystem**: Annotate your C# classes once for CSV, Excel, Fixed-Width, JSONL, and HTB, stream database results out with NativeAOT-safe `DbDataReader` export, and read or write `.gz`, `.br`, and `.zst`/`.zstd` (.NET 11+) files transparently by extension.

---

## Performance & Memory

Performance depends on the input, API, runtime, and CPU. Both UTF-8 (`byte[]`) and UTF-16 (`string`) span readers support SIMD scanning; use the representation you already have to avoid unnecessary encoding conversions.

Allocation profile by API tier:
* **Zero-string-allocation row scanning**: `Csv.ReadFromByteSpan`, `Csv.ReadFromText`, and `Csv.ReadFromPipeReaderAsync` expose `ReadOnlySpan<byte>` / `ReadOnlySpan<char>` fields and parse primitives (`int`, `decimal`, `DateOnly`, `TimeOnly`, `DateTimeOffset`, `TimeSpan`, `Half`, `Int128`, `UInt128`, `Guid`) without allocating per-field strings.
* **Streaming & low-footprint I/O**: async readers, `[GenerateBinder]` JSONL/HTB/CSV/Fixed-Width paths, direct-to-buffer writers, transparent `.gz`/`.br`/`.zst` streams, and `WriteFromDataReader*` reuse pooled scratch buffers and format `ISpanFormattable` values directly via spans.
* **Materialized outputs**: `ToList()`, `ToText()`, decoded `string` properties, and JSON/Excel DOM structures allocate the requested objects and output buffers.

The [2026-10-23 CI report, Base `cba2bcd`](https://github.com/KoalaFacts/HeroParser/pull/156#issuecomment-6355972696) measured **176 B per operation** for HeroParser's legacy 10,000-row × 25-column count-only fixture on AMD EPYC 7763, Ubuntu 24.04, .NET 10.0.13. Input preparation was outside the timed operation; the test did not decode every field, bind typed records, or perform file I/O. Its library options were not equivalent, so the archived comparison does not establish a general performance ranking. The corrected fixture requires a new baseline.

See the [Benchmarks Guide](docs/benchmarks.md) for the historical table, limitations, current dependencies, and commands for measuring your own workload. Capacity estimates in text writers can reduce buffer growth, but output strings and application objects still allocate.

View live performance graphs and history on the [HeroParser Performance Portal](https://KoalaFacts.github.io/HeroParser/).

---

## Install

### Core Library

```bash
dotnet add package HeroParser
```

### High-Performance Console Engine

```bash
dotnet add package HeroParser.Console
```

### Command-Line Utility (CLI)

#### Option 1: Dotnet Global Tool (Cross-Platform)
```bash
dotnet tool install --global HeroParser.Cli
```

#### Option 2: Homebrew Tap (macOS & Linux)
Install the native binary using Homebrew:
```bash
brew tap KoalaFacts/heroparser
brew install heroparser
```

#### Option 3: Shell Installer Script (macOS & Linux fallback)
Install the native binary without Homebrew:
```bash
curl -fsSL https://raw.githubusercontent.com/KoalaFacts/HeroParser/main/install.sh | sh
```

#### Option 4: Snap Store (Linux, pending)
The Snap package is not yet publicly available in the Snap Store. Use the shell installer above or a Linux archive from [GitHub Releases](https://github.com/KoalaFacts/HeroParser/releases) until store publication is confirmed.

#### Option 5: WinGet (Windows)
Install the portable binary via WinGet:
```bash
winget install KoalaFacts.HeroParser
```

#### Option 6: Scoop (Windows)
Install via Scoop:
```bash
scoop bucket add heroparser https://github.com/KoalaFacts/scoop-heroparser.git
scoop install heroparser/heroparser
```

### WebAssembly / JavaScript (Node.js & Browser)

The JavaScript package is an unpublished preview in this repository, not yet available from npm. The workspace requires npm 12 or newer and rejects unreviewed dependency install scripts. Build the WebAssembly runtime locally before using the workspace package; see [the package README](npm/packages/heroparser/README.md). The browser demo is separate from an npm release.

#### Interactive Playground Demo
Try the interactive WebAssembly sandbox directly in your browser:
👉 **[https://KoalaFacts.github.io/HeroParser/demo/](https://KoalaFacts.github.io/HeroParser/demo/)**

#### JS/TS Quick Start
```javascript
import { init, readCsv } from 'heroparser';

// Initialize the WebAssembly runtime
await init();

// Parse CSV text directly in the browser or Node.js
const records = readCsv("Name,Age,Role\nAlice,30,Developer\nBob,25,Designer", {
    delimiter: ',',
    hasHeader: true
});
console.log(records);
```

---

## Try HeroParser on a real integration

Already using HeroParser, or considering it for a production CSV, Excel, fixed-width, JSONL, or HTB import/export? We welcome **specific, reproducible integration enquiries** rather than generic feature wish lists.

[**Describe your import/export scenario in a GitHub issue**](https://github.com/KoalaFacts/HeroParser/issues/new) with:

- The input/output formats, HeroParser version, .NET target, operating system, and whether Native AOT matters.
- A minimal **synthetic or redacted** sample, expected result, and the exact failure, gap, or resource constraint you want to evaluate.
- If relevant, whether your team needs a scoped **commercial integration or support discussion**. Describe the scope first; no support commitment, price, or SLA is implied.

Do **not** attach real customer datasets, personal information, credentials, or confidential files to public GitHub issues. Start with the [installation steps](#install) and [benchmark methodology](docs/benchmarks.md); performance depends on the workload and API.

**Also need a PDF report template?** [FactsPDF](https://github.com/KoalaFacts/FactsPDF) is a **separate, experimental, source-available .NET renderer** for controlled HTML/CSS business-report layouts—not an integration shipped with HeroParser. It has no public NuGet/CLI package or stable SDK yet; any company may evaluate it free for up to 90 days under the [PolyForm Free Trial licence](https://github.com/KoalaFacts/FactsPDF/blob/main/README.md#licensing). Check [template fit and licensing](https://github.com/KoalaFacts/FactsPDF/blob/main/docs/evaluation.md#check-template-fit-before-integration), then [describe a non-confidential report requirement](https://github.com/KoalaFacts/FactsPDF/issues/new?template=report-template-fit.md) using synthetic examples only.

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
using HeroParser.SeparatedValues.Reading.Records; // ToList() and other reader extensions

// Read a file into typed records
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
    .WithMergeDuplicates(p => p.Name) // Vertically merge contiguous duplicate Names
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
using System.Text.Json.Serialization;

// Option A: Read JSONL using [GenerateBinder] (zero reflection, AOT-safe span parsing via Utf8JsonReader)
using var binderReader = Jsonl.Read<Product>().FromFile("products.jsonl");
var binderRecords = binderReader.ToList();

// Option B: Read & write JSONL using source-generated JsonTypeInfo<T> metadata (AOT-safe)
using var reader = Jsonl.Read<Product>()
    .FromFile("products.jsonl", ProductJsonContext.Default.Product);
var records = reader.ToList();

// Write JSONL files
Jsonl.Write<Product>().ToFile("out.jsonl", records, ProductJsonContext.Default.Product);

[JsonSerializable(typeof(Product))]
internal partial class ProductJsonContext : JsonSerializerContext { }
```

Add `[GenerateBinder]` on your record class for reflection-free, AOT-safe JSONL **reading** (`float[]` embeddings, `[Parse]`, `[Validate]`, and extended CLR types). JSONL **writing** (and custom `System.Text.Json` options) uses `JsonTypeInfo<T>` above.

### High-Throughput Tabular Binary (HTB)

A custom binary serialization format with source-generated mapping, platform-independent byte order, and vector embedding storage (supporting `float[]` arrays).

#### Why HTB?
* **Source-Generated Mapping**: Uses Roslyn source generators (`[GenerateBinder]`) to map supported properties without reflection. Records, strings, and arrays still have allocation costs.
* **Vector Embedding Native**: Natively supports floating-point arrays (`float[]`), enabling ultra-fast vector embedding serialization without string parsing overhead.
* **Platform-Independent Endianness**: Automatically handles big-endian byte-order reversal for floats, doubles, and ints for cross-architecture safety.
* **CSV ↔ HTB Conversion**: Provides synchronous file conversion and asynchronous stream conversion between CSV and HTB.
* **AOT & Trim Ready**: Fully compatible with Native AOT compilation out-of-the-box.

```csharp
using HeroParser.Htbs.Records; // HtbSchema

// Read HTB binary files (AOT-safe)
List<Product> products = Htb.Read<Product>().FromFile("products.htb").ToList();

// Async stream HTB records
await foreach (Product p in Htb.Read<Product>().FromFileAsync("products.htb"))
{
    Console.WriteLine($"{p.Name}: {p.Price:C}");
}

// Write records to an HTB file
Htb.Write<Product>().ToFile("out.htb", products);

// Direct CSV ↔ HTB conversion
Htb.ConvertFromCsv("products.csv", "products.htb", HtbSchema.FromType<Product>());
```

### Console (High-Performance Terminal Widget Engine)

An allocation-conscious, reflection-free, and Native AOT-compatible library designed for high-performance terminal applications.

```csharp
using HeroParser.Console;

// Render styled ANSI markup
AnsiConsole.MarkupLine("[bold green]Success:[/] Row validation completed in [yellow]4.2ms[/].");

// Render styled tables and panels
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

| Attribute | Purpose | CSV | Excel | Fixed-Width | JSONL | HTB |
|-----------|---------|:---:|:---:|:---:|:---:|:---:|
| `[GenerateBinder]` | Emits Roslyn source-generated, reflection-free mapping binder | Yes | Yes | Yes | Yes (Read) | Yes |
| `[TabularMap(Name, Index)]` | Maps property to column header or index | Yes | Yes | No | Yes | Yes |
| `[PositionalMap(Start, Length...)]` | Declares character position, alignment, and pad characters | No | No | Yes | No | No |
| `[Parse(Format)]` | Converts raw values to custom types (e.g. DateTime format) | Yes | Yes | Yes | Yes | No |
| `[Format(WriteFormat...)]` | Customizes output formatting during serialization | Yes | Yes | Yes | No | No |
| `[Validate(Range, Pattern...)]` | Validates properties bidirectionally (Strict/Lenient modes) | Yes | Yes | Yes | Yes (Read) | Yes |

---

## AI-Native Tabular Capabilities

HeroParser includes first-class support for LLM, vector search, and RAG pipelines:

### 1. LLM Structured Output Repair (`LlmRepair`)
Repairs truncated final rows (unclosed quotes/escapes) and strips markdown code blocks from raw LLM text streams.
<!-- snippet: setup
[GenerateBinder]
public class Developer
{
    public string Name { get; set; } = "";
    public string Role { get; set; } = "";
}
string rawLlmResponse = "";
-->
```csharp
using HeroParser.AI;

// Repaired on-the-fly and parsed directly into strongly-typed records
await foreach (var dev in LlmRepair.ReadFromTextAsync<Developer>(rawLlmResponse))
{
    Console.WriteLine($"{dev.Name} is a {dev.Role}");
}
```

### 2. Tabular Embedding Pipeline (`ToLlmEmbeddingsAsync`)
Batches streamed records and pairs them with vector embeddings using a token-budgeted streaming wrapper. Chunks, request text, and returned embedding arrays have allocation costs.
<!-- snippet: setup
IAsyncEnumerable<Developer> developers = AsyncEnumerable.Empty<Developer>();
static ValueTask<IReadOnlyList<float[]>> GetEmbeddingsFromApiAsync(IReadOnlyList<string> texts, CancellationToken ct) => default;
-->
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
<!-- snippet: setup
IReadOnlyDictionary<string, object?> arguments = new Dictionary<string, object?>();
-->
```csharp
using HeroParser.AI;

Developer dev = SchemaMetadata.MapFromToolCall<Developer>(arguments);
```

### 4. Tabular Context Profiler (`TabularContextProfiler`)
Generates structured statistical profile cards in markdown directly from datasets to inject into LLM system prompts.
<!-- snippet: setup
List<Developer> developers = [];
-->
```csharp
string contextCard = developers.GenerateContextCard(datasetName: "Engineering Team");
```

### 5. Token-Bounded JSON Chunker (`JsonLlmChunker`)
Chunks an `IAsyncEnumerable<T>` into valid, token-bounded JSON array blocks for RAG context windows. Serialization uses a source-generated `JsonTypeInfo<T>`, so it stays AOT-safe.
<!-- snippet: setup
IAsyncEnumerable<Developer> developers = AsyncEnumerable.Empty<Developer>();
var options = new LlmChunkOptions { MaxTokensPerChunk = 500 };
-->
```csharp
await foreach (var jsonChunk in developers.ToJsonLlmChunksAsync(DeveloperJsonContext.Default.Developer, options)) { /* send jsonChunk to the model */ }

[JsonSerializable(typeof(Developer))]
internal partial class DeveloperJsonContext : JsonSerializerContext { }
```

---

## Key Features

* **Cross-platform SIMD CSV scanning** — AVX-512 and AVX2 (with PCLMULQDQ quote tracking) on x86-64, plus ARM64 `AdvSimd` (NEON) and portable 128-bit hardware `Vector128` SIMD (with branchless 6-step software prefix-XOR quote tracking); scalar fallback where SIMD is unavailable
* **Allocation-conscious scanning & writing** — span field access, pooled buffers, direct-to-buffer `ISpanFormattable` formatting in CSV/Fixed-Width/Excel/JSONL writers, and non-boxing `DbDataReader` exports; allocation profiles vary by API, options, and output representation
* **AOT/trimming ready** — source generators emit reflection-free binders for CSV, Fixed-Width, Excel, JSONL (read), and HTB; annotated with `[RequiresUnreferencedCode]` where reflection is unavoidable
* **Async streaming** — `IAsyncEnumerable<T>` for CSV, Fixed-Width, Excel, JSONL, and HTB; true non-blocking I/O with sync fast paths
* **Transparent `.gz`, `.br`, and `.zst`/`.zstd` compression** — automatic streaming compression/decompression by file extension across readers, writers, converters, schema inference, validation, and CLI commands (zero-dependency `ZstandardStream` on .NET 11+), enforcing `MaxInputSize` decompression caps
* **Extended CLR types & `ISpanParsable<T>`** — full read/write/generator/validation support for `DateOnly`, `TimeOnly`, `DateTimeOffset`, `TimeSpan` (`TimeSpanFormat`), `char`, `Half`, `Int128`, `UInt128`, `BigInteger`, `Uri`, and `byte[]` (Base64 in text/Excel/JSONL, raw binary in HTB), plus generic `ISpanParsable<T>`/`IUtf8SpanParsable<T>`/`ISpanFormattable`
* **Excel without extra dependencies** — reads and writes `.xlsx` using only `System.IO.Compression` and `System.Xml`
* **JSONL for AI/ML pipelines** — `Jsonl.Read<T>()` / `Jsonl.Write<T>()` mirror the CSV builder pattern; AOT-safe via `[GenerateBinder]` (read) or `JsonTypeInfo<T>`; `CsvToJsonlConverter` projects tabular data into OpenAI/Anthropic fine-tuning shapes
* **HTB binary format** — float-array embedding support, extended binary tags (`byte[]`, 128-bit/big integers, temporal types), big-endian byte-order reversal, and direct CSV ↔ HTB conversion
* **Embedding-API batching** — `IAsyncEnumerable<T>.BatchAsync(size)` groups streamed records into fixed-size batches for OpenAI/Voyage/Cohere/Anthropic embedding calls
* **Inline vector parser** — `VectorParser.ParseFloats(span)` handles pre-computed embeddings (`"[0.1,0.2,…]"`, comma/semicolon/whitespace separators, culture-aware)
* **DataReader import & export** — `Csv.CreateDataReader()`, `FixedWidth.CreateDataReader()`, `Excel.CreateDataReader()`, `Jsonl.CreateDataReader()`, and `Htb.CreateDataReader()` for streaming database bulk loads (`CsvDataReader`, `FixedWidthDataReader`, and `ExcelDataReader` inherit `TabularDataReader`), plus reflection-free, non-boxing `WriteFromDataReader*` export APIs across all five formats
* **PipeReader integration** — `Csv.ReadFromPipeReaderAsync(pipe)` for network streaming without buffering the entire payload
* **Multi-schema CSV** — discriminator-based row routing to different record types, with runtime and source-generated dispatch
* **Delimiter detection & schema inference** — auto-detect comma, semicolon, pipe, or tab from sample rows with a confidence score, and infer column types with optional `DistinguishTemporalTypes` (`DateOnly`, `TimeOnly`, `DateTimeOffset`, `TimeSpan`)
* **WebAssembly / JavaScript preview** — `readCsv`, `writeCsv`, `readFixedWidth`, `writeFixedWidth`, `readExcel`, `writeExcel`, `detectCsvDelimiter`, `validateCsv`, `inferCsvSchema`, `repairLlmCsv`, and `convertCsvToJsonl` in [the npm preview package](npm/packages/heroparser/README.md)
* **CSV validation** — pre-flight structural checks with detailed per-row error reporting
* **Field validation** — `[Validate]` constraints (NotNull, NotEmpty, Range, Pattern) collected lazily; in strict mode (the default) `ToList()` and other terminal methods throw a `ValidationException` when any row failed
* **CSV injection protection** — configurable sanitization modes for user-data exports
* **Progress reporting** — row/byte callbacks for large-file UX
* **Custom type converters** — register converters for domain types on any reader or writer
* **Write capacity pre-allocation** — estimates backing buffer capacity from collection counts when writing to strings to reduce buffer growth; actual sizes and benefits depend on the records
* **Multi-framework** — .NET 8, 9, 10, and 11 (net11.0 from 2.9.0, built and tested against the .NET 11 GA SDK); CI validates every target on Windows, Linux, and macOS

---

## Detailed Documentation

For advanced features and full API guides, see the files under the `docs` folder:
* [CSV Guide](docs/csv.md) — Fluent readers/writers, validation, PipeReader, and multi-schema dispatching.
* [Excel Guide](docs/excel.md) — Multi-sheet workbooks, custom formatting, and progress tracking.
* [Fixed-Width Guide](docs/fixed-width.md) — Positional mapping, alignment, padding, and custom type converters.
* [JSONL Guide](docs/jsonl.md) — Fine-tuning templates, vector parsing, and Native AOT setups.
* [HTB Guide](docs/htb.md) — High-Throughput Tabular Binary format, fluent APIs, CSV ↔ HTB conversion, and Native AOT support.
* [Console Guide](docs/console.md) — Standard rendering, markup, widgets (Table, Panel, Rule), and interactive prompts.
* [CLI Guide](docs/cli.md) — Local-First AI-native architecture, auto-detecting terminal CLIs (`agy`/`openai`/`claude`/`copilot`/`ollama`), stdin streaming, process management, and interactive query/translate wizard.
* [Benchmarks Guide](docs/benchmarks.md) — Execution environments, detailed CPU metrics, and comparisons.

---

## Building & Testing

```bash
# Build all projects
dotnet build

# xUnit v3 uses Microsoft.Testing.Platform; pass MTP arguments after `--`.
# Run unit tests
dotnet test tests/HeroParser.Tests -- --filter-trait Category=Unit

# Run integration tests
dotnet test tests/HeroParser.Tests -- --filter-trait Category=Integration

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
