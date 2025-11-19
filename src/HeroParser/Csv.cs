using HeroParser.SeparatedValues;

namespace HeroParser;

/// <summary>
/// Provides factory methods for creating high-performance CSV readers backed by SIMD parsing.
/// </summary>
/// <remarks>
/// The returned readers stream the source spans without allocating intermediate rows.
/// Call <c>Dispose</c> (or use a <c>using</c> statement) when you are finished to return pooled buffers.
/// </remarks>
public static class Csv
{
    /// <summary>
    /// Creates a reader that iterates over CSV records stored in a managed <see cref="string"/>.
    /// </summary>
    /// <param name="data">Complete CSV payload encoded as UTF-16.</param>
    /// <param name="options">
    /// Optional parser configuration. When <see langword="null"/>, <see cref="CsvParserOptions.Default"/> is used.
    /// </param>
    /// <returns>A <see cref="CsvCharSpanReader"/> that enumerates the parsed rows.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="data"/> is <see langword="null"/>.</exception>
    /// <exception cref="CsvException">Thrown when the payload violates the supplied <paramref name="options"/>.</exception>
    public static CsvCharSpanReader ReadFromText(string data, CsvParserOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        return ReadFromCharSpan(data.AsSpan(), options);
    }

    /// <summary>
    /// Creates a reader over a UTF-16 span (e.g., a substring or memory-mapped buffer).
    /// </summary>
    /// <param name="data">Span containing the CSV content.</param>
    /// <param name="options">
    /// Optional parser configuration. When <see langword="null"/>, <see cref="CsvParserOptions.Default"/> is used.
    /// </param>
    /// <returns>A streaming reader that exposes each row as a <see cref="CsvCharSpanRow"/>.</returns>
    /// <exception cref="CsvException">Thrown when the input violates the supplied <paramref name="options"/>.</exception>
    public static CsvCharSpanReader ReadFromCharSpan(ReadOnlySpan<char> data, CsvParserOptions? options = null)
    {
        options ??= CsvParserOptions.Default;
        options.Validate();
        return new CsvCharSpanReader(data, options);
    }

    /// <summary>
    /// Creates a reader over UTF-8 encoded CSV data without transcoding to UTF-16.
    /// </summary>
    /// <param name="data">Span containing UTF-8 encoded CSV content.</param>
    /// <param name="options">
    /// Optional parser configuration. When <see langword="null"/>, <see cref="CsvParserOptions.Default"/> is used.
    /// </param>
    /// <returns>A <see cref="CsvByteSpanReader"/> that yields UTF-8 backed rows.</returns>
    /// <exception cref="CsvException">Thrown when the payload violates the supplied <paramref name="options"/>.</exception>
    public static CsvByteSpanReader ReadFromByteSpan(ReadOnlySpan<byte> data, CsvParserOptions? options = null)
    {
        options ??= CsvParserOptions.Default;
        options.Validate();
        return new CsvByteSpanReader(data, options);
    }

}
