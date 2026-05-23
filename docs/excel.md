# HeroParser Excel (.xlsx) API Reference

[Back to README](../README.md)

HeroParser reads and writes `.xlsx` files with zero extra dependencies — only `System.IO.Compression` and `System.Xml` from the .NET BCL are used. All APIs share the same fluent builder pattern as the CSV and Fixed-Width modules.

---

## Table of Contents

- [Reading](#reading)
  - [Typed Record Reading](#typed-record-reading)
  - [Sheet Selection](#sheet-selection)
  - [Row-Level Reading](#row-level-reading)
  - [All Sheets (Same Type)](#all-sheets-same-type)
  - [Multi-Sheet (Different Types)](#multi-sheet-different-types)
  - [Read Configuration](#read-configuration)
  - [OnError Callback](#onerror-callback-reading)
  - [Progress Reporting (Reading)](#progress-reporting-reading)
  - [Custom Type Converters](#custom-type-converters)
  - [Fluent Column Mapping (Reading)](#fluent-column-mapping-reading)
  - [DataReader](#datareader)
- [Writing](#writing)
  - [Typed Record Writing](#typed-record-writing)
  - [Sheet Naming](#sheet-naming)
  - [Multi-Sheet Writing](#multi-sheet-writing)
  - [In-Memory Writing](#in-memory-writing)
  - [Async Writing](#async-writing)
  - [OnError Callback (Writing)](#onerror-callback-writing)
  - [Progress Reporting (Writing)](#progress-reporting-writing)
  - [Output Size Limit](#output-size-limit)
  - [Fluent Column Mapping (Writing)](#fluent-column-mapping-writing)
  - [Write Formatting Options](#write-formatting-options)
- [Options Reference](#options-reference)
  - [ExcelReadOptions](#excelreadoptions)
  - [ExcelWriteOptions](#excelwriteoptions)
  - [ExcelDataReaderOptions](#exceldatareaderoptions)
- [Source Generators](#source-generators)
- [Excel-Specific Details](#excel-specific-details)

---

## Reading

### Typed Record Reading

Read an Excel file and deserialize each row into a strongly-typed record using `Excel.Read<T>()`.

```csharp
// Read all records from the first sheet
List<OrderRecord> orders = Excel.Read<OrderRecord>().FromFile("orders.xlsx");

// Read from a stream
using var stream = File.OpenRead("orders.xlsx");
List<OrderRecord> orders = Excel.Read<OrderRecord>().FromStream(stream);

// One-liner convenience method (default options, first sheet)
List<OrderRecord> orders = Excel.DeserializeRecords<OrderRecord>("orders.xlsx");
```

Column mapping follows the same attribute system as CSV:

```csharp
public class OrderRecord
{
    // Defaults to column named "Id" (property name)
    public int Id { get; set; }

    // Explicit column name
    [TabularMap(Name = "Customer Name")]
    public string CustomerName { get; set; } = "";

    // Date parsing format
    [TabularMap(Name = "Order Date")]
    [Parse(Format = "yyyy-MM-dd")]
    public DateOnly OrderDate { get; set; }

    // Validation rules
    [TabularMap(Name = "Amount")]
    [Validate(RangeMin = 0)]
    public decimal Amount { get; set; }
}
```

### Sheet Selection

By default the first sheet is read. Use `FromSheet()` to target a specific sheet.

```csharp
// By name
List<SalesRecord> sales = Excel.Read<SalesRecord>()
    .FromSheet("January")
    .FromFile("report.xlsx");

// By zero-based index
List<SalesRecord> sales = Excel.Read<SalesRecord>()
    .FromSheet(1)   // second sheet
    .FromFile("report.xlsx");
```

### Row-Level Reading

Read rows as `string[]` without any type binding when you need raw cell values.

```csharp
// Returns List<string[]> — one array per data row (header row is consumed but not returned)
List<string[]> rows = Excel.Read().FromFile("data.xlsx");

// Specific sheet
List<string[]> rows = Excel.Read()
    .FromSheet("RawData")
    .FromFile("data.xlsx");

// No header row — all rows returned as data
List<string[]> rows = Excel.Read()
    .WithoutHeader()
    .FromFile("data.xlsx");

// Skip banner rows before the header
List<string[]> rows = Excel.Read()
    .SkipRows(3)
    .FromFile("data.xlsx");
```

### All Sheets (Same Type)

Read every sheet in the workbook as the same record type. Returns a `Dictionary<string, List<T>>` keyed by sheet name.

```csharp
Dictionary<string, List<SalesRecord>> bySheet = Excel.Read<SalesRecord>()
    .AllSheets()
    .FromFile("annual_sales.xlsx");

foreach (var (sheetName, records) in bySheet)
{
    Console.WriteLine($"{sheetName}: {records.Count} rows");
}
```

Options configured on the parent builder are forwarded to each sheet:

```csharp
Dictionary<string, List<SalesRecord>> bySheet = Excel.Read<SalesRecord>()
    .WithCulture("de-DE")
    .AllowMissingColumns()
    .AllSheets()
    .FromFile("annual_sales.xlsx");
```

### Multi-Sheet (Different Types)

Read sheets with different record types in a single pass using `Excel.Read().WithSheet<T>()`.

```csharp
ExcelMultiSheetResult result = Excel.Read()
    .WithSheet<OrderRecord>("Orders")
    .WithSheet<CustomerRecord>("Customers")
    .FromFile("workbook.xlsx");

List<OrderRecord> orders = result.Get<OrderRecord>();
List<CustomerRecord> customers = result.Get<CustomerRecord>();
```

Call `FromStream` instead of `FromFile` when reading from a stream:

```csharp
ExcelMultiSheetResult result = Excel.Read()
    .WithSheet<OrderRecord>("Orders")
    .WithSheet<CustomerRecord>("Customers")
    .FromStream(stream);
```

### Read Configuration

The fluent builder exposes all read-time configuration options:

```csharp
List<OrderRecord> orders = Excel.Read<OrderRecord>()
    .FromSheet("Orders")
    .WithCulture("en-US")           // parse numbers/dates using this culture
    .WithNullValues("N/A", "-", "") // treat these cell values as null
    .SkipRows(2)                    // skip 2 rows before header
    .WithMaxRows(10_000)            // stop after 10 000 data rows
    .CaseSensitiveHeaders()         // "Id" != "ID"
    .AllowMissingColumns()          // columns absent from the sheet get default values
    .WithValidationMode(ValidationMode.Lenient) // skip [Validate] checks on read
    .FromFile("orders.xlsx");
```

To read a file without a header row, map columns by `[TabularMap(Index = N)]` and call `WithoutHeader()`:

```csharp
public class RowRecord
{
    [TabularMap(Index = 0)] public string Code { get; set; } = "";
    [TabularMap(Index = 1)] public decimal Value { get; set; }
}

List<RowRecord> records = Excel.Read<RowRecord>()
    .WithoutHeader()
    .FromFile("data.xlsx");
```

### OnError Callback (Reading)

By default, a row that fails to deserialize throws an exception immediately. Register an `OnError` handler to skip bad rows and continue reading.

```csharp
var errors = new List<string>();

List<OrderRecord> orders = Excel.Read<OrderRecord>()
    .OnError((context, ex) =>
    {
        errors.Add($"Row {context.Row} in '{context.SheetName}': {ex.Message}");
        return ExcelDeserializeErrorAction.SkipRecord;
    })
    .FromFile("orders.xlsx");

// Inspect skipped rows
foreach (var error in errors)
    Console.WriteLine(error);
```

The `ExcelDeserializeErrorContext` provided to the handler contains:

| Property | Description |
|---|---|
| `Row` | 1-based row number where the error occurred |
| `SheetName` | Name of the sheet being read |
| `FieldName` | Name of the property that failed (if available) |
| `RawValue` | Raw cell text that triggered the error (if available) |
| `TargetType` | The target property type (if available) |

Return `ExcelDeserializeErrorAction.Throw` to rethrow the exception instead of skipping.

### Progress Reporting (Reading)

Receive `ExcelProgress` notifications during large reads:

```csharp
var progress = new Progress<ExcelProgress>(p =>
    Console.WriteLine($"Read {p.RowsRead} rows from '{p.SheetName}'..."));

List<OrderRecord> orders = Excel.Read<OrderRecord>()
    .WithProgress(progress, intervalRows: 500)  // report every 500 rows (default: 1000)
    .FromFile("large_orders.xlsx");
```

### Custom Type Converters

Register a converter for types not handled natively:

```csharp
List<OrderRecord> orders = Excel.Read<OrderRecord>()
    .RegisterConverter<Money>(span =>
        Money.Parse(span, CultureInfo.InvariantCulture))
    .FromFile("orders.xlsx");
```

### Fluent Column Mapping (Reading)

Use `WithMap` or inline `Map()` calls to configure column bindings without attributes:

```csharp
var map = new CsvMap<OrderRecord>()
    .Map(o => o.Id, col => col.Index(0))
    .Map(o => o.CustomerName, col => col.Name("Customer Name"))
    .Map(o => o.Amount, col => col.Name("Total"));

List<OrderRecord> orders = Excel.Read<OrderRecord>()
    .WithMap(map)
    .FromFile("orders.xlsx");
```

Or use inline `Map()` calls directly on the builder:

```csharp
List<OrderRecord> orders = Excel.Read<OrderRecord>()
    .Map(o => o.Id, col => col.Index(0))
    .Map(o => o.CustomerName, col => col.Name("Customer Name"))
    .FromFile("orders.xlsx");
```

### DataReader

`Excel.CreateDataReader` wraps an Excel sheet in a `DbDataReader` for streaming bulk loads into a database. All cell values are exposed as strings.

```csharp
// Simple overload
using var reader = Excel.CreateDataReader("data.xlsx");
await SqlBulkCopy.WriteToServerAsync(reader);

// With sheet selection
using var reader = Excel.CreateDataReader("data.xlsx", sheetName: "Orders");

// From a stream
using var stream = File.OpenRead("data.xlsx");
using var reader = Excel.CreateDataReader(stream, hasHeaderRow: true, skipRows: 2);

// With full options
var options = new ExcelDataReaderOptions
{
    HasHeaderRow = true,
    SkipRows = 1,
    CaseSensitiveHeaders = false,
    AllowMissingColumns = true,
    NullValues = ["N/A", "NULL", ""],
    ColumnNames = ["Id", "Name", "Amount"]  // override header row names
};

using var reader = Excel.CreateDataReader("data.xlsx", options, sheetName: "Sheet2");
```

Always dispose the `ExcelDataReader`; it holds open the underlying file or stream.

---

## Writing

### Typed Record Writing

Write a collection of records to an Excel file using `Excel.Write<T>()`.

```csharp
var orders = new List<OrderRecord> { /* ... */ };

// Fluent builder
Excel.Write<OrderRecord>()
    .ToFile("orders.xlsx", orders);

// Write to stream
using var stream = File.Create("orders.xlsx");
Excel.Write<OrderRecord>()
    .ToStream(stream, orders);

// One-liner convenience methods (default options)
Excel.WriteToFile("orders.xlsx", orders);
Excel.WriteToStream(outputStream, orders);
```

### Sheet Naming

The default sheet name is `"Sheet1"`. Override it with `WithSheetName`:

```csharp
Excel.Write<OrderRecord>()
    .WithSheetName("Orders")
    .ToFile("report.xlsx", orders);
```

To suppress the header row:

```csharp
Excel.Write<OrderRecord>()
    .WithoutHeader()
    .ToFile("data.xlsx", orders);
```

### Multi-Sheet Writing

Write multiple typed sheets to a single workbook using `Excel.WriteMultiSheet()`. Sheets are written in the order they are registered.

```csharp
Excel.WriteMultiSheet()
    .WithSheet("Orders", orders)
    .WithSheet("Customers", customers)
    .WithSheet("Products", products)
    .ToFile("report.xlsx");

// In-memory
byte[] bytes = Excel.WriteMultiSheet()
    .WithSheet("Orders", orders)
    .WithSheet("Customers", customers)
    .ToBytes();

// Apply shared options to all sheets
var options = new ExcelWriteOptions { Culture = CultureInfo.GetCultureInfo("en-US") };

Excel.WriteMultiSheet()
    .WithOptions(options)
    .WithSheet("Orders", orders)
    .WithSheet("Customers", customers)
    .ToFile("report.xlsx");
```

### In-Memory Writing

Produce the `.xlsx` file as a `byte[]` for direct HTTP responses or downstream processing:

```csharp
byte[] bytes = Excel.Write<OrderRecord>()
    .WithSheetName("Orders")
    .ToBytes(orders);

// Return from an ASP.NET Core endpoint
return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "orders.xlsx");

// Convenience method
byte[] bytes = Excel.SerializeRecords(orders, sheetName: "Orders");
```

### Async Writing

All terminal write methods have async counterparts. The async variants run on a thread pool thread because the underlying ZIP format requires synchronous sequential writes.

```csharp
// Write IEnumerable async (file, stream, bytes)
await Excel.Write<OrderRecord>().ToFileAsync("orders.xlsx", orders, ct);
await Excel.Write<OrderRecord>().ToStreamAsync(stream, orders, leaveOpen: true, ct);
byte[] bytes = await Excel.Write<OrderRecord>().ToBytesAsync(orders, ct);

// Write IAsyncEnumerable (materializes the sequence first, then writes)
await Excel.Write<OrderRecord>().ToFileAsync("orders.xlsx", asyncOrders, ct);
await Excel.Write<OrderRecord>().ToStreamAsync(stream, asyncOrders, ct: ct);
byte[] bytes = await Excel.Write<OrderRecord>().ToBytesAsync(asyncOrders, ct);

// Async convenience methods
await Excel.WriteToFileAsync("orders.xlsx", orders);
await Excel.WriteToStreamAsync(stream, orders);
byte[] bytes = await Excel.SerializeRecordsAsync(orders);
```

> Note: `IAsyncEnumerable` overloads buffer the entire sequence in memory before writing, because the OOXML format cannot be written incrementally.

### OnError Callback (Writing)

Register an error handler for serialization failures that occur on individual cells. This handler applies to reflection-based writing only; source-generated writers skip it.

```csharp
var skippedRows = new List<int>();

Excel.Write<OrderRecord>()
    .OnError(ctx =>
    {
        Console.WriteLine(
            $"Sheet '{ctx.SheetName}', row {ctx.Row}, column {ctx.Column} " +
            $"({ctx.MemberName}): {ctx.Exception?.Message}");

        skippedRows.Add(ctx.Row);
        return ExcelSerializeErrorAction.SkipRow;
    })
    .ToFile("orders.xlsx", orders);
```

Available `ExcelSerializeErrorAction` values:

| Value | Behavior |
|---|---|
| `Throw` | Rethrow the exception (default when no handler is set) |
| `SkipRow` | Skip the entire row and continue with the next record |
| `WriteEmpty` | Write an empty cell for the failed field and continue the row |

The `ExcelSerializeErrorContext` passed to the handler contains:

| Property | Description |
|---|---|
| `Row` | 1-based row number |
| `Column` | 1-based column number |
| `MemberName` | Property name being serialized |
| `SourceType` | Declared type of the property |
| `Value` | The value that failed to serialize (may be null) |
| `Exception` | The exception, or null if the failure was not exception-based |
| `SheetName` | Name of the worksheet being written |

### Progress Reporting (Writing)

Receive `ExcelWriteProgress` notifications while writing large datasets:

```csharp
var progress = new Progress<ExcelWriteProgress>(p =>
    Console.WriteLine($"Written {p.RowsWritten} rows to '{p.SheetName}'..."));

Excel.Write<OrderRecord>()
    .WithProgress(progress, intervalRows: 2000)  // report every 2000 rows (default: 1000)
    .ToFile("large_orders.xlsx", orders);
```

### Output Size Limit

Protect against excessively large outputs (for example when writing untrusted data) using `WithMaxOutputSize`. An `ExcelException` is thrown if the uncompressed worksheet XML exceeds the limit.

```csharp
Excel.Write<OrderRecord>()
    .WithMaxOutputSize(50 * 1024 * 1024)  // 50 MB limit
    .ToFile("orders.xlsx", orders);
```

### Fluent Column Mapping (Writing)

Override the default attribute-based mapping with a `CsvMap<T>` or compatible `ICsvWriteMapSource<T>`:

```csharp
var map = new CsvMap<OrderRecord>()
    .Map(o => o.Id, col => col.Name("Order ID"))
    .Map(o => o.CustomerName, col => col.Name("Customer"));

Excel.Write<OrderRecord>()
    .WithMap(map)
    .ToFile("orders.xlsx", orders);
```

### Write Formatting Options

Control how values are formatted using the builder's formatting methods:

```csharp
Excel.Write<OrderRecord>()
    .WithCulture("de-DE")                      // culture for all formatting
    .WithNullValue("N/A")                      // text to write for null values
    .WithDateTimeFormat("yyyy-MM-dd HH:mm:ss") // string format (stored as shared string)
    .WithDateOnlyFormat("yyyy-MM-dd")          // string format (stored as shared string)
    .WithTimeOnlyFormat("HH:mm:ss")            // string format for TimeOnly
    .WithNumberFormat("N2")                    // numeric format string
    .WithMaxRowCount(50_000)                   // stop after N data rows
    .WithValidationMode(ValidationMode.Strict) // enforce [Validate] attributes
    .ToFile("orders.xlsx", orders);
```

When `WithDateTimeFormat` / `WithDateOnlyFormat` is **not** set, date values are stored as OA date serial numbers with an Excel date style applied, which allows Excel to render them as native dates. When a format string **is** set, the value is written as a formatted string in the shared string table.

---

## Options Reference

### ExcelReadOptions

`ExcelReadOptions` is a sealed record type used when passing configuration outside the fluent builder.

| Property | Type | Default | Description |
|---|---|---|---|
| `HasHeaderRow` | `bool` | `true` | Whether the first data row is a header row |
| `CaseSensitiveHeaders` | `bool` | `false` | Whether header name matching is case-sensitive |
| `AllowMissingColumns` | `bool` | `false` | Whether columns absent from the sheet get default values |
| `NullValues` | `IReadOnlyList<string>?` | `null` | Cell text values treated as null |
| `Culture` | `CultureInfo` | `InvariantCulture` | Culture used to parse cell values |
| `MaxRows` | `int?` | `null` | Maximum data rows to read (unlimited when null) |
| `SkipRows` | `int` | `0` | Number of rows to skip before header/data |
| `ValidationMode` | `ValidationMode` | `Strict` | Whether `[Validate]` rules are enforced |
| `OnDeserializeError` | `ExcelDeserializeErrorHandler?` | `null` | Callback for deserialization errors |

Use `ExcelReadOptions.Default` for the zero-allocation default instance.

### ExcelWriteOptions

`ExcelWriteOptions` is a sealed record type. It is thread-safe after construction.

| Property | Type | Default | Description |
|---|---|---|---|
| `Culture` | `CultureInfo` | `InvariantCulture` | Culture used for value formatting |
| `NullValue` | `string` | `""` | Text written for null values |
| `DateTimeFormat` | `string?` | `null` | Format string for `DateTime` (null = OA date serial) |
| `DateOnlyFormat` | `string?` | `null` | Format string for `DateOnly` (null = OA date serial) |
| `TimeOnlyFormat` | `string?` | `null` | Format string for `TimeOnly` (null = culture default) |
| `NumberFormat` | `string?` | `null` | Format string for numeric values |
| `MaxRowCount` | `int?` | `null` | Maximum data rows to write (unlimited when null) |
| `ValidationMode` | `ValidationMode` | `Strict` | Whether `[Validate]` rules are enforced on write |
| `WriteHeader` | `bool` | `true` | Whether to write a header row |
| `OnSerializeError` | `ExcelSerializeErrorHandler?` | `null` | Callback for serialization errors |
| `MaxOutputSize` | `long?` | `null` | Maximum uncompressed XML bytes (null = no limit) |
| `WriteProgress` | `IProgress<ExcelWriteProgress>?` | `null` | Progress reporter |
| `WriteProgressIntervalRows` | `int` | `1000` | Row interval between progress reports |

Use `ExcelWriteOptions.Default` for the zero-allocation default instance.

### ExcelDataReaderOptions

`ExcelDataReaderOptions` is a sealed record type for configuring `Excel.CreateDataReader`.

| Property | Type | Default | Description |
|---|---|---|---|
| `HasHeaderRow` | `bool` | `true` | Whether the first data row contains column headers |
| `CaseSensitiveHeaders` | `bool` | `false` | Whether header lookup is case-sensitive |
| `AllowMissingColumns` | `bool` | `false` | Whether rows with fewer columns than header are tolerated |
| `NullValues` | `IReadOnlyList<string>?` | `null` | Cell values exposed as `DBNull.Value` |
| `ColumnNames` | `IReadOnlyList<string>?` | `null` | Explicit column names overriding header row values |
| `SkipRows` | `int` | `0` | Rows to skip before reading header or data |

Use `ExcelDataReaderOptions.Default` for the zero-allocation default instance.

---

## Source Generators

Excel reading and writing participate in the same `[GenerateBinder]` source generator system as CSV. Add the attribute to your record type to get an AOT-safe, allocation-free binder:

```csharp
[GenerateBinder]
public class OrderRecord
{
    [TabularMap(Name = "Order ID")]
    public int Id { get; set; }

    [TabularMap(Name = "Customer")]
    public string CustomerName { get; set; } = "";

    [TabularMap(Name = "Total")]
    [Validate(RangeMin = 0)]
    public decimal Amount { get; set; }

    [TabularMap(Name = "Date")]
    [Parse(Format = "yyyy-MM-dd")]
    public DateOnly OrderDate { get; set; }
}
```

With the generated binder registered, all `Excel.Read<OrderRecord>()` and `Excel.Write<OrderRecord>()` calls use the generated code path automatically. No further changes to calling code are required.

When `[GenerateBinder]` is absent, HeroParser falls back to a reflection-based binder at runtime. This is annotated with `[RequiresUnreferencedCode]` and `[RequiresDynamicCode]`, so AOT builds require the attribute.

---

## Excel-Specific Details

### Zero-Dependency Design

HeroParser's Excel support uses only assemblies that ship with the .NET runtime:

- `System.IO.Compression` — reads and writes the ZIP container of `.xlsx` files
- `System.Xml` — parses the XML parts inside the ZIP (worksheet, shared strings, workbook)

No third-party library (EPPlus, ClosedXML, NPOI, etc.) is referenced.

### Shared String Table

Excel stores repeated strings in a shared string table to reduce file size. HeroParser's writer automatically deduplicates string cells into the shared string table. Numeric and date serial values are written as inline numeric cells, not as strings, which preserves native Excel sorting and filtering behaviour.

### OA Date Format Handling

When no date format string is configured, `DateTime` and `DateOnly` values are stored as OA (OLE Automation) date serial numbers with a built-in Excel date style applied. This makes the cells behave as native date cells in Excel. When a format string such as `"yyyy-MM-dd"` is set via `WithDateOnlyFormat`, the value is formatted as a string and stored in the shared string table — useful when you want a consistent text representation but do not need Excel date semantics.

### Sheet Name Validation

Excel sheet names must satisfy these constraints (enforced by `ArgumentException.ThrowIfNullOrEmpty` and OOXML rules):

- Must not be empty or null
- Must not exceed 31 characters
- Must not contain the characters `\ / ? * [ ]`
- Must not begin or end with a single quote (`'`)

Violating these constraints when calling `WithSheetName` or registering a sheet in `WriteMultiSheet` will produce an exception before any bytes are written.
