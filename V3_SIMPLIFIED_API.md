# HeroParser v3.0 - Simplified API Design

**Date:** 2025-11-15
**Status:** Final Design - Ready to Implement

---

## API Design (FINAL)

### Two Methods Only

```csharp
public static class Csv
{
    /// <summary>
    /// Parse CSV data synchronously.
    /// </summary>
    public static CsvReader Parse(
        ReadOnlySpan<char> csv,
        CsvParserOptions? options = null);

    /// <summary>
    /// Parse CSV data asynchronously (for large files/streams).
    /// </summary>
    public static IAsyncEnumerable<CsvRow> ParseAsync(
        string csv,
        CsvParserOptions? options = null,
        CancellationToken cancellationToken = default);
}
```

**That's it!** Two methods, one options object.

---

## CsvParserOptions Design

```csharp
public sealed class CsvParserOptions
{
    /// <summary>
    /// Field delimiter character. Default: comma (',').
    /// Must be ASCII (0-127) for SIMD performance.
    /// </summary>
    public char Delimiter { get; init; } = ',';

    /// <summary>
    /// Maximum columns per row. Default: 10,000.
    /// Throws CsvException if exceeded.
    /// </summary>
    public int MaxColumns { get; init; } = 10_000;

    /// <summary>
    /// Maximum rows to parse. Default: 100,000.
    /// Throws CsvException if exceeded.
    /// </summary>
    public int MaxRows { get; init; } = 100_000;

    /// <summary>
    /// Default options: comma delimiter, 10k columns, 100k rows.
    /// </summary>
    public static CsvParserOptions Default { get; } = new();
}
```

### Usage Examples

```csharp
// Example 1: Simple parsing (defaults)
var reader = Csv.Parse(csvData);
foreach (var row in reader)
{
    var id = row[0].Parse<int>();
    var name = row[1].ToString();
}

// Example 2: Custom delimiter
var options = new CsvParserOptions { Delimiter = '\t' };
var reader = Csv.Parse(csvData, options);

// Example 3: Custom limits
var options = new CsvParserOptions
{
    MaxColumns = 50_000,
    MaxRows = 1_000_000
};
var reader = Csv.Parse(csvData, options);

// Example 4: Async parsing (large files)
await foreach (var row in Csv.ParseAsync(largeFile))
{
    // Process row by row without loading entire file
}
```

---

## Multi-Framework Targeting

### Target Frameworks

```xml
<TargetFrameworks>
  netstandard2.0;
  netstandard2.1;
  net6.0;
  net7.0;
  net8.0;
  net9.0;
  net10.0
</TargetFrameworks>
```

### Framework Differences & Compatibility

| Feature | netstandard2.0 | netstandard2.1 | net6.0+ |
|---------|----------------|----------------|---------|
| `ReadOnlySpan<T>` | ✅ (via package) | ✅ Native | ✅ Native |
| `IAsyncEnumerable` | ❌ Polyfill needed | ✅ Native | ✅ Native |
| `ArrayPool<T>` | ✅ (via package) | ✅ Native | ✅ Native |
| AVX-512 | ❌ | ❌ | ✅ (net6+) |
| AVX2 | ⚠️ Limited | ⚠️ Limited | ✅ Full |
| ARM NEON | ❌ | ❌ | ✅ (net6+) |
| `MemoryMarshal` | ⚠️ Limited | ✅ Native | ✅ Enhanced |
| `Unsafe` class | ⚠️ (via package) | ✅ Native | ✅ Native |

### Strategy for Old Frameworks

```csharp
#if NETSTANDARD2_0
    // Use scalar parser only (no SIMD)
    // Polyfill IAsyncEnumerable
    // Use System.Memory package for Span<T>
#elif NETSTANDARD2_1
    // Scalar + limited AVX2 (if available)
    // Native IAsyncEnumerable
#else // net6.0+
    // Full SIMD support (AVX-512, AVX2, NEON)
    // All modern features
#endif
```

---

## Package Dependencies by Framework

### netstandard2.0
```xml
<PackageReference Include="System.Memory" Version="4.5.5" />
<PackageReference Include="System.Runtime.CompilerServices.Unsafe" Version="6.0.0" />
<PackageReference Include="Microsoft.Bcl.AsyncInterfaces" Version="8.0.0" />
```

### netstandard2.1
```xml
<PackageReference Include="System.Memory" Version="4.5.5" />
<PackageReference Include="System.Runtime.CompilerServices.Unsafe" Version="6.0.0" />
```

### net6.0+
```xml
<!-- No additional packages needed -->
```

---

## Implementation Details

### 1. Parse (Synchronous)

