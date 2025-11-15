namespace HeroParser.Simd;

/// <summary>
/// Interface for CSV column parsers with different SIMD implementations.
/// </summary>
internal interface ISimdParser
{
    /// <summary>
    /// Parse a CSV line into columns by finding delimiter positions.
    /// </summary>
    /// <param name="line">The CSV line to parse</param>
    /// <param name="delimiter">Field delimiter character</param>
    /// <param name="columnStarts">Output: starting positions of each column</param>
    /// <param name="columnLengths">Output: lengths of each column</param>
    /// <returns>Number of columns found</returns>
    int ParseColumns(
        ReadOnlySpan<char> line,
        char delimiter,
        Span<int> columnStarts,
        Span<int> columnLengths);
}
