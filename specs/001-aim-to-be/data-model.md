# Data Model: HeroParser

**Date**: 2025-01-25 | **Phase**: 1 | **Status**: Complete

## Core Entities

### CsvParser (Static API)
**Purpose**: Primary entry point for simple CSV parsing operations

**Fields/Methods**:
- `Parse(string content)` → `IEnumerable<string[]>`
- `Parse<T>(string content)` → `IEnumerable<T>`
- `Parse(ReadOnlySpan<char> content)` → `IEnumerable<string[]>`
- `Parse<T>(ReadOnlySpan<char> content)` → `IEnumerable<T>`
- `ParseFile(string filePath)` → `IEnumerable<string[]>`
- `ParseFile<T>(string filePath)` → `IEnumerable<T>`
- `ParseAsync(Stream stream)` → `IAsyncEnumerable<string[]>`
- `ParseAsync<T>(Stream stream)` → `IAsyncEnumerable<T>`
- `ParseFileAsync(string filePath)` → `IAsyncEnumerable<string[]>`
- `ParseFileAsync<T>(string filePath)` → `IAsyncEnumerable<T>`
- `Configure()` → `CsvParserBuilder`

**Validation Rules**:
- Input content must not be null
- File paths must exist and be readable
- Stream must be readable
- Generic type T must have parameterless constructor or be source-generated

### CsvParserBuilder (Fluent Configuration)
**Purpose**: Advanced configuration for CSV parsing with fluent API

**Fields/Methods**:
- `WithDelimiter(char delimiter)` → `CsvParserBuilder`
- `WithQuoteChar(char quote)` → `CsvParserBuilder`
- `WithEscapeChar(char escape)` → `CsvParserBuilder`
- `AllowComments(bool allow = true)` → `CsvParserBuilder`
- `TrimWhitespace(bool trim = true)` → `CsvParserBuilder`
- `EnableParallelProcessing(bool enable = true)` → `CsvParserBuilder`
- `EnableSIMDOptimizations(bool enable = true)` → `CsvParserBuilder`
- `MapField<T>(Expression<Func<T, object>> property, int columnIndex)` → `CsvParserBuilder`
- `MapField<T>(Expression<Func<T, object>> property, string columnName)` → `CsvParserBuilder`
- `WithErrorHandling(CsvErrorHandling mode)` → `CsvParserBuilder`
- `Build()` → `ICsvParser`

**State Transitions**:
- Builder → Builder (fluent methods)
- Builder → ICsvParser (Build())

**Validation Rules**:
- Delimiter cannot be quote or escape character
- Column indices must be non-negative
- Column names must not be null or empty

### ICsvParser (Configured Parser Interface)
**Purpose**: Configured parser instance with custom settings

**Fields/Methods**:
- `Parse(string content)` → `IEnumerable<string[]>`
- `Parse<T>(string content)` → `IEnumerable<T>`
- `Parse(ReadOnlySpan<char> content)` → `IEnumerable<string[]>`
- `Parse<T>(ReadOnlySpan<char> content)` → `IEnumerable<T>`
- `ParseFile(string filePath)` → `IEnumerable<string[]>`
- `ParseFile<T>(string filePath)` → `IEnumerable<T>`
- `ParseAsync(Stream stream)` → `IAsyncEnumerable<string[]>`
- `ParseAsync<T>(Stream stream)` → `IAsyncEnumerable<T>`
- `Configuration` → `CsvConfiguration` (read-only)

### CsvConfiguration (Immutable Settings)
**Purpose**: Immutable configuration object for parser settings

**Fields**:
- `Delimiter: char` (default: ',')
- `QuoteChar: char` (default: '"')
- `EscapeChar: char` (default: '"')
- `AllowComments: bool` (default: false)
- `TrimWhitespace: bool` (default: false)
- `ParallelProcessing: bool` (default: true)
- `SIMDOptimizations: bool` (default: true)
- `ErrorHandling: CsvErrorHandling` (default: Strict)
- `FieldMappings: IReadOnlyDictionary<string, int>`

**Validation Rules**:
- All characters must be distinct (delimiter ≠ quote ≠ escape)
- Characters must be ASCII for performance

### FixedLengthParser (Static API)
**Purpose**: Entry point for fixed-length file parsing

