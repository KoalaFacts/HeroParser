# HeroParser - A .Net high performant, Zero-Allocation CSV Parser with RFC 4180 Quote Handling

[![Build and Test](https://github.com/KoalaFacts/HeroParser/actions/workflows/ci.yml/badge.svg)](https://github.com/KoalaFacts/HeroParser/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/HeroParser.svg)](https://www.nuget.org/packages/HeroParser)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

**High-Performance SIMD Parsing** | RFC 4180 Quote Handling | Zero Allocations

## 🚀 Key Features

- **RFC 4180 Quote Handling**: Supports quoted fields with escaped quotes (`""`), commas in quotes, per spec
- **Quote-Aware SIMD**: Maintains SIMD performance even with quoted fields
- **Zero Allocations**: Stack-only parsing with ArrayPool for column metadata
- **Lazy Evaluation**: Columns parsed only when accessed
- **Configurable RFC vs Speed**: Toggle quote parsing and opt-in newlines-in-quotes; defaults favor speed
- **Multi-Framework**: .NET 8, 9, and 10 support

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
    MaxColumns = 100, // Default
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

## Benchmarks

```bash
# Throughput (string-based)
dotnet run --project benchmarks/HeroParser.Benchmarks -c Release -- --throughput

# Streaming vs text (file + stream + async)
dotnet run --project benchmarks/HeroParser.Benchmarks -c Release -- --streaming

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

### Writing CSV Data

HeroParser includes a high-performance CSV writer for creating RFC 4180 compliant output:

```csharp
// Write to string
var rows = new[] {
    new[] { "Name", "Age", "City" },
    new[] { "Alice", "30", "New York" },
    new[] { "Bob", "25", "San Francisco" }
};
var csv = Csv.WriteToString(rows);

// Write to file
using var writer = Csv.WriteToFile("output.csv");
writer.WriteRow("Name", "Age", "City");
writer.WriteRow("Alice", "30", "New York");
writer.WriteRow("Bob", "25", "San Francisco");

// Write to stream
using var stream = new MemoryStream();
using var writer = Csv.WriteToStream(stream);
writer.WriteField("Name");
writer.WriteField("Age");
writer.EndRow();
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
    MaxFieldLength = 10_000  // Throw exception if any field exceeds 10KB
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

Track the line number of each row for error reporting:

```csharp
foreach (var row in Csv.ReadFromText(csv))
{
    try
    {
        var id = row[0].Parse<int>();
    }
    catch (FormatException)
    {
        Console.WriteLine($"Invalid data on line {row.LineNumber}");
    }
}
```

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
