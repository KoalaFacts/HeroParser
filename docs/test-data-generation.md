# Test Data Generation Strategy

## Overview

HeroParser uses **on-the-fly test data generation** instead of checking large CSV files into the git repository. This approach provides several key benefits:

- **Git repository efficiency**: No large binary or text files
- **Deterministic testing**: Reproducible data generation with consistent patterns
- **Flexible scenarios**: Dynamic generation for different test cases
- **Memory optimization**: Precise control over data sizes and characteristics

## Generation Components

### 1. CsvDataGenerator (Benchmarks)

**Location**: `tests/HeroParser.BenchmarkTests/Utilities/CsvDataGenerator.cs`

**Purpose**: High-performance data generation for benchmark testing

**Features**:
- **Realistic schemas**: Employee, Financial, IoT, Ecommerce, LogData
- **Stress testing**: QuotedFields, EscapeSequences, LargeFields, ManyColumns
- **Size estimation**: Predictable memory usage calculations
- **Performance optimized**: Pre-computed data pools for efficiency

**Usage Examples**:
```csharp
// Generate 1MB of employee data
var csvData = CsvDataGenerator.GenerateRealistic(8_500, CsvSchema.Employee);

// Generate stress test for quoted fields
var stressData = CsvDataGenerator.GenerateStressTest(1000, StressTestType.QuotedFields);

// Estimate data size
var estimatedSize = CsvDataGenerator.EstimateSize(10_000, CsvSchema.Financial);
```

### 2. ComplianceTestDataGenerator (Compliance Tests)

**Location**: `tests/HeroParser.ComplianceTests/Utilities/ComplianceTestDataGenerator.cs`

**Purpose**: Specialized generation for RFC 4180 compliance and format validation

**Features**:
- **RFC 4180 compliance**: Standards-compliant CSV generation
- **Edge cases**: Malformed data, encoding issues, special characters
- **COBOL formats**: Fixed-length, PICTURE clauses, NACHA specifications
- **Binary data**: EBCDIC, packed decimal (COMP-3) test patterns

**Usage Examples**:
```csharp
// RFC 4180 compliant data
var compliantData = ComplianceTestDataGenerator.GenerateRfc4180Compliant();

// COBOL fixed-length records
var cobolData = ComplianceTestDataGenerator.GenerateCobolFixedLength();

// Malformed data for error testing
var malformedData = ComplianceTestDataGenerator.GenerateMalformed();
```

## Data Schemas

### Employee Schema (Default)
- **Size**: ~120 bytes per record
- **Fields**: Id, FirstName, LastName, Email, Age, Salary, Department, StartDate, IsActive
- **Use case**: General purpose benchmarking and testing

### Financial Schema
- **Size**: ~180 bytes per record
- **Fields**: TransactionId, AccountId, Amount, Currency, TransactionType, Timestamp, MerchantId, CardLast4
- **Use case**: Financial data processing scenarios

### IoT Schema
- **Size**: ~80 bytes per record
- **Fields**: DeviceId, Timestamp, Temperature, Humidity, Pressure, BatteryLevel
- **Use case**: Sensor data and time-series processing

### Ecommerce Schema
- **Size**: ~200 bytes per record
- **Fields**: OrderId, CustomerId, ProductId, ProductName, Quantity, Price, Discount, ShippingAddress, OrderDate
- **Use case**: E-commerce and retail data scenarios

### LogData Schema
- **Size**: ~300 bytes per record
- **Fields**: Timestamp, Level, Source, Message, UserId, SessionId, IpAddress, UserAgent
- **Use case**: Application logging and monitoring scenarios

## Benchmark Data Sizes

The generators are optimized for specific benchmark categories:

### Startup Latency (1KB)
- **Records**: 10 employees
- **Purpose**: Parser initialization and small data overhead
- **Target**: <1ms parsing time

### Throughput Testing (1MB)
- **Records**: 8,500 employees
- **Purpose**: Sustained parsing performance measurement
- **Target**: >25 GB/s throughput (.NET 8+)

### Sustained Performance (1GB)
- **Records**: 8,500,000 employees
- **Purpose**: Large file processing and memory management
- **Target**: Constant memory usage, no GC pressure

## Stress Test Scenarios

### QuotedFields
- Heavy use of quoted fields with embedded commas
- Tests quote parsing performance and correctness

