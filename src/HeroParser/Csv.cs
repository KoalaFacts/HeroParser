using System;

namespace HeroParser;

/// <summary>
/// Ultra-fast CSV parser targeting 30+ GB/s throughput using SIMD optimization.
/// Zero allocations in hot path, no unsafe code.
/// </summary>
public static class Csv
{
    /// <summary>
    /// Parse CSV data with zero allocations.
    /// </summary>
    /// <param name="csv">The CSV content to parse</param>
    /// <param name="options">Parser options (null for defaults: comma delimiter, 10k columns, 100k rows)</param>
    /// <returns>A zero-allocation CSV reader</returns>
    /// <exception cref="CsvException">Thrown when parsing fails or limits are exceeded</exception>
    public static CsvReader Parse(string csv, CsvParserOptions? options = null)
    {
        if (csv == null)
            throw new ArgumentNullException(nameof(csv));

        options ??= CsvParserOptions.Default;
        options.Validate();

        return new CsvReader(csv.AsSpan(), options);
    }
}
