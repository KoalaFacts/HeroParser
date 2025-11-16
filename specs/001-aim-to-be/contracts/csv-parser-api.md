# CSV Parser API Contract

**Date**: 2025-01-25 | **Phase**: 1 | **Status**: Updated for HeroParser

## Static API Contract

### Csv (Static Entry Point)

```csharp
public static class Csv
{
    // ========== Synchronous Content Parsing ==========

    // Parse content immediately and return all rows
    public static string[][] ParseContent(string content, bool hasHeaders = true, char delimiter = ',');
    public static string[][] ParseContent(byte[] content, bool hasHeaders = true, char delimiter = ',', Encoding? encoding = null);
    public static string[][] ParseContent(ReadOnlyMemory<byte> content, bool hasHeaders = true, char delimiter = ',', Encoding? encoding = null);

    #if NET5_0_OR_GREATER
    public static string[][] ParseContent(ReadOnlySpan<char> content, bool hasHeaders = true, char delimiter = ',');
    public static string[][] ParseContent(ReadOnlySpan<byte> content, bool hasHeaders = true, char delimiter = ',', Encoding? encoding = null);
    #endif

    // ========== Asynchronous Content Parsing ==========

    // Asynchronously parse content and return all rows
    public static async Task<string[][]> FromContent(string content, bool hasHeaders = true, char delimiter = ',', CancellationToken cancellationToken = default);
    public static async Task<string[][]> FromContent(byte[] content, bool hasHeaders = true, char delimiter = ',', Encoding? encoding = null, CancellationToken cancellationToken = default);
    public static async Task<string[][]> FromContent(ReadOnlyMemory<byte> content, bool hasHeaders = true, char delimiter = ',', Encoding? encoding = null, CancellationToken cancellationToken = default);

    // ========== File Operations ==========

    // Synchronous file parsing
    public static string[][] ParseFile(string filePath, bool hasHeaders = true, char delimiter = ',', Encoding? encoding = null);

    // Asynchronous file parsing
    public static async Task<string[][]> FromFile(string filePath, bool hasHeaders = true, char delimiter = ',', CancellationToken cancellationToken = default);

    // ========== Stream Operations ==========

    // Asynchronous stream parsing
    public static async Task<string[][]> FromStream(Stream stream, bool hasHeaders = true, char delimiter = ',', Encoding? encoding = null, CancellationToken cancellationToken = default);

    // ========== Advanced Reader API ==========

    // Create CSV readers for advanced scenarios
    public static CsvReader OpenContent(string content, CsvReadConfiguration? configuration = null);
    public static CsvReader OpenFile(string filePath, CsvReadConfiguration? configuration = null);
    public static CsvReader OpenStream(Stream stream, CsvReadConfiguration? configuration = null);
}
```

**Performance Contracts**:
- Parse throughput: >25 GB/s single-threaded for simple CSV with SIMD optimizations
- Zero allocations for ref struct API (HeroCsvRow/HeroCsvCol)
- <1ms startup time for first parse operation
- Multi-target framework support: .NET Standard 2.0/2.1, .NET 5-10
- AOT friendly design

**Error Contracts**:
- ArgumentNullException: When content/filePath/stream is null
- CsvParseException: When CSV format is malformed
- ObjectDisposedException: When reader is disposed
- InvalidOperationException: When thread safety is violated

## Advanced Reader API Contract

### ICsvReader (High-Performance Reader Interface)

```csharp
public interface ICsvReader : IDisposable
{
    // Configuration access
    CsvReadConfiguration Configuration { get; }

    // Header information
    IReadOnlyList<string>? Headers { get; }

    // State properties
    bool EndOfCsv { get; }
    HeroCsvRow CurrentRow { get; }  // Zero-allocation current row access

    // Core reading API
    bool Read();  // Advances to next row, returns true if successful

    // Traditional string-based API
    IEnumerable<string[]> ReadAll();
    Task<IEnumerable<string[]>> ReadAllAsync(CancellationToken cancellationToken = default);
    string[]? ReadRecord();
    Task<string[]?> ReadRecordAsync(CancellationToken cancellationToken = default);

    // Field access by column name
    string GetField(string[] record, string columnName);
    bool TryGetField(string[] record, string columnName, out string? value);
}
```

### Zero-Allocation API Contract

