# Fixed-Length Parser API Contract

**Date**: 2025-01-25 | **Phase**: 1 | **Status**: Complete

## Static API Contract

### FixedLengthParser (Static Entry Point)

```csharp
public static class FixedLengthParser
{
    // Parsing with custom field layout
    public static IEnumerable<string[]> Parse(string content, FieldLayout layout);
    public static IEnumerable<T> Parse<T>(string content, FieldLayout layout);

    // Parsing with COBOL copybook
    public static IEnumerable<FixedLengthRecord> Parse(string content, CobolCopybook copybook);

    // File parsing with layouts
    public static IEnumerable<string[]> ParseFile(string filePath, FieldLayout layout);
    public static IEnumerable<T> ParseFile<T>(string filePath, FieldLayout layout);

    // COBOL file parsing
    public static IEnumerable<FixedLengthRecord> ParseFile(string filePath, CobolCopybook copybook);

    // Configuration builder entry point
    public static FixedLengthParserBuilder Configure();
}
```

**Performance Contracts**:
- Parse throughput: >20 GB/s for fixed-length records
- Zero allocations for field extraction
- SIMD optimization for field boundary detection

## Field Layout Contract

### FieldLayout (Schema Definition)

```csharp
public sealed class FieldLayout
{
    public IReadOnlyList<FieldDefinition> Fields { get; }
    public int TotalLength { get; }
    public PaddingMode Padding { get; }

    // Builder methods
    public FieldLayout AddField(string name, int start, int length, Type type = typeof(string));
    public FieldLayout AddField(string name, int start, int length, FieldType type);

    // Validation
    public bool IsValid { get; }
    public IEnumerable<string> ValidationErrors { get; }
}
```

**Validation Contracts**:
- Fields cannot overlap
- Start positions must be non-negative
- Lengths must be positive
- Field names must be unique

### FieldDefinition (Individual Field Schema)

```csharp
public sealed class FieldDefinition
{
    public string Name { get; }
    public int StartPosition { get; }    // 0-based
    public int Length { get; }
    public Type Type { get; }
    public PaddingMode Padding { get; }
    public FieldAlignment Alignment { get; }
}
```

## COBOL Support Contract

### CobolCopybook (Mainframe Schema)

```csharp
public sealed class CobolCopybook
{
    public string Name { get; }
    public IReadOnlyList<CobolField> Fields { get; }
    public int RecordLength { get; }
    public Encoding Encoding { get; }

    // Factory methods
    public static CobolCopybook LoadFromFile(string copybookPath);
    public static CobolCopybook Parse(string copybookContent);
}
```

### CobolField (COBOL Field Definition)

```csharp
public sealed class CobolField
{
    public string Name { get; }
    public int Level { get; }           // COBOL level number (01-88)
    public string Picture { get; }      // PIC clause
    public int StartPosition { get; }
    public int Length { get; }
    public CobolFieldType Type { get; }
    public bool IsRedefines { get; }
    public string RedefinesField { get; }
}
```

### FixedLengthRecord (Parsed Record)

```csharp
public sealed class FixedLengthRecord
{
    public IReadOnlyDictionary<string, object> Fields { get; }
    public int RecordNumber { get; }
    public string RawData { get; }

    // Field access
    public T GetField<T>(string fieldName);
    public string GetFieldAsString(string fieldName);
    public bool TryGetField<T>(string fieldName, out T value);
}
```

## Builder API Contract

### FixedLengthParserBuilder (Fluent Configuration)

```csharp
public sealed class FixedLengthParserBuilder
{
    // Layout configuration
    public FixedLengthParserBuilder WithLayout(FieldLayout layout);
    public FixedLengthParserBuilder WithCopybook(CobolCopybook copybook);

    // Record processing
    public FixedLengthParserBuilder WithRecordLength(int length);
    public FixedLengthParserBuilder WithPadding(PaddingMode mode);
    public FixedLengthParserBuilder WithEncoding(Encoding encoding);

    // Performance optimization
    public FixedLengthParserBuilder EnableParallelProcessing(bool enable = true);
    public FixedLengthParserBuilder EnableSIMDOptimizations(bool enable = true);

    // Error handling
    public FixedLengthParserBuilder WithErrorHandling(FixedLengthErrorHandling mode);

    // Build configured parser
    public IFixedLengthParser Build();
}
```

## Configured Parser Contract

### IFixedLengthParser (Configured Instance)

```csharp
public interface IFixedLengthParser
{
    // Parsing methods
    IEnumerable<string[]> Parse(string content);
    IEnumerable<T> Parse<T>(string content);
    IEnumerable<FixedLengthRecord> ParseRecords(string content);

    IEnumerable<string[]> ParseFile(string filePath);
    IEnumerable<T> ParseFile<T>(string filePath);
    IEnumerable<FixedLengthRecord> ParseFileRecords(string filePath);

    // Configuration access
    FixedLengthConfiguration Configuration { get; }
}
```

## Enumeration Contracts

### FieldType

```csharp
public enum FieldType
{
    String,      // Text data (default)
    Integer,     // Numeric integer
    Decimal,     // Decimal number
    Date,        // Date value
    Boolean,     // True/false value
    Binary       // Binary data (hex-encoded)
}
```

### PaddingMode

```csharp
public enum PaddingMode
{
    None,        // No padding
    SpacePadded, // Pad with spaces
    ZeroPadded,  // Pad with zeros
    LeftPadded,  // Pad on left side
    RightPadded  // Pad on right side
}
```

### FieldAlignment

```csharp
public enum FieldAlignment
{
    Left,    // Left-aligned
    Right,   // Right-aligned
    Center   // Center-aligned
}
```

### CobolFieldType

```csharp
public enum CobolFieldType
{
    Alphanumeric,    // X format
    Numeric,         // 9 format
    SignedNumeric,   // S9 format
    Computational,   // COMP format
    Packed,          // COMP-3 format
    Binary,          // COMP-4/COMP-5 format
    FloatingPoint    // COMP-1/COMP-2 format
}
```

### FixedLengthErrorHandling

```csharp
public enum FixedLengthErrorHandling
{
    Strict,        // Throw exceptions on malformed data
    Lenient,       // Skip invalid records with warning
    IgnoreErrors   // Continue parsing, return null for invalid fields
}
```

## Usage Examples

### Custom Field Layout
```csharp
var layout = new FieldLayout()
    .AddField("CustomerID", 0, 10, FieldType.String)
    .AddField("Balance", 10, 15, FieldType.Decimal)
    .AddField("Date", 25, 8, FieldType.Date);

var records = FixedLengthParser.Parse(fileContent, layout);
```

### COBOL Copybook Processing
```csharp
var copybook = CobolCopybook.LoadFromFile("customer.cpy");
var records = FixedLengthParser.Parse(mainframeData, copybook);

foreach (var record in records)
{
    var customerId = record.GetField<string>("CUSTOMER-ID");
    var balance = record.GetField<decimal>("ACCOUNT-BALANCE");
}
```

### Advanced Configuration
```csharp
var parser = FixedLengthParser.Configure()
    .WithLayout(layout)
    .WithPadding(PaddingMode.SpacePadded)
    .EnableParallelProcessing()
    .WithErrorHandling(FixedLengthErrorHandling.Lenient)
    .Build();

var customers = parser.ParseFile<Customer>("customers.dat");
```