```csharp
public static CsvReader Parse(
    ReadOnlySpan<char> csv,
    CsvParserOptions? options = null)
{
    options ??= CsvParserOptions.Default;

    // Validate options
    if (options.Delimiter > 127)
        throw new ArgumentException(
            "Delimiter must be ASCII (0-127) for performance",
            nameof(options));

    if (options.MaxColumns <= 0)
        throw new ArgumentException(
            "MaxColumns must be positive",
            nameof(options));

    if (options.MaxRows <= 0)
        throw new ArgumentException(
            "MaxRows must be positive",
            nameof(options));

    return new CsvReader(csv, options);
}
```

### 2. ParseAsync (Asynchronous)

**Design Decision:** Return `IAsyncEnumerable<CsvRow>` for streaming

```csharp
public static async IAsyncEnumerable<CsvRow> ParseAsync(
    string csv,
    CsvParserOptions? options = null,
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
{
    options ??= CsvParserOptions.Default;

    // Process in chunks to avoid blocking
    const int ChunkSize = 64 * 1024; // 64KB chunks
    int position = 0;
    int rowCount = 0;

    while (position < csv.Length)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Find chunk boundary (don't split rows)
        int chunkEnd = Math.Min(position + ChunkSize, csv.Length);
        if (chunkEnd < csv.Length)
        {
            // Scan forward to next newline
            while (chunkEnd < csv.Length &&
                   csv[chunkEnd] != '\n' &&
                   csv[chunkEnd] != '\r')
                chunkEnd++;
        }

        var chunk = csv.AsSpan(position, chunkEnd - position);
        var reader = new CsvReader(chunk, options);

        foreach (var row in reader)
        {
            if (++rowCount > options.MaxRows)
                throw new CsvException(
                    $"CSV exceeds maximum row limit of {options.MaxRows}");

            yield return row;

            // Yield periodically to avoid blocking
            if (rowCount % 1000 == 0)
                await Task.Yield();
        }

        position = chunkEnd;
    }
}
```

**Alternative Design:** For file streams

```csharp
// Future: Add overload for Stream
public static async IAsyncEnumerable<CsvRow> ParseAsync(
    Stream stream,
    Encoding? encoding = null,
    CsvParserOptions? options = null,
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
{
    // Read stream in chunks, decode, parse
    // More complex but useful for large files
}
```

---

## Row Limit Enforcement

```csharp
public ref struct CsvReader
{
    private readonly ReadOnlySpan<char> _csv;
    private readonly CsvParserOptions _options;
    private int _rowCount;

    public bool MoveNext()
    {
        if (_rowCount >= _options.MaxRows)
            throw new CsvException(
                $"CSV exceeds maximum row limit of {_options.MaxRows}");

        // ... parse next row ...

        _rowCount++;
        return true;
    }
}
```

---

## Column Limit Enforcement

```csharp
internal int ParseColumns(
    ReadOnlySpan<char> line,
    char delimiter,
    Span<int> starts,
    Span<int> lengths,
    int maxColumns)
{
    int columnCount = 0;

    // ... SIMD parsing ...

    if (columnCount > maxColumns)
        throw new CsvException(
            $"Row has {columnCount} columns, exceeds limit of {maxColumns}");

    return columnCount;
}
```

---

## Exception Design

```csharp
/// <summary>
/// Exception thrown when CSV parsing fails.
/// </summary>
public class CsvException : Exception
{
    public int? Row { get; init; }
    public int? Column { get; init; }

    public CsvException(string message) : base(message) { }

    public CsvException(string message, int row, int column)
        : base($"Row {row}, Column {column}: {message}")
    {
        Row = row;
        Column = column;
    }
}
```

---

## Project Structure

```
HeroParser/
├── src/
│   └── HeroParser/
│       ├── Csv.cs                      # Public API (2 methods)
│       ├── CsvParserOptions.cs         # Options class
│       ├── CsvReader.cs                # Sync reader (ref struct)
│       ├── CsvRow.cs                   # Row accessor (ref struct)
│       ├── CsvCol.cs                   # Column value (ref struct)
│       ├── CsvException.cs             # Custom exception
│       ├── Simd/
│       │   ├── ISimdParser.cs          # Parser interface
│       │   ├── ScalarParser.cs         # Baseline (all frameworks)
│       │   ├── Avx512Parser.cs         # net6+ only
│       │   ├── Avx2Parser.cs           # net6+ only
│       │   ├── NeonParser.cs           # net6+ only
│       │   └── SimdParserFactory.cs    # Hardware detection
│       └── HeroParser.csproj
└── ...
```

**Total files:** ~12

---

