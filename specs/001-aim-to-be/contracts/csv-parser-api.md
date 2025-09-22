# CSV Parser API Contract

**Date**: 2025-01-25 | **Phase**: 1 | **Status**: Complete

## Static API Contract

### CsvParser (Static Entry Point)

```csharp
public static class CsvParser
{
    // Synchronous parsing from string content
    public static IEnumerable<string[]> Parse(string content);
    public static IEnumerable<T> Parse<T>(string content);

    // Synchronous parsing from ReadOnlySpan
    public static IEnumerable<string[]> Parse(ReadOnlySpan<char> content);
    public static IEnumerable<T> Parse<T>(ReadOnlySpan<char> content);

    // Synchronous file parsing
    public static IEnumerable<string[]> ParseFile(string filePath);
    public static IEnumerable<T> ParseFile<T>(string filePath);

    // Asynchronous stream parsing
    public static IAsyncEnumerable<string[]> ParseAsync(Stream stream);
    public static IAsyncEnumerable<T> ParseAsync<T>(Stream stream);

    // Asynchronous file parsing
    public static IAsyncEnumerable<string[]> ParseFileAsync(string filePath);
    public static IAsyncEnumerable<T> ParseFileAsync<T>(string filePath);

    // Configuration builder entry point
    public static CsvParserBuilder Configure();
}
```

**Performance Contracts**:
- Parse throughput: >25 GB/s single-threaded for simple CSV
- Zero allocations for 99th percentile operations
- <1ms startup time for first parse operation

**Error Contracts**:
- ArgumentNullException: When content/filePath is null
- CsvParseException: When CSV format is malformed
- CsvMappingException: When type conversion fails

## Builder API Contract

### CsvParserBuilder (Fluent Configuration)

```csharp
public sealed class CsvParserBuilder
{
    // Character configuration
    public CsvParserBuilder WithDelimiter(char delimiter);
    public CsvParserBuilder WithQuoteChar(char quote);
    public CsvParserBuilder WithEscapeChar(char escape);

    // Parsing behavior
    public CsvParserBuilder AllowComments(bool allow = true);
    public CsvParserBuilder TrimWhitespace(bool trim = true);

    // Performance optimization
    public CsvParserBuilder EnableParallelProcessing(bool enable = true);
    public CsvParserBuilder EnableSIMDOptimizations(bool enable = true);

    // Field mapping for typed parsing
    public CsvParserBuilder MapField<T>(Expression<Func<T, object>> property, int columnIndex);
    public CsvParserBuilder MapField<T>(Expression<Func<T, object>> property, string columnName);

    // Error handling configuration
    public CsvParserBuilder WithErrorHandling(CsvErrorHandling mode);

    // Build configured parser
    public ICsvParser Build();
}
```

**Validation Contracts**:
- Characters must be distinct (delimiter ≠ quote ≠ escape)
- Column indices must be non-negative
- Column names must not be null or empty

## Configured Parser Contract

### ICsvParser (Configured Instance)

```csharp
public interface ICsvParser
{
    // Same parsing methods as static API
    IEnumerable<string[]> Parse(string content);
    IEnumerable<T> Parse<T>(string content);
    IEnumerable<string[]> Parse(ReadOnlySpan<char> content);
    IEnumerable<T> Parse<T>(ReadOnlySpan<char> content);
    IEnumerable<string[]> ParseFile(string filePath);
    IEnumerable<T> ParseFile<T>(string filePath);
    IAsyncEnumerable<string[]> ParseAsync(Stream stream);
    IAsyncEnumerable<T> ParseAsync<T>(Stream stream);

    // Configuration access
    CsvConfiguration Configuration { get; }
}
```

**Thread Safety Contract**:
- All instances are thread-safe for concurrent reading
- Configuration is immutable after creation

## Configuration Contract

### CsvConfiguration (Immutable Settings)

```csharp
public sealed class CsvConfiguration
{
    public char Delimiter { get; }           // Default: ','
    public char QuoteChar { get; }           // Default: '"'
    public char EscapeChar { get; }          // Default: '"'
    public bool AllowComments { get; }       // Default: false
    public bool TrimWhitespace { get; }      // Default: false
    public bool ParallelProcessing { get; }  // Default: true
    public bool SIMDOptimizations { get; }   // Default: true
    public CsvErrorHandling ErrorHandling { get; } // Default: Strict
    public IReadOnlyDictionary<string, int> FieldMappings { get; }
}
```

**Immutability Contract**:
- All properties are read-only
- Configuration cannot be modified after creation
- Thread-safe for concurrent access

## Error Handling Contracts

### CsvParseException

```csharp
public class CsvParseException : Exception
{
    public int LineNumber { get; }
    public int ColumnNumber { get; }
    public string FieldValue { get; }
    public CsvErrorType ErrorType { get; }
}
```

### CsvMappingException

```csharp
public class CsvMappingException : Exception
{
    public Type TargetType { get; }
    public string FieldName { get; }
    public string FieldValue { get; }
    public int LineNumber { get; }
    public int ColumnNumber { get; }
}
```

## Enumeration Contracts

### CsvErrorHandling

```csharp
public enum CsvErrorHandling
{
    Strict,        // Throw exceptions on malformed data
    Lenient,       // Skip invalid rows with warning
    IgnoreErrors   // Continue parsing, return null for invalid fields
}
```

### CsvErrorType

```csharp
public enum CsvErrorType
{
    MalformedField,       // Invalid field format
    UnterminatedQuote,    // Unclosed quoted field
    InvalidCharacter,     // Invalid character in field
    LineTooLong,         // Line exceeds maximum length
    FieldCountMismatch   // Wrong number of fields in row
}
```

## Usage Examples

### Simple Parsing
```csharp
var records = CsvParser.Parse("name,age\nJohn,25\nJane,30");
var people = CsvParser.Parse<Person>("name,age\nJohn,25\nJane,30");
```

### Advanced Configuration
```csharp
var parser = CsvParser.Configure()
    .WithDelimiter(';')
    .EnableParallelProcessing()
    .WithErrorHandling(CsvErrorHandling.Lenient)
    .Build();

var records = parser.ParseFile("data.csv");
```

### Async Processing
```csharp
await foreach (var record in CsvParser.ParseFileAsync("large-file.csv"))
{
    // Process records as they're parsed
}
```