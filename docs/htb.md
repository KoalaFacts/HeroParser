# HTB API Reference

[Back to README](../README.md)

High-Throughput Tabular Binary (HTB) is HeroParser's custom binary format designed for maximum throughput, low overhead, and platform-independent binary serialization. It supports C# primitive types, nullable types, string values, floats/doubles with proper endianness reversal, and floating-point arrays (`float[]`), making it ideal for storing and transferring high-dimensional vector embeddings and AI datasets.

When a record class is decorated with `[GenerateBinder]`, Roslyn source generators emit allocation-free, reflection-free HTB binding paths, ensuring full compatibility with Native AOT compilation.

The primary entry point is the `Htb` static class.

---

## Table of Contents

1. [HTB Reading](#1-htb-reading)
   - [Quick Start](#11-quick-start)
   - [Fluent Builder Options](#12-fluent-builder-options)
   - [Async Streaming](#13-async-streaming)
   - [OnError Callback](#14-onerror-callback)
   - [Progress Reporting](#15-progress-reporting)
2. [HTB Writing](#2-htb-writing)
   - [Quick Start](#21-quick-start)
   - [Fluent Builder Options](#22-fluent-builder-options)
   - [Async Writing](#23-async-writing)
   - [OnError Callback](#24-onerror-callback)
   - [Progress Reporting](#25-progress-reporting)
3. [Options Reference](#3-options-reference)
   - [HtbReadOptions](#31-htbreadoptions)
   - [HtbWriteOptions](#32-htbwriteoptions)
4. [CSV ↔ HTB Conversion](#4-csv--htb-conversion)
   - [CSV → HTB Conversion](#41-csv--htb-conversion)
   - [HTB → CSV Conversion](#42-htb--csv-conversion)
5. [AOT & Trimming Safety](#5-aot--trimming-safety)

---

## 1. HTB Reading

### 1.1 Quick Start

```csharp
using HeroParser;

// Sync - Load all records to a List
List<Product> products = Htb.Read<Product>()
    .FromFile("data.htb")
    .ToList();

// Async - Stream records without buffering
await foreach (Product p in Htb.Read<Product>().FromFileAsync("data.htb"))
{
    Console.WriteLine($"{p.Name}: {p.Price:C}");
}
```

### 1.2 Fluent Builder Options

```csharp
var products = Htb.Read<Product>()
    .WithMaxRowCount(5_000_000)      // Max row limit (default: 1,000,000)
    .SkipRows(100)                    // Skip first 100 data records
    .WithValidationMode(ValidationMode.Strict)
    .WithProgress(new Progress<HtbProgress>(p =>
        Console.WriteLine($"Read {p.RecordsRead:N0} records / {p.BytesRead:N0} bytes")))
    .OnError((ctx, ex) =>
    {
        Console.WriteLine($"Error at index {ctx.RecordIndex} (property: {ctx.MemberName}): {ex.Message}");
        return HtbDeserializeErrorAction.SkipRecord; // Skip and continue
    })
    .FromFile("data.htb")
    .ToList();
```

### 1.3 Async Streaming

You can read asynchronously from a file or any standard `Stream` (such as network streams, `MemoryStream`, or HTTP response content):

```csharp
// From a file
await foreach (Product p in Htb.Read<Product>().FromFileAsync("data.htb"))
{
    // Process record
}

// From any Stream
await foreach (Product p in Htb.Read<Product>().FromStreamAsync(networkStream))
{
    // Process record
}
```

### 1.4 OnError Callback

The `OnError` delegate receives a context struct (`HtbDeserializeErrorContext`) containing the record index, target type, and failing member name. It returns a directive on how to proceed:

```csharp
Htb.Read<Product>()
    .OnError((ctx, ex) =>
    {
        // Decide whether to SkipRecord or Throw
        return ex is HtbException ? HtbDeserializeErrorAction.SkipRecord : HtbDeserializeErrorAction.Throw;
    })
    .FromFile("data.htb");
```

### 1.5 Progress Reporting

Use standard `IProgress<HtbProgress>` to monitor large file read operations:

```csharp
var progress = new Progress<HtbProgress>(p => 
{
    Console.WriteLine($"Read {p.RecordsRead} records...");
});

var records = Htb.Read<Product>()
    .WithProgress(progress, intervalRows: 5000)
    .FromFile("data.htb");
```

---

## 2. HTB Writing

### 2.1 Quick Start

```csharp
using HeroParser;

List<Product> products = GetProducts();

// Sync - Write to a file
Htb.Write<Product>().ToFile("out.htb", products);

// Async - Write a collection asynchronously
await Htb.Write<Product>().ToFileAsync("out.htb", products);
```

### 2.2 Fluent Builder Options

```csharp
Htb.Write<Product>()
    .WithMaxRowCount(10_000_000)       // Set row count limit
    .WithMaxOutputSize(500 * 1024 * 1024) // 500 MB output cap (DoS guard)
    .WithProgress(new Progress<HtbWriteProgress>(p =>
        Console.WriteLine($"Written {p.RecordsWritten} records / {p.BytesWritten} bytes")))
    .OnError(ctx =>
    {
        Console.WriteLine($"Failed to write record {ctx.RecordIndex}: {ctx.Exception.Message}");
        return HtbSerializeErrorAction.SkipRecord;
    })
    .ToFile("out.htb", products);
```

### 2.3 Async Writing

You can write asynchronously using `IEnumerable<T>` or `IAsyncEnumerable<T>`:

```csharp
IAsyncEnumerable<Product> stream = FetchProductsAsync();

// Async write to stream
await Htb.Write<Product>().ToStreamAsync(outputStream, stream);

// Async write to file
await Htb.Write<Product>().ToFileAsync("out.htb", stream);
```

### 2.4 OnError Callback

The writer's `OnError` delegate receives an `HtbSerializeErrorContext` allowing you to skip individual records that fail validation or serialization:

```csharp
Htb.Write<Product>()
    .OnError(ctx =>
    {
        Console.WriteLine($"Skipping write-side error at index {ctx.RecordIndex}");
        return HtbSerializeErrorAction.SkipRecord;
    })
    .ToFile("out.htb", products);
```

### 2.5 Progress Reporting

Track execution progress during writes using `IProgress<HtbWriteProgress>`:

```csharp
var progress = new Progress<HtbWriteProgress>(p => 
{
    Console.WriteLine($"Written {p.RecordsWritten} records...");
});

Htb.Write<Product>()
    .WithProgress(progress, intervalRows: 1000)
    .ToFile("out.htb", products);
```

---

## 3. Options Reference

### 3.1 `HtbReadOptions`

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `MaxRowCount` | `int` | `1,000,000` | Hard cap on total records allowed. Exceeding this throws an `HtbException`. |
| `SkipRows` | `int` | `0` | Skip the first N records before yielding data. |
| `ValidationMode` | `ValidationMode` | `Strict` | Field constraint checks (`Strict` throws immediately, `Lenient` collects errors). |
| `OnError` | `HtbDeserializeErrorHandler?` | `null` | Per-record error handler callback. |
| `Progress` | `IProgress<HtbProgress>?` | `null` | Optional progress reporter. |
| `ProgressIntervalRows` | `int` | `1000` | Number of rows processed between progress updates. |

### 3.2 `HtbWriteOptions`

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `MaxRowCount` | `int?` | `null` | Optional hard limit on written records. |
| `MaxOutputSize` | `long?` | `null` | Optional hard byte size cap on the output file/stream. |
| `OnError` | `HtbSerializeErrorHandler?` | `null` | Per-record serialization error callback. |
| `Progress` | `IProgress<HtbWriteProgress>?` | `null` | Optional progress reporter. |
| `ProgressIntervalRows` | `int` | `1000` | Number of rows processed between progress updates. |

---

## 4. CSV ↔ HTB Conversion

HeroParser provides direct converter classes (`CsvToHtbConverter` and `HtbToCsvConverter`) that stream data between formats with zero heap allocation overhead.

### 4.1 CSV → HTB Conversion

To convert CSV data to the binary HTB format, you need to provide a schema description (`HtbSchema`):

```csharp
using HeroParser.Htbs.Records;

// 1. Manually define the schema
var schema = new HtbSchema([
    new HtbColumn("Id", HtbDataType.Int32, isNullable: false),
    new HtbColumn("Name", HtbDataType.String, isNullable: true),
    new HtbColumn("Embedding", HtbDataType.FloatArray, isNullable: true)
]);

// Or dynamically derive the schema from a model type
var schema = HtbSchema.FromType<Product>();

// 2. Perform file conversion
Htb.ConvertFromCsv("data.csv", "data.htb", schema);

// Or convert streams asynchronously
await Htb.ConvertFromCsvAsync(csvStream, htbStream, schema);
```

### 4.2 HTB → CSV Conversion

Converting back to CSV does not require a manual schema, as the HTB file itself embeds its own schema headers:

```csharp
// Sync file conversion
Htb.ConvertToCsv("data.htb", "data.csv");

// Async stream conversion to a TextWriter
using var textWriter = new StreamWriter("data.csv");
await Htb.ConvertToCsvAsync(htbStream, textWriter);
```

---

## 5. AOT & Trimming Safety

HeroParser's source generator compiles optimized binders for types annotated with the `[GenerateBinder]` attribute:

```csharp
[GenerateBinder]
public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    
    [TabularMap(Index = 2)]
    public float[]? Embedding { get; set; }
}
```

The generator automatically hooks into HTB's deserializer and writer factory, allowing the JIT (or AOT compiler) to use direct, non-reflection assignments. 

> [!IMPORTANT]
> To compile with Native AOT and run warning-free, always ensure your record classes have `[GenerateBinder]` applied. If a type does not have `[GenerateBinder]`, HeroParser will fall back to reflection-based deserialization, which will generate warnings under trimming.

---

[Back to README](../README.md)
