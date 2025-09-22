# HeroParser Quickstart Guide

**Date**: 2025-01-25 | **Phase**: 1 | **Status**: Complete

## Installation

```bash
dotnet add package HeroParser
```

## Quick Start: CSV Parsing

### Simple CSV Parsing
```csharp
using HeroParser;

// Parse simple CSV content
var csvContent = "name,age,city\nJohn,25,NYC\nJane,30,LA";
var records = CsvParser.Parse(csvContent);

foreach (var record in records)
{
    Console.WriteLine($"Name: {record[0]}, Age: {record[1]}, City: {record[2]}");
}
```

### Typed CSV Parsing
```csharp
public class Person
{
    public string Name { get; set; }
    public int Age { get; set; }
    public string City { get; set; }
}

var people = CsvParser.Parse<Person>(csvContent);
foreach (var person in people)
{
    Console.WriteLine($"{person.Name} is {person.Age} years old");
}
```

### File Parsing
```csharp
// Parse CSV file directly
var records = CsvParser.ParseFile("data.csv");

// Async file parsing for large files
await foreach (var record in CsvParser.ParseFileAsync("large-data.csv"))
{
    // Process records as they're parsed - zero memory pressure
    ProcessRecord(record);
}
```

## Advanced CSV Configuration

### Custom Delimiters and Options
```csharp
var parser = CsvParser.Configure()
    .WithDelimiter(';')              // Use semicolon delimiter
    .WithQuoteChar('\'')             // Use single quotes
    .TrimWhitespace(true)            // Trim whitespace
    .AllowComments(true)             // Allow # comments
    .EnableParallelProcessing()      // Multi-threaded for large files
    .Build();

var records = parser.ParseFile("european-data.csv");
```

### Field Mapping
```csharp
var parser = CsvParser.Configure()
    .MapField<Person>(p => p.Name, "full_name")       // Map "full_name" column to Name property
    .MapField<Person>(p => p.Age, 1)                  // Map column index 1 to Age property
    .WithErrorHandling(CsvErrorHandling.Lenient)      // Skip invalid rows
    .Build();

var people = parser.Parse<Person>(csvWithHeaders);
```

## Quick Start: Fixed-Length Parsing

### Custom Field Layout
```csharp
using HeroParser;

// Define field positions and types
var layout = new FieldLayout()
    .AddField("CustomerID", 0, 10, FieldType.String)
    .AddField("Balance", 10, 15, FieldType.Decimal)
    .AddField("LastUpdate", 25, 8, FieldType.Date);

var fixedData = "CUST000001     12345.67 20240125";
var records = FixedLengthParser.Parse(fixedData, layout);

foreach (var record in records)
{
    Console.WriteLine($"Customer: {record[0]}, Balance: {record[1]}");
}
```

### COBOL Copybook Support
```csharp
// Load COBOL copybook definition
var copybook = CobolCopybook.LoadFromFile("customer.cpy");

// Parse mainframe data
var mainframeData = File.ReadAllText("customer.dat");
var records = FixedLengthParser.Parse(mainframeData, copybook);

foreach (var record in records)
{
    var customerId = record.GetField<string>("CUSTOMER-ID");
    var balance = record.GetField<decimal>("ACCOUNT-BALANCE");
    var lastUpdate = record.GetField<DateTime>("LAST-UPDATE-DATE");

    Console.WriteLine($"Customer {customerId}: ${balance:F2} (Updated: {lastUpdate:yyyy-MM-dd})");
}
```

## Performance Optimization

### Zero-Allocation Parsing
```csharp
// Use Span<char> for zero-allocation parsing
ReadOnlySpan<char> csvSpan = csvContent.AsSpan();
var records = CsvParser.Parse(csvSpan);  // No string allocations

// Use ArrayPool for buffer management
using var buffer = ArrayPool<char>.Shared.Rent(bufferSize);
// HeroParser automatically uses ArrayPool internally
```

