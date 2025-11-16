namespace HeroParser;

/// <summary>
/// Ultra-fast CSV parser targeting 30+ GB/s throughput using SIMD optimization.
/// Zero allocations in hot path, no unsafe code.
/// </summary>
public static class Csv
{
    /// <summary>
    /// Parses the specified CSV string and returns a <see cref="CsvReader"/> for reading its records.
    /// </summary>
    /// <param name="csv">The CSV data to parse. Cannot be null.</param>
    /// <param name="options">Optional parser settings that control how the CSV data is interpreted. If null, default options are used.</param>
    /// <returns>A <see cref="CsvReader"/> instance that can be used to read records from the provided CSV data.</returns>
    /// <exception cref="CsvException">Thrown when parsing fails or limits are exceeded</exception>
    public static CsvReader Parse(string csv, CsvParserOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(csv);

        return Parse(csv.AsSpan(), options);
    }

    /// <summary>
    /// Creates a new <see cref="CsvReader"/> to read
    /// and parse CSV data from the specified character span using the provided parser options.
    /// </summary>
    /// <remarks>The returned <see cref="CsvReader"/> does not own the underlying data; callers must ensure
    /// the lifetime of the <paramref name="csv"/> span is sufficient for reading. The <paramref name="options"/>
    /// parameter allows customization of parsing rules, such as delimiter and quoting behavior.</remarks>
    /// <param name="csv">A read-only span of characters containing the CSV data to be parsed.</param>
    /// <param name="options">The options to configure CSV parsing behavior. If <see langword="null"/>, default options are used.</param>
    /// <returns>A <see cref="CsvReader"/> instance that can be used to read records from the specified CSV data.</returns>
    /// <exception cref="CsvException">Thrown when parsing fails or limits are exceeded</exception>
    public static CsvReader Parse(ReadOnlySpan<char> csv, CsvParserOptions? options = null)
    {
        options ??= CsvParserOptions.Default;
        options.Validate();

        return new CsvReader(csv, options);
    }
}
