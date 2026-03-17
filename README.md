# HeroParser - A .NET High-Performance CSV, Fixed-Width & Excel (.xlsx) Parser

[![Build and Test](https://github.com/KoalaFacts/HeroParser/actions/workflows/ci.yml/badge.svg)](https://github.com/KoalaFacts/HeroParser/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/HeroParser.svg)](https://www.nuget.org/packages/HeroParser)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

High-performance SIMD-accelerated CSV, Fixed-Width, and Excel (.xlsx) parsing and writing for .NET 8, 9, and 10. Zero extra dependencies, AOT/trimming ready, source-generated binding.

## Install

```bash
dotnet add package HeroParser
```

## Quick Start: CSV

Define a record — `[GenerateBinder]` enables source-generated, reflection-free binding. Unmarked properties auto-map by name.

```csharp
[GenerateBinder]
public class Order
{
    public int Id { get; set; }
    public string Customer { get; set; } = "";

    [Validate(NotEmpty = true, RangeMin = 0)]
    public decimal Amount { get; set; }

    [Parse(Format = "yyyy-MM-dd")]
    public DateTime OrderDate { get; set; }
}
```

### Read

```csharp
// Full fluent chain — delimiter, culture, validation, progress, error handling
var orders = Csv.Read<Order>()
    .WithDelimiter(';')
    .TrimFields()
    .WithCulture("de-DE")
    .WithNullValues("N/A", "NULL", "")
    .SkipRows(2)
    .WithValidationMode(ValidationMode.Lenient)
    .OnError((ctx, ex) =>
    {
        Console.WriteLine($"Row {ctx.Row}: {ex.Message}");
        return CsvDeserializeErrorAction.SkipRecord;
    })
    .WithProgress(new Progress<CsvProgress>(p =>
        Console.WriteLine($"{p.RowsProcessed:N0} rows ({p.ProgressPercentage:P0})")))
    .FromFile("orders.csv")
    .ToList();

// Async streaming — process millions of rows without buffering
await foreach (var order in Csv.Read<Order>().FromFileAsync("orders.csv"))
    Console.WriteLine($"{order.Id}: {order.Customer}");

// Zero-alloc row-level — no record type, no heap allocations
foreach (var row in Csv.ReadFromText(csv))
{
    var id    = row[0].Parse<int>();
    var name  = row[1].CharSpan;      // ReadOnlySpan<char>
    var price = row[2].Parse<decimal>();
}
```

### Write

```csharp
Csv.Write<Order>()
    .WithDelimiter(';')
    .WithDateTimeFormat("yyyy-MM-dd")
    .WithNumberFormat("N2")
    .WithCulture("en-US")
    .AlwaysQuote()
    .WithInjectionProtection(CsvInjectionProtection.EscapeWithTab)
    .WithMaxRowCount(100_000)
    .OnError(ctx =>
    {
        Console.WriteLine($"Row {ctx.Row}, {ctx.MemberName}: {ctx.Exception.Message}");
        return SerializeErrorAction.SkipRow;
    })
    .WithProgress(new Progress<CsvWriteProgress>(p =>
        Console.WriteLine($"{p.RowsWritten:N0} rows written")))
    .ToFile("out.csv", orders);

// Async — same chain, different terminal
await Csv.Write<Order>()
    .WithDateTimeFormat("O")
    .ToFileAsync("out.csv", orders);
```

## Quick Start: Excel

### Read

```csharp
// Full chain — sheet selection, culture, validation, error handling
var orders = Excel.Read<Order>()
    .FromSheet("Sales")
    .WithCulture("en-US")
    .WithNullValues("N/A", "")
    .SkipRows(1)
    .CaseSensitiveHeaders()
    .WithValidationMode(ValidationMode.Lenient)
    .OnError((ctx, ex) =>
    {
        Console.WriteLine($"Sheet '{ctx.SheetName}', row {ctx.Row}: {ex.Message}");
        return ExcelDeserializeErrorAction.SkipRecord;
    })
    .WithProgress(new Progress<ExcelProgress>(p =>
        Console.WriteLine($"Sheet '{p.SheetName}': {p.RowsRead:N0} rows")))
    .FromFile("orders.xlsx");

// Multi-sheet — different types per sheet
var result = Excel.Read()
    .WithSheet<Order>("Orders")
    .WithSheet<Customer>("Customers")
    .FromFile("workbook.xlsx");

var orders    = result.Get<Order>();
var customers = result.Get<Customer>();

// All sheets, same type
var allSheets = Excel.Read<Order>().AllSheets().FromFile("orders.xlsx");
// allSheets: Dictionary<string, List<Order>>
```

### Write

```csharp
// Full chain — formatting, error handling, progress, output limits
Excel.Write<Order>()
    .WithSheetName("Sales Q1")
    .WithDateTimeFormat("yyyy-MM-dd")
    .WithNumberFormat("N2")
    .WithCulture("en-US")
    .WithMaxRowCount(1_000_000)
    .WithMaxOutputSize(500 * 1024 * 1024)  // 500 MB limit
    .OnError(ctx =>
    {
        Console.WriteLine($"Row {ctx.Row}, {ctx.MemberName}: {ctx.Exception.Message}");
        return ExcelSerializeErrorAction.SkipRow;
    })
    .WithProgress(new Progress<ExcelWriteProgress>(p =>
        Console.WriteLine($"Sheet '{p.SheetName}': {p.RowsWritten:N0} rows")))
    .ToFile("out.xlsx", orders);

// Multi-sheet write
Excel.WriteMultiSheet()
    .WithSheet("Orders", orders)
    .WithSheet("Customers", customers)
    .ToFile("workbook.xlsx");

// Async (offloads to thread pool — ZIP format requires sync I/O internally)
await Excel.Write<Order>().ToFileAsync("out.xlsx", orders);
```

## Quick Start: Fixed-Width

Define a record — `[PositionalMap]` declares character-position boundaries.

```csharp
[GenerateBinder]
public class Employee
{
    [PositionalMap(Start = 0, Length = 10)]
    public string Id { get; set; } = "";

    [PositionalMap(Start = 10, Length = 30)]
    public string Name { get; set; } = "";

    [PositionalMap(Start = 40, Length = 10, Alignment = FieldAlignment.Right, PadChar = '0')]
    [Validate(RangeMin = 0)]
    public decimal Salary { get; set; }

    [PositionalMap(Start = 50, Length = 8)]
    [Parse(Format = "yyyyMMdd")]
    public DateTime HireDate { get; set; }
}
```

### Read

```csharp
// Full chain — padding, alignment, validation, error handling
var result = FixedWidth.Read<Employee>()
    .WithDefaultPadChar(' ')
    .WithDefaultAlignment(FieldAlignment.Left)
    .AllowShortRows()
    .CaseSensitiveHeaders()
    .WithValidationMode(ValidationMode.Lenient)
    .OnError((ctx, ex) =>
    {
        Console.WriteLine($"Row {ctx.RecordNumber}: {ex.Message}");
        return FixedWidthDeserializeErrorAction.SkipRecord;
    })
    .WithProgress(new Progress<FixedWidthProgress>(p =>
        Console.WriteLine($"{p.RecordsProcessed:N0} records")))
    .FromFile("employees.dat");

var employees = result.Records;

// Async streaming
await foreach (var emp in FixedWidth.Read<Employee>().FromFileAsync("employees.dat"))
    Console.WriteLine($"{emp.Name}: {emp.Salary:C}");

// Inline mapping — no attributes needed
var records = FixedWidth.Read<Employee>()
    .Map(e => e.Id, f => f.Start(0).Length(10))
    .Map(e => e.Name, f => f.Start(10).Length(30))
    .Map(e => e.Salary, f => f.Start(40).Length(10).Alignment(FieldAlignment.Right).PadChar('0'))
    .Map(e => e.HireDate, f => f.Start(50).Length(8))
    .FromText(fixedWidthData);
```

### Write

```csharp
FixedWidth.Write<Employee>()
    .WithPadChar(' ')
    .AlignLeft()
    .WithDateTimeFormat("yyyyMMdd")
    .TruncateOnOverflow()
    .WithMaxRowCount(500_000)
    .OnError(ctx =>
    {
        Console.WriteLine($"Row {ctx.Row}: {ctx.Exception.Message}");
        return FixedWidthSerializeErrorAction.SkipRow;
    })
    .ToFile("out.dat", employees);

// Async
await FixedWidth.Write<Employee>()
    .WithNewLine("\r\n")
    .ToFileAsync("out.dat", employees);
```

## Unified Attribute System

HeroParser v2 uses concern-separated attributes that work across all formats:

| Attribute | Purpose |
|-----------|---------|
| `[GenerateBinder]` | Triggers source generator — emits a compile-time binder for AOT/trimming compatibility |
| `[TabularMap(Name, Index)]` | Column mapping for CSV and Excel |
| `[PositionalMap(Start, Length, End, PadChar, Alignment)]` | Position mapping for Fixed-Width |
| `[Parse(Format)]` | Read-side type conversion (e.g., date format string) |
| `[Format(WriteFormat, ExcludeIfAllEmpty)]` | Write-side formatting |
| `[Validate(NotNull, NotEmpty, MaxLength, MinLength, RangeMin, RangeMax, Pattern)]` | Bidirectional field validation |

A single record class can carry both `[TabularMap]` and `[PositionalMap]` attributes, allowing it to be used with CSV, Excel, and Fixed-Width APIs simultaneously.

Convention-based mapping: unmarked properties default to `[TabularMap(Name = propertyName)]` for CSV/Excel. Fixed-Width requires explicit `[PositionalMap]`.

## Key Features

- **SIMD-accelerated CSV parsing** — AVX-512, AVX2, and ARM NEON instruction sets; PCLMULQDQ-based branchless quote tracking
- **Zero allocations** — fixed 4 KB stack footprint regardless of column count or file size; `ArrayPool` for buffers
- **AOT/trimming ready** — source generators emit reflection-free binders; annotated with `[RequiresUnreferencedCode]` where reflection is unavoidable
- **Async streaming** — `IAsyncEnumerable<T>` for all three formats; true non-blocking I/O with sync fast paths
- **Excel without extra dependencies** — reads and writes `.xlsx` using only `System.IO.Compression` and `System.Xml`
- **DataReader support** — `Csv.CreateDataReader()`, `FixedWidth.CreateDataReader()`, `Excel.CreateDataReader()` for database bulk loading via `SqlBulkCopy`
- **PipeReader integration** — `Csv.ReadFromPipeReaderAsync(pipe)` for network streaming without buffering the entire payload
- **Multi-schema CSV** — discriminator-based row routing to different record types; source-generated dispatch for ~2.85x faster throughput
- **Delimiter detection** — auto-detect comma, semicolon, pipe, or tab from sample rows with a confidence score
- **CSV validation** — pre-flight structural checks with detailed per-row error reporting
- **Field validation** — `[Validate]` constraints (NotNull, NotEmpty, Range, Pattern) collected lazily; `result.ThrowIfAnyError()` for fail-fast
- **CSV injection protection** — configurable sanitization modes for user-data exports
- **Progress reporting** — row/byte callbacks for large-file UX
- **Custom type converters** — register converters for domain types on any reader or writer
- **Multi-framework** — .NET 8, 9, 10; CI validates all three on Windows, Linux, and macOS

## Benchmarks

Test configuration: AMD Ryzen AI 9 HX PRO 370, .NET 10, Release build.

### Reading Performance

HeroParser uses CLMUL-based branchless quote masking (PCLMULQDQ instruction) for quote-aware SIMD parsing.

| Rows | Columns | Quotes | Time | Throughput |
|------|---------|--------|------|------------|
| 10k | 25 | No | 552 μs | ~6.1 GB/s |
| 10k | 25 | Yes | 1,344 μs | ~5.1 GB/s |
| 10k | 100 | No | 1,451 μs | ~4.5 GB/s |
| 10k | 100 | Yes | 3,617 μs | ~1.9 GB/s |
| 100k | 100 | No | 14,568 μs | ~4.5 GB/s |
| 100k | 100 | Yes | 35,396 μs | ~1.9 GB/s |

Key characteristics:
- Fixed 4 KB allocation regardless of column count or file size
- UTF-8 optimized — use `byte[]` or `ReadOnlySpan<byte>` APIs for best performance
- Quote-aware SIMD — maintains high throughput even with quoted fields

### Writing Performance

| Scenario | Throughput | Memory |
|----------|------------|--------|
| Sync Writing | ~2-3 GB/s | 35-85% less than alternatives |
| Async Writing | ~1.5-2 GB/s | Pooled buffers, minimal GC |

Run benchmarks locally:

```bash
dotnet run -c Release --project benchmarks/HeroParser.Benchmarks -- --all
```

## Detailed API Reference

- [CSV API Reference](docs/csv.md) — Full CSV reading, writing, options, delimiter detection, validation, multi-schema, security, PipeReader, DataReader
- [Excel API Reference](docs/excel.md) — Full Excel reading, writing, multi-sheet, DataReader, options
- [Fixed-Width API Reference](docs/fixed-width.md) — Full fixed-width reading, writing, fluent mapping, options, converters, PipeReader

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

# Regenerate NuGet lock files after adding/updating packages
dotnet restore --force-evaluate
```

CI builds Release configuration across .NET 8, 9, and 10 on Windows, Linux, and macOS.

## License

MIT

## Acknowledgments

HeroParser was inspired by the excellent work in the .NET CSV parsing ecosystem:

- **[simdjson](https://github.com/simdjson/simdjson)** by Geoff Langdale & Daniel Lemire — The PCLMULQDQ carry-less multiplication technique for branchless prefix-XOR quote tracking
- **[Sep](https://github.com/nietras/Sep)** by nietras — Pioneering SIMD-based CSV parsing techniques for .NET
- **Sylvan.Data.Csv** — High-performance CSV parsing patterns
- **SimdUnicode** — SIMD text processing techniques

Special thanks to the .NET performance community for their research and open-source contributions.

---

**High-performance, zero-allocation, AOT-ready CSV, Fixed-Width & Excel parsing for .NET**