**Fields/Methods**:
- `Parse(string content, FieldLayout layout)` → `IEnumerable<string[]>`
- `Parse<T>(string content, FieldLayout layout)` → `IEnumerable<T>`
- `Parse(string content, CobolCopybook copybook)` → `IEnumerable<FixedLengthRecord>`
- `ParseFile(string filePath, FieldLayout layout)` → `IEnumerable<string[]>`
- `Configure()` → `FixedLengthParserBuilder`

### FieldLayout (Fixed-Length Schema)
**Purpose**: Definition of fixed-length field positions and types

**Fields**:
- `Fields: IReadOnlyList<FieldDefinition>`
- `TotalLength: int`
- `Padding: PaddingMode`

**Methods**:
- `AddField(string name, int start, int length, Type type = typeof(string))` → `FieldLayout`
- `AddField(string name, int start, int length, FieldType type)` → `FieldLayout`

### FieldDefinition (Single Field Schema)
**Purpose**: Individual field definition within fixed-length record

**Fields**:
- `Name: string`
- `StartPosition: int` (0-based)
- `Length: int`
- `Type: Type`
- `Padding: PaddingMode`
- `Alignment: FieldAlignment`

**Validation Rules**:
- Start position must be non-negative
- Length must be positive
- Fields cannot overlap
- Name must be unique within layout

### CobolCopybook (COBOL Schema)
**Purpose**: COBOL copybook definition for mainframe file parsing

**Fields**:
- `Name: string`
- `Fields: IReadOnlyList<CobolField>`
- `RecordLength: int`
- `Encoding: Encoding`

**Methods**:
- `LoadFromFile(string copybookPath)` → `CobolCopybook`
- `Parse(string copybookContent)` → `CobolCopybook`

### CsvParseException (Error Handling)
**Purpose**: Exception for CSV parsing errors with detailed position information

**Fields**:
- `LineNumber: int`
- `ColumnNumber: int`
- `FieldValue: string`
- `ErrorType: CsvErrorType`

### CsvMappingException (Type Conversion Errors)
**Purpose**: Exception for object mapping failures during typed parsing

**Fields**:
- `TargetType: Type`
- `FieldName: string`
- `FieldValue: string`
- `LineNumber: int`
- `ColumnNumber: int`

## Enums

### CsvErrorHandling
- `Strict`: Throw exceptions on any malformed data
- `Lenient`: Skip invalid rows with warning
- `IgnoreErrors`: Continue parsing, return null for invalid fields

### FieldType
- `String`: Text data (default)
- `Integer`: Numeric integer
- `Decimal`: Decimal number
- `Date`: Date value
- `Boolean`: True/false value
- `Binary`: Binary data (hex-encoded)

### PaddingMode
- `None`: No padding
- `SpacePadded`: Pad with spaces
- `ZeroPadded`: Pad with zeros
- `LeftPadded`: Pad on left side
- `RightPadded`: Pad on right side

### FieldAlignment
- `Left`: Left-aligned
- `Right`: Right-aligned
- `Center`: Center-aligned

### CsvErrorType
- `MalformedField`: Invalid field format
- `UnterminatedQuote`: Unclosed quoted field
- `InvalidCharacter`: Invalid character in field
- `LineTooLong`: Line exceeds maximum length
- `FieldCountMismatch`: Wrong number of fields in row

## Relationships

```
CsvParser (static) --> CsvParserBuilder --> ICsvParser
                   \-> Direct parsing methods

CsvParserBuilder --> CsvConfiguration (immutable)
ICsvParser --> CsvConfiguration (immutable)

FixedLengthParser --> FieldLayout --> FieldDefinition[]
                  \-> CobolCopybook --> CobolField[]

All parsers --> CsvParseException
Typed parsing --> CsvMappingException
```

## Performance Considerations

### Memory Management
- All parsing operations designed for zero allocations in common scenarios
- ArrayPool<char> for internal buffers
- Span<char> for string operations
- Source generators for object creation

### Threading Model
- Static methods are thread-safe
- Parser instances are thread-safe for reading
- Configuration objects are immutable and thread-safe
- Parallel processing uses work-stealing approach

### SIMD Optimization
- Internal parsing uses vectorized operations
- Hardware detection with fallbacks
- Optimized for common CSV patterns