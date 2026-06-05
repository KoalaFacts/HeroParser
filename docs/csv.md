# CSV API Reference

[Back to README](../README.md)

HeroParser provides a high-performance, zero-allocation CSV parser and writer for .NET 8, 9, and 10. The primary entry point is the `Csv` static class.

---

## Table of Contents

1. [CSV Reading](#1-csv-reading)
   - [Basic row iteration (zero allocations)](#11-basic-row-iteration-zero-allocations)
   - [Files and streams](#12-files-and-streams)
   - [Async I/O](#13-async-io)
   - [Typed record reading with fluent builder](#14-typed-record-reading-with-fluent-builder)
   - [LINQ extensions](#15-linq-extensions)
   - [Multi-schema (discriminator-based routing)](#16-multi-schema-discriminator-based-routing)
   - [DataReader for database bulk loading](#17-datareader-for-database-bulk-loading)
   - [OnError callback](#18-onerror-callback)
   - [Progress reporting](#19-progress-reporting)
   - [Custom type converters](#110-custom-type-converters)
2. [CSV Writing](#2-csv-writing)
   - [Typed record writing](#21-typed-record-writing)
   - [Manual row writing (CsvStreamWriter)](#22-manual-row-writing-csvstreamwriter)
   - [Async writing](#23-async-writing)
   - [Quote styles](#24-quote-styles)
   - [CSV injection protection](#25-csv-injection-protection)
   - [OnSerializeError](#26-onserializeerror)
   - [Progress reporting](#27-progress-reporting)
3. [Options Reference](#3-options-reference)
   - [CsvReadOptions](#31-csvreadoptions)
   - [CsvWriteOptions](#32-csvwriteoptions)
   - [CsvRecordOptions](#33-csvrecordoptions)
   - [CsvDataReaderOptions](#34-csvdatareaderoptions)
4. [Detection and Validation](#4-detection-and-validation)
   - [Delimiter detection](#41-delimiter-detection)
   - [CSV structural validation](#42-csv-structural-validation)
   - [Schema inference](#43-schema-inference)
5. [Security](#5-security)
   - [CSV injection protection modes](#51-csv-injection-protection-modes)
   - [DoS protection](#52-dos-protection)
6. [Advanced](#6-advanced)
   - [SIMD pipeline details](#61-simd-pipeline-details)
   - [Span-based parsing](#62-span-based-parsing)
   - [PipeReader integration](#63-pipereader-integration)
   - [Hardware diagnostics](#64-hardware-diagnostics)
   - [RFC 4180 compliance notes](#65-rfc-4180-compliance-notes)
7. [Source Generators](#7-source-generators)
   - [[GenerateBinder] attribute](#71-generatebinder-attribute)
   - [Generated binder vs reflection binder](#72-generated-binder-vs-reflection-binder)
   - [Multi-schema dispatcher [CsvGenerateDispatcher]](#73-multi-schema-dispatcher-csvgeneratedispatcher)
8. [Fluent Mapping](#8-fluent-mapping)
   - [WithMap() for read](#81-withmap-for-read)
   - [WithMap() for write](#82-withmap-for-write)
   - [Map<TProperty>() inline mapping](#83-maptproperty-inline-mapping)

---

## 1. CSV Reading

### 1.1 Basic row iteration (zero allocations)

The lowest-level API reads CSV row by row as a ref struct. Columns are parsed lazily — only when accessed. Allocations are zero for invariant primitive types on the UTF-8 path.

```csharp
foreach (var row in Csv.ReadFromText(csv))
{
    // Access columns by index — no allocations for primitive types
    var id    = row[0].Parse<int>();
    var name  = row[1].CharSpan;       // ReadOnlySpan<char>
    var price = row[2].Parse<decimal>();
}
```

**Type parsing methods on a column:**

```csharp
foreach (var row in Csv.ReadFromText(csv))
{
    // Generic (ISpanParsable<T>)
    var value = row[0].Parse<int>();

    // Optimized type-specific overloads
    if (row[1].TryParseDouble(out double d))       { }
    if (row[2].TryParseDateTime(out DateTime dt))  { }
    if (row[3].TryParseBoolean(out bool b))        { }
    if (row[4].TryParseGuid(out Guid id))          { }
    if (row[5].TryParseEnum<DayOfWeek>(out var day)) { }   // Case-insensitive
    if (row[6].TryParseTimeZoneInfo(out TimeZoneInfo tz)) { }
}
```

**Quote handling:**

```csharp
var csv = "field1,\"field2\",\"field,3\"\n" +
          "aaa,\"b,bb\",ccc\n" +
          "zzz,\"y\"\"yy\",xxx";   // Escaped quote

foreach (var row in Csv.ReadFromText(csv))
{
    var raw      = row[1].ToString();         // includes surrounding quotes: "b,bb"
    var unquoted = row[1].UnquoteToString();  // b,bb
    var span     = row[1].Unquote();          // ReadOnlySpan<char> — zero allocation
}
```

**Lazy evaluation:**

```csharp
// Columns are NOT parsed until first access
foreach (var row in Csv.ReadFromText(csv))
{
    if (ShouldSkip(row))
        continue;   // Skipped rows cost nothing

    var value = row[0].Parse<int>();   // First access triggers parsing
}
```

**Line number tracking:**

```csharp
foreach (var row in Csv.ReadFromText(csv))
{
    try
    {
        var id = row[0].Parse<int>();
    }
    catch (FormatException)
    {
        // LineNumber:       1-based logical row position (ordinal)
        // SourceLineNumber: 1-based physical line — differs when rows contain newlines inside quotes
        Console.WriteLine($"Invalid data at row {row.LineNumber} (source line {row.SourceLineNumber})");
    }
}
```

**Storing rows safely:**

Rows are ref structs and cannot outlive their iteration scope. Use `Clone()` to capture an owned copy:

```csharp
var storedRows = new List<CsvCharSpanRow>();

foreach (var row in Csv.ReadFromText(csv))
{
    // ❌ WRONG: Cannot store ref struct directly
    // storedRows.Add(row);

    // ✅ CORRECT: Clone creates an owned copy
    storedRows.Add(row.Clone());
}

foreach (var row in storedRows)
{
    var value = row[0].ToString();
}
```

**Resource management:**

HeroParser readers use `ArrayPool` buffers. Always dispose to prevent leaks:

```csharp
// ✅ Preferred: using statement
using (var reader = Csv.ReadFromText(csv))
{
    foreach (var row in reader)
        Console.WriteLine(row[0].ToString());
} // ArrayPool buffers automatically returned

// ✅ Also fine: foreach disposes automatically
foreach (var row in Csv.ReadFromText(csv))
    Console.WriteLine(row[0].ToString());

// ❌ AVOID: Manual iteration without disposal
var reader = Csv.ReadFromText(csv);
while (reader.MoveNext()) { /* ... */ }
// MEMORY LEAK! ArrayPool buffers not returned

// ✅ Fix: wrap in try/finally
var reader = Csv.ReadFromText(csv);
try
{
    while (reader.MoveNext()) { /* ... */ }
}
finally
{
    reader.Dispose();
}
```

**Reader options (basic):**

```csharp
var options = new CsvReadOptions
{
    Delimiter               = ',',     // Default
    Quote                   = '"',     // Default — RFC 4180 compliant
    MaxColumnCount          = 100,     // Default
    AllowNewlinesInsideQuotes = false, // Enable for full RFC newlines-in-quotes (slower)
    EnableQuotedFields      = true     // Disable for maximum speed when data has no quotes
};

var reader = Csv.ReadFromText(csvData, options);
```

**Comment lines:**

```csharp
var options = new CsvReadOptions { CommentCharacter = '#' };

var csv = @"# This is a comment
Name,Age
Alice,30
# Another comment
Bob,25";

foreach (var row in Csv.ReadFromText(csv, options))
{
    // Only data rows are processed
}
```

**Field trimming:**

```csharp
var options = new CsvReadOptions { TrimFields = true };

var csv = "  Name  ,  Age  \nAlice,  30  ";
foreach (var row in Csv.ReadFromText(csv, options))
{
    var name = row[0].ToString();   // "Name" (trimmed)
    var age  = row[1].ToString();   // "30"   (trimmed)
}
```

**Skip metadata rows:**

```csharp
var recordOptions = new CsvRecordOptions
{
    SkipRows    = 2,        // Skip first 2 rows (e.g., file metadata)
    HasHeaderRow = true     // The 3rd row is the header
};

var csv = @"File Version: 1.0
Generated: 2024-01-01
Name,Age
Alice,30
Bob,25";

foreach (var record in Csv.ParseRecords<MyRecord>(csv, recordOptions))
{
    // First 2 rows skipped; 3rd row used as header
}
```

**Null value handling:**

```csharp
var recordOptions = new CsvRecordOptions
{
    NullValues = ["NULL", "N/A", "NA", ""]
};

var csv = "Name,Value\nAlice,100\nBob,NULL\nCharlie,N/A";
foreach (var record in Csv.ParseRecords<MyRecord>(csv, recordOptions))
{
    // record.Value will be null when the field contains "NULL" or "N/A"
}
```

---

### 1.2 Files and streams

```csharp
// Stream a file without loading it fully
using var fileReader = Csv.ReadFromFile("data.csv");

// From an already-open stream (leaveOpen defaults to true)
using var stream = File.OpenRead("data.csv");
using var streamReader = Csv.ReadFromStream(stream);
```

Both overloads stream with pooled buffers. Rows remain valid until the next `MoveNext` call.

**Streaming large files (low memory):**

```csharp
using var reader = Csv.ReadFromStream(File.OpenRead("data.csv"));
while (reader.MoveNext())
{
    var row = reader.Current;
    var id  = row[0].Parse<int>();
}
```

**Fluent file reading:**

```csharp
using var fileReader = Csv.Read()
    .WithMaxFieldSize(10_000)
    .AllowNewlinesInQuotes()
    .FromFile("data.csv");
```

---

### 1.3 Async I/O

**Buffer-then-read (simple):**

```csharp
var source = await Csv.ReadFromFileAsync("data.csv");
using var reader = source.CreateReader();
```

Async overloads buffer the full payload (required because readers are ref structs). Use when you need non-blocking file or stream reads.

**True async streaming (low memory):**

```csharp
await using var reader = Csv.CreateAsyncStreamReader(File.OpenRead("data.csv"));
while (await reader.MoveNextAsync())
{
    var row = reader.Current;
    var id  = row[0].Parse<int>();
}
```

Uses pooled buffers and async I/O. Each row stays valid until the next `MoveNextAsync` call.

**Async streaming with fluent builder:**

```csharp
await foreach (var person in Csv.Read<Person>()
    .WithDelimiter(',')
    .FromFileAsync("data.csv"))
{
    Console.WriteLine($"{person.Name}: {person.Age}");
}
```

**Async streaming with cancellation:**

```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
await foreach (var record in Csv.Read<T>()
    .FromFileAsync("untrusted.csv")
    .WithCancellation(cts.Token))
{
    // Process record...
}
```

**Multi-schema async streaming:**

```csharp
await foreach (var record in Csv.Read()
    .WithMultiSchema()
    .WithDiscriminator("Type")
    .MapRecord<HeaderRecord>("H")
    .MapRecord<DetailRecord>("D")
    .FromFileAsync("transactions.csv"))
{
    // Process records asynchronously
}
```

---

### 1.4 Typed record reading with fluent builder

Use `Csv.Read<T>()` for a clean, chainable API that binds CSV columns to record properties automatically via convention or attributes.

```csharp
// Read all records at once
var records = Csv.Read<Person>()
    .WithDelimiter(';')
    .TrimFields()
    .AllowMissingColumns()
    .SkipRows(2)
    .FromText(csvData)
    .ToList();

// Async streaming
await foreach (var person in Csv.Read<Person>()
    .WithDelimiter(',')
    .FromFileAsync("data.csv"))
{
    Console.WriteLine($"{person.Name}: {person.Age}");
}
```

**Convention-based mapping (no attributes):**

Properties are mapped by name to CSV header columns. No `[TabularMap]` attribute is needed for simple cases:

```csharp
public class Person
{
    public string Name { get; set; } = "";
    public int Age { get; set; }
    public string? Email { get; set; }
}

var records = Csv.Read<Person>().FromText(csv).ToList();
```

**Attribute-based mapping:**

```csharp
[GenerateBinder]
public class Transaction
{
    [TabularMap(Name = "Id")]
    [Validate(NotNull = true, NotEmpty = true)]
    public string TransactionId { get; set; } = "";

    [TabularMap(Name = "Amount", Index = 1)]
    [Validate(NotNull = true, RangeMin = 0, RangeMax = 100_000)]
    public decimal Amount { get; set; }

    [TabularMap(Name = "Currency", Index = 2)]
    [Validate(MinLength = 3, MaxLength = 3)]
    public string Currency { get; set; } = "";

    [TabularMap(Name = "Ref", Index = 3)]
    [Validate(Pattern = @"^[A-Z]{2}\d{4}$")]
    public string Reference { get; set; } = "";
}
```

**Validation — lazy collection:**

```csharp
var reader = Csv.DeserializeRecords<Transaction>(csvData);
var records = new List<Transaction>();
foreach (var record in reader)
    records.Add(record);

if (reader.Errors.Count > 0)
{
    foreach (var error in reader.Errors)
        Console.WriteLine(error);
        // Row 2, Column 'Amount' (index 1), Property 'Amount':
        // [Range] Value must be between 0 and 100000 (raw: '-50.00')
}

// Or throw immediately if there are any errors
reader.ThrowIfAnyError();
```

**Validation properties on `[Validate]`:**

| Property | Type | Default | Description |
|---|---|---|---|
| `NotNull` | `bool` | `false` | Value must not be null, empty, or whitespace |
| `NotEmpty` | `bool` | `false` | String value must not be empty or whitespace |
| `MaxLength` | `int` | `-1` (unchecked) | Maximum string length |
| `MinLength` | `int` | `-1` (unchecked) | Minimum string length |
| `RangeMin` | `double` | `NaN` (unchecked) | Minimum numeric value (inclusive) |
| `RangeMax` | `double` | `NaN` (unchecked) | Maximum numeric value (inclusive) |
| `Pattern` | `string?` | `null` | Regex pattern the value must match |
| `PatternTimeoutMs` | `int` | `1000` | Regex evaluation timeout in milliseconds (ReDoS protection) |

**Header validation:**

```csharp
// Require specific headers
var records = Csv.Read<Person>()
    .RequireHeaders("Name", "Email", "Age")
    .FromText(csv)
    .ToList();

// Detect duplicate headers
var records = Csv.Read<Person>()
    .DetectDuplicateHeaders()
    .FromText(csv)
    .ToList();

// Custom header validation
var records = Csv.Read<Person>()
    .ValidateHeaders(headers =>
    {
        if (!headers.Contains("Id"))
            throw new CsvException(CsvErrorCode.InvalidHeader, "Missing required 'Id' column");
    })
    .FromText(csv)
    .ToList();
```

---

### 1.5 LINQ extensions

CSV record readers expose familiar LINQ-style operations. Because readers are ref structs they cannot implement `IEnumerable<T>` directly — these extension methods consume the reader and return materialized results.

```csharp
// Materialize
var allPeople  = Csv.Read<Person>().FromText(csv).ToList();
var peopleArray = Csv.Read<Person>().FromText(csv).ToArray();

// Filtering
var adults = Csv.Read<Person>()
    .FromText(csv)
    .Where(p => p.Age >= 18);

// Projection
var names = Csv.Read<Person>()
    .FromText(csv)
    .Select(p => p.Name);

// First / single
var first      = Csv.Read<Person>().FromText(csv).First();
var firstAdult = Csv.Read<Person>().FromText(csv).First(p => p.Age >= 18);
var single     = Csv.Read<Person>().FromText(csv).SingleOrDefault();

// Aggregation
var count      = Csv.Read<Person>().FromText(csv).Count();
var adultCount = Csv.Read<Person>().FromText(csv).Count(p => p.Age >= 18);
var hasRecords = Csv.Read<Person>().FromText(csv).Any();
var allAdults  = Csv.Read<Person>().FromText(csv).All(p => p.Age >= 18);

// Pagination
var page = Csv.Read<Person>().FromText(csv).Skip(10).Take(5);

// Grouping
var byCity = Csv.Read<Person>()
    .FromText(csv)
    .GroupBy(p => p.City);

// Dictionary
var byId = Csv.Read<Person>()
    .FromText(csv)
    .ToDictionary(p => p.Id);

// Iteration
Csv.Read<Person>()
    .FromText(csv)
    .ForEach(p => Console.WriteLine(p.Name));
```

---

### 1.6 Multi-schema (discriminator-based routing)

Parse CSV files where different rows map to different record types based on a discriminator column. Common in banking and financial file formats (NACHA, BAI, EDI) with header/detail/trailer patterns.

```csharp
[GenerateBinder]
public class HeaderRecord
{
    [TabularMap(Name = "Type")]  public string Type { get; set; } = "";
    [TabularMap(Name = "Date")]  public DateTime Date { get; set; }
}

[GenerateBinder]
public class DetailRecord
{
    [TabularMap(Name = "Type")]   public string Type { get; set; } = "";
    [TabularMap(Name = "Id")]     public int Id { get; set; }
    [TabularMap(Name = "Amount")] public decimal Amount { get; set; }
}

[GenerateBinder]
public class TrailerRecord
{
    [TabularMap(Name = "Type")]  public string Type { get; set; } = "";
    [TabularMap(Name = "Count")] public int Count { get; set; }
}

var csv = """
Type,Id,Amount,Date,Count
H,0,0.00,2024-01-15,0
D,1,100.50,,0
D,2,200.75,,0
T,0,301.25,,2
""";

foreach (var record in Csv.Read()
    .WithMultiSchema()
    .WithDiscriminator("Type")
    .MapRecord<HeaderRecord>("H")
    .MapRecord<DetailRecord>("D")
    .MapRecord<TrailerRecord>("T")
    .AllowMissingColumns()
    .FromText(csv))
{
    switch (record)
    {
        case HeaderRecord h:  Console.WriteLine($"Header: {h.Date}"); break;
        case DetailRecord d:  Console.WriteLine($"Detail: {d.Id} = {d.Amount:C}"); break;
        case TrailerRecord t: Console.WriteLine($"Trailer: {t.Count} records"); break;
    }
}
```

**Discriminator options:**

```csharp
// By column index (0-based)
.WithDiscriminator(columnIndex: 0)

// By column name (resolved from header row)
.WithDiscriminator("RecordType")

// Case-insensitive discriminator matching (default: false)
.CaseSensitiveDiscriminator(false)
```

**Handling unmatched rows:**

```csharp
// Skip rows that don't match any registered type
.OnUnmatchedRow(UnmatchedRowBehavior.Skip)

// Throw on unmatched rows (default)
.OnUnmatchedRow(UnmatchedRowBehavior.Throw)

// Custom factory for unmatched rows
.MapRecord((discriminator, columns, rowNum) => new UnknownRecord
{
    Type    = discriminator,
    RawData = string.Join(",", columns)
})
```

**Streaming and async:**

```csharp
// From file
foreach (var record in Csv.Read()
    .WithMultiSchema()
    .WithDiscriminator("Type")
    .MapRecord<HeaderRecord>("H")
    .MapRecord<DetailRecord>("D")
    .FromFile("transactions.csv"))
{
    // ...
}

// Async
await foreach (var record in Csv.Read()
    .WithMultiSchema()
    .WithDiscriminator("Type")
    .MapRecord<HeaderRecord>("H")
    .MapRecord<DetailRecord>("D")
    .FromFileAsync("transactions.csv"))
{
    // ...
}
```

---

### 1.7 DataReader for database bulk loading

Stream CSV data directly through `System.Data.IDataReader` into `SqlBulkCopy`, Dapper, or any ADO.NET consumer:

```csharp
// From a stream
using var reader = Csv.CreateDataReader(File.OpenRead("data.csv"));

// From a file path
using var reader = Csv.CreateDataReader("data.csv");

// Bulk load into SQL Server
using var bulkCopy = new SqlBulkCopy(connection);
bulkCopy.DestinationTableName = "MyTable";
await bulkCopy.WriteToServerAsync(reader);
```

**DataReader options:**

```csharp
var readerOptions = new CsvDataReaderOptions
{
    HasHeaderRow        = true,               // First row is header (default: true)
    CaseSensitiveHeaders = false,             // Header name lookup (default: false)
    AllowMissingColumns = false,              // Tolerate rows with fewer columns
    SkipRows            = 2,                  // Skip metadata rows before header
    NullValues          = ["NULL", "N/A"],    // Values treated as DBNull
    ColumnNames         = ["Id", "Name"]      // Override header names
};

using var reader = Csv.CreateDataReader(stream, readerOptions: readerOptions);
```

---

### 1.8 OnError callback

Handle deserialization errors at the field level without stopping the whole parse:

```csharp
var records = Csv.Read<Person>()
    .OnError(ctx =>
    {
        Console.WriteLine($"Error at row {ctx.Row}, column '{ctx.MemberName}': {ctx.Exception?.Message}");
        return DeserializeErrorAction.Skip;   // Or UseDefault, Throw
    })
    .FromText(csv)
    .ToList();
```

**`DeserializeErrorAction` values:**

| Value | Behaviour |
|---|---|
| `Skip` | Skip the entire record |
| `UseDefault` | Use the default value for the field and continue |
| `Throw` | Re-throw the exception (same as no callback) |

---

### 1.9 Progress reporting

Track parsing progress for large files using `IProgress<CsvProgress>`:

```csharp
var progress = new Progress<CsvProgress>(p =>
{
    var pct = p.TotalBytes > 0 ? (p.BytesProcessed * 100.0 / p.TotalBytes) : 0;
    Console.WriteLine($"Processed {p.RowsProcessed} rows ({pct:F1}%)");
});

var records = Csv.Read<Person>()
    .WithProgress(progress, intervalRows: 1000)
    .FromFile("large-file.csv")
    .ToList();
```

---

### 1.10 Custom type converters

Register converters for domain-specific types not handled by the built-in parsers:

```csharp
var records = Csv.Read<Order>()
    .RegisterConverter<Money>((column, culture) =>
    {
        var text = column.ToString();
        if (Money.TryParse(text, out var money))
            return money;
        throw new FormatException($"Invalid money format: {text}");
    })
    .FromText(csv)
    .ToList();
```

---

## 2. CSV Writing

### 2.1 Typed record writing

```csharp
var records = new[]
{
    new Person { Name = "Alice", Age = 30 },
    new Person { Name = "Bob",   Age = 25 }
};

// Write to string
string csv = Csv.WriteToText(records);
// Name,Age
// Alice,30
// Bob,25

// Write to file
Csv.WriteToFile("output.csv", records);

// Write to stream
using var stream = File.Create("output.csv");
Csv.WriteToStream(stream, records);
```

**Fluent writer builder:**

```csharp
var csv = Csv.Write<Person>()
    .WithDelimiter(';')
    .AlwaysQuote()
    .WithDateTimeFormat("yyyy-MM-dd")
    .WithHeader()
    .ToText(records);

// Without header
await Csv.Write<Person>()
    .WithDelimiter(',')
    .WithoutHeader()
    .ToFileAsync("output.csv", recordsAsync);
```

---

### 2.2 Manual row writing (CsvStreamWriter)

Use the non-generic builder for low-level field-by-field writing:

```csharp
using var writer = Csv.Write()
    .WithDelimiter(';')
    .AlwaysQuote()
    .WithDateTimeFormat("yyyy-MM-dd")
    .CreateWriter(Console.Out);

writer.WriteField("Name");
writer.WriteField("Age");
writer.EndRow();

writer.WriteField("Alice");
writer.WriteField(30);
writer.EndRow();

writer.Flush();

// Write to file with custom options
using var fileWriter = Csv.Write()
    .WithNewLine("\n")
    .WithCulture("de-DE")
    .CreateFileWriter("output.csv");
```

**Low-level API (without builder):**

```csharp
using var writer = Csv.CreateWriter(Console.Out);

writer.WriteField("Name");
writer.WriteField("Age");
writer.EndRow();

writer.WriteField("Alice");
writer.WriteField(30);
writer.EndRow();

writer.Flush();
```

---

### 2.3 Async writing

**Simple async (optimized for in-memory collections):**

```csharp
await Csv.WriteToFileAsync("output.csv", records);

// IAsyncEnumerable overload (for streaming data sources)
await Csv.WriteToFileAsync("output.csv", GetRecordsAsync());
```

**High-performance async streaming:**

```csharp
// Low-level async writer with sync fast paths
await using var writer = Csv.CreateAsyncStreamWriter(stream);
await writer.WriteRowAsync(new[] { "Alice", "30", "NYC" });
await writer.WriteRowAsync(new[] { "Bob",   "25", "LA" });
await writer.FlushAsync();

// Builder API with async streaming (16-43% faster than sync at scale)
await Csv.Write<Person>()
    .WithDelimiter(',')
    .WithHeader()
    .ToStreamAsyncStreaming(stream, records);
```

The async writer uses sync fast paths when data fits in the buffer, avoiding async overhead for small writes while supporting true non-blocking I/O for large datasets.

---

### 2.4 Quote styles

Control when fields are quoted:

```csharp
var options = new CsvWriteOptions
{
    QuoteStyle = QuoteStyle.WhenNeeded   // Default: only when required by RFC 4180
};
```

| `QuoteStyle` value | Behaviour |
|---|---|
| `WhenNeeded` | Quote only when the field contains the delimiter, quote character, or newline |
| `Always` | Quote every field unconditionally |
| `Never` | Never quote (use only when data is known clean) |

**Fluent shorthand:**

```csharp
Csv.Write<Person>().AlwaysQuote().ToText(records);
```

---

### 2.5 CSV injection protection

Protect against formula injection when the CSV output may be opened in a spreadsheet:

```csharp
Csv.Write<T>()
    .WithInjectionProtection(CsvInjectionProtection.Sanitize)
    .ToFile("export.csv");
```

See [Section 5.1](#51-csv-injection-protection-modes) for full mode descriptions.

---

### 2.6 OnSerializeError

Handle serialization errors per record without aborting the entire write:

```csharp
var options = new CsvWriteOptions
{
    OnSerializeError = ctx =>
    {
        Console.WriteLine($"Error at row {ctx.Row}, column '{ctx.MemberName}': {ctx.Exception?.Message}");
        return SerializeErrorAction.WriteNull;   // Or SkipRow, Throw
    }
};

Csv.WriteToText(records, options);
```

| `SerializeErrorAction` value | Behaviour |
|---|---|
| `WriteNull` | Write an empty/null value for the failed field and continue |
| `SkipRow` | Skip the entire record |
| `Throw` | Re-throw the exception |

---

### 2.7 Progress reporting

Track write progress in the same way as reads:

```csharp
var progress = new Progress<CsvProgress>(p =>
    Console.WriteLine($"Written {p.RowsProcessed} rows"));

await Csv.Write<Person>()
    .WithProgress(progress, intervalRows: 5000)
    .ToFileAsync("output.csv", records);
```

---

## 3. Options Reference

### 3.1 CsvReadOptions

| Property | Type | Default | Description |
|---|---|---|---|
| `Delimiter` | `char` | `','` | Field separator character |
| `Quote` | `char` | `'"'` | Quote character (RFC 4180) |
| `MaxColumnCount` | `int` | `100` | Maximum number of columns per row |
| `MaxRowCount` | `long` | `long.MaxValue` | Maximum rows processed |
| `MaxFieldSize` | `int` | `int.MaxValue` | Maximum bytes/chars per field |
| `MaxRowSize` | `int` | `int.MaxValue` | Maximum row size in bytes for streaming readers |
| `EnableQuotedFields` | `bool` | `true` | Enable RFC 4180 quote parsing (disable for maximum speed) |
| `AllowNewlinesInsideQuotes` | `bool` | `false` | Enable embedded newlines in quoted fields (slower) |
| `TrimFields` | `bool` | `false` | Trim whitespace from unquoted fields |
| `CommentCharacter` | `char?` | `null` | Lines starting with this character are skipped |
| `UseSimdIfAvailable` | `bool` | `true` | Set to `false` to force scalar mode (for debugging) |

Set `options.Validate()` to catch invalid configurations before processing untrusted input.

---

### 3.2 CsvWriteOptions

| Property | Type | Default | Description |
|---|---|---|---|
| `Delimiter` | `char` | `','` | Field separator character |
| `Quote` | `char` | `'"'` | Quote character |
| `NewLine` | `string` | `"\r\n"` | Line ending (CRLF per RFC 4180) |
| `WriteHeader` | `bool` | `true` | Include header row |
| `QuoteStyle` | `QuoteStyle` | `WhenNeeded` | When to quote fields |
| `NullValue` | `string` | `""` | String written for null values |
| `Culture` | `CultureInfo` | `InvariantCulture` | Culture for formatting |
| `DateTimeFormat` | `string` | `"O"` | ISO 8601 format for date/time values |
| `NumberFormat` | `string` | `"G"` | General format for numbers |
| `InjectionProtection` | `CsvInjectionProtection` | `EscapeWithQuote` | Formula injection protection mode |
| `OnSerializeError` | `Func<...>?` | `null` | Callback invoked on serialization errors |

---

### 3.3 CsvRecordOptions

| Property | Type | Default | Description |
|---|---|---|---|
| `HasHeaderRow` | `bool` | `true` | First (non-skipped) row is the header |
| `SkipRows` | `int` | `0` | Number of rows to skip before the header |
| `NullValues` | `string[]` | `[]` | Values that map to `null` during record parsing |
| `AllowMissingColumns` | `bool` | `false` | Treat missing columns as empty/default |
| `CaseSensitiveHeaders` | `bool` | `false` | Case sensitivity for header name matching |

---

### 3.4 CsvDataReaderOptions

| Property | Type | Default | Description |
|---|---|---|---|
| `HasHeaderRow` | `bool` | `true` | First row is header |
| `CaseSensitiveHeaders` | `bool` | `false` | Case-sensitive header lookup |
| `AllowMissingColumns` | `bool` | `false` | Tolerate rows with fewer columns |
| `SkipRows` | `int` | `0` | Skip rows before header |
| `NullValues` | `string[]` | `[]` | Values treated as `DBNull` |
| `ColumnNames` | `string[]?` | `null` | Override inferred header names |

---

## 4. Detection and Validation

### 4.1 Delimiter detection

Automatically detect the delimiter character used in CSV data:

```csharp
// Quick detection — returns the detected delimiter char
char delimiter = Csv.DetectDelimiter(csvData);

var records = Csv.Read<Person>()
    .WithDelimiter(delimiter)
    .FromText(csvData)
    .ToList();
```

**Supported delimiters:** comma (`,`), semicolon (`;`), pipe (`|`), tab (`\t`)

**Detailed detection results:**

```csharp
var result = Csv.DetectDelimiterWithDetails(csvData);

Console.WriteLine($"Detected: '{result.DetectedDelimiter}'");
Console.WriteLine($"Confidence: {result.Confidence}%");
Console.WriteLine($"Average count per row: {result.AverageDelimiterCount}");

if (result.Confidence < 50)
{
    Console.WriteLine("Low confidence — manual verification recommended");
    foreach (var candidate in result.CandidateCounts)
        Console.WriteLine($"  '{candidate.Key}': {candidate.Value} occurrences");
}

var records = Csv.Read<Person>()
    .WithDelimiter(result.DetectedDelimiter)
    .FromText(csvData)
    .ToList();
```

**Detection algorithm:**
- Samples first N rows (default 10, configurable)
- Counts occurrences of each candidate delimiter
- Selects the delimiter with the most consistent count across rows
- Confidence is 100% for perfect per-row consistency

**Typical use cases:**
- User-uploaded files with unknown format
- Processing CSVs from multiple sources with varying delimiters
- European CSVs (semicolon-delimited)
- Log files (pipe or tab-delimited)

---

### 4.2 CSV structural validation

Validate CSV structure before processing — useful for ETL pipelines and user-uploaded file gates:

```csharp
var options = new CsvValidationOptions
{
    RequiredHeaders          = ["Name", "Email", "Age"],
    ExpectedColumnCount      = 3,
    MaxRows                  = 10000
};

var result = Csv.Validate(csvData, options);

if (!result.IsValid)
{
    Console.WriteLine($"Validation failed with {result.Errors.Count} errors:");
    foreach (var error in result.Errors)
        Console.WriteLine($"  Row {error.RowNumber}: {error.Message}");
    return;
}

// Proceed with processing
var records = Csv.Read<Person>().FromText(csvData).ToList();
```

**Full options:**

```csharp
var options = new CsvValidationOptions
{
    Delimiter                 = null,        // Auto-detect delimiter
    HasHeaderRow              = true,        // Expect a header row
    RequiredHeaders           = ["Id", "Name"],
    ExpectedColumnCount       = 5,
    MaxRows                   = 1_000_000,
    CheckConsistentColumnCount = true,       // All rows must have same column count
    AllowEmptyFile            = false        // Reject empty files
};
```

**Inspecting the result:**

```csharp
var result = Csv.Validate(csvData, options);

if (result.IsValid)
{
    Console.WriteLine($"Valid CSV: {result.TotalRows} rows, {result.ColumnCount} columns");
    Console.WriteLine($"Delimiter: '{result.Delimiter}'");
    Console.WriteLine($"Headers: {string.Join(", ", result.Headers)}");
}

foreach (var error in result.Errors)
{
    Console.WriteLine($"[{error.ErrorType}] Row {error.RowNumber}, Col {error.ColumnNumber}");
    Console.WriteLine($"  Message: {error.Message}");
    if (error.Expected != null)
        Console.WriteLine($"  Expected: {error.Expected}, Actual: {error.Actual}");
}
```

**Error types:**

| `ErrorType` | Description |
|---|---|
| `ParseError` | CSV structure could not be parsed |
| `MissingHeader` | Required header is missing |
| `ColumnCountMismatch` | Column count does not match expected |
| `TooManyRows` | Row count exceeds `MaxRows` |
| `EmptyFile` | File contains no data |
| `InconsistentColumnCount` | Rows have different column counts |
| `DelimiterDetectionFailed` | Could not auto-detect delimiter |

---

### 4.3 Schema inference

Automatically detect column types from CSV data without defining record classes:

```csharp
var schema = Csv.InferSchema(csvData);
foreach (var col in schema.Columns)
{
    Console.WriteLine($"{col.Name}: {col.InferredType}{(col.IsNullable ? "?" : "")} (max length: {col.MaxLength})");
}
```

**Inferred types:** `Boolean`, `Integer`, `Long`, `Decimal`, `Guid`, `DateTime`, `String`

The inference algorithm samples rows and tries to parse each value in order of specificity, falling back to the widest compatible type (for example, `int` + `decimal` = `decimal`, `int` + `string` = `string`).

**Configure inference:**

```csharp
var options = new CsvSchemaInferenceOptions
{
    Delimiter  = ';',    // Auto-detects if null
    SampleRows = 200     // Default: 100
};

var schema = Csv.InferSchema(csvData, options);
Console.WriteLine($"Sampled {schema.SampledRowCount} rows, found {schema.Columns.Count} columns");
```

**Use cases:**
- Dynamic CSV import without pre-defined schemas
- Generating `CREATE TABLE` statements from CSV files
- Validating data types before ETL processing

---

## 5. Security

### 5.1 CSV injection protection modes

When exporting user data to CSV that may be opened in a spreadsheet application, enable formula injection protection:

```csharp
// Via fluent builder
Csv.Write<T>()
    .WithInjectionProtection(CsvInjectionProtection.Sanitize)
    .ToFile("export.csv");

// Via options
var writeOptions = new CsvWriteOptions
{
    InjectionProtection = CsvInjectionProtection.Sanitize
};
Csv.WriteToText(records, writeOptions);
```

| Mode | Description | Example: `=1+1` |
|---|---|---|
| `EscapeWithQuote` | **(Default)** Prefixes dangerous values with a single quote inside the quoted field | `"'=1+1"` |
| `None` | No protection — use only for trusted data that will not be opened in spreadsheet tools | `=1+1` |
| `Sanitize` | Removes dangerous characters (`=`, `@`, `+`, `-`, `\t`, `\r`) | `1+1` |
| `EscapeWithTab` | Prefixes dangerous characters with a tab | `\t=1+1` |

**Dangerous value triggers:** any field starting with `=`, `@`, `+`, `-`, `\t`, or `\r`.

---

### 5.2 DoS protection

Protect against malicious or malformed CSV files with configurable limits:

```csharp
var options = new CsvReadOptions
{
    MaxColumnCount = 100,         // Prevent column explosion attacks
    MaxRowCount    = 1_000_000,   // Limit total rows
    MaxFieldSize   = 10_000,      // Prevent huge field allocations (bytes)
    MaxRowSize     = 512 * 1024   // 512 KB row limit for streaming readers
};

var reader = Csv.Read().WithOptions(options).FromFile("untrusted.csv");
```

**Recommended limits for untrusted input:**

| Limit | Suggested value |
|---|---|
| `MaxColumnCount` | 100–1000 (based on expected schema) |
| `MaxRowCount` | 1,000,000 (based on available memory) |
| `MaxFieldSize` | 10,000–100,000 bytes |
| `MaxRowSize` | 512 KB–1 MB (streaming readers) |

**Field length limits:**

```csharp
var options = new CsvReadOptions
{
    MaxFieldSize = 10_000   // Throws CsvException if any field exceeds 10 KB
};
var reader = Csv.ReadFromText(csv, options);
```

**Validation before processing:**

```csharp
var options = new CsvReadOptions { MaxColumnCount = 50, MaxRowCount = 100_000 };
options.Validate();   // Throws if configuration is invalid
```

**Streaming for large files (avoid loading entire file):**

```csharp
await using var reader = Csv.CreateAsyncStreamReader(File.OpenRead("large.csv"));
while (await reader.MoveNextAsync())
{
    var row = reader.Current;
    // Process row...
}
```

**Exception handling:**

```csharp
try
{
    var records = Csv.Read<T>().FromFile("untrusted.csv").ToList();
}
catch (CsvException ex)
{
    Console.WriteLine($"CSV error at row {ex.Row}, col {ex.Column}: {ex.Message}");
}
```

**Thread safety:**

HeroParser readers and writers are not thread-safe by design:

```csharp
// ✅ Good: Each thread has its own reader
Parallel.ForEach(files, file =>
{
    var reader = Csv.Read<T>().FromFile(file);
    // Process...
});

// ❌ Bad: Shared reader across threads
var reader = Csv.Read<T>().FromFile("data.csv");
Parallel.ForEach(reader, record => { /* NOT SAFE */ });
```

`CsvReadOptions` and `CsvWriteOptions` are immutable and safe to share after validation.

---

## 6. Advanced

### 6.1 SIMD pipeline details

The read path processes UTF-8 input through a layered SIMD pipeline:

```
Input (UTF-8 bytes) → BOM detection → Row Scanner (SIMD) → Column Extraction → Binding → Records
                                           │
                                      ┌────┴─────┐
                                      │ AVX-512  │  (64-byte chunks)
                                      │ AVX2     │  (32-byte chunks)
                                      │ NEON     │  (16-byte chunks, ARM)
                                      │ Scalar   │  (fallback)
                                      └──────────┘
```

- **Row Scanner**: Uses SIMD to find delimiters and newlines in parallel. Uses the PCLMULQDQ instruction for branchless prefix-XOR quote tracking.
- **Column Extraction**: `AppendColumn` tracks column boundaries via a `columnEnds[]` array (stackalloc for ≤128 columns).
- **Binding**: `ICsvSourceBinder<TElement, T>` maps columns to record properties. Source-generated binders inline type parsing.
- **UTF-16 fallback**: `CsvCharToByteBinderAdapter` converts char data to UTF-8 via `ArrayPool` + `stackalloc`, then uses the byte path.

**Write path:**

```
Records → PropertyAccessor (compiled expression trees) → CsvStreamWriter (buffered) → TextWriter
                                                               │
                                                         Quote analysis (SIMD AVX2/SSE2)
```

**Key abstractions:**

| Type | Description |
|---|---|
| `CsvRowReader<T>` | ref struct row iterator (`T` = `byte` or `char`) |
| `CsvRecordReader<TElement, T>` | ref struct wrapping row reader + binder |
| `CsvStreamWriter` | Buffered writer with `ArrayPool<char>` management |
| `CsvAsyncStreamWriter` | Async variant with dual `char[]` + `byte[]` buffers |
| `ICsvSourceBinder<TElement, T>` | Interface for source-generated and reflection binders |

**Performance characteristics (AMD Ryzen AI 9 HX PRO 370, .NET 10):**

| Rows | Columns | Quotes | Time | Throughput |
|------|---------|--------|------|------------|
| 10k | 25 | No | 552 µs | ~6.1 GB/s |
| 10k | 25 | Yes | 1,344 µs | ~5.1 GB/s |
| 10k | 100 | No | 1,451 µs | ~4.5 GB/s |
| 10k | 100 | Yes | 3,617 µs | ~1.9 GB/s |
| 100k | 100 | No | 14,568 µs | ~4.5 GB/s |
| 100k | 100 | Yes | 35,396 µs | ~1.9 GB/s |

- Fixed **4 KB allocation** regardless of column count or file size
- Compared to Sep 0.12.1: HeroParser UTF-8 is **~21% faster** on quoted 25-column data, **25–45% faster** on wide CSVs
- Always prefer the UTF-8 APIs (`byte[]` / `ReadOnlySpan<byte>`); the UTF-16 path is provided for compatibility but is not SIMD-optimized

**Disable SIMD (debugging):**

```csharp
var options = new CsvReadOptions { UseSimdIfAvailable = false };
```

---

### 6.2 Span-based parsing

For maximum performance when data is already in memory, use span overloads that bypass stream and string overhead:

```csharp
// Parse from char span
ReadOnlySpan<char> charData = csvText.AsSpan();
using var charReader = Csv.ReadFromCharSpan(charData);
while (charReader.MoveNext())
{
    var value = charReader.Current[0].Parse<int>();
}

// Parse from UTF-8 byte span (fastest path — no allocation for invariant primitives)
ReadOnlySpan<byte> utf8Data = File.ReadAllBytes("data.csv");
using var byteReader = Csv.ReadFromByteSpan(utf8Data);
while (byteReader.MoveNext())
{
    var value = byteReader.Current[0].Parse<int>();
}
```

---

### 6.3 PipeReader integration

Parse CSV data from `System.IO.Pipelines` sources (network sockets, HTTP response bodies) without buffering the entire payload:

```csharp
var pipe = PipeReader.Create(networkStream);
await foreach (var row in Csv.ReadFromPipeReaderAsync(pipe))
{
    var id   = row[0].ToString();
    var name = row[1].ToString();
    Console.WriteLine($"{id}: {name}");
}
```

Each row is yielded as a `CsvPipeRow` with column access via UTF-8 byte spans.

**With options:**

```csharp
var options = new CsvReadOptions
{
    Delimiter         = ';',
    EnableQuotedFields = true,
    MaxColumnCount    = 100,
    MaxFieldSize      = 10_000,
    MaxRowSize        = 512 * 1024,
    MaxRowCount       = 100_000
};

await foreach (var row in Csv.ReadFromPipeReaderAsync(pipe, options, cancellationToken))
{
    // row.RowNumber             — 1-based row number
    // row.ColumnCount           — number of columns
    // row[i].Span               — raw UTF-8 bytes
    // row[i].ToUnquotedString() — decoded string with quotes stripped
}
```

**Use cases:**
- Parsing CSV from HTTP response streams
- Processing CSV over network sockets
- High-throughput pipeline architectures

---

### 6.4 Hardware diagnostics

Check which SIMD instruction sets are active at runtime:

```csharp
Console.WriteLine(Hardware.GetHardwareInfo());
// Example output: "SIMD: AVX-512F, AVX-512BW, AVX2, SSE2"
```

HeroParser selects the best available path automatically:

| Instruction set | Chunk size |
|---|---|
| AVX-512 | 64 bytes |
| AVX2 | 32 bytes (common on modern x64) |
| NEON | 16 bytes (ARM64: Apple Silicon, AWS Graviton) |
| Scalar | byte-by-byte fallback (always works) |

---

### 6.5 RFC 4180 compliance notes

HeroParser implements **core RFC 4180** behaviour:

**Supported:**
- Quoted fields with the double-quote character (`"`)
- Escaped quotes using double-double-quotes (`""`)
- Delimiters (commas) within quoted fields
- Both LF (`\n`) and CRLF (`\r\n`) line endings
- Newlines inside quoted fields when `AllowNewlinesInsideQuotes = true` (default `false` for performance)
- Empty fields and spaces preserved
- Custom delimiters and quote characters

**Not supported:**
- Automatic header detection — skip header rows manually via `SkipRows`

---

## 7. Source Generators

### 7.1 [GenerateBinder] attribute

Attach `[GenerateBinder]` to a record class to trigger compile-time binder generation. This is the recommended approach for AOT, trimming, and maximum read performance:

```csharp
using HeroParser.SeparatedValues.Reading.Shared;

[GenerateBinder]
public class Person
{
    public string Name  { get; set; } = "";
    public int    Age   { get; set; }
    public string? Email { get; set; }
}
```

The source generator emits a binder alongside the class — no separate package required (generators ship inside the main `HeroParser` package).

**With explicit column mapping:**

```csharp
[GenerateBinder]
public class Trade
{
    [TabularMap(Name = "Ticker",     Index = 0)] public string Symbol   { get; set; } = "";
    [TabularMap(Name = "TradePrice", Index = 1)] public decimal Price   { get; set; }
    [TabularMap(Name = "Qty",        Index = 2)] public int Quantity    { get; set; }
}
```

> **Note:** When using `[GenerateBinder]`, each `[TabularMap]` must specify either `Name` or `Index`. Omitting both produces a **HERO008** build error.

**Compile-time diagnostics:**

| Code | Severity | Description |
|---|---|---|
| `HERO004` | Error | `NotEmpty` applied to a non-string property |
| `HERO005` | Error | `MaxLength` or `MinLength` applied to a non-string property |
| `HERO006` | Error | `RangeMin` or `RangeMax` applied to a non-numeric property |
| `HERO007` | Error | `Pattern` applied to a non-string property |
| `HERO008` | Error | `[TabularMap]` used with `[GenerateBinder]` but neither `Name` nor `Index` is specified |

---

### 7.2 Generated binder vs reflection binder

| Feature | Source-generated (`[GenerateBinder]`) | Reflection binder |
|---|---|---|
| AOT / trimming compatible | Yes | No (`[RequiresUnreferencedCode]`) |
| Startup time | Fast (pre-compiled) | Slower (first-use compilation) |
| Runtime performance | Maximum (inlined type parsing) | Good |
| Configuration | Attributes or fluent map at design time | Fluent map at runtime |
| SIMD path | UTF-8 byte path | Char path (no SIMD) |

Use `[GenerateBinder]` for production code paths. Use fluent maps (`CsvMap<T>`) when the schema is known only at runtime.

---

### 7.3 Multi-schema dispatcher [CsvGenerateDispatcher]

For maximum multi-schema dispatch performance, use the source-generated dispatcher instead of runtime multi-schema. The generator produces a switch-based dispatch that compiles to a jump table:

```csharp
[CsvGenerateDispatcher(DiscriminatorIndex = 0)]
[CsvSchemaMapping("H", typeof(HeaderRecord))]
[CsvSchemaMapping("D", typeof(DetailRecord))]
[CsvSchemaMapping("T", typeof(TrailerRecord))]
public partial class BankingDispatcher { }
```

**Usage:**

```csharp
var reader = Csv.Read().FromText(csv);
if (reader.MoveNext()) { }   // Skip header row
int rowNumber = 1;
while (reader.MoveNext())
{
    rowNumber++;
    var record = BankingDispatcher.Dispatch(reader.Current, rowNumber);
    switch (record)
    {
        case HeaderRecord h:  /* ... */ break;
        case DetailRecord d:  /* ... */ break;
        case TrailerRecord t: /* ... */ break;
    }
}
```

**Why source-generated dispatch is faster than runtime multi-schema:**

- Switch expression compiles to a jump table (no dictionary lookup)
- Direct binder invocation (no interface dispatch overhead)
- No boxing/unboxing
- Approximately **2.85x faster** than the runtime `WithMultiSchema()` path

> All mapped types must have `[GenerateBinder]` for AOT compatibility.

---

## 8. Fluent Mapping

Define column-to-property mappings at runtime using `CsvMap<T>` — no attributes required. Maps are reusable objects that work with both the read and write builders.

### 8.1 WithMap() for read

```csharp
// Define a reusable map
var map = new CsvMap<Trade>();
map.Map(t => t.Symbol,   c => c.Name("Ticker"))
   .Map(t => t.Price,    c => c.Name("TradePrice"))
   .Map(t => t.Quantity, c => c.Name("Qty"));

// Use it in a read
var reader = Csv.Read<Trade>().WithMap(map).FromText(csvText);
foreach (var trade in reader)
    Console.WriteLine($"{trade.Symbol}: {trade.Price}");
```

**Subclass pattern for reuse across the application:**

```csharp
public class TradeMap : CsvMap<Trade>
{
    public TradeMap()
    {
        Map(t => t.Symbol,   c => c.Name("Ticker"));
        Map(t => t.Price,    c => c.Name("TradePrice"));
        Map(t => t.Quantity, c => c.Name("Qty"));
    }
}

var reader = Csv.Read<Trade>().WithMap(new TradeMap()).FromText(csv);
```

---

### 8.2 WithMap() for write

The same map is passed to the write builder:

```csharp
var map = new CsvMap<Trade>();
map.Map(t => t.Symbol,   c => c.Name("Ticker"))
   .Map(t => t.Price,    c => c.Name("TradePrice"))
   .Map(t => t.Quantity, c => c.Name("Qty"));

var csv = Csv.Write<Trade>().WithMap(map).ToText(trades);
```

---

### 8.3 Map<TProperty>() inline mapping

Add field-level validation rules inline on the map:

```csharp
var map = new CsvMap<Trade>();
map.Map(t => t.Symbol,   c => c.Name("Symbol").NotEmpty().MaxLength(5))
   .Map(t => t.Price,    c => c.Name("Price").Range(0.01, 999999))
   .Map(t => t.Quantity, c => c.Name("Qty"));

var reader = Csv.Read<Trade>().WithMap(map).FromText(csv);
foreach (var trade in reader)
    Console.WriteLine($"{trade.Symbol}: {trade.Price}");

foreach (var error in reader.Errors)
    Console.WriteLine(error);
    // Row 2, Column 'Price' (index 1), Property 'Price':
    // [Range] Value must be between 0.01 and 999999 (raw: '-5.00')
```

**Notes:**

- Fluent maps use reflection and expression trees — annotated with `[RequiresUnreferencedCode]` / `[RequiresDynamicCode]` for AOT awareness.
- Map-based reads use the char path (no SIMD byte-path optimization). Use `[GenerateBinder]` for maximum throughput.
- `ForEach` extension methods are not supported with fluent maps — use `FromText()` / `FromFile()` / `FromStream()` instead.

---

*HeroParser — High-performance, zero-allocation, AOT-ready CSV parsing for .NET*
