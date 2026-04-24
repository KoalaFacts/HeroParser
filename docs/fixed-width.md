[Back to README](../README.md)

# Fixed-Width File Parsing & Writing

HeroParser provides comprehensive support for fixed-width (fixed-length) file parsing and writing, commonly used in legacy systems, mainframe exports, and financial data interchange.

## Table of Contents

- [Reading](#reading)
  - [Typed Record Reading](#typed-record-reading)
  - [Row-Level Reading (Untyped)](#row-level-reading-untyped)
  - [Async Reading](#async-reading)
  - [PipeReader Integration](#pipereader-integration)
  - [DataReader for Database Loading](#datareader-for-database-loading)
  - [ForEach (Object Reuse)](#foreach-object-reuse)
- [Reading Configuration](#reading-configuration)
  - [Parser Options](#parser-options)
  - [Record Options](#record-options)
  - [Inline Map](#inline-map)
  - [FixedWidthMap Class](#fixedwidthmap-class)
  - [Validation Mode](#validation-mode)
  - [Progress Reporting](#progress-reporting)
  - [Custom Type Converters](#custom-type-converters)
- [Writing](#writing)
  - [Typed Record Writing](#typed-record-writing)
  - [Manual Row-by-Row Writing](#manual-row-by-row-writing)
  - [Async Writing](#async-writing)
- [Writing Configuration](#writing-configuration)
- [Field Alignment and Padding](#field-alignment-and-padding)
- [Attributes Reference](#attributes-reference)
  - [PositionalMap Attribute](#positionalmap-attribute)
  - [Validation Attributes](#validation-attributes)
- [Options Reference](#options-reference)
  - [FixedWidthReadOptions](#fixedwidthreadoptions)
  - [FixedWidthWriteOptions](#fixedwidthwriteoptions)
  - [FixedWidthDataReaderOptions](#fixedwidthdatareaderoptions)
- [Format Converters](#format-converters)
- [Source Generators](#source-generators)

---

## Reading

### Typed Record Reading

Annotate record properties with `[PositionalMap]` to declare field positions, then call `FixedWidth.Read<T>()`. (Migrating from 1.x? See [migration-v1-to-v2.md](migration-v1-to-v2.md).)

```csharp
[GenerateBinder]
public class Employee
{
    [PositionalMap(Start = 0, Length = 10)]
    public string Id { get; set; } = "";

    [PositionalMap(Start = 10, Length = 30)]
    public string Name { get; set; } = "";

    [PositionalMap(Start = 40, Length = 10, Alignment = FieldAlignment.Right, PadChar = '0')]
    public decimal Salary { get; set; }
}

// Read from file
foreach (var emp in FixedWidth.Read<Employee>().FromFile("employees.dat"))
{
    Console.WriteLine($"{emp.Name}: {emp.Salary:C}");
}

// Read from string
var records = FixedWidth.Read<Employee>().FromText(data).ToList();

// Read from stream
var records = FixedWidth.Read<Employee>().FromStream(stream).ToList();
```

The `FromFile` and `FromStream` terminal methods return `FixedWidthReadResult<T>`, which implements `IEnumerable<T>`. Validation errors collected during iteration are available via `result.Errors`.

### Row-Level Reading (Untyped)

When you need raw field access without binding to a type, use the non-generic overload:

```csharp
// Line-based (default): each line is one record
foreach (var row in FixedWidth.Read().FromFile("data.dat"))
{
    var id   = row.GetField(0, 10).ToString();
    var name = row.GetField(10, 30).ToString();
    Console.WriteLine($"{id}: {name}");
}

// Fixed-length blocks: records are exactly 80 characters, no newline dependence
foreach (var row in FixedWidth.Read()
    .WithRecordLength(80)
    .WithDefaultPadChar(' ')
    .FromFile("legacy.dat"))
{
    var id   = row.GetField(0, 10).ToString();
    var name = row.GetField(10, 30).ToString();
}
```

`FixedWidthCharSpanRow.GetField(start, length)` returns a `ReadOnlySpan<char>` with no heap allocation.

### Async Reading

```csharp
// Async file reading with IAsyncEnumerable
await foreach (var emp in FixedWidth.Read<Employee>().FromFileAsync("data.dat"))
{
    Console.WriteLine(emp.Name);
}

// Async stream reading
await foreach (var emp in FixedWidth.Read<Employee>()
    .FromStreamAsync(stream, cancellationToken))
{
    Process(emp);
}
```

### PipeReader Integration

For streaming from network sockets or HTTP pipelines:

```csharp
await foreach (var emp in FixedWidth.Read<Employee>()
    .FromPipeReaderAsync(pipeReader, cancellationToken))
{
    await ProcessAsync(emp);
}
```

### DataReader for Database Loading

Use `FixedWidth.CreateDataReader` to produce an `IDataReader` / `DbDataReader` suitable for `SqlBulkCopy` or similar database bulk-load APIs:

```csharp
var columns = new[]
{
    new FixedWidthDataReaderColumn { Start = 0,  Length = 10, Name = "Id"     },
    new FixedWidthDataReaderColumn { Start = 10, Length = 30, Name = "Name"   },
    new FixedWidthDataReaderColumn { Start = 40, Length = 10, Name = "Salary",
        Alignment = FieldAlignment.Right, PadChar = '0' },
};

using var reader = FixedWidth.CreateDataReader("employees.dat", new FixedWidthDataReaderOptions
{
    Columns         = columns,
    HasHeaderRow    = false,
    AllowMissingColumns = false,
    NullValues      = ["NULL", "N/A"],
});

using var bulkCopy = new SqlBulkCopy(connection);
await bulkCopy.WriteToServerAsync(reader);
```

`FixedWidthDataReaderOptions` accepts explicit `ColumnNames` to override header values and supports `CaseSensitiveHeaders` for header-name lookup.

### ForEach (Object Reuse)

For maximum throughput with large files, `ForEach` methods reuse a single record instance per iteration, eliminating per-row allocations. **Do not store the instance** — copy needed values inside the callback.

```csharp
var totals = new Dictionary<string, decimal>();

FixedWidth.Read<Employee>()
    .ForEachFromFile("employees.dat", emp =>
    {
        // Copy values immediately; emp is reused on the next iteration
        totals[emp.Department] = totals.GetValueOrDefault(emp.Department) + emp.Salary;
    });
```

`ForEachFromText` and `ForEachFromFile` are the available variants. `ForEach` is not supported when inline `Map<>()` or `WithMap()` is configured; use `FromFile()` in those cases.

---

## Reading Configuration

All options are set via chainable methods on `FixedWidthReaderBuilder<T>`.

### Parser Options

```csharp
var records = FixedWidth.Read<Employee>()
    // Record mode
    .WithRecordLength(80)                          // Fixed-length blocks (null = line-based)
    .LineBased()                                   // Explicit line-based mode (default)

    // Padding / alignment defaults (overridable per field via attribute)
    .WithDefaultPadChar(' ')
    .WithDefaultAlignment(FieldAlignment.Left)

    // Row filtering
    .SkipRows(2)                                   // Skip N rows before data (metadata, etc.)
    .WithHeader()                                  // Treat first data row as header (discarded)
    .SkipEmptyLines()                              // Skip blank lines (default: true)
    .IncludeEmptyLines()                           // Keep blank lines as records
    .WithCommentCharacter('#')                     // Lines starting with '#' are skipped

    // Tolerance
    .AllowShortRows()                              // Short rows yield empty values; alias: AllowMissingColumns()

    // Limits (DoS protection)
    .WithMaxRecords(10_000)
    .WithMaxInputSize(50 * 1024 * 1024)            // 50 MB; null = unlimited

    // Diagnostics
    .TrackLineNumbers()                            // Populates FixedWidthCharSpanRow.SourceLineNumber

    .FromFile("data.dat")
    .ToList();
```

### Record Options

```csharp
var records = FixedWidth.Read<Employee>()
    .WithCulture("de-DE")                          // Culture for numeric / date parsing
    .WithNullValues("NULL", "N/A", "")             // String values treated as null
    .WithEncoding(Encoding.Latin1)                 // File encoding (default: UTF-8)
    .OnError((ctx, ex) =>                          // Deserialization error handler
    {
        Console.Error.WriteLine($"Row {ctx.RecordNumber}: {ex.Message}");
        return FixedWidthDeserializeErrorAction.SkipRecord;
    })
    .FromFile("data.dat")
    .ToList();
```

### Inline Map

The `Map<TProperty>()` method lets you configure field positions inline on the builder — without attributes and without a separate map class. This is useful for dynamic schemas or when annotating the record type is not feasible.

```csharp
var records = FixedWidth.Read<Employee>()
    .Map(e => e.Id,     b => b.Start(0).Length(10))
    .Map(e => e.Name,   b => b.Start(10).Length(30))
    .Map(e => e.Salary, b => b.Start(40).End(50).Alignment(FieldAlignment.Right).PadChar('0'))
    .FromFile("employees.dat")
    .ToList();
```

`FixedWidthFieldBuilder` supports:

| Method | Description |
|---|---|
| `.Start(int)` | 0-based start character position (required) |
| `.Length(int)` | Field length in characters |
| `.End(int)` | Exclusive end position; overrides `Length` when both are set |
| `.PadChar(char)` | Per-field padding character |
| `.Alignment(FieldAlignment)` | Per-field alignment |
| `.WithHeaderName(string)` | Expected column name when a header row is present |

`Map<>()` and `WithMap()` are mutually exclusive — using both on the same builder throws `InvalidOperationException`.

Inline `Map<>()` uses reflection and expression compilation; it is not AOT/trim-safe. Use `[GenerateBinder]` for AOT scenarios.

#### Header Validation with Inline Map

When the file contains a header row, you can validate that each field position matches the expected column name:

```csharp
var records = FixedWidth.Read<Employee>()
    .WithHeader()                                  // First row is the header
    .CaseSensitiveHeaders()                        // Default is case-insensitive
    .Map(e => e.Id,   b => b.Start(0).Length(10).WithHeaderName("EMP_ID"))
    .Map(e => e.Name, b => b.Start(10).Length(30).WithHeaderName("EMP_NAME"))
    .FromFile("employees.dat")
    .ToList();
```

If the actual header value at the field's position does not match `WithHeaderName`, an `InvalidOperationException` is thrown with a descriptive message.

`CaseSensitiveHeaders()` enables case-sensitive comparison (default is case-insensitive). It only has effect when `WithHeader()` is also set and at least one `Map<>()` entry has a `WithHeaderName` configured.

### FixedWidthMap Class

For shared or reusable mappings — especially when you need both reading and writing from the same definition — extend `FixedWidthMap<T>`:

```csharp
public class EmployeeMap : FixedWidthMap<Employee>
{
    public EmployeeMap()
    {
        Map(e => e.Id,     c => c.Start(0).Length(10));
        Map(e => e.Name,   c => c.Start(10).Length(30));
        Map(e => e.Salary, c => c.Start(40).Length(10).Alignment(FieldAlignment.Right).PadChar('0'));
    }
}

// Reading
var records = FixedWidth.Read<Employee>()
    .WithMap(new EmployeeMap())
    .FromFile("employees.dat")
    .ToList();

// Writing
FixedWidth.Write<Employee>()
    .WithMap(new EmployeeMap())
    .ToFile("out.dat", records);
```

`FixedWidthMap<T>` requires `T : class, new()`. For structs or records, use `Map<TProperty>()` inline on the builder.

### Validation Mode

```csharp
// Strict (default): throws ValidationException after iteration if any errors were collected
var result = FixedWidth.Read<Employee>()
    .WithValidationMode(ValidationMode.Strict)
    .FromFile("data.dat");

foreach (var emp in result) { /* ... */ }
// throws ValidationException here if any row failed validation

// Lenient: invalid rows are silently excluded; no exception is thrown
var result = FixedWidth.Read<Employee>()
    .WithValidationMode(ValidationMode.Lenient)
    .FromFile("data.dat");

var employees = result.ToList();
var errors    = result.Errors;  // Inspect what was skipped
```

### Progress Reporting

```csharp
var progress = new Progress<FixedWidthProgress>(p =>
    Console.WriteLine($"Processed {p.RecordsRead} records ({p.BytesRead:N0} bytes)"));

var records = FixedWidth.Read<Employee>()
    .WithProgress(progress, intervalRows: 5_000)   // Report every 5 000 rows
    .FromFile("large.dat")
    .ToList();
```

### Custom Type Converters

Register a converter for any type not natively supported. The converter receives the raw `ReadOnlySpan<char>`, the active culture, and the optional format string from `[Parse(Format = "...")]`.

```csharp
var records = FixedWidth.Read<Order>()
    .RegisterConverter<Money>((value, culture, format, out result) =>
    {
        if (decimal.TryParse(value, NumberStyles.Currency, culture, out var amount))
        {
            result = new Money(amount);
            return true;
        }
        result = default;
        return false;
    })
    .FromFile("orders.dat")
    .ToList();
```

---

## Writing

### Typed Record Writing

```csharp
// Write to string
var text = FixedWidth.Write<Employee>()
    .WithPadChar(' ')
    .AlignLeft()
    .ToText(employees);

// Write to file
FixedWidth.Write<Employee>()
    .WithNewLine("\r\n")
    .ToFile("output.dat", employees);

// Write to stream
FixedWidth.Write<Employee>()
    .ToStream(stream, employees);

// Write to TextWriter
FixedWidth.Write<Employee>()
    .ToWriter(textWriter, employees);
```

### Manual Row-by-Row Writing

Use `FixedWidth.Write()` (non-generic) to create a `FixedWidthStreamWriter` for full control over each field and row:

```csharp
using var writer = FixedWidth.Write()
    .WithPadChar(' ')
    .CreateFileWriter("output.dat");

// Write header row
writer.WriteField("ID",     10);
writer.WriteField("NAME",   30);
writer.WriteField("AMOUNT", 10, FieldAlignment.Right);
writer.EndRow();

// Write data rows
writer.WriteField("001",   10);
writer.WriteField("Alice", 30);
writer.WriteField("12345", 10, FieldAlignment.Right, '0');
writer.EndRow();

writer.Flush();
```

Alternatively, create the writer from a `TextWriter` or `Stream` directly:

```csharp
// From TextWriter
using var writer = FixedWidth.Write().CreateWriter(Console.Out);

// From stream
using var writer = FixedWidth.Write().CreateStreamWriter(stream);
```

### Async Writing

```csharp
// Async file write (IAsyncEnumerable)
await FixedWidth.Write<Employee>()
    .ToFileAsync("output.dat", asyncRecords, cancellationToken);

// Async file write (IEnumerable)
await FixedWidth.Write<Employee>()
    .ToFileAsync("output.dat", employees, cancellationToken);

// Async stream write
await FixedWidth.Write<Employee>()
    .ToStreamAsync(stream, employees, leaveOpen: true, cancellationToken);

// Async string output
var text = await FixedWidth.Write<Employee>()
    .ToTextAsync(asyncRecords, cancellationToken);
```

For true async row-by-row writing using `FixedWidthAsyncStreamWriter` (sync fast paths when data fits the buffer):

```csharp
await using var writer = FixedWidth.CreateAsyncStreamWriter(stream);
await writer.WriteFieldAsync("Alice", 20);
await writer.WriteFieldAsync("30",    5, FieldAlignment.Right);
await writer.EndRowAsync();
await writer.FlushAsync();
```

---

## Writing Configuration

All options are set via chainable methods on `FixedWidthWriterBuilder<T>`:

```csharp
FixedWidth.Write<Employee>()
    // Row formatting
    .WithNewLine("\r\n")                           // Row separator (default: CRLF)
    .WithPadChar(' ')                              // Default pad character
    .WithAlignment(FieldAlignment.Left)            // Default alignment; aliases: AlignLeft(), AlignRight()

    // Value formatting
    .WithCulture("en-US")
    .WithNullValue("")                             // Written for null properties
    .WithDateTimeFormat("yyyyMMdd HHmmss")
    .WithDateOnlyFormat("yyyyMMdd")
    .WithTimeOnlyFormat("HHmmss")
    .WithNumberFormat("N2")

    // Overflow handling
    .WithOverflowBehavior(OverflowBehavior.Truncate)  // Aliases: TruncateOnOverflow(), ThrowOnOverflow()

    // Error handling
    .OnError(ctx =>
    {
        Console.Error.WriteLine($"Row {ctx.Row}, field '{ctx.MemberName}': {ctx.Exception?.Message}");
        return FixedWidthSerializeErrorAction.WriteEmpty;
    })

    // Validation
    .WithValidationMode(ValidationMode.Strict)

    // Limits (DoS protection)
    .WithMaxRowCount(1_000_000)
    .WithMaxOutputSize(500 * 1024 * 1024L)         // 500 MB

    .ToFile("output.dat", employees);
```

---

## Field Alignment and Padding

Four alignment modes control how padding is applied on write and trimmed on read:

| Mode | Write | Read (trim) |
|---|---|---|
| `Left` (default) | Value padded on the right | Trailing pad characters removed |
| `Right` | Value padded on the left | Leading pad characters removed |
| `Center` | Value padded on both sides | Pad characters trimmed from both ends |
| `None` | No padding; value written as-is | No trimming |

```csharp
public class Transaction
{
    // "John      " -> "John"
    [PositionalMap(Start = 0,  Length = 10, Alignment = FieldAlignment.Left)]
    public string Name { get; set; } = "";

    // "000012345" -> "12345"
    [PositionalMap(Start = 10, Length = 10, Alignment = FieldAlignment.Right, PadChar = '0')]
    public int Amount { get; set; }

    // "  Data  " -> "Data"
    [PositionalMap(Start = 20, Length = 10, Alignment = FieldAlignment.Center)]
    public string Code { get; set; } = "";

    // "  raw  " -> "  raw  " (no trimming)
    [PositionalMap(Start = 30, Length = 10, Alignment = FieldAlignment.None)]
    public string RawField { get; set; } = "";
}
```

**Overflow behavior** (write side): when a formatted value is longer than the field width, the default behavior is to truncate silently. Use `.ThrowOnOverflow()` (or `OverflowBehavior.Throw`) to detect data loss.

---

## Attributes Reference

### PositionalMap Attribute

`[PositionalMap]` declares a property's position in the fixed-width record.

```csharp
[PositionalMap(Start = 0, Length = 10)]
public string Id { get; set; } = "";

// End property as alternative to Length (exclusive upper bound)
[PositionalMap(Start = 10, End = 40)]
public string Name { get; set; } = "";

// Numeric field padded with zeros, right-aligned
[PositionalMap(Start = 40, Length = 10, Alignment = FieldAlignment.Right, PadChar = '0')]
public decimal Salary { get; set; }

// Date format applied on read
[PositionalMap(Start = 50, Length = 8)]
[Parse(Format = "yyyyMMdd")]
public DateTime TransactionDate { get; set; }
```

| Parameter | Type | Default | Description |
|---|---|---|---|
| `Start` | `int` | required | 0-based start character index |
| `Length` | `int` | — | Field length; mutually exclusive with `End` |
| `End` | `int` | — | Exclusive end index; `End - Start` gives the length |
| `PadChar` | `char` | `' '` | Padding character |
| `Alignment` | `FieldAlignment` | `Left` | Alignment mode |

When both `Length` and `End` are specified, `End` takes precedence.

A single property can have both `[PositionalMap]` and `[TabularMap]`, enabling a class to be read from both fixed-width and CSV/Excel sources.

### Validation Attributes

Apply `[Validate]` to declare constraints that are enforced on both read and write:

```csharp
[GenerateBinder]
public class ValidatedRecord
{
    [PositionalMap(Start = 0,  Length = 10)]
    [Validate(NotNull = true, NotEmpty = true)]
    public string Id { get; set; } = "";

    [PositionalMap(Start = 10, Length = 20)]
    [Validate(MinLength = 2, MaxLength = 20)]
    public string Name { get; set; } = "";

    [PositionalMap(Start = 30, Length = 10)]
    [Validate(RangeMin = 0, RangeMax = 1_000_000)]
    public decimal Amount { get; set; }

    [PositionalMap(Start = 40, Length = 15)]
    [Validate(Pattern = @"^\d{3}-\d{3}-\d{4}$")]
    public string Phone { get; set; } = "";
}
```

---

## Options Reference

### FixedWidthReadOptions

| Property | Type | Default | Description |
|---|---|---|---|
| `RecordLength` | `int?` | `null` | Fixed block length; `null` = line-based |
| `DefaultPadChar` | `char` | `' '` | Global padding character |
| `DefaultAlignment` | `FieldAlignment` | `Left` | Global alignment |
| `MaxRecordCount` | `int` | `100_000` | Maximum records to parse |
| `TrackSourceLineNumbers` | `bool` | `false` | Populate `SourceLineNumber` on rows |
| `SkipEmptyLines` | `bool` | `true` | Skip blank lines (line-based mode only) |
| `AllowShortRows` | `bool` | `false` | Fields past row end return empty; alias `AllowMissingColumns` |
| `CommentCharacter` | `char?` | `null` | Lines starting with this char are skipped |
| `HasHeaderRow` | `bool` | `false` | First data row is a header (discarded or validated) |
| `CaseSensitiveHeaders` | `bool` | `false` | Case-sensitive header name matching |
| `SkipRows` | `int` | `0` | Rows to skip before header or data |
| `MaxInputSize` | `long?` | `104_857_600` (100 MB) | Max file/stream size; `null` = unlimited |
| `ValidationMode` | `ValidationMode` | `Strict` | Error surfacing behavior |

### FixedWidthWriteOptions

| Property | Type | Default | Description |
|---|---|---|---|
| `NewLine` | `string` | `"\r\n"` | Row separator (CR, LF, or CRLF only) |
| `DefaultPadChar` | `char` | `' '` | Global padding character |
| `DefaultAlignment` | `FieldAlignment` | `Left` | Global alignment |
| `Culture` | `CultureInfo` | `InvariantCulture` | Formatting culture |
| `NullValue` | `string` | `""` | String written for null properties |
| `DateTimeFormat` | `string?` | `null` | DateTime format string |
| `DateOnlyFormat` | `string?` | `null` | DateOnly format string |
| `TimeOnlyFormat` | `string?` | `null` | TimeOnly format string |
| `NumberFormat` | `string?` | `null` | Numeric format string |
| `OverflowBehavior` | `OverflowBehavior` | `Truncate` | `Truncate` or `Throw` on field overflow |
| `OnSerializeError` | `FixedWidthSerializeErrorHandler?` | `null` | Error callback; return `Throw`, `SkipRow`, or `WriteEmpty` |
| `ValidationMode` | `ValidationMode` | `Strict` | Validation on write |
| `MaxRowCount` | `int?` | `null` | Maximum rows to write |
| `MaxOutputSize` | `long?` | `null` | Maximum output size in characters |

### FixedWidthDataReaderOptions

| Property | Type | Default | Description |
|---|---|---|---|
| `Columns` | `IReadOnlyList<FixedWidthDataReaderColumn>` | `[]` | Column definitions (at least one required) |
| `HasHeaderRow` | `bool` | `false` | First parsed row is used for column names |
| `CaseSensitiveHeaders` | `bool` | `false` | Case-sensitive header name lookup |
| `SkipRows` | `int` | `0` | Rows to skip before header or data |
| `AllowMissingColumns` | `bool` | `false` | Missing columns return `DBNull.Value` |
| `NullValues` | `IReadOnlyList<string>?` | `null` | String values treated as SQL `NULL` |
| `ColumnNames` | `IReadOnlyList<string>?` | `null` | Explicit column names (overrides header/definition names) |

`FixedWidthDataReaderColumn` properties: `Start` (int), `Length` (int), `Name` (string?), `PadChar` (char?), `Alignment` (FieldAlignment?).

---

## Format Converters

### Fixed-Width to CSV

```csharp
var columns = new[]
{
    new FixedWidthFieldDefinition("Id",     10),
    new FixedWidthFieldDefinition("Name",   30),
    new FixedWidthFieldDefinition("Salary", 10, FieldAlignment.Right, '0'),
};

string csv = FixedWidthToCsvConverter.Convert(fixedWidthData, columns, new FixedWidthToCsvOptions
{
    Delimiter     = ',',
    IncludeHeader = true,
    NewLine       = "\r\n",
});
```

### CSV to Fixed-Width

```csharp
string fixedWidth = CsvToFixedWidthConverter.Convert(csvData, columns, new CsvToFixedWidthOptions
{
    Delimiter = ',',
    NewLine   = "\r\n",
});
```

Both converters operate on `IReadOnlyList<FixedWidthFieldDefinition>`. Each `FixedWidthFieldDefinition` takes `(string name, int width, FieldAlignment alignment = Left, char padChar = ' ')`.

---

## Source Generators

Add `[GenerateBinder]` to your record class to have the source generator emit a compile-time binder.

```csharp
[GenerateBinder]
public class Employee
{
    [PositionalMap(Start = 0,  Length = 10)]
    public string Id { get; set; } = "";

    [PositionalMap(Start = 10, Length = 30)]
    public string Name { get; set; } = "";

    [PositionalMap(Start = 40, Length = 10, Alignment = FieldAlignment.Right, PadChar = '0')]
    public decimal Salary { get; set; }
}
```

The generated binder provides:

- **AOT compatibility** — no runtime reflection
- **Faster startup** — binders are pre-compiled at build time
- **Trimming-safe** — works with `dotnet publish --self-contained`

The source generator ships inside the main `HeroParser` package; no separate `HeroParser.Generators` NuGet reference is needed.

For scenarios where attributes are not feasible (e.g., third-party types), use the runtime `FixedWidthMap<T>` class or the inline `Map<TProperty>()` builder methods — both work without `[GenerateBinder]` but are not AOT-safe.