```csharp
// HeroCsvRow - Zero allocation row access
public readonly ref struct HeroCsvRow
{
    public bool IsEmpty { get; }
    public int ColumnCount { get; }
    public HeroCsvCol this[int index] { get; }
    public HeroCsvCol this[string columnName] { get; }
    public bool TryGetColumn(string columnName, out HeroCsvCol column);
    // Enumeration and conversion methods...
}

// HeroCsvCol - Zero allocation column access
public readonly ref struct HeroCsvCol
{
    public bool IsEmpty { get; }
    public ReadOnlySpan<char> Span { get; }
    public override string ToString();
    public T Parse<T>() where T : IParsable<T>;
    public bool TryParse<T>(out T result) where T : IParsable<T>;
    // Conversion methods for all primitive types...
}
```

**Thread Safety Contract**:
- CsvReader instances are NOT thread-safe
- Each thread must use its own CsvReader instance
- Thread ownership validation prevents cross-thread access
- Configuration is immutable after creation

## Configuration Contract

### CsvReadConfiguration (Immutable Settings)

```csharp
public record CsvReadConfiguration
{
    public char Delimiter { get; init; } = ',';
    public char Quote { get; init; } = '"';
    public bool HasHeaderRow { get; init; } = true;
    public bool TrimValues { get; init; } = false;
    public bool IgnoreEmptyLines { get; init; } = true;
    public bool StrictMode { get; init; } = false;
    public int BufferSize { get; init; } = 8192;
    public Encoding? Encoding { get; init; }

    // Data source properties (one must be specified)
    public string? StringContent { get; init; }
    public TextReader? Reader { get; init; }
    public Stream? Stream { get; init; }
    public string? FilePath { get; init; }
    public ReadOnlyMemory<byte>? ByteContent { get; init; }
}
```

**Immutability Contract**:
- Configuration is a record type with init-only properties
- Thread-safe for concurrent access
- Validation occurs during CsvReader construction

## Error Handling Contracts

### CsvParseException

```csharp
public class CsvParseException : Exception
{
    public long LineNumber { get; }
    public string? ErrorDetails { get; }
    public string? FieldValue { get; }
}
```

## Usage Examples

### Simple Parsing
```csharp
// Synchronous parsing
var records = Csv.ParseContent("name,age\nJohn,25\nJane,30");

// Multiple input types supported
var fromBytes = Csv.ParseContent(System.Text.Encoding.UTF8.GetBytes("name,age\nJohn,25"));
var fromSpan = Csv.ParseContent("name,age\nJohn,25".AsSpan());  // .NET 5+
```

### Asynchronous Parsing
```csharp
// Async content parsing
var records = await Csv.FromContent("name,age\nJohn,25\nJane,30");

// Async file parsing
var fileRecords = await Csv.FromFile("data.csv");

// Async stream parsing
using var stream = File.OpenRead("data.csv");
var streamRecords = await Csv.FromStream(stream);
```

### Advanced Reader Usage
```csharp
// Zero-allocation reading for maximum performance
using var reader = Csv.OpenFile("large-data.csv");

while (reader.Read())
{
    var row = reader.CurrentRow;
    if (!row.IsEmpty)
    {
        var name = row[0].ToString();
        var age = row[1].ToInt32();
        var salary = row["Salary"].ToDecimal();

        // Process data with zero allocations
    }
}
```

### Custom Configuration
```csharp
var config = new CsvReadConfiguration
{
    Delimiter = ';',
    HasHeaderRow = false,
    TrimValues = true,
    StrictMode = true
};

using var reader = new CsvReader(config with { FilePath = "data.csv" });
var records = reader.ReadAll().ToArray();
```

### Performance-Critical Scenarios
```csharp
// Process rows as they're read for memory efficiency
using var reader = Csv.OpenFile("huge-file.csv");
reader.ProcessRows((in HeroCsvRow row) =>
{
    // Zero-allocation processing
    if (row.ColumnCount > 2)
    {
        var id = row[0].ToInt32();
        var value = row[1].ToDecimal();
        // Process immediately without string allocations
    }
});
```

## Key Design Principles

1. **Consumer-Focused API**: Simple methods for common scenarios (`ParseContent`, `FromContent`)
2. **Multiple Input Types**: Support for strings, byte arrays, spans, and memory
3. **Performance**: Zero-allocation ref struct API for high-performance scenarios
4. **Flexibility**: Advanced reader API for custom processing patterns
5. **Thread Safety**: Clear ownership model with validation
6. **Modern .NET**: Multi-target support with framework-specific optimizations