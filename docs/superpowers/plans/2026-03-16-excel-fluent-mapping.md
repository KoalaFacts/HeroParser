# Excel Fluent Mapping Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `WithMap()`/`Map<TProperty>()` fluent mapping to `ExcelRecordReaderBuilder<T>`, reusing the existing `CsvDescriptorBinder<T>` and `ICsvReadMapSource<T>` infrastructure.

**Architecture:** Excel cells are already adapted into `CsvRow<char>` via `XlsxRowAdapter`, so `CsvDescriptorBinder<T>` (char-based) is a drop-in replacement for the source-generated binder. When a map is configured, create `CsvDescriptorBinder<T>` instead of calling `CsvRecordBinderFactory.GetCharBinder<T>()`.

**Tech Stack:** C#, existing `CsvDescriptorBinder<T>`, `CsvMap<T>`, `InlineCsvMapWrapper<T>`, `CsvColumnBuilder`

---

## Task 1: Add fluent mapping to ExcelRecordReaderBuilder

**Files:**
- Modify: `src/HeroParser/Excels/Reading/ExcelRecordReaderBuilder.cs`

- [ ] Add `ICsvReadMapSource<T>? mapSource` field
- [ ] Add `using System.Diagnostics.CodeAnalysis;`, `using System.Linq.Expressions;`, `using HeroParser.SeparatedValues.Mapping;`
- [ ] Add `WithMap(ICsvReadMapSource<T> map)` with `[RequiresUnreferencedCode]` + `[RequiresDynamicCode]`
- [ ] Add `Map<TProperty>(Expression<Func<T, TProperty>> property, Action<CsvColumnBuilder>? configure = null)` with same attributes
- [ ] Add private `CreateInlineWrapper()` helper
- [ ] In `ReadRecords()`, replace binder creation: when `mapSource` is not null, create `CsvDescriptorBinder<T>` from `mapSource.BuildReadDescriptor()` instead of `CsvRecordBinderFactory.GetCharBinder<T>()`

## Task 2: Write tests

**Files:**
- Create: `tests/HeroParser.Tests/Excel/ExcelFluentMappingTests.cs`

Tests:
- [ ] `WithMap` by header name
- [ ] `WithMap` by index (with `WithoutHeader()`)
- [ ] Inline `Map<TProperty>` usage
- [ ] Subclass map pattern (`class TradeMap : CsvMap<Trade>`)
- [ ] Validation errors with map + lenient mode
- [ ] `Map()` after `WithMap()` throws `InvalidOperationException`

## Task 3: Verify

- [ ] `dotnet test -f net10.0`
- [ ] `dotnet format --verify-no-changes`