## .csproj Configuration

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFrameworks>netstandard2.0;netstandard2.1;net6.0;net7.0;net8.0;net9.0;net10.0</TargetFrameworks>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <AllowUnsafeBlocks>false</AllowUnsafeBlocks>

    <!-- Package info -->
    <PackageId>HeroParser</PackageId>
    <Version>3.0.0</Version>
    <Description>World's fastest .NET CSV parser - 30+ GB/s, zero allocations, no unsafe code</Description>
    <PackageTags>csv;parser;simd;performance;zero-allocation;netstandard</PackageTags>
  </PropertyGroup>

  <!-- netstandard2.0 needs polyfills -->
  <ItemGroup Condition="'$(TargetFramework)' == 'netstandard2.0'">
    <PackageReference Include="System.Memory" Version="4.5.5" />
    <PackageReference Include="System.Runtime.CompilerServices.Unsafe" Version="6.0.0" />
    <PackageReference Include="Microsoft.Bcl.AsyncInterfaces" Version="8.0.0" />
  </ItemGroup>

  <!-- netstandard2.1 needs some helpers -->
  <ItemGroup Condition="'$(TargetFramework)' == 'netstandard2.1'">
    <PackageReference Include="System.Memory" Version="4.5.5" />
    <PackageReference Include="System.Runtime.CompilerServices.Unsafe" Version="6.0.0" />
  </ItemGroup>

</Project>
```

---

## Performance Targets by Framework

| Framework | SIMD Support | Target Throughput | Notes |
|-----------|--------------|-------------------|-------|
| netstandard2.0 | Scalar only | 2-5 GB/s | Wide compatibility |
| netstandard2.1 | Limited AVX2 | 5-10 GB/s | Decent performance |
| net6.0 | AVX-512, AVX2, NEON | 20-30 GB/s | Full SIMD |
| net7.0 | AVX-512, AVX2, NEON | 25-30 GB/s | Improved intrinsics |
| net8.0+ | AVX-512, AVX2, NEON | 30+ GB/s | Best performance |

**Key insight:** netstandard2.0 is slow, but provides broad compatibility for libraries that need it.

---

## Conditional Compilation Strategy

```csharp
namespace HeroParser.Simd;

internal static class SimdParserFactory
{
    public static ISimdParser GetParser()
    {
#if NET6_0_OR_GREATER
        // Full SIMD support
        if (Avx512F.IsSupported && Avx512BW.IsSupported)
            return Avx512Parser.Instance;

        if (Avx2.IsSupported)
            return Avx2Parser.Instance;

        if (AdvSimd.IsSupported)
            return NeonParser.Instance;
#endif
        // Fallback for all frameworks
        return ScalarParser.Instance;
    }
}
```

---

## Migration Examples

### Old v2.0 Code

```csharp
// OLD
var reader = Csv.ParseComma(csv);

// OLD
var reader = Csv.ParseFile("data.csv");

// OLD
var reader = Csv.ParseParallel(csv);
```

### New v3.0 Code

```csharp
// NEW - default is comma
var reader = Csv.Parse(csv);

// NEW - user handles file I/O
var data = File.ReadAllText("data.csv");
var reader = Csv.Parse(data);

// NEW - async for large files
await foreach (var row in Csv.ParseAsync(data))
{
    // Process
}
```

---

## Implementation Phases

### Phase 1: Core (netstandard2.0) - 6 hours
- [ ] CsvParserOptions class
- [ ] Csv.Parse() implementation
- [ ] CsvReader, CsvRow, CsvCol (ref structs)
- [ ] ScalarParser (works on all frameworks)
- [ ] CsvException
- [ ] Basic tests

**Deliverable:** Working parser on netstandard2.0 (slow but correct)

---

### Phase 2: SIMD (net6.0+) - 8 hours
- [ ] Avx512Parser (safe, net6+)
- [ ] Avx2Parser (safe, net6+)
- [ ] NeonParser (safe, net6+)
- [ ] SimdParserFactory with conditional compilation
- [ ] SIMD correctness tests

**Deliverable:** 30+ GB/s on modern frameworks

---

### Phase 3: Async API - 4 hours
- [ ] Csv.ParseAsync() implementation
- [ ] Polyfill IAsyncEnumerable for netstandard2.0
- [ ] Chunked processing logic
- [ ] Cancellation support
- [ ] Async tests

**Deliverable:** Complete async API

---

### Phase 4: Polish - 4 hours
- [ ] Multi-framework build testing
- [ ] Benchmarks on all frameworks
- [ ] Documentation (XML comments)
- [ ] README with examples
- [ ] NuGet package validation

**Deliverable:** Production-ready v3.0

---

## Definition of Done

- [ ] Builds on all 7 target frameworks
- [ ] Zero unsafe code
- [ ] All tests pass on all frameworks
- [ ] Performance targets met (per framework)
- [ ] Zero allocations verified (net6+)
- [ ] Documentation complete
- [ ] NuGet package ready

---

## Next Steps

Ready to implement? Let me know and I'll start with Phase 1!

**Questions:**
1. Should `ParseAsync` accept `Stream` in addition to `string`?
2. Default limits (10k columns, 100k rows) - are these good?
3. Should we throw or truncate when limits are exceeded?
