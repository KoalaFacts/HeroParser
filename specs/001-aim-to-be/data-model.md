# Data Model: High-Performance CSV/Fixed-Length Parser

## Core Entities

### CsvRecord
**Purpose**: Represents a single parsed CSV row with zero-allocation field access
**Key Attributes**:
- `FieldCount`: Number of fields in the record
- `LineNumber`: Source line number for error reporting
- `RawData`: ReadOnlySpan<char> to original source data
- `FieldSpans`: ReadOnlySpan<Range> array for field boundaries

**Validation Rules**:
- FieldCount must be positive
- LineNumber must be >= 1
- RawData cannot be empty for non-empty records
- FieldSpans length must equal FieldCount

**Performance Characteristics**:
- Zero heap allocations during enumeration
- Lazy field parsing on demand
- SIMD-optimized field boundary detection

### FixedLengthRecord
**Purpose**: Represents a fixed-width record with copybook-defined field positions
**Key Attributes**:
- `RecordLength`: Total character length of the record
- `FieldDefinitions`: Array of field position and length specifications
- `RawData`: ReadOnlySpan<char> to source record data
- `FieldValues`: Lazy-evaluated field extraction

**Relationships**:
- Composed of multiple `FieldDefinition` entities
- Maps to `CobolCopybook` specifications
- Validates against `RecordSchema` definitions

### ParserConfiguration
**Purpose**: Immutable configuration object for parsing behavior
**Key Attributes**:
- `Delimiter`: Character used to separate CSV fields (default: comma)
- `QuoteCharacter`: Character used for field quoting (default: double quote)
- `EscapeCharacter`: Character used for escaping (default: none)
- `AllowComments`: Whether to support comment lines starting with #
- `TrimWhitespace`: Whether to trim leading/trailing whitespace
- `EnableParallelProcessing`: Auto-enable for files >10MB
- `SIMDOptimization`: Hardware-specific vectorization settings

**State Transitions**:
- Immutable after creation (builder pattern for construction)
- Validation occurs during build phase
- Runtime optimization applied based on input characteristics

### ParseResult<T>
**Purpose**: Container for parsed data with comprehensive error reporting
**Key Attributes**:
- `Records`: IEnumerable<T> of successfully parsed records
- `Errors`: Collection of parsing errors with line/column information
- `Statistics`: Performance metrics (throughput, memory usage, parse time)
- `Metadata`: Source information (encoding, line endings, record count)

**Error Handling**:
- Non-fatal errors allow continued parsing
- Fatal errors halt processing with detailed diagnostics
- Error recovery strategies for malformed data

## Entity Relationships

```
ParserConfiguration
├── CsvOptions
│   ├── Delimiter
│   ├── QuoteCharacter
│   └── EscapeHandling
├── FixedLengthOptions
│   ├── RecordLength
│   └── FieldDefinitions[]
└── PerformanceOptions
    ├── EnableSIMD
    ├── ParallelThreshold
    └── BufferSizeHint

ParseResult<T>
├── Records: IEnumerable<T>
├── Errors: ParseError[]
├── Statistics: ParseStatistics
└── Metadata: SourceMetadata

CsvRecord
├── FieldSpans: Range[]
├── RawData: ReadOnlySpan<char>
└── GetField(int index): ReadOnlySpan<char>

FixedLengthRecord
├── FieldDefinitions: FieldDefinition[]
├── RawData: ReadOnlySpan<char>
└── GetField(string name): ReadOnlySpan<char>
```

## Type Mapping System

### Built-in Type Support
- **Primitives**: int, long, float, double, decimal, bool
- **Strings**: string, ReadOnlySpan<char>, ReadOnlyMemory<char>
- **Dates**: DateTime, DateTimeOffset, DateOnly, TimeOnly
- **Nullables**: All nullable value types
- **Collections**: Arrays and IEnumerable<T> for multi-value fields

### Custom Type Mapping
```csharp
public interface ITypeConverter<T>
{
    bool TryParse(ReadOnlySpan<char> input, out T result);
    ReadOnlySpan<char> Format(T value, Span<char> buffer);
}
```

### Source Generator Integration
- Compile-time generation of mapping code
- Zero-allocation parsing for custom types
- Automatic validation rule enforcement
- Performance-optimized field access patterns

## Memory Management Model

### Buffer Pool Architecture
```
ThreadLocalBufferPool
├── SmallBuffers: 64B, 128B, 256B, 512B
├── MediumBuffers: 1KB, 2KB, 4KB, 8KB
├── LargeBuffers: 16KB, 32KB, 64KB, 128KB
└── StreamingBuffers: 1MB+ for large file processing
```

### Allocation Strategy
- **Hot Path**: Stack allocation for small operations
- **Warm Path**: Thread-local buffer pools
- **Cold Path**: Shared buffer pools with locking
- **Streaming**: Memory-mapped files for >1GB datasets

### Zero-Allocation Guarantees
- 99th percentile operations produce zero garbage
- Enumerators use value types and ref returns
- String interning disabled by default
- Span<T> and Memory<T> throughout public API

---

This data model ensures maximum performance while maintaining type safety and comprehensive error handling capabilities.