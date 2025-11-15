# HeroParser v3.0 - Exception Design

## CsvException with Error Codes

### Error Code Enum

```csharp
/// <summary>
/// Error codes for CSV parsing failures.
/// </summary>
public enum CsvErrorCode
{
    /// <summary>
    /// Row exceeds maximum column limit.
    /// </summary>
    TooManyColumns = 1,

    /// <summary>
    /// CSV exceeds maximum row limit.
    /// </summary>
    TooManyRows = 2,

    /// <summary>
    /// Invalid delimiter (must be ASCII 0-127).
    /// </summary>
    InvalidDelimiter = 3,

    /// <summary>
    /// Invalid parser options.
    /// </summary>
    InvalidOptions = 4,

    /// <summary>
    /// General parsing error.
    /// </summary>
    ParseError = 99
}
```

### Exception Class

```csharp
/// <summary>
/// Exception thrown when CSV parsing fails.
/// </summary>
public class CsvException : Exception
{
    /// <summary>
    /// Error code indicating the type of failure.
    /// </summary>
    public CsvErrorCode ErrorCode { get; }

    /// <summary>
    /// Row number where error occurred (1-based), or null if not applicable.
    /// </summary>
    public int? Row { get; }

    /// <summary>
    /// Column number where error occurred (1-based), or null if not applicable.
    /// </summary>
    public int? Column { get; }

    public CsvException(CsvErrorCode errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public CsvException(CsvErrorCode errorCode, string message, int row)
        : base($"Row {row}: {message}")
    {
        ErrorCode = errorCode;
        Row = row;
    }

    public CsvException(CsvErrorCode errorCode, string message, int row, int column)
        : base($"Row {row}, Column {column}: {message}")
    {
        ErrorCode = errorCode;
        Row = row;
        Column = column;
    }
}
```

### Usage Examples

```csharp
// Too many columns
throw new CsvException(
    CsvErrorCode.TooManyColumns,
    $"Row has {actualColumns} columns, exceeds limit of {maxColumns}",
    rowNumber);

// Too many rows
throw new CsvException(
    CsvErrorCode.TooManyRows,
    $"CSV exceeds maximum row limit of {maxRows}");

// Invalid delimiter
throw new CsvException(
    CsvErrorCode.InvalidDelimiter,
    $"Delimiter '{delimiter}' (U+{(int)delimiter:X4}) must be ASCII (0-127)");

// User can check error codes
try
{
    var reader = Csv.Parse(data, options);
}
catch (CsvException ex) when (ex.ErrorCode == CsvErrorCode.TooManyRows)
{
    // Handle too many rows specifically
}
```

---

## API Surface (String only for now)

```csharp
public static class Csv
{
    /// <summary>
    /// Parse CSV data synchronously.
    /// </summary>
    public static CsvReader Parse(
        string csv,
        CsvParserOptions? options = null);

    /// <summary>
    /// Parse CSV data asynchronously.
    /// </summary>
    public static IAsyncEnumerable<CsvRow> ParseAsync(
        string csv,
        CsvParserOptions? options = null,
        CancellationToken cancellationToken = default);
}
```

**Note:** Using `string` instead of `ReadOnlySpan<char>` for now to keep it simple. Span overloads can be added later.

---

Ready to implement Phase 1! 🚀
