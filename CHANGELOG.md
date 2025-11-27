# Changelog

## Unreleased

### Added
- **Fluent Reader Builder**: `Csv.Read<T>()` fluent API for reading CSV records
  - Symmetric API with `CsvWriterBuilder<T>` for consistent developer experience
  - Configure delimiter, quote character, max columns/rows, and more
  - Terminal methods: `FromText()`, `FromFile()`, `FromStream()`, and async variants
  - All parser and record options accessible through fluent methods
- **CSV Writing**: High-performance CSV writer with RFC 4180 compliance
  - `Csv.WriteToText<T>()` - Write records to a string
  - `Csv.WriteToFile<T>()` / `Csv.WriteToFileAsync<T>()` - Write to files
  - `Csv.WriteToStream<T>()` / `Csv.WriteToStreamAsync<T>()` - Write to streams
  - `Csv.SerializeRecords<T>()` - Symmetric counterpart to `DeserializeRecords<T>()`
  - `CsvStreamWriter` - Low-level writer for row-by-row writing
  - `CsvWriterBuilder` - Fluent API for configuring writers
- **Writer Options**: Full control over CSV output format
  - `QuoteStyle` - Control when fields are quoted (Always, Never, WhenNeeded)
  - `NullValue` - Configure string representation of null values
  - `DateTimeFormat`, `DateOnlyFormat`, `TimeOnlyFormat` - Custom date/time formatting
  - `NumberFormat` - Custom numeric formatting
  - `MaxRowCount` - DoS protection for output
  - `OnSerializeError` - Error handling callback with Skip/WriteNull/Throw options
- **SIMD-Accelerated Writing**: Uses AVX2/SSE2 for single-pass field analysis
- **Source Generator Support**: Compiled expression getters for record serialization

### Performance
- CSV writing is 2-5x faster than Sep with 35-85% less memory allocation
- Single-pass field analysis for quote detection and counting
- Direct type handling for common types (int, double, bool, DateTime, etc.) to avoid interface dispatch
- Cached options in hot paths for minimal property access overhead

## 1.0.0 - 2025-11-20
- Added configurable RFC compliance options (newlines-in-quotes opt-in, ability to disable quote parsing for speed).
- Expanded parsing helpers: culture-aware and format overloads for date/time types; enum parsing; numeric helpers across byte/short/int/long/float/decimal; timezone parsing.
- Improved UTF-8 parsing consistency and clarified allocation behavior (UTF-8 culture/format parsing decodes to UTF-16).
- Updated CI permissions for publishing test results; benchmarks now toggle RFC options for comparison.
