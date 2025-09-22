# CSV Parser API Contract

## Simple APIs

### Synchronous Parsing
```csharp
// Basic string array parsing
IEnumerable<string[]> Parse(string csvContent);
IEnumerable<string[]> Parse(ReadOnlySpan<char> csvContent);
IEnumerable<string[]> ParseFile(string filePath);

// Strongly-typed parsing
IEnumerable<T> Parse<T>(string csvContent);
IEnumerable<T> Parse<T>(ReadOnlySpan<char> csvContent);
IEnumerable<T> ParseFile<T>(string filePath);
```

### Asynchronous Parsing
```csharp
// Stream-based async parsing
IAsyncEnumerable<string[]> ParseAsync(Stream csvStream, CancellationToken cancellationToken = default);
IAsyncEnumerable<T> ParseAsync<T>(Stream csvStream, CancellationToken cancellationToken = default);

// File-based async parsing
IAsyncEnumerable<string[]> ParseFileAsync(string filePath, CancellationToken cancellationToken = default);
IAsyncEnumerable<T> ParseFileAsync<T>(string filePath, CancellationToken cancellationToken = default);
```

## Advanced Configuration APIs

### Fluent Configuration Builder
```csharp
var parser = CsvParser.Configure()
    .WithDelimiter(',')
    .WithQuoteChar('"')
    .WithEscapeChar('\\')
    .AllowComments()
    .TrimWhitespace()
    .EnableParallelProcessing()
    .EnableSIMDOptimizations()
    .WithBufferSize(64 * 1024)
    .Build();

var records = parser.Parse<Customer>(csvContent);
```

### Custom Type Mapping
```csharp
var parser = CsvParser.Configure()
    .MapField<Customer>(c => c.Id, 0)
    .MapField<Customer>(c => c.Name, 1)
    .MapField<Customer>(c => c.Email, 2)
    .MapField<Customer>(c => c.CreatedDate, 3, "yyyy-MM-dd")
    .WithCustomConverter<Money>(new MoneyConverter())
    .Build();
```

## Performance Contract Guarantees

### Throughput Requirements
- **Single-threaded**: >25 GB/s parse throughput
- **Multi-threaded**: >50 GB/s parse throughput
- **Write operations**: >20 GB/s single-threaded, >40 GB/s multi-threaded
- **Competitive advantage**: >20% faster than Sep, Sylvan.Data.Csv, CsvHelper

### Memory Efficiency
- **Zero allocations**: 99th percentile operations produce no garbage
- **Memory overhead**: <1KB per 1MB parsed (excluding user objects)
- **Buffer management**: Automatic buffer pooling with configurable sizes
- **Large file support**: Constant memory usage regardless of file size

### Latency Requirements
- **Startup time**: <1ms for first parse operation
- **Response time**: <100ms to process first 1MB of data
- **Streaming latency**: <10ms to emit first record from stream
- **Configuration overhead**: <100μs to build parser with custom configuration

## Error Handling Contract

### Exception Hierarchy
```csharp
public class CsvParseException : Exception
{
    public int LineNumber { get; }
    public int ColumnNumber { get; }
    public string FieldValue { get; }
}

public class CsvFormatException : CsvParseException
{
    public CsvValidationError[] ValidationErrors { get; }
}

public class CsvMappingException : CsvParseException
{
    public Type TargetType { get; }
    public string FieldName { get; }
}
```

### Error Recovery Modes
- **Strict Mode**: Throw exception on first error
- **Tolerant Mode**: Continue parsing, collect errors
- **Skip Mode**: Skip invalid records, continue processing
- **Custom Mode**: User-defined error handling strategy

## Thread Safety Contract

### Immutable Configuration
- All parser configurations are immutable after creation
- Thread-safe for concurrent read operations
- No shared mutable state between parser instances

### Concurrent Usage
- Parser instances are thread-safe for read operations
- Multiple threads can parse different data simultaneously
- Internal buffer pools use thread-local storage where possible

### Parallel Processing
- Automatic parallel processing for files >10MB
- Work-stealing queue for optimal load distribution
- NUMA-aware thread affinity for large datasets
- Configurable parallelism degree

## Compatibility Contract

### Framework Support
- .NET Standard 2.0: Full API surface with polyfills
- .NET 5.0+: Native Span<T> and Memory<T> support
- .NET 6.0+: Enhanced SIMD optimizations
- .NET 8.0+: Latest hardware acceleration features

### Platform Support
- Windows: x64, ARM64
- Linux: x64, ARM64
- macOS: x64, ARM64 (Apple Silicon)
- AOT Compilation: Full compatibility with trimming and ahead-of-time compilation

### Dependency Contract
- **Zero external dependencies**: Microsoft BCL only
- **Source generator**: Optional compile-time code generation
- **No reflection**: AOT-friendly implementation throughout
- **Version compatibility**: Semantic versioning with performance indicators

---

This API contract ensures maximum performance while providing both simple and advanced usage patterns for all scenarios.