# Fixed-Length Parser API Contract

## Simple APIs

### Copybook-Based Parsing
```csharp
// Parse with COBOL copybook definition
IEnumerable<FixedLengthRecord> Parse(string content, CobolCopybook copybook);
IEnumerable<T> Parse<T>(string content, CobolCopybook copybook);

// Parse with field layout definition
IEnumerable<FixedLengthRecord> Parse(string content, FieldLayout layout);
IEnumerable<T> Parse<T>(string content, FieldLayout layout);
```

### Schema-Based Parsing
```csharp
// Define schema programmatically
var schema = FixedLengthSchema.Create()
    .Field("CustomerId", 0, 10, FieldType.Numeric)
    .Field("CustomerName", 10, 30, FieldType.Text)
    .Field("AccountBalance", 40, 12, FieldType.Decimal, 2)
    .Field("LastTransactionDate", 52, 8, FieldType.Date, "yyyyMMdd");

var records = FixedLengthParser.Parse<Customer>(content, schema);
```

## Advanced Configuration APIs

### Fluent Configuration Builder
```csharp
var parser = FixedLengthParser.Configure()
    .WithRecordLength(120)
    .WithEncoding(Encoding.EBCDIC)
    .WithPadding(PaddingMode.Space)
    .WithTrimming(TrimmingMode.Both)
    .EnableParallelProcessing()
    .WithErrorHandling(ErrorMode.Tolerant)
    .Build();
```

### COBOL Copybook Integration
```csharp
// Load copybook from file
var copybook = CobolCopybook.LoadFromFile("customer-record.cpy");

// Parse with copybook
var parser = FixedLengthParser.Configure()
    .WithCopybook(copybook)
    .EnableEBCDICSupport()
    .WithSignedNumberFormat(SignFormat.Trailing)
    .Build();

var customers = parser.Parse<Customer>(mainframeData);
```

## Format Support Contract

### COBOL Copybook Compatibility
- **PICTURE Clauses**: Full support for X, 9, A, S, V, P picture specifications
- **OCCURS Clauses**: Array field definitions with repetition
- **REDEFINES**: Multiple field interpretations of same data area
- **COMP Fields**: Binary, packed decimal, and display formats
- **Sign Handling**: Leading, trailing, separate, and embedded signs

### IBM Mainframe Format Support
- **EBCDIC Encoding**: Automatic conversion to/from Unicode
- **Packed Decimal**: COMP-3 field format support
- **Binary Fields**: COMP and COMP-4 format support
- **Zoned Decimal**: Display numeric with sign handling

### NACHA Specification Support
- **File Header/Trailer**: Standard ACH file structure
- **Batch Header/Trailer**: ACH batch record formats
- **Entry Detail**: ACH transaction record parsing
- **Addenda Records**: Optional additional information records

## Performance Contract Guarantees

### Throughput Requirements
- **Single-threaded**: >20 GB/s parse throughput for fixed-length
- **Multi-threaded**: >45 GB/s parse throughput with parallel processing
- **COBOL parsing**: >15 GB/s with full copybook interpretation
- **EBCDIC conversion**: >25 GB/s with character set conversion

### Memory Efficiency
- **Zero allocations**: 99th percentile operations for record enumeration
- **Field access**: Zero-allocation field extraction using Span<T>
- **Schema caching**: Compiled field accessors for repeated parsing
- **Buffer reuse**: Automatic buffer pooling for conversion operations

### Latency Requirements
- **Schema compilation**: <5ms for copybook parsing and compilation
- **First record**: <50ms to parse and emit first record from stream
- **Field access**: <10μs for individual field extraction
- **Type conversion**: <1μs for primitive type conversions

## Error Handling Contract

### Format Validation
```csharp
public class FixedLengthParseException : Exception
{
    public int RecordNumber { get; }
    public int FieldPosition { get; }
    public string FieldName { get; }
    public FixedLengthError ErrorType { get; }
}

public enum FixedLengthError
{
    RecordTooShort,
    RecordTooLong,
    InvalidNumericFormat,
    InvalidDateFormat,
    InvalidPackedDecimal,
    UnexpectedEndOfFile
}
```

### Recovery Strategies
- **Strict Validation**: Throw exception on any format violation
- **Padding Adjustment**: Auto-pad short records, truncate long records
- **Field Substitution**: Use default values for invalid field data
- **Record Skipping**: Skip malformed records with error logging

## Compatibility Contract

### Character Encoding Support
- **EBCDIC**: Full IBM mainframe character set support
- **ASCII**: Standard text file processing
- **UTF-8**: Unicode text with BOM detection
- **UTF-16**: Little/big endian with automatic detection
- **Custom**: User-defined encoding with conversion tables

### Numeric Format Support
- **Display Numeric**: Zoned decimal with configurable sign handling
- **Packed Decimal**: COMP-3 format with precision handling
- **Binary Integer**: COMP and COMP-4 with endianness detection
- **Floating Point**: IEEE 754 single and double precision

### Platform Compatibility
- **Cross-platform**: Windows, Linux, macOS support
- **Architecture**: x64, ARM64 with platform-specific optimizations
- **Framework**: .NET Standard 2.0 through .NET 10 support
- **AOT Compilation**: Full ahead-of-time compilation compatibility

## Thread Safety Contract

### Concurrent Processing
- **Schema instances**: Thread-safe for read operations
- **Parser instances**: Immutable configuration, safe for concurrent use
- **Field accessors**: Generated code is thread-safe
- **Buffer management**: Thread-local pools for optimal performance

### Parallel Processing
- **Record-level parallelism**: Automatic for large datasets
- **Field-level vectorization**: SIMD optimizations where applicable
- **Work distribution**: Dynamic load balancing across CPU cores
- **Memory locality**: NUMA-aware processing for large systems

---

This API contract ensures comprehensive fixed-length format support while maintaining the same performance standards as CSV parsing.