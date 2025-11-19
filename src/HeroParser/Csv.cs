using HeroParser.SeparatedValues;

namespace HeroParser;

/// <summary>
/// Ultra-fast CSV parser targeting 30+ GB/s throughput using SIMD optimization.
/// Zero allocations in hot path. No unsafe keyword - uses safe Unsafe class and MemoryMarshal APIs.
/// </summary>
public static class Csv
{
    /// <summary>
    /// Reads the specified CSV string and returns a <see cref="CsvCharSpanReader"/> for reading its records.
    /// </summary>
    public static CsvCharSpanReader ReadFromText(string data, CsvParserOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        return ReadFromCharSpan(data.AsSpan(), options);
    }

    /// <summary>
    /// Reads CSV data from a UTF-16 span.
    /// </summary>
    public static CsvCharSpanReader ReadFromCharSpan(ReadOnlySpan<char> data, CsvParserOptions? options = null)
    {
        options ??= CsvParserOptions.Default;
        options.Validate();
        return new CsvCharSpanReader(data, options);
    }

    /// <summary>
    /// Reads CSV data from a UTF-8 byte span.
    /// </summary>
    public static CsvByteSpanReader ReadFromByteSpan(ReadOnlySpan<byte> data, CsvParserOptions? options = null)
    {
        options ??= CsvParserOptions.Default;
        options.Validate();
        return new CsvByteSpanReader(data, options);
    }

}
