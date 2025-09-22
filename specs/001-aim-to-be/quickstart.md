# HeroParser Quickstart Guide

## Installation

```bash
# Install via NuGet Package Manager
Install-Package HeroParser

# Or via .NET CLI
dotnet add package HeroParser
```

## Quick Start Examples

### 1. Simple CSV Parsing

```csharp
using HeroParser;

// Parse CSV string to string arrays
string csvData = "Name,Age,Email\nJohn,25,john@example.com\nJane,30,jane@example.com";
foreach (var row in CsvParser.Parse(csvData))
{
    Console.WriteLine($"Name: {row[0]}, Age: {row[1]}, Email: {row[2]}");
}
```

### 2. Strongly-Typed CSV Parsing

```csharp
public class Person
{
    public string Name { get; set; }
    public int Age { get; set; }
    public string Email { get; set; }
}

// Parse CSV to strongly-typed objects
var people = CsvParser.Parse<Person>(csvData).ToList();
foreach (var person in people)
{
    Console.WriteLine($"{person.Name} is {person.Age} years old");
}
```

### 3. Asynchronous File Parsing

```csharp
// Parse large CSV files asynchronously
await foreach (var person in CsvParser.ParseFileAsync<Person>("large-dataset.csv"))
{
    // Process each person as it's parsed (streaming)
    await ProcessPersonAsync(person);
}
```

### 4. Advanced CSV Configuration

```csharp
var parser = CsvParser.Configure()
    .WithDelimiter(';')                    // Use semicolon delimiter
    .WithQuoteChar('"')                    // Quote character
    .AllowComments()                       // Allow # comment lines
    .TrimWhitespace()                      // Trim field whitespace
    .EnableParallelProcessing()            // Auto-parallel for large files
    .EnableSIMDOptimizations()             // Hardware acceleration
    .Build();

var results = parser.Parse<Customer>(csvData);
```

### 5. Custom Field Mapping

```csharp
public class Product
{
    public int Id { get; set; }
    public string Name { get; set; }
    public decimal Price { get; set; }
    public DateTime LaunchDate { get; set; }
}

var parser = CsvParser.Configure()
    .MapField<Product>(p => p.Id, 0)
    .MapField<Product>(p => p.Name, 1)
    .MapField<Product>(p => p.Price, 2, "C")          // Currency format
    .MapField<Product>(p => p.LaunchDate, 3, "yyyy-MM-dd")
    .Build();

var products = parser.Parse<Product>(productCsv);
```

### 6. Fixed-Length Record Parsing

```csharp
// Define fixed-length schema
var schema = FixedLengthSchema.Create()
    .Field("CustomerId", 0, 10, FieldType.Numeric)
    .Field("CustomerName", 10, 30, FieldType.Text)
    .Field("AccountBalance", 40, 12, FieldType.Decimal, 2)
    .Field("LastTransaction", 52, 8, FieldType.Date, "yyyyMMdd");

var customers = FixedLengthParser.Parse<Customer>(fixedLengthData, schema);
```

### 7. COBOL Copybook Integration

```csharp
// Load COBOL copybook
var copybook = CobolCopybook.LoadFromFile("customer-record.cpy");

var parser = FixedLengthParser.Configure()
    .WithCopybook(copybook)
    .EnableEBCDICSupport()                 // Mainframe character encoding
    .WithSignedNumberFormat(SignFormat.Trailing)
    .Build();

var mainframeRecords = parser.Parse<MainframeCustomer>(ebcdicData);
```

### 8. Error Handling and Validation

```csharp
try
{
    var parser = CsvParser.Configure()
        .WithErrorHandling(ErrorMode.Tolerant)  // Continue on errors
        .Build();

    var result = parser.ParseWithErrors<Person>(csvData);

    // Process successfully parsed records
    foreach (var person in result.Records)
    {
        ProcessPerson(person);
    }

    // Handle parsing errors
    foreach (var error in result.Errors)
    {
        Console.WriteLine($"Error at line {error.LineNumber}: {error.Message}");
    }
}
catch (CsvParseException ex)
{
    Console.WriteLine($"Parse failed at line {ex.LineNumber}, column {ex.ColumnNumber}");
}
```

### 9. High-Performance Streaming

```csharp
// Process massive files with constant memory usage
using var fileStream = File.OpenRead("100gb-dataset.csv");

await foreach (var batch in CsvParser.ParseBatchAsync<Transaction>(fileStream, batchSize: 10000))
{
    // Process 10,000 records at a time
    await ProcessTransactionBatchAsync(batch);

    // Memory is automatically released after each batch
}
```

### 10. Custom Type Converters

```csharp
public class MoneyConverter : ITypeConverter<Money>
{
    public bool TryParse(ReadOnlySpan<char> input, out Money result)
    {
        // Custom parsing logic
        if (decimal.TryParse(input, out var amount))
        {
            result = new Money(amount);
            return true;
        }
        result = default;
        return false;
    }

    public ReadOnlySpan<char> Format(Money value, Span<char> buffer)
    {
        // Custom formatting logic
        return value.Amount.TryFormat(buffer, out var written)
            ? buffer[..written]
            : ReadOnlySpan<char>.Empty;
    }
}

var parser = CsvParser.Configure()
    .WithCustomConverter<Money>(new MoneyConverter())
    .Build();
```

## Performance Tips

### 1. Use Asynchronous APIs for Large Files
```csharp
// ✅ Good: Streaming with constant memory
await foreach (var record in CsvParser.ParseFileAsync<T>("large.csv")) { }

// ❌ Avoid: Loading entire file into memory
var allRecords = CsvParser.ParseFile<T>("large.csv").ToList();
```

### 2. Enable Parallel Processing
```csharp
// ✅ Automatic parallel processing for files >10MB
var parser = CsvParser.Configure()
    .EnableParallelProcessing()
    .Build();
```

### 3. Use ReadOnlySpan<char> for Hot Paths
```csharp
// ✅ Zero-allocation field access
foreach (var record in CsvParser.Parse(csvData))
{
    ReadOnlySpan<char> nameField = record.GetField(0);
    // Process without string allocation
}
```

### 4. Configure Buffer Sizes for Your Workload
```csharp
var parser = CsvParser.Configure()
    .WithBufferSize(1024 * 1024)    // 1MB buffer for large files
    .Build();
```

## Integration Examples

### ASP.NET Core Web API
```csharp
[HttpPost("upload")]
public async Task<IActionResult> UploadCsv(IFormFile file)
{
    var results = new List<Customer>();

    using var stream = file.OpenReadStream();
    await foreach (var customer in CsvParser.ParseAsync<Customer>(stream))
    {
        results.Add(customer);
    }

    return Ok(new { Count = results.Count, Data = results });
}
```

### Entity Framework Integration
```csharp
public async Task ImportCustomersAsync(Stream csvStream)
{
    await foreach (var customer in CsvParser.ParseAsync<Customer>(csvStream))
    {
        _context.Customers.Add(customer);

        // Batch insert every 1000 records
        if (_context.ChangeTracker.Entries().Count() >= 1000)
        {
            await _context.SaveChangesAsync();
            _context.ChangeTracker.Clear();
        }
    }

    await _context.SaveChangesAsync();
}
```

---

This quickstart guide demonstrates the full range of HeroParser capabilities from simple usage to advanced enterprise scenarios.