### Multi-Threading for Large Files
```csharp
var parser = CsvParser.Configure()
    .EnableParallelProcessing(true)      // Enable multi-threading
    .EnableSIMDOptimizations(true)       // Use hardware acceleration
    .Build();

// Process 1GB+ files efficiently
var stopwatch = Stopwatch.StartNew();
var recordCount = 0;

await foreach (var record in parser.ParseFileAsync("huge-dataset.csv"))
{
    recordCount++;
    // Parallel processing automatically chunks the file
}

Console.WriteLine($"Processed {recordCount:N0} records in {stopwatch.Elapsed.TotalSeconds:F2}s");
Console.WriteLine($"Throughput: {(fileSize / 1_000_000_000.0) / stopwatch.Elapsed.TotalSeconds:F1} GB/s");
```

## Error Handling

### Strict Mode (Default)
```csharp
try
{
    var records = CsvParser.Parse(malformedCsv);
}
catch (CsvParseException ex)
{
    Console.WriteLine($"Parse error at line {ex.LineNumber}, column {ex.ColumnNumber}");
    Console.WriteLine($"Error type: {ex.ErrorType}");
    Console.WriteLine($"Field value: '{ex.FieldValue}'");
}
```

### Lenient Mode
```csharp
var parser = CsvParser.Configure()
    .WithErrorHandling(CsvErrorHandling.Lenient)  // Skip invalid rows
    .Build();

var records = parser.Parse(messyCsv);  // Invalid rows are skipped, warnings logged
```

### Type Conversion Errors
```csharp
try
{
    var people = CsvParser.Parse<Person>(csvWithBadData);
}
catch (CsvMappingException ex)
{
    Console.WriteLine($"Cannot convert '{ex.FieldValue}' to {ex.TargetType.Name}");
    Console.WriteLine($"Field: {ex.FieldName} at line {ex.LineNumber}");
}
```

## Stream Processing

### Process Large Files Without Loading Into Memory
```csharp
using var fileStream = File.OpenRead("massive-file.csv");

await foreach (var record in CsvParser.ParseAsync(fileStream))
{
    // Each record is processed immediately
    // Memory usage remains constant regardless of file size
    ProcessRecord(record);
}
```

### Real-Time Data Processing
```csharp
// Process data as it arrives from network/pipe
using var pipe = new Pipe();

// Producer writes CSV data to pipe
_ = Task.Run(async () => await WriteDataToPipe(pipe.Writer));

// Consumer parses data in real-time
await foreach (var record in CsvParser.ParseAsync(pipe.Reader.AsStream()))
{
    // Process records as soon as complete rows are available
    await ProcessRecordAsync(record);
}
```

## Benchmarking Your Usage

```csharp
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80)]
public class CsvParsingBenchmark
{
    private string _csvData;

    [GlobalSetup]
    public void Setup()
    {
        _csvData = GenerateTestCsv(1_000_000); // 1M rows
    }

    [Benchmark]
    public void HeroParser_Parse()
    {
        var records = CsvParser.Parse(_csvData);
        foreach (var record in records) { /* consume */ }
    }

    [Benchmark]
    public void HeroParser_ParseTyped()
    {
        var records = CsvParser.Parse<TestRecord>(_csvData);
        foreach (var record in records) { /* consume */ }
    }
}
```

## Next Steps

1. **Explore Advanced Features**: Custom type converters, source generators for even faster parsing
2. **Performance Tuning**: Use the benchmarking tools to optimize for your specific data patterns
3. **Integration**: Combine with your ETL pipelines, data processing frameworks
4. **Contribute**: Help make HeroParser even faster by contributing benchmarks and use cases

For complete API documentation and advanced scenarios, see the [API Reference](./contracts/).

## Performance Expectations

Based on constitutional targets, you should expect:

- **Small files (<1MB)**: Parse in <40ms
- **Medium files (1-100MB)**: >25 GB/s throughput
- **Large files (1GB+)**: >50 GB/s with multi-threading
- **Memory usage**: <1KB overhead per 1MB parsed
- **Zero allocations**: For standard CSV parsing scenarios

If you're not seeing these performance levels, please file an issue with your benchmark code!