using System.Globalization;

namespace HeroParser.SeparatedValues.Detection;

/// <summary>
/// Represents an inferred column type from CSV data analysis.
/// </summary>
public enum CsvInferredType
{
    /// <summary>Text/string data.</summary>
    String,
    /// <summary>32-bit integer values.</summary>
    Integer,
    /// <summary>64-bit integer values (exceeding int range).</summary>
    Long,
    /// <summary>Decimal/floating-point numeric values.</summary>
    Decimal,
    /// <summary>Boolean true/false values.</summary>
    Boolean,
    /// <summary>Date and/or time values.</summary>
    DateTime,
    /// <summary>GUID/UUID values.</summary>
    Guid
}

/// <summary>
/// Describes an inferred column in a CSV schema.
/// </summary>
/// <param name="Name">The column header name.</param>
/// <param name="InferredType">The detected data type.</param>
/// <param name="IsNullable">Whether any empty/null values were observed.</param>
/// <param name="MaxLength">The maximum string length observed for this column.</param>
public sealed record CsvInferredColumn(
    string Name,
    CsvInferredType InferredType,
    bool IsNullable,
    int MaxLength);

/// <summary>
/// The result of schema inference on CSV data.
/// </summary>
/// <param name="Columns">The inferred column definitions.</param>
/// <param name="SampledRowCount">The number of data rows sampled.</param>
public sealed record CsvSchemaInferenceResult(
    IReadOnlyList<CsvInferredColumn> Columns,
    int SampledRowCount);

/// <summary>
/// Options for controlling schema inference behavior.
/// </summary>
public sealed record CsvSchemaInferenceOptions
{
    /// <summary>
    /// Gets or sets the delimiter character (default: auto-detect or comma).
    /// </summary>
    public char? Delimiter { get; init; }

    /// <summary>
    /// Gets or sets the maximum number of data rows to sample (default: 100).
    /// </summary>
    public int SampleRows { get; init; } = 100;

    /// <summary>
    /// Gets the default options.
    /// </summary>
    public static CsvSchemaInferenceOptions Default { get; } = new();
}

/// <summary>
/// Analyzes CSV data to infer column types from sample rows.
/// </summary>
/// <remarks>
/// <para>
/// The inference algorithm samples data rows and attempts to parse each column value
/// against type candidates in order of specificity: Boolean, Integer, Long, Decimal,
/// Guid, DateTime, and finally String as a fallback.
/// </para>
/// <para>
/// Thread-Safety: All methods are thread-safe as they operate on local state only.
/// </para>
/// </remarks>
public static class CsvSchemaInference
{
    /// <summary>
    /// Infers the schema of CSV data by analyzing sample rows.
    /// </summary>
    /// <param name="data">The CSV data to analyze.</param>
    /// <param name="options">Optional inference options.</param>
    /// <returns>The inferred schema with column types and statistics.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the data is empty.</exception>
    public static CsvSchemaInferenceResult Infer(string data, CsvSchemaInferenceOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (string.IsNullOrWhiteSpace(data))
            throw new InvalidOperationException("Cannot infer schema from empty data.");

        options ??= CsvSchemaInferenceOptions.Default;
        var delimiter = options.Delimiter ?? DetectDelimiter(data);
        var maxSampleRows = options.SampleRows;

        // Parse the CSV data to extract headers and values
        var readOptions = new Core.CsvReadOptions { Delimiter = delimiter };
        var reader = Csv.ReadFromText(data, readOptions);

        // Read header row
        if (!reader.MoveNext())
            throw new InvalidOperationException("Cannot infer schema from empty data.");

        var headerRow = reader.Current;
        int columnCount = headerRow.ColumnCount;
        var headers = new string[columnCount];
        for (int i = 0; i < columnCount; i++)
            headers[i] = headerRow[i].ToString();

        // Track type candidates per column
        var candidates = new ColumnTypeTracker[columnCount];
        for (int i = 0; i < columnCount; i++)
            candidates[i] = new ColumnTypeTracker();

        int sampledRows = 0;
        while (reader.MoveNext() && sampledRows < maxSampleRows)
        {
            var row = reader.Current;
            sampledRows++;

            for (int i = 0; i < Math.Min(columnCount, row.ColumnCount); i++)
            {
                var value = row[i].ToString();
                candidates[i].Observe(value);
            }
        }

        // Build results
        var columns = new CsvInferredColumn[columnCount];
        for (int i = 0; i < columnCount; i++)
        {
            columns[i] = new CsvInferredColumn(
                headers[i],
                candidates[i].GetInferredType(),
                candidates[i].HasNulls,
                candidates[i].MaxLength);
        }

        return new CsvSchemaInferenceResult(columns, sampledRows);
    }

    private static char DetectDelimiter(string data)
    {
        try
        {
            return CsvDelimiterDetector.DetectDelimiter(data);
        }
        catch (FormatException)
        {
            return ','; // default fallback for ambiguous data
        }
        catch (InvalidOperationException)
        {
            return ','; // default fallback for single-column data
        }
    }

    private sealed class ColumnTypeTracker
    {
        private bool hasIntegers;
        private bool hasLongs;
        private bool hasDecimals;
        private bool hasBooleans;
        private bool hasDateTimes;
        private bool hasGuids;
        private bool hasStrings;
        private int nonEmptyCount;

        public bool HasNulls { get; private set; }
        public int MaxLength { get; private set; }

        public void Observe(string value)
        {
            if (value.Length > MaxLength)
                MaxLength = value.Length;

            if (string.IsNullOrEmpty(value))
            {
                HasNulls = true;
                return;
            }

            nonEmptyCount++;

            // Try types from most specific to least
            if (bool.TryParse(value, out _))
            {
                hasBooleans = true;
                return;
            }

            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            {
                hasIntegers = true;
                return;
            }

            if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            {
                hasLongs = true;
                return;
            }

            if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
            {
                hasDecimals = true;
                return;
            }

            if (Guid.TryParse(value, out _))
            {
                hasGuids = true;
                return;
            }

            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            {
                hasDateTimes = true;
                return;
            }

            hasStrings = true;
        }

        public CsvInferredType GetInferredType()
        {
            if (nonEmptyCount == 0)
                return CsvInferredType.String;

            // If any value is string, the whole column is string
            if (hasStrings)
                return CsvInferredType.String;

            // Boolean: only booleans
            if (hasBooleans && !hasIntegers && !hasLongs && !hasDecimals && !hasGuids && !hasDateTimes)
                return CsvInferredType.Boolean;

            // Guid: only guids
            if (hasGuids && !hasIntegers && !hasLongs && !hasDecimals && !hasBooleans && !hasDateTimes)
                return CsvInferredType.Guid;

            // DateTime: only datetimes
            if (hasDateTimes && !hasIntegers && !hasLongs && !hasDecimals && !hasBooleans && !hasGuids)
                return CsvInferredType.DateTime;

            // Numeric hierarchy: int < long < decimal
            bool hasAnyNumeric = hasIntegers || hasLongs || hasDecimals;
            if (hasAnyNumeric && !hasBooleans && !hasGuids && !hasDateTimes)
            {
                if (hasDecimals)
                    return CsvInferredType.Decimal;
                if (hasLongs)
                    return CsvInferredType.Long;
                return CsvInferredType.Integer;
            }

            // Mixed types fall back to String
            return CsvInferredType.String;
        }
    }
}
