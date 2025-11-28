# HeroParser - A .Net High-Performance CSV Parser & Writer with RFC 4180 Compliance

[![Build and Test](https://github.com/KoalaFacts/HeroParser/actions/workflows/ci.yml/badge.svg)](https://github.com/KoalaFacts/HeroParser/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/HeroParser.svg)](https://www.nuget.org/packages/HeroParser)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

**High-Performance SIMD Parsing & Writing** | RFC 4180 Quote Handling | Zero Allocations

## 🚀 Key Features

### Reading
- **RFC 4180 Quote Handling**: Supports quoted fields with escaped quotes (`""`), commas in quotes, per spec
- **Quote-Aware SIMD**: Maintains SIMD performance even with quoted fields
- **Zero Allocations**: Stack-only parsing with ArrayPool for column metadata
- **Lazy Evaluation**: Columns parsed only when accessed
- **Configurable RFC vs Speed**: Toggle quote parsing and opt-in newlines-in-quotes; defaults favor speed
- **Fluent Builder API**: Configure readers with chainable methods (`Csv.Read<T>()`)
- **LINQ-Style Extensions**: `Where()`, `Select()`, `First()`, `ToList()`, `GroupBy()`, and more

### Writing
- **High-Performance CSV Writer**: 2-5x faster than Sep with 35-85% less memory allocation
- **SIMD-Accelerated**: Uses AVX2/SSE2 for quote detection and field analysis
- **RFC 4180 Compliant**: Proper quote escaping and field quoting
- **Fluent Builder API**: Configure writers with chainable methods (`Csv.Write<T>()`)
- **Multiple Output Targets**: Write to strings, streams, or files

### General
- **Async Streaming**: True async I/O with `IAsyncEnumerable<T>` support for reading and writing
- **AOT/Trimming Support**: Source generators for reflection-free binding (`[CsvGenerateBinder]`)
- **Line Number Tracking**: Both logical row numbers and physical source line numbers for error reporting
- **Progress Reporting**: Track parsing progress for large files with callbacks
- **Custom Type Converters**: Register converters for domain-specific types
- **Multi-Framework**: .NET 8, 9, and 10 support
- **Zero Dependencies**: No external packages for core library

## 🎯 Design Philosophy

### Zero-Allocation, RFC-Compliant Design

- **Target Frameworks**: .NET 8, 9, 10 (modern JIT optimizations)
- **Memory Safety**: No `unsafe` keyword - uses safe `Unsafe` class and `MemoryMarshal` APIs for performance
- **Minimal API**: Simple, focused API surface
- **Zero Dependencies**: No external packages for core library
- **RFC 4180**: Quote handling, escaped quotes, delimiters in quotes; optional newlines-in-quotes (default off), no header detection
- **SIMD First**: Quote-aware SIMD for AVX-512, AVX2, NEON
- **Allocation Notes**: Char-span parsing remains allocation-free; UTF-8 parsing stays zero-allocation for invariant primitives. Culture/format-based parsing on UTF-8 columns decodes to UTF-16 and allocates by design.

### API Surface

```csharp
// Primary API - parse from string with options
var reader = Csv.ReadFromText(csvData);

// Custom options (delimiter, quote character, max columns)
var options = new CsvParserOptions
{
    Delimiter = ',',  // Default
    Quote = '"',      // Default - RFC 4180 compliant
    MaxColumnCount = 100, // Default
    AllowNewlinesInsideQuotes = false, // Enable for full RFC newlines-in-quotes support (slower)
    EnableQuotedFields = true         // Disable for maximum speed when your data has no quotes
};
var reader = Csv.ReadFromText(csvData, options);
```

## 📊 Usage Examples

### Basic Iteration (Zero Allocations)

```csharp
foreach (var row in Csv.ReadFromText(csv))
{
    // Access columns by index - no allocations
    var id = row[0].Parse<int>();
    var name = row[1].CharSpan; // ReadOnlySpan<char>
    var price = row[2].Parse<decimal>();
}
```

### Files and Streams

```csharp
using var fileReader = Csv.ReadFromFile("data.csv"); // streams file without loading it fully

using var stream = File.OpenRead("data.csv");
using var streamReader = Csv.ReadFromStream(stream); // leaveOpen defaults to true
```

Both overloads stream with pooled buffers and do not load the entire file/stream; dispose the reader (and the stream if you own it) to release resources.

#### Async I/O

```csharp
var source = await Csv.ReadFromFileAsync("data.csv");
using var reader = source.CreateReader();
```

Async overloads also buffer the full payload (required because readers are ref structs); use when you need non-blocking file/stream reads.

#### Streaming large files (low memory)

```csharp
using var reader = Csv.ReadFromStream(File.OpenRead("data.csv"));
while (reader.MoveNext())
{
    var row = reader.Current;
    var id = row[0].Parse<int>();
}
```

Streaming keeps a pooled buffer and does not load the entire file into memory; rows remain valid until the next `MoveNext` call.

#### Async streaming (without buffering entire file)

```csharp
await using var reader = Csv.CreateAsyncStreamReader(File.OpenRead("data.csv"));
while (await reader.MoveNextAsync())
{
    var row = reader.Current;
    var id = row[0].Parse<int>();
}
```

Async streaming uses pooled buffers and async I/O; each row stays valid until the next `MoveNextAsync` invocation.

### Fluent Reader Builder

Use the fluent builder API for a clean, chainable configuration:

```csharp
// Read CSV records with fluent configuration
var records = Csv.Read<Person>()
    .WithDelimiter(';')
    .TrimFields()
    .AllowMissingColumns()
    .SkipRows(2)  // Skip metadata rows
    .FromText(csvData)
    .ToList();

// Read from file with async streaming
await foreach (var person in Csv.Read<Person>()
    .WithDelimiter(',')
    .FromFileAsync("data.csv"))
{
    Console.WriteLine($"{person.Name}: {person.Age}");
}
```

The builder provides a symmetric API to `CsvWriterBuilder<T>` for reading records.

### Manual Row-by-Row Reading (Fluent)

Use the non-generic builder for low-level row-by-row parsing:

```csharp
// Manual row-by-row reading with fluent configuration
using var reader = Csv.Read()
    .WithDelimiter(';')
    .TrimFields()
    .WithCommentCharacter('#')
    .FromText(csvData);

foreach (var row in reader)
{
    var id = row[0].Parse<int>();
    var name = row[1].ToString();
}

// Stream from file with custom options
using var fileReader = Csv.Read()
    .WithMaxFieldSize(10_000)
    .AllowNewlinesInQuotes()
    .FromFile("data.csv");
```

### LINQ-Style Extension Methods

CSV record readers provide familiar LINQ-style operations for working with records:

```csharp
// Materialize all records
var allPeople = Csv.Read<Person>().FromText(csv).ToList();
var peopleArray = Csv.Read<Person>().FromText(csv).ToArray();

// Query operations
var adults = Csv.Read<Person>()
    .FromText(csv)
    .Where(p => p.Age >= 18);

var names = Csv.Read<Person>()
    .FromText(csv)
    .Select(p => p.Name);

// First/Single operations
var first = Csv.Read<Person>().FromText(csv).First();
var firstAdult = Csv.Read<Person>().FromText(csv).First(p => p.Age >= 18);
var single = Csv.Read<Person>().FromText(csv).SingleOrDefault();

// Aggregation
var count = Csv.Read<Person>().FromText(csv).Count();
var adultCount = Csv.Read<Person>().FromText(csv).Count(p => p.Age >= 18);
var hasRecords = Csv.Read<Person>().FromText(csv).Any();
var allAdults = Csv.Read<Person>().FromText(csv).All(p => p.Age >= 18);

// Pagination
var page = Csv.Read<Person>().FromText(csv).Skip(10).Take(5);

// Grouping and indexing
var byCity = Csv.Read<Person>()
    .FromText(csv)
    .GroupBy(p => p.City);

var byId = Csv.Read<Person>()
    .FromText(csv)
    .ToDictionary(p => p.Id);

// Iteration
Csv.Read<Person>()
    .FromText(csv)
    .ForEach(p => Console.WriteLine(p.Name));
```

> **Note**: Since CSV readers are ref structs, they cannot implement `IEnumerable<T>`. These extension methods consume the reader and return materialized results.

### Advanced Reader Options

#### Progress Reporting

Track parsing progress for large files:

```csharp
var progress = new Progress<CsvProgress>(p =>
{
    var pct = p.TotalBytes > 0 ? (p.BytesProcessed * 100.0 / p.TotalBytes) : 0;
    Console.WriteLine($"Processed {p.RowsProcessed} rows ({pct:F1}%)");
});

var records = Csv.Read<Person>()
    .WithProgress(progress, intervalRows: 1000)
    .FromFile("large-file.csv")
    .ToList();
```

#### Error Handling

Handle deserialization errors gracefully:

```csharp
var records = Csv.Read<Person>()
    .OnError(ctx =>
    {
        Console.WriteLine($"Error at row {ctx.Row}, column '{ctx.MemberName}': {ctx.Exception?.Message}");
        return DeserializeErrorAction.Skip;  // Or UseDefault, Throw
    })
    .FromText(csv)
    .ToList();
```

#### Header Validation

Enforce required headers and detect duplicates:

```csharp
// Require specific headers
var records = Csv.Read<Person>()
    .RequireHeaders("Name", "Email", "Age")
    .FromText(csv)
    .ToList();

// Detect duplicate headers
var records = Csv.Read<Person>()
    .DetectDuplicateHeaders()
    .FromText(csv)
    .ToList();

// Custom header validation
var records = Csv.Read<Person>()
    .ValidateHeaders(headers =>
    {
        if (!headers.Contains("Id"))
            throw new CsvException(CsvErrorCode.InvalidHeader, "Missing required 'Id' column");
    })
    .FromText(csv)
    .ToList();
```

#### Custom Type Converters

Register custom converters for domain-specific types:

```csharp
var records = Csv.Read<Order>()
    .RegisterConverter<Money>((column, culture) =>
    {
        var text = column.ToString();
        if (Money.TryParse(text, out var money))
            return money;
        throw new FormatException($"Invalid money format: {text}");
    })
    .FromText(csv)
    .ToList();
```

## ✍️ CSV Writing

HeroParser includes a high-performance CSV writer that is 2-5x faster than Sep with significantly lower memory allocations.

### Basic Writing

```csharp
// Write records to a string
var records = new[]
{
    new Person { Name = "Alice", Age = 30 },
    new Person { Name = "Bob", Age = 25 }
};

string csv = Csv.WriteToText(records);
// Output:
// Name,Age
// Alice,30
// Bob,25
```

### Writing to Files and Streams

```csharp
// Write to a file
Csv.WriteToFile("output.csv", records);

// Write to a stream
using var stream = File.Create("output.csv");
Csv.WriteToStream(stream, records);

// Async writing (optimized for in-memory collections)
await Csv.WriteToFileAsync("output.csv", records);

// Async writing with IAsyncEnumerable (for streaming data sources)
await Csv.WriteToFileAsync("output.csv", GetRecordsAsync());
```

### High-Performance Async Writing

For scenarios requiring true async I/O, use the `CsvAsyncStreamWriter`:

```csharp
// Low-level async writer with sync fast paths
await using var writer = Csv.CreateAsyncStreamWriter(stream);
await writer.WriteRowAsync(new[] { "Alice", "30", "NYC" });
await writer.WriteRowAsync(new[] { "Bob", "25", "LA" });
await writer.FlushAsync();

// Builder API with async streaming (16-43% faster than sync at scale)
await Csv.Write<Person>()
    .WithDelimiter(',')
    .WithHeader()
    .ToStreamAsyncStreaming(stream, records);  // IEnumerable overload
```

The async writer uses sync fast paths when data fits in the buffer, avoiding async overhead for small writes while supporting true non-blocking I/O for large datasets.

### Writer Options

```csharp
var options = new CsvWriterOptions
{
    Delimiter = ',',           // Field delimiter (default: comma)
    Quote = '"',               // Quote character (default: double quote)
    NewLine = "\r\n",          // Line ending (default: CRLF per RFC 4180)
    WriteHeader = true,        // Include header row (default: true)
    QuoteStyle = QuoteStyle.WhenNeeded,  // Quote only when necessary
    NullValue = "",            // String to write for null values
    Culture = CultureInfo.InvariantCulture,
    DateTimeFormat = "O",      // ISO 8601 format for dates
    NumberFormat = "G"         // General format for numbers
};

string csv = Csv.WriteToText(records, options);
```

### Fluent Writer Builder

```csharp
// Write records with fluent configuration
var csv = Csv.Write<Person>()
    .WithDelimiter(';')
    .AlwaysQuote()
    .WithDateTimeFormat("yyyy-MM-dd")
    .WithHeader()
    .ToText(records);

// Write to file with async streaming
await Csv.Write<Person>()
    .WithDelimiter(',')
    .WithoutHeader()
    .ToFileAsync("output.csv", recordsAsync);
```

The builder provides a symmetric API to `CsvReaderBuilder<T>` for writing records.

### Manual Row-by-Row Writing (Fluent)

Use the non-generic builder for low-level row-by-row writing:

```csharp
// Manual row-by-row writing with fluent configuration
using var writer = Csv.Write()
    .WithDelimiter(';')
    .AlwaysQuote()
    .WithDateTimeFormat("yyyy-MM-dd")
    .CreateWriter(Console.Out);

writer.WriteField("Name");
writer.WriteField("Age");
writer.EndRow();

writer.WriteField("Alice");
writer.WriteField(30);
writer.EndRow();

writer.Flush();

// Write to file with custom options
using var fileWriter = Csv.Write()
    .WithNewLine("\n")
    .WithCulture("de-DE")
    .CreateFileWriter("output.csv");
```

### Low-Level Row Writing

```csharp
using var writer = Csv.CreateWriter(Console.Out);

// Write header
writer.WriteField("Name");
writer.WriteField("Age");
writer.EndRow();

// Write data rows
writer.WriteField("Alice");
writer.WriteField(30);
writer.EndRow();

writer.Flush();
```

### Error Handling

```csharp
var options = new CsvWriterOptions
{
    OnSerializeError = ctx =>
    {
        Console.WriteLine($"Error at row {ctx.Row}, column '{ctx.MemberName}': {ctx.Exception?.Message}");
        return SerializeErrorAction.WriteNull;  // Or SkipRow, Throw
    }
};
```

## Benchmarks

```bash
# Reading: Throughput (string-based)
dotnet run --project benchmarks/HeroParser.Benchmarks -c Release -- --throughput

# Reading: Streaming vs text (file + stream + async)
dotnet run --project benchmarks/HeroParser.Benchmarks -c Release -- --streaming

# Reading: HeroParser vs Sep comparison
dotnet run --project benchmarks/HeroParser.Benchmarks -c Release -- --vs-sep-reading

# Writing: HeroParser vs Sep comparison
dotnet run --project benchmarks/HeroParser.Benchmarks -c Release -- --vs-sep-writing

# Writing: All writer benchmarks (sync + async)
dotnet run --project benchmarks/HeroParser.Benchmarks -c Release -- --writer

# Writing: Sync writer benchmarks only
dotnet run --project benchmarks/HeroParser.Benchmarks -c Release -- --sync-writer

# Writing: Async writer benchmarks only
dotnet run --project benchmarks/HeroParser.Benchmarks -c Release -- --async-writer

# Run all configured benchmarks
dotnet run --project benchmarks/HeroParser.Benchmarks -c Release -- --all
```

### Quote Handling (RFC 4180)

```csharp
var csv = "field1,\"field2\",\"field,3\"\n" +
          "aaa,\"b,bb\",ccc\n" +
          "zzz,\"y\"\"yy\",xxx";  // Escaped quote

foreach (var row in Csv.ReadFromText(csv))
{
    // Access raw value (includes quotes)
    var raw = row[1].ToString(); // "b,bb"

    // Remove surrounding quotes and unescape
    var unquoted = row[1].UnquoteToString(); // b,bb

    // Zero-allocation unquote (returns span)
    var span = row[1].Unquote(); // ReadOnlySpan<char>
}
```

### Type Parsing

```csharp
foreach (var row in Csv.ReadFromText(csv))
{
    // Generic parsing (ISpanParsable<T>)
    var value = row[0].Parse<int>();

    // Optimized type-specific methods
    if (row[1].TryParseDouble(out double d)) { }
    if (row[2].TryParseDateTime(out DateTime dt)) { }
    if (row[3].TryParseBoolean(out bool b)) { }

    // Additional type parsing
    if (row[4].TryParseGuid(out Guid id)) { }
    if (row[5].TryParseEnum<DayOfWeek>(out var day)) { }  // Case-insensitive
    if (row[6].TryParseTimeZoneInfo(out TimeZoneInfo tz)) { }
}
```

### Lazy Evaluation

```csharp
// Columns are NOT parsed until first access
foreach (var row in Csv.ReadFromText(csv))
{
    // Skip rows without parsing columns
    if (ShouldSkip(row))
        continue;

    // Only parse columns when accessed
    var value = row[0].Parse<int>();  // First access triggers parsing
}
```

### Comment Lines

Skip comment lines in CSV files:

```csharp
var options = new CsvParserOptions
{
    CommentCharacter = '#'  // Lines starting with # are ignored
};

var csv = @"# This is a comment
Name,Age
Alice,30
# Another comment
Bob,25";

foreach (var row in Csv.ReadFromText(csv, options))
{
    // Only data rows are processed
}
```

### Trimming Whitespace

Remove leading and trailing whitespace from unquoted fields:

```csharp
var options = new CsvParserOptions
{
    TrimFields = true  // Trim whitespace from unquoted fields
};

var csv = "  Name  ,  Age  \nAlice,  30  ";
foreach (var row in Csv.ReadFromText(csv, options))
{
    var name = row[0].ToString();  // "Name" (trimmed)
    var age = row[1].ToString();   // "30" (trimmed)
}
```

### Null Value Handling

Treat specific string values as null during record parsing:

```csharp
var recordOptions = new CsvRecordOptions
{
    NullValues = new[] { "NULL", "N/A", "NA", "" }
};

var csv = "Name,Value\nAlice,100\nBob,NULL\nCharlie,N/A";
foreach (var record in Csv.ParseRecords<MyRecord>(csv, recordOptions))
{
    // record.Value will be null when the field contains "NULL" or "N/A"
}
```

### Security: Field Length Limits

Protect against DoS attacks with oversized fields:

```csharp
var options = new CsvParserOptions
{
    MaxFieldSize = 10_000  // Throw exception if any field exceeds 10KB
};

// This will throw CsvException if a field is too large
var reader = Csv.ReadFromText(csv, options);
```

### Skip Metadata Rows

Skip header rows or metadata before parsing:

```csharp
var recordOptions = new CsvRecordOptions
{
    SkipRows = 2,  // Skip first 2 rows (e.g., metadata)
    HasHeaderRow = true  // The 3rd row is the header
};

var csv = @"File Version: 1.0
Generated: 2024-01-01
Name,Age
Alice,30
Bob,25";

foreach (var record in Csv.ParseRecords<MyRecord>(csv, recordOptions))
{
    // First 2 rows are skipped, 3rd row used as header
}
```

### Storing Rows Safely

Rows are ref structs and cannot escape their scope. Use `Clone()` or `ToImmutable()` to store them:

```csharp
var storedRows = new List<CsvCharSpanRow>();

foreach (var row in Csv.ReadFromText(csv))
{
    // ❌ WRONG: Cannot store ref struct directly
    // storedRows.Add(row);

    // ✅ CORRECT: Clone creates an owned copy
    storedRows.Add(row.Clone());
}

// Rows can now be safely accessed after enumeration
foreach (var row in storedRows)
{
    var value = row[0].ToString();
}
```

### Line Number Tracking

Track row positions and source line numbers for error reporting:

```csharp
foreach (var row in Csv.ReadFromText(csv))
{
    try
    {
        var id = row[0].Parse<int>();
    }
    catch (FormatException)
    {
        // LineNumber: 1-based logical row position (ordinal)
        // SourceLineNumber: 1-based physical line in the file (handles multi-line quoted fields)
        Console.WriteLine($"Invalid data at row {row.LineNumber} (source line {row.SourceLineNumber})");
    }
}
```

This distinction is important when CSV files contain multi-line quoted fields - `LineNumber` gives you the row index while `SourceLineNumber` tells you the exact line in the source file where the row starts.

### ⚠️ Important: Resource Management

**HeroParser readers use `ArrayPool` buffers and MUST be disposed to prevent memory leaks.**

```csharp
// ✅ RECOMMENDED: Use 'using' statement
using (var reader = Csv.ReadFromText(csv))
{
    foreach (var row in reader)
    {
        var value = row[0].ToString();
    }
} // ArrayPool buffers automatically returned

// ✅ ALSO WORKS: foreach automatically disposes
foreach (var row in Csv.ReadFromText(csv))
{
    var value = row[0].ToString();
} // Disposed after foreach completes

// ❌ AVOID: Manual iteration without disposal
var reader = Csv.ReadFromText(csv);
while (reader.MoveNext())
{
    // ...
}
// MEMORY LEAK! ArrayPool buffers not returned

// ✅ FIX: Manually dispose if not using foreach
var reader = Csv.ReadFromText(csv);
try
{
    while (reader.MoveNext()) { /* ... */ }
}
finally
{
    reader.Dispose(); // Always dispose!
}
```

## 🏗️ Building

**Requirements:**
- .NET 8, 9, or 10 SDK
- C# 12+ language features
- Recommended: AVX-512 or AVX2 capable CPU for maximum performance

```bash
# Build library
dotnet build src/HeroParser/HeroParser.csproj

# Run tests
dotnet test tests/HeroParser.Tests/HeroParser.Tests.csproj

# Run all benchmarks
dotnet run --project benchmarks/HeroParser.Benchmarks -c Release -- --all
```

### Development Setup

To enable pre-commit format checks (recommended):

```bash
# Configure git to use the project's hooks
git config core.hooksPath .githooks
```

This runs `dotnet format --verify-no-changes` before each commit. If formatting issues are found, the commit is blocked until you run `dotnet format` to fix them.

## 🔧 Source Generators (AOT Support)

For AOT (Ahead-of-Time) compilation scenarios, HeroParser supports source-generated binders that avoid reflection:

```csharp
using HeroParser.SeparatedValues.Records.Binding;

[CsvGenerateBinder]
public class Person
{
    public string Name { get; set; } = "";
    public int Age { get; set; }
    public string? Email { get; set; }
}
```

The `[CsvGenerateBinder]` attribute instructs the source generator to emit a compile-time binder, enabling:
- **AOT compatibility** - No runtime reflection required
- **Faster startup** - Binders are pre-compiled
- **Trimming-safe** - Works with .NET trimming/linking

> **Note**: Source generators require the `HeroParser.Generators` package and a compatible SDK.

## ⚠️ RFC 4180 Compliance

HeroParser implements **core RFC 4180 features**:

✅ **Supported:**
- Quoted fields with double-quote character (`"`)
- Escaped quotes using double-double-quotes (`""`)
- Delimiters (commas) within quoted fields
- Both LF (`\n`) and CRLF (`\r\n`) line endings
- Newlines inside quoted fields when `AllowNewlinesInsideQuotes = true` (default is `false` for performance)
- Empty fields and spaces preserved
- Custom delimiters and quote characters

❌ **Not Supported:**
- **Automatic header detection** - Users skip header rows manually

This provides excellent RFC 4180 compatibility for most CSV use cases (logs, exports, data interchange).

## 📝 License

MIT

## 🙏 Acknowledgments & Credits

HeroParser was deeply inspired by the excellent work in the .NET CSV parsing ecosystem:

### Primary Inspiration: Sep by nietras

**[Sep](https://github.com/nietras/Sep)** by nietras is currently one of the fastest CSV parsers for .NET and served as the primary inspiration for HeroParser's architecture. The core techniques learned from Sep include:

- **Bitmask-based Quote-Aware SIMD**: The fundamental approach of using bitmasks to track delimiters and quotes simultaneously, allowing SIMD performance even with quoted fields
- **Quote Parity Tracking**: Using quote count parity (`quoteCount & 1`) to determine when inside/outside quotes, which elegantly handles escaped quotes (`""`) without special cases
- **UTF-8 First Design**: Processing bytes directly rather than UTF-16 characters for better SIMD efficiency
- **Streaming Architecture**: Single-pass parsing that identifies all column boundaries in one SIMD loop

HeroParser adapts these techniques while focusing on:
- Lazy column evaluation to minimize allocations in filtering scenarios
- .NET 8-10 targeting for the latest JIT optimizations and SIMD intrinsics
- Zero external dependencies for the core library
- Extensive quote handling test coverage for RFC 4180 compliance

The `VsSepBenchmarks.cs` benchmarks provide head-to-head performance comparisons to ensure HeroParser remains competitive while offering these additional features.

### Additional Inspiration

- **Sylvan.Data.Csv** - Alternative high-performance CSV parsing approach and API design patterns
- **SimdUnicode** - SIMD validation and text processing techniques

### Special Thanks

Deep gratitude to nietras for creating Sep and making it open source. The research documented in `docs/sep-research.md` was instrumental in understanding modern SIMD-based CSV parsing. Without Sep's pioneering work, HeroParser would not exist.

---

Built to be a **competitive, RFC 4180 compliant, zero-allocation CSV parser for .NET**! 🚀