### EscapeSequences
- Special characters, Unicode, escape sequences
- Validates character encoding and escape handling

### LargeFields
- Fields from 500-5000 characters
- Tests memory management with large field values

### ManyColumns
- Records with 50+ columns
- Validates parsing performance with wide records

### MixedLineEndings
- CRLF, LF, CR line endings mixed in same file
- Tests line ending detection and handling

## Fixed-Length Generation

### COBOL Copybook Support
- **Customer records**: ID(8) + Name(20) + Amount(10) + Date(8)
- **PICTURE clauses**: X(alphanumeric), 9(numeric), A(alphabetic)
- **OCCURS arrays**: Repeating field groups
- **COMP fields**: Binary and packed decimal

### NACHA Format
- **File Header**: 94-character financial records
- **Batch Header**: Banking batch information
- **Entry Detail**: Individual transaction records

## Memory Management

### Buffer Size Optimization
Data generators calculate optimal buffer sizes based on:
- Target data size (1KB, 1MB, 1GB)
- Record count and average record length
- StringBuilder initial capacity for minimal allocations

### Performance Characteristics
- **Pre-computed pools**: Names, departments, email domains
- **Deterministic patterns**: Reproducible data using record index
- **Memory efficient**: Single StringBuilder allocation per generation

## Integration with Benchmarks

### Baseline Benchmarks
```csharp
[GlobalSetup]
public void Setup()
{
    _csv1KB = CsvDataGenerator.GenerateRealistic(10, CsvSchema.Employee);
    _csv1MB = CsvDataGenerator.GenerateRealistic(8_500, CsvSchema.Employee);
    _csv1GB = CsvDataGenerator.GenerateRealistic(8_500_000, CsvSchema.Employee);
}
```

### Memory Benchmarks
```csharp
[GlobalSetup]
public void Setup()
{
    _smallCsvData = CsvDataGenerator.GenerateRealistic(100, CsvSchema.Employee);     // ~10KB
    _mediumCsvData = CsvDataGenerator.GenerateRealistic(8_500, CsvSchema.Employee);  // ~1MB
    _largeCsvData = CsvDataGenerator.GenerateRealistic(850_000, CsvSchema.Employee); // ~100MB
}
```

## Temporary File Management

### In-Memory Generation (Default)
All test data is generated in-memory as strings and used directly in tests. This is the preferred approach for:
- Benchmark tests (BaselineBenchmarks, MemoryBenchmarks)
- Unit tests and compliance tests
- Performance measurements

### Temporary File Generation (Debugging Only)
When debugging test data, developers can write generated data to temporary files:

```csharp
// Write to bin/temp-csv/ directory (automatically git-ignored)
var csvData = CsvDataGenerator.GenerateRealistic(1000, CsvSchema.Employee);
var tempFile = CsvDataGenerator.WriteToTempFile(csvData, "debug-employee-data.csv");

// Write to bin/temp-compliance/ directory
var complianceData = ComplianceTestDataGenerator.GenerateRfc4180Compliant();
var tempFile = ComplianceTestDataGenerator.WriteComplianceDataToTempFile(complianceData);
```

### File Location Strategy
- **In-memory**: Default for all production test runs
- **bin/temp-**/**: Temporary files for debugging (auto-ignored by git)
- **bin/Debug/**: Output directory files (auto-ignored by git)
- **Never in source directories**: Prevents accidental git commits

### Git Ignore Protection
The `.gitignore` file automatically excludes:
- All `bin/` directory contents (including temp files)
- Generated CSV patterns (`*-generated.csv`, `*-benchmark.csv`)
- Temporary directories (`temp-csv/`, `temp-compliance/`)
- Common debug file patterns

## Benefits Over Static Files

### Repository Efficiency
- **No large files**: Git history stays clean and fast
- **No LFS required**: Avoids Git Large File Storage complexity
- **Clone speed**: Faster repository cloning

### Test Flexibility
- **Dynamic sizing**: Generate exactly the size needed
- **Scenario variation**: Different data patterns for different tests
- **Deterministic**: Same data every test run for reproducibility

### Performance Benefits
- **Memory control**: Precise memory usage patterns
- **Cache efficiency**: Data generated once per test session
- **Realistic patterns**: Varied but predictable data distributions

This approach ensures that HeroParser can be thoroughly tested and benchmarked without requiring large test files in the repository, while providing comprehensive coverage of real-world data scenarios.