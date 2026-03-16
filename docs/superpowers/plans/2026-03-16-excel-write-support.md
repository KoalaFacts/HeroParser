# Excel (.xlsx) Write Support Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add Excel write support to HeroParser, enabling `Excel.Write<T>().ToFile()` / `.ToStream()` with the same builder pattern, validation enforcement, and source-generator support as CSV and FixedWidth writers.

**Architecture:** `ExcelWriterBuilder<T>` (fluent config) → `ExcelRecordWriter<T>` (property extraction, validation, formatting) → `XlsxWriter` (ZIP/XML generation). The `XlsxWriter` manages the ZipArchive, XmlWriter per sheet, shared string table, and minimal stylesheet. Records are written as typed XML cells (shared string, number, boolean, date serial).

**Tech Stack:** C#, `System.IO.Compression.ZipArchive`, `System.Xml.XmlWriter`, existing `WriteValidationRunner`, `PropertyAccessor`/`WriterTemplate` patterns.

---

## File Structure

### New Files
- `src/HeroParser/Excels/Core/ExcelWriteOptions.cs` — Options record (culture, formats, validation, null value, max rows)
- `src/HeroParser/Excels/Xlsx/XlsxWriter.cs` — Low-level .xlsx package writer (ZipArchive + XmlWriter)
- `src/HeroParser/Excels/Xlsx/XlsxSharedStringTable.cs` — Mutable shared string table for write-side dedup
- `src/HeroParser/Excels/Writing/ExcelRecordWriter.cs` — Typed record writer (property extraction, validation, cell writing)
- `src/HeroParser/Excels/Writing/ExcelRecordWriterFactory.cs` — Factory with source-generator registration
- `src/HeroParser/Excels/Writing/ExcelWriterBuilder.cs` — Fluent builder
- `src/HeroParser/Excels/Writing/ExcelMultiSheetWriterBuilder.cs` — Multi-sheet write builder
- `src/HeroParser/Excel.Write.cs` — Facade (`Excel.Write<T>()`, `Excel.WriteToFile<T>()`)

### Test Files
- `tests/HeroParser.Tests/Excel/ExcelWriteTests.cs` — Core write tests
- `tests/HeroParser.Tests/Excel/ExcelWriteValidationTests.cs` — Validation enforcement
- `tests/HeroParser.Tests/Excel/ExcelWriteRoundTripTests.cs` — Write then read back

---

## Chunk 1: Core Infrastructure

### Task 1: ExcelWriteOptions
- [ ] Create options record with: Culture, NullValue, DateTimeFormat, DateOnlyFormat, TimeOnlyFormat, NumberFormat, MaxRowCount, ValidationMode, WriteHeader, OnSerializeError
- [ ] Build + verify

### Task 2: XlsxSharedStringTable
- [ ] Create mutable shared string table: `GetOrAdd(string) → int`, `Count`, `Strings`
- [ ] Unit tests

### Task 3: XlsxWriter
- [ ] Create `XlsxWriter : IDisposable` managing ZipArchive (Create mode)
- [ ] Implement `SheetWriter StartSheet(string name)` — nested class writing row/cell XML
- [ ] Cell methods: `WriteCellString`, `WriteCellNumber`, `WriteCellBoolean`, `WriteCellDate`, `WriteCellEmpty`
- [ ] Package metadata: `[Content_Types].xml`, `_rels/.rels`, `xl/workbook.xml`, `xl/_rels/workbook.xml.rels`, `xl/sharedStrings.xml`, `xl/styles.xml`
- [ ] `Dispose()` finalizes sheets, writes metadata, disposes ZipArchive
- [ ] Unit tests: create simple xlsx, read back with existing `XlsxReader`

## Chunk 2: Record Writer

### Task 4: ExcelRecordWriter<T>
- [ ] PropertyAccessor with reflection-based property discovery (reads `[TabularMap]`, `[Format]`, `[Validate]`)
- [ ] WriterTemplate record for source-generated path
- [ ] `WriteRecords(SheetWriter, IEnumerable<T>, bool writeHeader)` with type dispatch and validation
- [ ] Type handling: string→shared string, numeric→number, bool→boolean, DateTime→OLE date, null→empty

### Task 5: ExcelRecordWriterFactory
- [ ] Factory with `RegisterGeneratedWriter()` + `GetWriter<T>()`

## Chunk 3: Builder + Facade

### Task 6: ExcelWriterBuilder<T>
- [ ] Fluent methods: `WithCulture()`, `WithNullValue()`, `WithDateTimeFormat()`, `WithValidationMode()`, `WithMaxRowCount()`, `WithHeader()`/`WithoutHeader()`, `WithSheetName()`, `OnError()`
- [ ] Terminal methods: `ToFile(path, records)`, `ToStream(stream, records)`, `ToBytes(records)`

### Task 7: Excel.Write.cs Facade
- [ ] `Excel.Write<T>()` → builder
- [ ] `Excel.WriteToFile<T>(path, records, options?)`
- [ ] `Excel.WriteToStream<T>(stream, records, options?)`

### Task 8: ExcelMultiSheetWriterBuilder
- [ ] `WithSheet<T>(sheetName, records)` for different types per sheet
- [ ] Terminal methods: `ToFile()`, `ToStream()`

## Chunk 4: Source Generator

### Task 9: Emit Excel writer registration
- [ ] In `CsvRecordBinderGenerator`, emit `ExcelRecordWriterFactory.RegisterGeneratedWriter()` alongside CSV writer registration

## Chunk 5: Tests

### Task 10: Integration tests
- [ ] Write-then-read round-trip for all supported types
- [ ] Validation enforcement (strict throws, lenient passes)
- [ ] Multi-sheet write + read back
- [ ] Unicode content (Chinese, Arabic, Emoji)
- [ ] Header writing and suppression
- [ ] Date/number formatting
- [ ] Max row count enforcement

---

## Design Decisions
1. **Shared string table** — standard xlsx dedup, built during write, serialized on Dispose
2. **Dates as OLE doubles** — `DateTime.ToOADate()` with style format codes
3. **No async initially** — xlsx requires random access (shared strings written last), no streaming benefit
4. **XlsxWriter owns full lifecycle** — buffers sheet XML via ZipArchive, writes metadata on Dispose
5. **ExcelTestHelper as reference** — existing test helper generates valid xlsx; production writer follows same structure with XmlWriter
